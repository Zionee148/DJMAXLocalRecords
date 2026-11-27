// SCORE 모듈의 공개 인터페이스. 앱은 ScoreLink 를 거쳐 이 클래스를 부른다.
//
//   세션: 한 판. BeginSession 으로 시작하고, 캡처한 화면을 ObserveFrame 으로 넣는다. 모듈이 점수 자리를 찾아 고정하고,
//         고정한 뒤에는 낮은 빈도로 점수를 읽어 검산 근거를 쌓는다(화면에 내보내지 않는다).
//   요청: 등록 키. BeginRegistration 으로 열고, 그 뒤에 캡처한 화면을 PushRegistrationFrame 으로 넣는다.
//         같은 값이 이어지면 Accepted, 기한까지 못 채우면 Failed. 최종 값은 요청 뒤의 화면에서만 가져온다.
//
// 모듈은 기록 파일을 읽거나 쓰지 않는다. Accepted 는 "사용자에게 확인시킬 수 있는 점수"이지 저장이 아니다.
//
// 구현의 범위:
//   - 호출한 스레드에서 바로 처리한다(화면 하나에 수십 ms). 앱의 화면 스레드에서 부르지 말고 작업 스레드에서 부른다.
//     밀릴 때 화면을 버리는 일은 부르는 쪽(앱에서는 ScoreLink) 몫이다.
//   - 화면은 1920x1080 좌표계만 받는다(다른 크기는 Unsupported). 잘라 보낼 때는 선택한 위치의 띠 전체가 들어 있어야 한다.
using System.Diagnostics;

namespace DjmaxScore;

public enum GearPosition { Left, Center, Right }

/// <summary>캡처 하나의 설명. 화소는 BGRA 32비트, 위에서 아래 순서. 시각은 단조 시계(Stopwatch.GetTimestamp)의 값.</summary>
public readonly record struct ScoreFrame(long SessionId, long FrameId, long CapturedAt, long CaptureEndedAt,
                                         int Width, int Height, int Stride, int SourceWidth, int SourceHeight, int CropX, int CropY);

public readonly record struct ScoreArea(int X, int Y, int Width, int Height, float AngleDegrees);

public enum LocationState { Searching, Locked, Unsupported }

/// <summary>같은 곡을 다시 시작한 것으로 보았다(읽은 점수의 흐름으로). FirstSeenAt 은 새 판의 값이 처음 읽힌 시각, Count 는 이 세션에서 몇 번째인지.</summary>
public sealed record PlayRestart(long SessionId, long At, long FirstSeenAt, int Count, int Before = 0, int[]? Values = null);

public sealed record LocationUpdate(long SessionId, LocationState State, long FrameId, long At, ScoreArea? Area, long? LockedAt, int NoteCount);

public enum RegistrationStatus { None, WaitingForFrames, Reading, Accepted, Failed, Cancelled }

public sealed record RegistrationResult(long RequestId, long SessionId, RegistrationStatus State, string Reason, int? RawScore, int? Candidate,
                                        ScoreArea? Area, PlayEvidence? Play, int FramesAfterRequest, int FramesUnread, double ElapsedMs, string Basis = "");

public sealed record ScoreOptions
{
    public TrackerOptions Tracker { get; init; } = new();
    public JudgeOptions Judge { get; init; } = new();
    public long TicksPerSecond { get; init; } = Stopwatch.Frequency;
    public int Threads { get; init; } = 1;                 // 모델 실행에 쓰는 스레드 수
}

public sealed class ScoreModule : IDisposable
{
    public const string ModuleVersion = "0.1.0-dev";
    public const int ScreenWidth = 1920, ScreenHeight = 1080, BandTop = 740, BandWidth = 960, BandHeight = ScreenHeight - BandTop;

    /// <summary>선택한 기어 위치에서 모듈이 보는 띠의 왼쪽 끝(1920x1080 기준). 띠는 가로 960, 세로 740 아래 전부다.</summary>
    public static int BandLeft(GearPosition position) => position switch { GearPosition.Left => 0, GearPosition.Center => 480, _ => 960 };

