// 앱과 SCORE 모듈 사이의 연결. 앱의 주 화면은 이 클래스만 부른다 (기획서 v3: docs\plans\20261114-SCORE모듈-앱연결-기획서-v3.md).
//
// 하는 일:
//   - 판 관리: Steam 곡 정보(와 기어 위치)가 바뀌면 새 판을 연다. 같은 곡을 다시 시작한 것은 모듈이 알아채서 알려 준다. 판 번호(PlaySerial)가 그때마다 오른다.
//   - 화면 넘기기: 캡처한 화면을 모듈에 넘긴다. 플레이 중에는 모듈이 바쁘면 그 화면을 버린다(쌓지 않는다). 등록 요청 뒤의 화면은 버리지 않고 순서대로 넘긴다.
//   - 등록 요청: 요청을 열고, 답이 나오면 한 번 알려 준다. 기한, 화면이 바뀜, 취소를 모듈에 전한다.
//   - 진단 기록: 판의 시작, 자리 고정(걸린 시간), 재시작 판단, 요청과 답을 남긴다.
//
// 스레드: 이 클래스의 메서드는 모두 한 스레드(앱에서는 화면 스레드)에서 부른다. 모듈을 부르는 일은 IScoreRunner 가 돌린다
// (앱에서는 작업 스레드 하나, 시험에서는 가짜 시계). ScoreLink 는 모듈 호출을 대기열에 넣어 한 번에 하나씩 차례로 요청하므로 모듈은 한 번에 한 곳에서만 불린다.
// 모듈이 알려 준 것(자리, 재시작, 답)은 일이 끝난 뒤 부른 쪽 스레드에서 전한다.
namespace DjmaxScore;

public interface IScoreRunner
{
    /// <summary>단조 시계의 지금 값.</summary>
    long Now { get; }
    long TicksPerSecond { get; }
    /// <summary>앞서 맡긴 일이 아직 안 끝났는가.</summary>
    bool Busy { get; }
    /// <summary>work 를 돌리고(다른 스레드여도 된다) 끝나면 done 을 ScoreLink 를 부르는 스레드에서 부른다.</summary>
    void Run(Action work, Action done);
}

/// <summary>캡처 하나의 설명(화소는 따로 넘긴다). 뜻은 ScoreFrame 과 같다.</summary>
public readonly record struct CapturedFrame(long CapturedAt, long CaptureEndedAt, int Width, int Height, int Stride, int SourceWidth, int SourceHeight, int CropX, int CropY);

/// <summary>등록 요청의 답. FramesOffered 는 요청 뒤 모듈에 넘긴 화면의 수, MaxFrameGapMs 는 그 화면들 사이의 가장 긴 간격.</summary>
public sealed record ScoreAnswer(long RequestId, int PlaySerial, string PlayKey, RegistrationResult Result, int FramesOffered, double MaxFrameGapMs);

public sealed class ScoreLink : IDisposable
{
    private readonly ScoreModule? _module;
    private readonly IScoreRunner _runner;
    private readonly ScoreDiagnostics? _log;
    private readonly Queue<Action> _queue = new();
    private readonly List<object> _news = new();          // 모듈이 알려 준 것. 일하는 스레드에서 쌓고 부른 쪽 스레드에서 전한다
    private bool _running, _disposed;
    private long _sessionId, _requestId, _frameId;
    private string? _playKey;
    private GearPosition _gear;
    private long _playBeganAt;
    private string _playWhy = "";
    private bool _lockLogged;
    private int _observed, _dropped;

    private sealed class Open
    {
        public long Id, SessionId, PressedAt, Deadline, LastFrameAt, LastFrameId;
        public string Key = "";
        public int Serial, Offered;
        public bool Left, Expired;
        public double MaxGapMs;
        public required Action<ScoreAnswer> Answered;
    }
    private Open? _open;

