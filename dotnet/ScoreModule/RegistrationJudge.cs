// 등록 요청의 판단(확정·보류·실패). Python 의 detector\judge.py("fast" 방식)를 옮긴 것이다.
//
//   플레이 중: 고정 뒤의 화면을 낮은 빈도로 읽어 둔 값(PlayLog)으로 노트 수를 맞추고 검산 근거를 만든다. 값을 만들어 내는 데 쓰지 않는다.
//   요청 뒤  : 요청 이후에 캡처한 화면만 최종 값의 근거로 쓴다. 점수로 볼 수 있는 같은 값이 서로 다른 캡처 Need 장 이상에서,
//              StableFor 초 이어져 읽히면 답한다. 기한까지 못 채우면 값을 내지 않는다.
//   사라지면 직전 값(2026-11-14 에 정한 규칙): StableFor 를 채우기 전에 점수가 사라지면, 그 값이 ShortFor 초 이상(캡처 NeedShort 장 이상) 이어졌고
//              그 뒤로 점수로 볼 수 있는 값이 다시 나오지 않은 채 GoneFor 초가 지났을 때 그 값을 낸다. 다른 점수로 바뀌면 앞의 값에는 이 예외 조건을 적용하지 않는다.
//              (키를 누른 뒤 최종 점수가 화면에 남는 시간이 짧아서, 늦게 누르면 StableFor 를 채우기 전에 점수판이 사라진다)
//
// 점수로 볼 수 없는 판독(0, 산식에 없는 값, 줄어든 값)은 후보가 되지 못하고, 같은 값으로 이어지던 연속 판독값만 끊는다
// (결과 화면의 같은 자리에 다른 숫자가 또렷이 읽히는 위치가 있다).
namespace DjmaxScore;

public enum RegistrationState { Accepted, Failed }

public sealed record JudgeOptions
{
    public double PlayEvery { get; init; } = 0.2;          // 플레이 중 읽어 두는 간격(초)
    public double Deadline { get; init; } = 8.0;           // 요청부터 기한(초)
    public int Need { get; init; } = 3;                    // 같은 값으로 읽힌 서로 다른 캡처의 수
    public double StableFor { get; init; } = 0.5;          // 그 캡처들의 처음과 끝 사이(초)
    public int MinPlayValues { get; init; } = 8;           // 플레이 중 규칙을 통과한 서로 다른 값
    public double MinAgree { get; init; } = 0.6;           // 또렷이 읽힌 값 가운데 규칙을 통과한 비율
    public double MaxNotesPerSecond { get; init; } = 50;   // 마지막 플레이 값에서 그 사이 시간에 오를 수 있는 한도
    public double ShortFor { get; init; } = 0.3;           // 사라지면 직전 값: 사라지기 직전에 같은 값이 이만큼(초)은 이어졌어야 한다(0.3초로 정함, 2026-11-14). 0 이면 이 규칙을 끈다
    public int NeedShort { get; init; } = 3;               // 그 사이의 서로 다른 캡처 수
    public double GoneFor { get; init; } = 2.0;            // 점수로 볼 값이 이만큼(초) 내리 안 보이면 사라진 것으로 본다 (2초로 정함, 2026-11-14)
}

/// <summary>플레이 중 읽어 둔 값에서 얻은 검산 근거.</summary>
public sealed record PlayEvidence(int PlayReads, int NoteCount, int PlayValuesVerified, double Agree, int? LastPlayValue, double? LastPlayAt);

/// <summary>Basis: 값을 낸 근거. Stable = 같은 값이 StableFor 이어짐, Gone = 그 전에 점수가 사라져 직전 값을 냄. 값을 내지 않았으면 빈 문자열.</summary>
public sealed record JudgeResult(RegistrationState State, string Reason, int? Score, int? Candidate, double Elapsed,
                                 PlayEvidence Play, int FramesAfterRequest, int FramesUnread, string Basis = "");

/// <summary>플레이 중 낮은 빈도로 읽어 둔 값. 시간 순서대로 넣는다.</summary>
public sealed class PlayLog
{
    private readonly List<double> _at = new();
    private readonly List<int> _value = new();      // 또렷이 읽힌 양수 값, 아니면 -1
    private readonly double _every;
    private double _last = -1e9;