    /// <summary>
    /// 래더 매치의 비교 정보판(RATE·BREAK)이 띠와 겹치는 자리(1920x1080 화면 전체 좌표. 오른쪽·아래 끝은 넣지 않는다). 이 안에서는 점수 자리를 찾지 않는다.
    ///   LEFT   X 736~960,  Y 740~912  (화면 가운데의 정보판 X 736~1184 가운데 띠에 들어오는 왼쪽)
    ///   CENTER X 480~544,  Y 740~912  (화면 왼쪽에 놓이는 정보판의 오른쪽 끝)
    ///   RIGHT  X 960~1184, Y 740~912  (가운데 정보판의 오른쪽)
    /// 까닭: 정보판의 RATE("97.25%")는 판독기가 여섯 자리 숫자로 읽고, 플레이 동안 변하는 값이 촘촘한 산식(노트 수 2500)의 점수 규칙을 통과해
    /// 그 자리가 점수 자리로 고정된 일이 있다(래더 RIGHT·CYTUS. 등록은 검산에서 막혔다). 기운 점수는 바로 읽히는 자리보다 늦게 조건을 채운다.
    /// 정보판의 높이까지만 뺀다: 그 아래(Y 912 부터)는 띠의 가로 전체를 그대로 본다(정보판 아래로 점수가 가운데 쪽까지 나오는 기어가 있다. KARTRIDER LEFT).
    /// 기어 표본 270개(90개 x 세 위치)의 점수 자리는 다시 읽을 때의 여백까지 쳐도 이 자리와 겹치지 않는다. 모드를 가리지 않고 언제나 뺀다(모듈은 모드를 모른다).
    /// </summary>
    public static Box PanelZone(GearPosition position) => position switch
    {
        GearPosition.Left => new Box(736, BandTop, 960, PanelBottom),
        GearPosition.Center => new Box(480, BandTop, 544, PanelBottom),
        _ => new Box(960, BandTop, 1184, PanelBottom),
    };
    public const int PanelBottom = 912;

    private readonly RowFinder _finder;
    private readonly ScoreReader _reader;
    private readonly ScoreOptions _options;

    private sealed class Session
    {
        public long Id;
        public GearPosition Position;
        public required ScoreTracker Tracker;
        public required PlayLog Play;
        public readonly RestartWatch Restart = new();
        public int Restarts;
        public long LastFrameId = long.MinValue;
        public bool Unsupported;
    }

    private sealed class Request
    {
        public long Id, SessionId;
        public double PressedAt, Deadline;                 // 초
        public required Session Owner;                     // 요청이 열릴 때의 세션. 그 뒤 새 세션이 시작돼도 이 요청은 이것을 쓴다
        public required RegistrationJudge Judge;
        public double LastAt;
    }

    private Session? _session;
    private Request? _request;
    private readonly HashSet<long> _usedRequests = new();

    public LocationUpdate Location { get; private set; } = new(0, LocationState.Searching, 0, 0, null, null, 0);
    public RegistrationResult Registration { get; private set; } = new(0, 0, RegistrationStatus.None, "", null, null, null, null, 0, 0, 0);
    public event Action<LocationUpdate>? LocationUpdated;
    public event Action<PlayRestart>? PlayRestarted;
    /// <summary>지금 세션에서 새 판으로 본 횟수.</summary>
    public int Restarts => _session?.Restarts ?? 0;
    public event Action<RegistrationResult>? RegistrationUpdated;
    /// <summary>번호가 맞지 않거나 순서가 어긋나 무시한 호출의 수.</summary>
    public int IgnoredCalls { get; private set; }

    private ScoreModule(RowFinder finder, ScoreReader reader, ScoreOptions options) { _finder = finder; _reader = reader; _options = options; }

    /// <summary>모델 파일 둘(탐지, 판독)로 모듈을 만든다. 모델이 없거나 못 읽으면 null 과 사유를 돌려준다(앱은 SCORE 없이 계속 돈다).</summary>
    public static ScoreModule? TryCreate(string finderPath, string readerPath, ScoreOptions? options, out string? error)
    {
        options ??= new ScoreOptions();
        if (!File.Exists(finderPath) || !File.Exists(readerPath)) { error = $"ModelUnavailable: {(File.Exists(finderPath) ? readerPath : finderPath)} 가 없다"; return null; }
        try
        {
            error = null;
            return new ScoreModule(new RowFinder(finderPath, options.Threads), new ScoreReader(readerPath, options.Threads), options);
        }
        catch (Exception e) { error = "ModelUnavailable: " + e.Message; return null; }
    }