    /// <summary>모델이나 실행 라이브러리를 못 읽었으면 그 사유. 쓸 수 있으면 null.</summary>
    public string? Unavailable { get; }
    public bool Available => _module is not null;
    /// <summary>판 번호. 새 판을 열 때와 같은 곡을 다시 시작했다고 볼 때마다 오른다.</summary>
    public int PlaySerial { get; private set; }
    public string? PlayKey => _playKey;
    public bool RequestOpen => _open is not null;
    /// <summary>지금 판에서 모듈에 넘긴 화면과, 바빠서 버린 화면의 수(요청 뒤의 화면은 세지 않는다).</summary>
    public int Observed => _observed;
    public int Dropped => _dropped;
    /// <summary>지금 판의 자리(모듈이 마지막으로 알려 준 것).</summary>
    public LocationUpdate? Location { get; private set; }
    /// <summary>자리가 바뀌었거나 판이 바뀌었다.</summary>
    public event Action? Changed;
    /// <summary>같은 곡을 다시 시작했다고 보았다.</summary>
    public event Action<PlayRestart>? Restarted;

    public ScoreLink(string finderPath, string readerPath, IScoreRunner runner, ScoreDiagnostics? log = null, ScoreOptions? options = null)
    {
        _runner = runner; _log = log;
        try
        {
            _module = ScoreModule.TryCreate(finderPath, readerPath, (options ?? new ScoreOptions()) with { TicksPerSecond = runner.TicksPerSecond }, out string? error);
            Unavailable = error;
        }
        catch (Exception e) { Unavailable = "ModelUnavailable: " + e.Message; }       // 실행 라이브러리를 못 읽으면 형식 초기화에서 던져질 수 있다
        if (_module is null) { _log?.Write("unavailable", new { reason = Unavailable }); return; }
        _module.LocationUpdated += u => { lock (_news) _news.Add(u); };
        _module.PlayRestarted += r => { lock (_news) _news.Add(r); };
        _module.RegistrationUpdated += r => { lock (_news) _news.Add(r); };
        _log?.Write("ready", new { module = ScoreModule.ModuleVersion });
    }

    private double Ms(long from, long to) => (to - from) * 1000.0 / _runner.TicksPerSecond;

    // ---------- 판 ----------

    /// <summary>판을 구분하는 키(Steam 곡 정보)나 기어 위치가 지금 판과 다를 때만 새 판을 연다. 같으면 아무 일도 하지 않는다(쌓인 근거를 지우지 않는다).</summary>
    public void EnsurePlay(string key, GearPosition gear, string why)
    {
        if (_playKey == key && _gear == gear) return;
        NewPlay(key, gear, why);
    }

    /// <summary>무조건 새 판을 연다("새로 감지하기"). 자리와 점수 이력을 모두 버리고 처음부터 찾는다.</summary>
    public void NewPlay(string key, GearPosition gear, string why)
    {
        if (_module is null) return;
        CloseTally();
        _playKey = key; _gear = gear; _playWhy = why; _lockLogged = false;
        PlaySerial++;
        long id = ++_sessionId, at = _runner.Now;
        _playBeganAt = at;
        Location = null;
        _log?.Write("play", new { serial = PlaySerial, key, gear = gear.ToString(), why });
        Post(() => _module.BeginSession(id, gear, at));        // 열려 있던 요청은 모듈이 그때까지 받은 화면으로 마친다
        Changed?.Invoke();
    }

    /// <summary>판을 끝낸다(곡 정보가 없어짐, 감지 중지). 열려 있는 요청은 건드리지 않는다.</summary>
    public void EndPlay(string why)
    {
        if (_module is null || _playKey is null) return;
        CloseTally();
        _log?.Write("play-end", new { serial = PlaySerial, why });
        long id = _sessionId;
        _playKey = null;
        Location = null;
        Post(() => _module.EndSession(id));
        Changed?.Invoke();
    }

    private void CloseTally()
    {
        if (_playKey is not null && _observed + _dropped > 0) _log?.Write("frames", new { serial = PlaySerial, observed = _observed, dropped = _dropped });
        _observed = _dropped = 0;
    }

    // ---------- 화면 ----------