    public PlayLog(double every = 0.2) => _every = every;

    /// <summary>지금 읽어 둘 때가 됐는가(안 됐으면 읽지 않아도 된다).</summary>
    public bool Due(double at) => at - _last >= _every - 1e-6;

    /// <summary>고정 뒤의 화면 하나. 간격이 안 됐으면 버린다(읽었는지를 돌려준다).</summary>
    public bool Offer(double at, int value, bool clear)
    {
        if (at - _last < _every - 1e-6) return false;
        _last = at;
        _at.Add(at);
        _value.Add(clear && value > 0 ? value : -1);
        return true;
    }

    public void Clear() { _at.Clear(); _value.Clear(); _last = -1e9; }

    public PlayEvidence Evidence()
    {
        int reads = _value.Count(v => v > 0);
        int? fit = ScoreRules.FitNotes(_value.Where(v => v > 0));
        if (fit is null) return new PlayEvidence(reads, 0, 0, 0.0, null, null);
        int notes = ScoreRules.RefineNotes(_value, fit.Value);
        bool[] keep = ScoreRules.VerifySequence(_value, notes, tolerant: true);     // 증가폭이 점수 규칙을 벗어난 구간이 드물게 있어도 그것 하나로 나머지 값을 버리지 않는다
        var distinct = new HashSet<int>();
        int last = -1, kept = 0;
        for (int i = 0; i < keep.Length; i++)
            if (keep[i]) { distinct.Add(_value[i]); last = i; kept++; }
        double agree = (double)kept / Math.Max(1, reads);
        return last < 0 ? new PlayEvidence(reads, notes, distinct.Count, agree, null, null)
                        : new PlayEvidence(reads, notes, distinct.Count, agree, _value[last], _at[last]);
    }
}

/// <summary>요청 하나. 요청 뒤의 캡처를 시간 순서대로 넣다가 답이 나오면 받는다.</summary>
public sealed class RegistrationJudge
{
    private readonly JudgeOptions _options;
    private readonly double _pressedAt;
    private readonly PlayEvidence _play;
    private readonly string _blocked;
    private int? _runValue, _stable;
    private readonly List<double> _run = new();
    private double _stableAt;
    private int _seen, _unread, _zeros, _others;
    private bool _seenClear, _firstLocked;
    private int? _last;                         // 가장 최근에 이어졌던 점수로 볼 수 있는 같은 값
    private double _lastFirst, _lastAt;         // 그 값이 처음·마지막으로 읽힌 시각
    private int _lastCount;                     // 그 캡처 수
    private double? _noScoreSince;              // 점수로 볼 값이 안 보이기 시작한 시각
    private string _basis = "Stable";

    public RegistrationJudge(double pressedAt, PlayEvidence play, JudgeOptions? options = null)
    {
        _options = options ?? new JudgeOptions();
        _pressedAt = pressedAt;
        _play = play;
        _blocked = play.NoteCount == 0 || play.PlayValuesVerified < _options.MinPlayValues ? "NotEnoughPlayEvidence"
                 : play.Agree < _options.MinAgree ? "RuleConflict" : "";
    }

    public PlayEvidence Play => _play;
    /// <summary>플레이 중 근거가 모자라거나 규칙과 안 맞아 이 요청이 값을 낼 수 없으면 그 사유, 아니면 빈 문자열.</summary>
    public string Blocked => _blocked;

    /// <summary>0 이 아니고, 산식으로 나올 수 있고, 마지막 플레이 값보다 작지 않으며, 그 사이 시간에 오를 수 있는 만큼만 올랐다.</summary>
    public bool ValueOk(int value, double when)
    {
        if (value <= 0 || _play.NoteCount == 0 || _play.LastPlayValue is null) return false;
        long? m = ScoreRules.LatticeM(value, _play.NoteCount);
        if (m is null || value < _play.LastPlayValue) return false;
        long rise = m.Value - ScoreRules.LatticeM(_play.LastPlayValue.Value, _play.NoteCount)!.Value;
        return rise <= 100 * (_options.MaxNotesPerSecond * Math.Max(0.0, when - _play.LastPlayAt!.Value) + 2);
    }