    public void Dispose() { _finder.Dispose(); _reader.Dispose(); }

    private double Seconds(long ticks) => (double)ticks / _options.TicksPerSecond;

    // ---------- 세션 ----------

    /// <summary>새 플레이. 위치와 판독 이력을 버리고 처음부터 찾는다. 열려 있는 등록 요청은 취소하지 않고, 그때까지 받은 화면으로 마친다.</summary>
    public void BeginSession(long sessionId, GearPosition position, long at)
    {
        if (_request is { } open) Finish(open.Judge.Close(open.LastAt, "ContextChangedNoEvidence", ended: true));   // NotifyContextLeft 없이 새 세션이 먼저 왔다: 이미 받은 것으로 마친다
        _session = new Session
        {
            Id = sessionId, Position = position,
            Tracker = new ScoreTracker(_finder, _reader, BandLeft(position), BandTop, _options.Tracker, skip: PanelZone(position)),
            Play = new PlayLog(_options.Judge.PlayEvery),
        };
        Publish(new LocationUpdate(sessionId, LocationState.Searching, 0, at, null, null, 0));
    }

    /// <summary>탐색만 끝낸다. 열려 있는 등록 요청은 없애지 않는다.</summary>
    public void EndSession(long sessionId)
    {
        if (_session?.Id != sessionId) { IgnoredCalls++; return; }
        _session = null;
    }

    /// <summary>캡처한 화면 하나(요청이 열려 있지 않을 때). 고정 전에는 위치를 찾고, 고정 뒤에는 영역을 다듬으며 낮은 빈도로 점수를 읽어 둔다.</summary>
    public void ObserveFrame(in ScoreFrame frame, ReadOnlySpan<byte> bgra)
    {
        Session? s = _session;
        if (s is null || frame.SessionId != s.Id || frame.FrameId == s.LastFrameId) { IgnoredCalls++; return; }
        s.LastFrameId = frame.FrameId;
        Picture? band = Band(frame, bgra, s.Position);
        if (band is null)
        {
            if (!s.Unsupported) { s.Unsupported = true; Publish(new LocationUpdate(s.Id, LocationState.Unsupported, frame.FrameId, frame.CapturedAt, null, null, 0)); }
            return;
        }
        double at = Seconds(frame.CapturedAt);
        bool was = s.Tracker.Locked;
        s.Tracker.Observe(band, at);
        if (s.Tracker.Locked)
        {
            if (s.Play.Due(at) && s.Tracker.Read(band) is var (r, _))
            {
                // 지난 값은 이 화면을 읽기 전의 것으로 견준다(Read 가 값을 받아들이면 지난 값이 바뀐다). 낮은 값은 받아들여지지 않으므로 순서는 상관없다.
                s.Play.Offer(at, r.Value, r.Clear);
                if (s.Restart.Offer(at, r.Value, r.Clear, s.Tracker.LastValue, s.Tracker.LastValueAt, s.Tracker.Notes, () => s.Play.Evidence(), _options.Judge.MaxNotesPerSecond))
                    StartOver(s, at);
            }
            if (!was || Location.Area != AreaOf(s.Tracker))
                Publish(new LocationUpdate(s.Id, LocationState.Locked, frame.FrameId, frame.CapturedAt, AreaOf(s.Tracker),
                                           (long)(s.Tracker.LockAt * _options.TicksPerSecond), s.Tracker.Notes));
        }
    }

    /// <summary>
    /// 같은 곡을 다시 시작했다: 앞 판의 점수 이력과 근거를 버리고, 새 판으로 보게 한 값들부터 새 근거를 쌓는다. 점수판 자리는 그대로 쓴다.
    /// (이미 열려 있는 등록 요청은 요청 때의 근거를 따로 들고 있어 영향을 받지 않는다)
    /// </summary>
    private void StartOver(Session s, double at)
    {
        var run = s.Restart.Run.ToArray();
        s.Play = new PlayLog(_options.Judge.PlayEvery);
        foreach (var (when, value) in run) s.Play.Offer(when, value, true);
        int before = s.Tracker.LastValue;
        s.Tracker.StartOver(run[^1].Value, run[^1].At);
        s.Restart.Reset();
        s.Restarts++;
        // Before(앞 판의 지난 값)와 Values(새 판으로 보게 한 값들)는 진단용이다: 잘못 알아챘을 때 무엇을 보고 그랬는지 남긴다.
        PlayRestarted?.Invoke(new PlayRestart(s.Id, (long)(at * _options.TicksPerSecond), (long)(run[0].At * _options.TicksPerSecond), s.Restarts,
                                              before, run.Select(x => x.Value).ToArray()));
    }