    /// <summary>
    /// 캡처한 화면 하나. key 는 그 화면이 어느 곡의 것인지(캡처 앞뒤의 Steam 곡 정보가 같을 때 그 값, 아니면 null).
    /// bgra 는 넘긴 뒤 부른 쪽이 바꾸지 않는다(일이 끝날 때까지 들고 있는다).
    /// 열려 있는 등록 요청의 화면으로 받았으면 그 요청에서 몇 번째 화면인지(1부터)를, 아니면 0 을 돌려준다.
    /// </summary>
    public int Offer(in CapturedFrame f, byte[] bgra, string? key)
    {
        if (_module is null || _disposed) return 0;
        var shape = f;
        if (_open is Open q)
        {
            // 요청이 열려 있는 동안에는 그 요청의 곡에서, 요청 뒤에 캡처한 화면만 넘긴다. 버리지 않고 순서대로.
            if (q.Left || key != q.Key || f.CapturedAt < q.PressedAt) return 0;
            if (q.Offered > 0) q.MaxGapMs = Math.Max(q.MaxGapMs, Ms(q.LastFrameAt, f.CapturedAt));
            q.Offered++; q.LastFrameAt = f.CaptureEndedAt;
            long id = q.LastFrameId = ++_frameId, request = q.Id, owner = q.SessionId;
            int index = q.Offered;
            Post(() => _module.PushRegistrationFrame(request, Frame(owner, id, shape), bgra));
            return index;
        }
        if (key is null || key != _playKey) return 0;
        if (_running || _queue.Count > 0 || _runner.Busy) { _dropped++; return 0; }      // 플레이 중에는 바쁘면 이 화면을 버린다
        _observed++;
        long frameId = ++_frameId, session = _sessionId;
        Post(() => _module.ObserveFrame(Frame(session, frameId, shape), bgra));
        return 0;
    }

    private static ScoreFrame Frame(long session, long id, in CapturedFrame f) =>
        new(session, id, f.CapturedAt, f.CaptureEndedAt, f.Width, f.Height, f.Stride, f.SourceWidth, f.SourceHeight, f.CropX, f.CropY);

    // ---------- 등록 요청 ----------

    /// <summary>
    /// 등록 키. 지금 판에 요청을 열고 요청 번호를 돌려준다. 답(Accepted·Failed·Cancelled)은 answered 로 한 번 온다.
    /// 모듈을 쓸 수 없거나 판이 없으면 그 자리에서 Failed 로 답한다.
    /// </summary>
    public long BeginRequest(long pressedAt, double seconds, Action<ScoreAnswer> answered)
    {
        if (_open is not null) throw new InvalidOperationException("이미 열려 있는 등록 요청이 있다");
        long id = ++_requestId;
        if (_module is null || _playKey is null)
        {
            string reason = _module is null ? "ModelUnavailable" : "NoPlay";
            var failed = new RegistrationResult(id, _sessionId, RegistrationStatus.Failed, reason, null, null, null, null, 0, 0, 0);
            _log?.Write("answer", new { request = id, serial = PlaySerial, state = "Failed", reason });
            answered(new ScoreAnswer(id, PlaySerial, _playKey ?? "", failed, 0, 0));
            return id;
        }
        var q = new Open { Id = id, SessionId = _sessionId, Key = _playKey, Serial = PlaySerial, PressedAt = pressedAt,
                           Deadline = pressedAt + (long)(seconds * _runner.TicksPerSecond), Answered = answered };
        _open = q;
        _log?.Write("request", new { request = id, serial = q.Serial, key = q.Key, gear = _gear.ToString(),
                                     located = Location?.State.ToString() ?? "Searching", sincePlayMs = Math.Round(Ms(_playBeganAt, pressedAt)) });
        Post(() => _module.BeginRegistration(q.Id, q.SessionId, q.PressedAt, q.Deadline));
        return id;
    }

    /// <summary>이 요청의 곡이 끝났다(Steam 곡 정보가 바뀜). 취소가 아니다. 더 넘기지 않고 받은 화면으로 마치게 한다.</summary>
    public void ContextLeft()
    {
        if (_module is null || _open is not Open q || q.Left) return;
        q.Left = true;
        Post(() => _module.NotifyContextLeft(q.Id, q.LastFrameId, q.LastFrameAt));
    }

    /// <summary>
    /// 사용자의 취소, 감지 중지. 요청을 그 자리에서 끝낸다(답은 Cancelled 로 한 번 간다). 모듈에도 알린다.
    /// 그 자리에서 끝내는 까닭: 취소한 직후에 다시 등록 키를 누를 수 있어야 한다. 모듈의 일은 순서대로 돌므로 취소가 그 뒤에 여는 요청보다 먼저 처리된다.
    /// 취소한 요청에 대해 모듈이 뒤늦게 알려 주는 것은 요청 번호가 달라 버려진다.
    /// </summary>
    public void CancelRequest()
    {
        if (_module is null || _open is not Open q) return;
        _open = null;
        Post(() => _module.CancelRegistration(q.Id));
        _log?.Write("answer", new { request = q.Id, serial = q.Serial, state = "Cancelled", reason = "Cancelled", framesOffered = q.Offered });
        var cancelled = new RegistrationResult(q.Id, q.SessionId, RegistrationStatus.Cancelled, "Cancelled", null, null, null, null, 0, 0, 0);
        q.Answered(new ScoreAnswer(q.Id, q.Serial, q.Key, cancelled, q.Offered, q.MaxGapMs));
    }