    /// <summary>요청 뒤의 캡처 하나. value 는 또렷이 읽힌 값(0 포함), 못 읽었으면 음수. 답이 정해지면 돌려준다.</summary>
    public JudgeResult? Offer(double at, int value, bool locked)
    {
        if (_seen == 0) _firstLocked = locked;
        _seen++;
        int v = locked ? value : -1;
        if (v < 0) _unread++;
        else { _seenClear = true; if (v == 0) _zeros++; }
        if (v < 0 || !ValueOk(v, at))
        {
            if (v > 0) _others++;
            if (v >= 0) { _runValue = null; _run.Clear(); }      // 다른 것으로 읽힌 화면이 끼었다
            _noScoreSince ??= at;
            if (LastHolds() && at - _noScoreSince.Value >= _options.GoneFor - 1e-6)
            {
                _stable = _last; _stableAt = at; _basis = "Gone";          // StableFor 를 채우기 전에 점수가 사라졌다: 직전 값을 낸다
                return Finish(at, "");
            }
            return null;
        }
        _noScoreSince = null;
        if (v == _runValue) _run.Add(at);
        else { _runValue = v; _run.Clear(); _run.Add(at); }
        _last = _runValue; _lastFirst = _run[0]; _lastAt = at; _lastCount = _run.Count;
        if (_run.Count >= _options.Need && at - _run[0] >= _options.StableFor - 1e-6 && _stable != _runValue)
        {
            _stable = _runValue; _stableAt = at;
        }
        return _stable is not null ? Finish(_stableAt, "") : null;
    }

    /// <summary>기한이 됐다(더 받을 캡처가 없다).</summary>
    public JudgeResult Expire()
    {
        string why = _stable is not null ? "" : _seenClear && !(_zeros > 0 && _others == 0) ? "Timeout" : "";
        return Finish(_pressedAt + _options.Deadline, why);
    }

    /// <summary>사라지기 직전의 값을 낼 수 있는가: 가장 최근에 이어진 같은 값이 ShortFor 초 이상, 캡처 NeedShort 장 이상이었다.</summary>
    private bool LastHolds() => _options.ShortFor > 0 && _last is not null && _lastCount >= _options.NeedShort && _lastAt - _lastFirst >= _options.ShortFor - 1e-6;

    /// <summary>
    /// 더 받을 화면이 없다(화면이 바뀌었거나 앱이 그만 보낸다). 가진 것으로 판단한다. 요청 뒤 화면을 하나도 못 받았으면 사유를 noFrames 로 둔다.
    /// ended: 이 플레이의 화면이 끝났다고 앱이 알렸다(곡 정보가 바뀜). 점수가 사라진 것과 같으므로, 기다리지 않고 직전 값을 낼 수 있으면 낸다.
    /// </summary>
    public JudgeResult Close(double when, string noFrames = "NoFrames", bool ended = false)
    {
        if (_seen == 0)
            return new JudgeResult(RegistrationState.Failed, _blocked != "" ? _blocked : noFrames, null, null, when - _pressedAt, _play, 0, 0);
        if (ended && _stable is null && LastHolds()) { _stable = _last; _stableAt = when; _basis = "Gone"; }
        return Finish(when, "");
    }

    private JudgeResult Finish(double when, string whyNot)
    {
        string reason;
        RegistrationState state = RegistrationState.Failed;
        int? score = null;
        if (_seen == 0 || !_firstLocked) reason = "LocationUncertain";
        else if (_stable is null)
            reason = _blocked != "" ? _blocked : whyNot != "" ? whyNot        // 플레이 중 근거가 모자라면 그것이 먼저다
                   : !_seenClear ? "Occluded" : _zeros > 0 && _others == 0 ? "ZeroScore" : _seen < _options.Need ? "NotEnoughFrames" : "AmbiguousDigits";
        else if (_blocked != "" || whyNot != "") reason = _blocked != "" ? _blocked : whyNot;
        else { reason = ""; state = RegistrationState.Accepted; score = _stable; }
        return new JudgeResult(state, reason, score, _stable, when - _pressedAt, _play, _seen, _unread, state == RegistrationState.Accepted ? _basis : "");
    }
}