    // ---------- 등록 요청 ----------

    /// <summary>
    /// 등록 키. pressedAt 이전의 화면은 근거로 쓰지 않는다. deadline 까지 답이 안 나오면 Failed.
    /// 자리가 고정되기 전이거나 플레이 중 근거가 모자라면 그 자리에서 Failed 로 답한다(화면을 더 넣어도 무시된다).
    /// </summary>
    public void BeginRegistration(long requestId, long sessionId, long pressedAt, long deadline)
    {
        if (_session is not { } s || s.Id != sessionId || _request is not null || !_usedRequests.Add(requestId)) { IgnoredCalls++; return; }
        double pressed = Seconds(pressedAt);
        var judge = new RegistrationJudge(pressed, s.Play.Evidence(), _options.Judge with { Deadline = Seconds(deadline) - pressed });
        _request = new Request { Id = requestId, SessionId = sessionId, PressedAt = pressed, Deadline = Seconds(deadline), Owner = s, Judge = judge, LastAt = pressed };
        // 기다려도 답이 달라지지 않는 요청은 화면을 기다리지 않고 바로 답한다(판단 규칙은 그대로다. 기한에 내던 답을 앞당겨 낼 뿐이다):
        //   - 자리가 고정되기 전에 온 요청은 점수를 내지 않는다(LocationUncertain). 요청 중에 고정돼도 플레이 중 근거가 없다.
        //   - 플레이 중 근거가 모자라거나 규칙과 안 맞는 요청(NotEnoughPlayEvidence·RuleConflict). 근거는 요청 때 정해지고 요청 뒤의 화면으로는 채워지지 않는다.
        string blocked = !s.Tracker.Locked ? "LocationUncertain" : judge.Blocked;
        if (blocked != "") { Finish(new JudgeResult(RegistrationState.Failed, blocked, null, null, 0, judge.Play, 0, 0)); return; }
        Report(RegistrationStatus.WaitingForFrames, "", null, 0);
    }

    /// <summary>요청 뒤에 캡처한 화면 하나. 순서대로 넣는다.</summary>
    public void PushRegistrationFrame(long requestId, in ScoreFrame frame, ReadOnlySpan<byte> bgra)
    {
        Request? q = _request;
        if (q is null || q.Id != requestId || frame.SessionId != q.SessionId || frame.FrameId == q.Owner.LastFrameId) { IgnoredCalls++; return; }
        double at = Seconds(frame.CapturedAt);
        if (at < q.PressedAt) { IgnoredCalls++; return; }
        if (at > q.Deadline + 1e-6) { Finish(q.Judge.Expire()); return; }
        q.Owner.LastFrameId = frame.FrameId; q.LastAt = at;
        Picture? band = Band(frame, bgra, q.Owner.Position);
        if (band is null) { IgnoredCalls++; return; }
        ScoreTracker tracker = q.Owner.Tracker;
        tracker.Observe(band, at);                       // 요청 중에도 영역은 계속 다듬는다(움직이는 점수판). 고정 전이면 여기서 찾는다
        int value = -1;
        if (tracker.Read(band) is var (r, _) && r.Clear) value = r.Value;
        JudgeResult? result = q.Judge.Offer(at, value, tracker.Locked);
        if (result is not null) Finish(result);
        else Report(RegistrationStatus.Reading, "", null, (at - q.PressedAt) * 1000);
    }

    /// <summary>앱이 주기적으로 부른다(화면이 안 들어와도 기한은 지나간다).</summary>
    public void Poll(long now)
    {
        if (_request is { } q && Seconds(now) > q.Deadline + 1e-6) Finish(q.Judge.Expire());
    }