    /// <summary>주기적으로 부른다. 기한이 지났으면 모듈에 알린다(화면이 안 들어와도 기한은 지나간다).</summary>
    public void Poll()
    {
        if (_module is null || _open is not Open q || q.Expired) return;
        long now = _runner.Now;
        if (now <= q.Deadline) return;
        q.Expired = true;
        Post(() => _module.Poll(now));
    }

    // ---------- 대기열: 모듈 호출을 한 번에 하나씩 차례로 처리 ----------

    private void Post(Action work)
    {
        _queue.Enqueue(work);
        Pump();
    }

    private void Pump()
    {
        while (!_running && _queue.Count > 0)
        {
            Action work = _queue.Dequeue();
            _running = true;
            _runner.Run(work, () => { _running = false; Deliver(); Pump(); });
        }
    }

    /// <summary>모듈이 알려 준 것을 부른 쪽 스레드에서 전한다.</summary>
    private void Deliver()
    {
        object[] news;
        lock (_news) { news = _news.ToArray(); _news.Clear(); }
        foreach (object item in news)
        {
            switch (item)
            {
                case LocationUpdate u when u.SessionId == _sessionId && _playKey is not null:
                    Location = u;
                    if (u.State == LocationState.Locked && !_lockLogged)
                    {
                        _lockLogged = true;
                        // 걸린 시간의 시작은 "이 판을 연 때"다(곡 정보 감지, 새로 감지하기 등 why 에 적힌 사건). 실제 플레이 시작 시각이 아니다.
                        _log?.Write("locked", new { serial = PlaySerial, sincePlayMs = Math.Round(Ms(_playBeganAt, u.At)), since = _playWhy,
                                                    area = u.Area is ScoreArea a ? $"{a.X},{a.Y},{a.Width},{a.Height}" : null, notes = u.NoteCount });
                    }
                    else if (u.State == LocationState.Unsupported) _log?.Write("unsupported", new { serial = PlaySerial });
                    Changed?.Invoke();
                    break;
                case PlayRestart r when r.SessionId == _sessionId && _playKey is not null:
                    PlaySerial++;
                    _log?.Write("restart", new { serial = PlaySerial, sincePlayMs = Math.Round(Ms(_playBeganAt, r.At)), firstValueMs = Math.Round(Ms(_playBeganAt, r.FirstSeenAt)),
                                                 before = r.Before, values = r.Values });
                    Restarted?.Invoke(r);
                    Changed?.Invoke();
                    break;
                case RegistrationResult r when _open is Open q && r.RequestId == q.Id && r.State is RegistrationStatus.Accepted or RegistrationStatus.Failed or RegistrationStatus.Cancelled:
                    _open = null;
                    _log?.Write("answer", new
                    {
                        request = q.Id, serial = q.Serial, state = r.State.ToString(), reason = r.Reason, score = r.RawScore, candidate = r.Candidate,
                        elapsedMs = Math.Round(r.ElapsedMs), framesOffered = q.Offered, framesJudged = r.FramesAfterRequest, framesUnread = r.FramesUnread, maxGapMs = Math.Round(q.MaxGapMs),
                        playReads = r.Play?.PlayReads, playVerified = r.Play?.PlayValuesVerified, notes = r.Play?.NoteCount, agree = r.Play is null ? (double?)null : Math.Round(r.Play.Agree, 3),
                        lastPlay = r.Play?.LastPlayValue, left = q.Left, expired = q.Expired, basis = r.Basis,
                    });
                    q.Answered(new ScoreAnswer(q.Id, q.Serial, q.Key, r, q.Offered, q.MaxGapMs));
                    break;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CloseTally();
        if (_module is ScoreModule m) Post(m.Dispose);        // 돌고 있는 일이 끝난 뒤에 닫는다
    }
}