    /// <summary>
    /// 이 캡처 뒤의 화면은 이 플레이 것이 아니다(곡·메뉴 전환, 게임 창이 사라짐). 취소가 아니다. 받은 것으로 판단을 마친다.
    /// 화면을 받는 대로 판단하므로 이미 넣은 화면을 되돌려 빼지는 않는다(아직 답이 안 났다면 앞부분만으로도 답이 나지 않는다).
    /// </summary>
    public void NotifyContextLeft(long requestId, long lastValidFrameId, long lastValidCaptureEndedAt)
    {
        if (_request is not { } q || q.Id != requestId) { IgnoredCalls++; return; }
        Finish(q.Judge.Close(Math.Max(q.LastAt, q.PressedAt), "ContextChangedNoEvidence", ended: true));
    }

    /// <summary>앱이 더 보낼 화면이 없다. 가진 것으로 바로 판단한다.</summary>
    public void CompleteRegistration(long requestId)
    {
        if (_request is not { } q || q.Id != requestId) { IgnoredCalls++; return; }
        Finish(q.Judge.Close(Math.Max(q.LastAt, q.PressedAt)));
    }

    /// <summary>사용자의 취소, 감지 중지, 앱 종료.</summary>
    public void CancelRegistration(long requestId)
    {
        if (_request is not { } q || q.Id != requestId) { IgnoredCalls++; return; }
        _request = null;
        Registration = new RegistrationResult(q.Id, q.SessionId, RegistrationStatus.Cancelled, "Cancelled", null, null, AreaOf(q.Owner.Tracker), q.Judge.Play, 0, 0, 0);
        RegistrationUpdated?.Invoke(Registration);
    }

    private void Finish(JudgeResult result)
    {
        Request q = _request!;
        _request = null;                                 // 한 요청의 최종 상태는 한 번만 정해진다. 그 뒤의 호출은 번호가 맞지 않아 무시된다
        Registration = new RegistrationResult(q.Id, q.SessionId, result.State == RegistrationState.Accepted ? RegistrationStatus.Accepted : RegistrationStatus.Failed,
                                              result.Reason, result.Score, result.Candidate, AreaOf(q.Owner.Tracker), result.Play,
                                              result.FramesAfterRequest, result.FramesUnread, result.Elapsed * 1000, result.Basis);
        RegistrationUpdated?.Invoke(Registration);
    }

    private void Report(RegistrationStatus status, string reason, int? candidate, double elapsedMs)
    {
        Request q = _request!;
        if (Registration.RequestId == q.Id && Registration.State == status) return;
        Registration = new RegistrationResult(q.Id, q.SessionId, status, reason, null, candidate, AreaOf(q.Owner.Tracker), q.Judge.Play, 0, 0, elapsedMs);
        RegistrationUpdated?.Invoke(Registration);
    }

    private void Publish(LocationUpdate update)
    {
        Location = update;
        LocationUpdated?.Invoke(update);
    }

    private static ScoreArea? AreaOf(ScoreTracker tracker)
    {
        if (tracker.Area is not Box a) return null;
        int x = Numbers.Round(a.X0), y = Numbers.Round(a.Y0);
        return new ScoreArea(x, y, Numbers.Round(a.X1) - x, Numbers.Round(a.Y1) - y, (float)(tracker.Pose?.Angle ?? 0));
    }

    /// <summary>받은 버퍼(BGRA)에서 선택한 위치의 띠를 BGR 그림으로 복사한다. 1920x1080 좌표계가 아니거나 띠가 다 들어 있지 않으면 null.</summary>
    private static Picture? Band(in ScoreFrame f, ReadOnlySpan<byte> bgra, GearPosition position)
    {
        if (f.SourceWidth != ScreenWidth || f.SourceHeight != ScreenHeight) return null;
        int left = BandLeft(position) - f.CropX, top = BandTop - f.CropY;
        if (left < 0 || top < 0 || left + BandWidth > f.Width || top + BandHeight > f.Height || bgra.Length < (f.Height - 1) * f.Stride + f.Width * 4) return null;
        var data = new byte[BandHeight * BandWidth * 3];
        for (int y = 0; y < BandHeight; y++)
        {
            ReadOnlySpan<byte> row = bgra.Slice((top + y) * f.Stride + left * 4, BandWidth * 4);
            int o = y * BandWidth * 3;
            for (int x = 0; x < BandWidth; x++)
            {
                data[o + x * 3] = row[x * 4]; data[o + x * 3 + 1] = row[x * 4 + 1]; data[o + x * 3 + 2] = row[x * 4 + 2];
            }
        }
        return new Picture(data, BandHeight, BandWidth);
    }
}
