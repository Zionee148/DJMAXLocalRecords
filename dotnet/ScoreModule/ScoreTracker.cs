// 한 판의 점수 자리를 찾아 고정하고, 고정한 뒤에도 영역을 다듬으며 그 자리를 읽는다. 화면(띠)을 시간 순서대로 받는다.
// Python 시제품의 locate_video(위치 찾기 → 영역 넓히기 → 움직이는 점수판 따라가기 → 못 읽으면 한 번 더 읽기)를 옮긴 것이다.
//
// 시제품과 다른 점(앞날을 볼 수 없기 때문에):
//   - 시제품은 녹화를 여러 번 훑는다(초당 10장으로 고정, 초당 2장으로 넓히기, 초당 5장으로 따라가기, 그리고 모든 프레임 읽기).
//     모듈은 한 줄기의 화면을 받으면서 지난 시각만 보고 그때그때 할 일을 정한다.
//   - 시제품은 "점수판이 위아래로 움직이는 기어인가"를 녹화 전체의 네모를 다 보고 정한 뒤 고정 시각부터 따라갔다.
//     모듈은 그때까지 본 네모만으로 정하고, 움직인다고 본 때부터 따라간다.
namespace DjmaxScore;

public sealed record TrackerOptions
{
    public double LocateEvery { get; init; } = 0.1;        // 고정 전: 이 간격으로 화면을 본다
    public int DetectEvery { get; init; } = 3;             // 고정 전: 본 화면 이만큼에 한 번 탐지 모델을 돌린다
    public int PoseBudget { get; init; } = 4;              // 고정 전: 기울여 읽어 보기를 화면마다 이만큼씩 (Locator)
    public int ColdEvery { get; init; } = 5;               // 고정 전: 값이 안 바뀌는 자리는 이만큼의 화면에 한 번만 (Locator)
    public double PartWait { get; init; } = 5.0;           // 고정 전: 더 긴 숫자열의 앞자리만 읽는 자리는 이만큼(초)까지 고정을 미룬다 (Locator)
    public double GrowEvery { get; init; } = 0.5;          // 고정 뒤: 영역을 넓힐지 보는 간격
    public double FollowEvery { get; init; } = 0.2;        // 고정 뒤: 움직이는 점수판을 따라가는 간격 (움직이는지 알아보는 동안에도 이 간격으로 본다)
    public double WatchMoving { get; init; } = 10.0;       // 고정 뒤 이만큼(초)은 움직이는지 알아보려고 FollowEvery 간격으로 탐지 모델을 돌린다
    public double Moving { get; init; } = 0.45;            // 잡힌 네모의 가운데 높이가 이만큼(네모 높이의 배율) 넘게 오르내리면 움직이는 점수판
    public int MovingSeen { get; init; } = 30;             // 그 판단에 필요한 네모의 수
    public double MaxGrow { get; init; } = 2.5;            // 영역의 너비는 고정 때의 이 배율까지만 넓힌다
    public bool ReadAgain { get; init; } = true;           // 점수로 볼 수 없는 값이 읽히면 여백을 바꿔 다시 읽고, 그래도 아니면 자리를 바로 확인한다
    public double CheckEvery { get; init; } = 0.03;        // 그렇게 자리를 바로 확인하는(탐지 모델을 돌리는) 간격의 아래 한도
    public double MaxNotesPerSecond { get; init; } = 50;   // 지난 값에서 그 사이 시간에 오를 수 있는 한도 (등록 판단과 같은 값)
}

public sealed class ScoreTracker
{
    private readonly RowFinder _finder;
    private readonly ScoreReader _reader;
    private readonly TrackerOptions _options;
    private readonly int _bandX0, _bandTop, _screenWidth;
    private readonly Box? _skip;                         // 점수 자리를 찾지 않는 곳(띠 좌표). 탐지 모델이 이 안에서 잡은 네모는 쓰지 않는다
    private readonly Locator _locator;
    private double _lastLocate = double.NegativeInfinity, _lastGrow = double.NegativeInfinity, _lastFinder = double.NegativeInfinity;
    private int _locateFrames;

    private Box _wide;                                   // 넓히기가 다듬는 영역(띠 좌표). 세로는 잡힌 네모들의 가운데 값
    private Box _area;                                   // 지금 읽는 영역(띠 좌표). 움직이는 점수판이면 세로가 _wide 와 다르다
    private double _firstWidth;
    private readonly List<double> _tops = new(), _bottoms = new();           // 넓히기가 받아들인 네모의 세로
    private readonly List<double> _seenTops = new(), _seenBottoms = new();   // 고정 뒤 영역 가까이에서 잡힌 모든 네모의 세로
    private Box? _waiting;
    private const double ShiftApart = 0.1;               // 지금 영역에서 세로로 이만큼(영역 높이의 배율) 넘게 떨어진 네모만 옮길 후보로 본다
    private bool _shifted;                               // 읽어서 확인하고 세로를 옮겨 둔 상태인가 (움직인다고 알아채기 전)
    private int _lastValue;                              // 지금까지 읽은 "점수로 볼 수 있는 값" 가운데 가장 최근 것
    private double _lastValueAt;
    private double _now;                                 // 가장 최근에 받은 화면의 시각 (Read 는 시각을 받지 않는다)
    private int _lowValue; private double _lowSince;     // 지난 값보다 작은데 자릿수가 같은 값이 이어서 읽히고 있다 (지난 값이 잘못 읽은 것이었을 수 있다)
    private readonly Queue<bool> _fits = new();          // 최근 판독이 점수로 볼 수 있는 값이었는가
    private int _fitCount;
    private const int TrustWindow = 30, TrustFrom = 10;
    private const double LowFor = 1.0;
    private static readonly (int X, int Y)[] MoreMargins = { (8, 4), (0, 0) };
    // 방금까지 잘 읽히다가 처음 막힌 화면에서, 자르는 자리(가로·세로로 민 양)와 여백을 조금씩 바꿔 다시 읽어 보는 조합 (아래 ReadOnce 의 4)
    private static readonly (int Dx, int Dy, int Mx, int My)[] Nudges =
    {
        (0, 0, 20, 4), (0, 0, 14, 2),
        (4, 0, 14, 8), (4, 0, 20, 4), (4, 0, 14, 2), (-4, 0, 14, 8), (-4, 0, 20, 4), (-4, 0, 14, 2),
        (0, 3, 14, 8), (0, 3, 20, 4), (0, 3, 14, 2), (0, -3, 14, 8), (0, -3, 20, 4), (0, -3, 14, 2),
    };
    private const int NudgeRun = 2;                      // 이 다시 읽기는 잘 읽히다가 막힌 뒤 잇달아 이만큼의 화면까지만 한다(가려진 화면이 이어질 때 판독이 몰리지 않게)
    private const double SureConfidence = 0.9;           // 새 값이 이보다 낮은 확신도로 읽히면 여백을 바꿔 한 번 더 읽어 본다 (ReadOnce 의 1)
    private const double LostFor = 0.5;                  // 움직이는 점수판: 잘 읽히다가 숫자가 안 읽히면 이만큼(초)까지는 자리를 바로 확인한다 (Read 의 3)
    private int _misses;                                 // 점수로 볼 수 있는 값을 못 읽은 화면이 잇달아 몇 장인가 (0 = 바로 앞의 화면은 읽었다)
    /// <summary>여백을 바꿔 읽은 값을 쓴 횟수(평소 여백의 값이 또렷했는데도 바꾼 것)와, 읽다가 자리를 바로 확인한 횟수.</summary>
    public long Reread { get; private set; }
    public long Checks { get; private set; }
    /// <summary>자르는 자리를 바꿔 읽어 지난 값과 같은 값을 얻은 횟수.</summary>
    public long Nudged { get; private set; }
    /// <summary>낮은 확신도로 읽힌 새 값 대신, 여백을 바꿔 또렷이 읽힌 지난 값을 쓴 횟수.</summary>
    public long Kept { get; private set; }
    /// <summary>읽어서 확인하고 영역의 세로를 옮긴 횟수.</summary>
    public int Shifts { get; private set; }

    public bool Locked { get; private set; }
    public double LockAt { get; private set; }
    public int Notes { get; private set; }
    public Pose? Pose { get; private set; }
    public bool IsMoving { get; private set; }
    public double MovingAt { get; private set; }
    /// <summary>영역이 바뀐 내역(시각, 화면 전체 좌표의 영역). 첫 항목이 고정 때의 영역이다.</summary>
    public List<(double At, Box Area)> Steps { get; } = new();
    /// <summary>고정 뒤 탐지 모델을 돌린 횟수와 영역을 견주려고 읽은 횟수(처리 시간을 가늠하려고 센다).</summary>
    public long FinderRuns { get; private set; }
    /// <summary>고정 전에, 조건을 채운 자리가 더 긴 숫자열의 앞자리로 읽혀 고정을 미룬 화면의 수(들여다보는 용도).</summary>
    public int LockHolds => _locator.Holds;
    public long CompareReads { get; private set; }

    /// <param name="skip">점수 자리를 찾지 않는 곳(화면 전체 좌표. ScoreModule.PanelZone). 고정 전의 후보에서도, 고정 뒤 영역을 다듬을 때에도 그 안의 네모는 쓰지 않는다.</param>
    public ScoreTracker(RowFinder finder, ScoreReader reader, int bandX0, int bandTop, TrackerOptions? options = null, int screenWidth = 1920, Box? skip = null)
    {
        _finder = finder; _reader = reader; _options = options ?? new TrackerOptions();
        _bandX0 = bandX0; _bandTop = bandTop; _screenWidth = screenWidth;
        _skip = skip?.Shift(-bandX0, -bandTop);
        _locator = new Locator(finder, reader) { PoseBudget = _options.PoseBudget, ColdEvery = _options.ColdEvery, PartWait = _options.PartWait, Skip = _skip };
    }

    /// <summary>지금 읽는 영역(화면 전체 좌표). 고정 전이면 null.</summary>
    public Box? Area => Locked ? _area.Shift(_bandX0, _bandTop) : null;

    /// <summary>화면(띠) 하나. 시각은 초 단위이고 늘어나기만 해야 한다. 고정 전에는 위치를 찾고, 고정 뒤에는 영역을 다듬는다.</summary>
    public void Observe(Picture band, double at)
    {
        if (!Locked)
        {
            if (at - _lastLocate < _options.LocateEvery - 1e-6) return;
            _lastLocate = at;
            LockResult? found = _locator.Offer(band, at, _locateFrames++ % _options.DetectEvery == 0);
            if (found is null) return;
            // 화면 밖으로 나간 가로는 화면 끝에서 자른다 (Python 의 lock_area)
            _wide = new Box(Math.Max(-_bandX0, found.Box.X0), found.Box.Y0, Math.Min(_screenWidth - _bandX0, found.Box.X1), found.Box.Y1);
            _area = _wide; _firstWidth = _wide.Width;
            _tops.Add(_wide.Y0); _bottoms.Add(_wide.Y1);
            Locked = true; LockAt = at; Notes = found.Notes; Pose = found.Pose;
            Steps.Add((at, _area.Shift(_bandX0, _bandTop)));
            _lastGrow = _lastFinder = double.NegativeInfinity;
            _now = at;
            return;
        }
        _now = at;
        if (Pose is not null) return;                    // 기울여 읽는 자리는 고정한 네모를 그대로 쓴다 (시제품과 같다)
        bool growDue = at - _lastGrow >= _options.GrowEvery - 1e-6;
        bool fast = IsMoving || at - LockAt < _options.WatchMoving;
        bool finderDue = growDue || (fast && at - _lastFinder >= _options.FollowEvery - 1e-6);
        if (finderDue) Refine(band, at, growDue);
    }

    /// <summary>탐지 모델을 돌려 영역을 다듬는다(넓히기, 움직이는지 알아보기, 따라가기).</summary>
    private void Refine(Picture band, double at, bool growDue)
    {
        _lastFinder = at;
        List<(Box Box, double Score)> rows = _finder.Find(band.Data, band.Height, band.Width);
        FinderRuns++;
        if (_skip is Box zone) rows.RemoveAll(row => row.Box.CenterIn(zone));

        // 영역 가까이에서 잡힌 네모: 지금 영역과 반 넘게 겹치고 높이가 비슷한 것 가운데 점수가 가장 높은 것
        Box? near = null;
        foreach (var (box, _) in rows)
            if (Box.Overlap(box, _wide) >= 0.5 && box.Height / _wide.Height is >= 0.6 and <= 1.6) { near = box; break; }
        if (near is Box seen && IsScoreRow(band, seen, at))
        {
            _seenTops.Add(seen.Y0); _seenBottoms.Add(seen.Y1);
            if (!IsMoving && _seenTops.Count >= _options.MovingSeen)
            {
                double[] centers = _seenTops.Zip(_seenBottoms, (t, b) => (t + b) / 2).ToArray();
                double height = Numbers.Median(_seenTops.Zip(_seenBottoms, (t, b) => b - t).ToArray());
                if (Numbers.Percentile(centers, 95) - Numbers.Percentile(centers, 5) >= _options.Moving * height) { IsMoving = true; MovingAt = at; }
            }
        }
        if (growDue)
        {
            _lastGrow = at;
            if (near is Box n && _waiting is Box w && Box.Iou(n, w) >= 0.7)
            {
                _tops.Add(n.Y0); _bottoms.Add(n.Y1);
                var grown = new Box(Math.Min(_wide.X0, n.X0), Numbers.Median(_tops.ToArray()), Math.Max(_wide.X1, n.X1), Numbers.Median(_bottoms.ToArray()));
                if (grown.Width <= _options.MaxGrow * _firstWidth && grown != _wide && BetterArea(band, _wide, grown)) _wide = grown;
            }
            _waiting = near;
        }
        Box next = _shifted ? new Box(_wide.X0, _area.Y0, _wide.X1, _area.Y1) : _wide;
        if (!IsMoving)
        {
            // 움직이는 점수판인지 아직 모를 때: 점수판이 자리를 옮긴 것 같으면 읽어서 확인하고 따라간다.
            // 지금 영역에서 세로로 떨어진 곳에 숫자 줄이 잡혔고, 지금 영역은 점수로 볼 수 있는 값이 안 읽히는데 그 자리는 또렷이 읽힐 때만 옮긴다.
            // "안 읽힘"만 보면 모자란다: 점수판이 반쯤 벗어나면 남은 자릿수만 또렷이 읽힌다("43107" 다음에 "3107", "820").
            // 그런 값은 지금까지 읽은 값보다 작으므로(점수는 한 판 안에서 줄지 않는다) 점수로 볼 수 없는 값으로 가려진다.
            // (움직인다고 알아채기까지 네모 30개, 6초쯤이 걸려서, 그 전에 점수판이 한 번 오르내리면 그동안 못 읽었다: welcome to the space LEFT 에서 63장)
            Box? away = null;
            foreach (var (box, _) in rows)
            {
                double across = (Math.Min(box.X1, _wide.X1) - Math.Max(box.X0, _wide.X0)) / Math.Max(1.0, box.Width);
                double apart = Math.Abs((box.Y0 + box.Y1) - (_area.Y0 + _area.Y1)) / 2;
                if (across < 0.6 || box.Height / _wide.Height is < 0.6 or > 1.6 || apart < ShiftApart * _wide.Height) continue;
                if (away is not Box a || apart < Math.Abs((a.Y0 + a.Y1) - (_area.Y0 + _area.Y1)) / 2) away = box;
            }
            if (away is Box to)
            {
                Reading here = _reader.Read(band.Part(_area));
                CompareReads++;
                if (!(here.Confidence >= ScoreReader.ReadConfidence && Plausible(here.Value, at)))
                {
                    var moved = new Box(_wide.X0, to.Y0, _wide.X1, to.Y1);
                    Reading there = _reader.Read(band.Part(moved));
                    CompareReads++;
                    if (there.Confidence >= ScoreReader.ReadConfidence && Plausible(there.Value, at))
                    {
                        next = moved; Shifts++;
                        _shifted = Math.Abs((moved.Y0 + moved.Y1) - (_wide.Y0 + _wide.Y1)) / 2 >= 0.1 * _wide.Height;
                    }
                }
            }
        }
        else
        {
            // 움직이는 점수판: 탐지 모델이 숫자 줄을 잡을 때마다 세로를 그 네모에 맞춘다. 가로는 넓히기가 정한 대로.
            // 그때까지 잡힌 네모들이 오르내린 범위에서 네모 높이의 반 넘게 벗어난 네모는 따르지 않는다.
            // 지금 영역에서 세로로 떨어진 네모는 그 자리를 읽어 점수로 볼 수 있는 값이 또렷이 나올 때만 따른다(IsScoreRow).
            double height = Numbers.Median(_seenTops.Zip(_seenBottoms, (t, b) => b - t).ToArray());
            double low = Numbers.Percentile(_seenTops, 2) - 0.5 * height, high = Numbers.Percentile(_seenBottoms, 98) + 0.5 * height;
            Box? best = null;
            foreach (var (box, _) in rows)
            {
                double across = (Math.Min(box.X1, _wide.X1) - Math.Max(box.X0, _wide.X0)) / Math.Max(1.0, box.Width);
                if (across < 0.6 || box.Height / height is < 0.6 or > 1.6 || box.Y0 < low || box.Y1 > high) continue;
                if (best is not Box b || Math.Abs((box.Y0 + box.Y1) - (_area.Y0 + _area.Y1)) < Math.Abs((b.Y0 + b.Y1) - (_area.Y0 + _area.Y1))) best = box;
            }
            next = best is Box follow && IsScoreRow(band, follow, at) ? new Box(_wide.X0, follow.Y0, _wide.X1, follow.Y1) : new Box(_wide.X0, _area.Y0, _wide.X1, _area.Y1);
        }
        if (next != _area)
        {
            _area = next;
            Steps.Add((at, _area.Shift(_bandX0, _bandTop)));
        }
    }

    /// <summary>
    /// 잡힌 네모가 점수 줄인가. 지금 영역과 거의 같은 높이면 그렇다고 본다. 세로로 떨어져 있으면 그 자리를 읽어
    /// 점수로 볼 수 있는 값이 또렷이 나올 때만 그렇다고 본다(최근 판독이 규칙에 잘 안 맞는 동안에는 또렷이 읽히기만 하면 된다).
    /// 까닭: 점수 줄 바로 아래에 숫자처럼 생긴 글꼴의 다른 줄이 있는 기어가 있다(EXTENSION 3 의 로고 줄). 탐지 모델이 가끔 그 줄을 잡는데,
    /// 읽어 보지 않고 쌓아 두면 한 세션이 길어질 때 "움직이는 점수판"으로 잘못 보고 그 줄을 따라가게 된다(같은 녹화를 두 번 이어 넣은 시험에서 드러남).
    /// </summary>
    private bool IsScoreRow(Picture band, Box box, double at)
    {
        double apart = Math.Abs((box.Y0 + box.Y1) - (_area.Y0 + _area.Y1)) / 2;
        if (apart < ShiftApart * _wide.Height) return true;
        Reading there = _reader.Read(band.Part(new Box(_wide.X0, box.Y0, _wide.X1, box.Y1)));
        CompareReads++;
        return Clear(there) && (Plausible(there.Value, at) || !Trusted);
    }

    /// <summary>
    /// 영역을 바꾸기 전에 두 영역을 같은 화면에서 읽어 본다. 고정 영역은 "읽힌 값이 점수 규칙대로 오른다"로 확인된 자리이므로
    /// 탐지 모델의 네모만 믿고 옮기지 않는다. 같은 값을 그만큼 또렷이 읽거나, 지금 읽은 숫자열이 새로 읽은 숫자열의 일부일 때만 바꾼다.
    /// </summary>
    private bool BetterArea(Picture band, Box current, Box proposed)
    {
        Reading a = _reader.Read(band.Part(current)), b = _reader.Read(band.Part(proposed));
        CompareReads += 2;
        if (a.Confidence < ScoreReader.ReadConfidence || b.Confidence < ScoreReader.ReadConfidence || a.Value <= 0 || b.Value <= 0) return false;
        if (a.Value == b.Value) return b.Confidence >= a.Confidence - 0.02;
        return b.Text.Length > a.Text.Length && b.Text.Contains(a.Text);
    }

    /// <summary>
    /// 지금 영역을 읽는다. 고정 전이면 null.
    ///   1) 숫자열이 안 나왔거나 확신도가 모자라면 여백을 좁혀 한 번 더 읽고, 그 판독이 또렷하면 그것을 쓴다(시제품과 같다).
    ///   2) 또렷이 읽혔어도 "점수로 볼 수 없는 값"이면 여백을 바꿔 가며 다시 읽고, 점수로 볼 수 있는 값이 또렷이 읽히면 그것을 쓴다.
    ///      (붙은 좁은 1 을 빠뜨리거나 끝자리를 두 번 읽는 것은 자른 여백에 따라 갈린다. 값을 만들어 내지 않는다: 쓰는 것은 언제나 화면에서 읽은 값이다.)
    ///   3) 그래도 아니면 점수판이 자리를 옮겼을 수 있으므로 탐지 모델을 기다리지 않고 바로 돌려 확인한다.
    ///      움직이는 점수판은 숫자가 아예 안 읽힐 때에도 그렇게 한다(잘 읽히다가 막힌 뒤 LostFor 초까지). 점수판이 0.2초 사이에 높이의 반쯤 뛰면
    ///      읽는 자리가 숫자의 아랫부분만 걸쳐서 몇 자리만 읽히거나 안 읽힌다(welcome to the space LEFT 의 7장, 사용자 판정 2026-11-14).
    /// 2)·3)은 최근 판독이 규칙에 잘 맞고 있을 때만 한다(노트 수가 어긋났거나 다른 화면이면 규칙으로 고르지 않는다).
    /// 값이 맞는지의 최종 판단은 여기서 하지 않는다(점수 규칙 검사와 등록 판단이 한다).
    /// </summary>
    public (Reading Reading, bool Retried)? Read(Picture band)
    {
        if (!Locked) return null;
        var (reading, retried, fit, clear) = ReadOnce(band);
        bool lost = !clear && IsMoving && _lastValue > 0 && _now - _lastValueAt <= LostFor + 1e-6;
        if (!fit && (clear || lost) && _options.ReadAgain && Pose is null && Trusted && _now - _lastFinder >= _options.CheckEvery - 1e-6)
        {
            Box before = _area;
            Checks++;
            Refine(band, _now, false);
            if (_area != before) (reading, retried, fit, clear) = ReadOnce(band);
        }
        if (fit || clear)
        {
            _fits.Enqueue(fit);
            if (fit) _fitCount++;
            if (_fits.Count > TrustWindow && _fits.Dequeue()) _fitCount--;
        }
        _misses = fit ? 0 : _misses + 1;
        return (reading, retried);
    }

    /// <summary>들여다보는 용도: 다시 읽기를 정하는 데 쓰는 속 상태를 글로 적은 것(ModuleCheck run --probe).</summary>
    internal string Inside => $"지난 값 {_lastValue}({_lastValueAt:F3}초), 잇달아 못 읽은 화면 {_misses}, 최근 판독 {_fitCount}/{_fits.Count} 맞음, 마지막 탐지 {_lastFinder:F3}초, 지금 {_now:F3}초";

    /// <summary>최근 판독의 반 이상이 점수로 볼 수 있는 값이었다.</summary>
    private bool Trusted => _fits.Count >= TrustFrom && _fitCount * 2 >= _fits.Count;

    private static bool Clear(Reading r) => r.Value > 0 && r.Confidence >= ScoreReader.ReadConfidence;

    private (Reading Reading, bool Retried, bool Fit, bool Clear) ReadOnce(Picture band)
    {
        Reading first = _reader.Read(band.Part(_area).Posed(Pose));
        bool firstClear = Clear(first);
        if (firstClear && Plausible(first.Value, _now))
        {
            // 새 값이 낮은 확신도로 읽혔다: 여백을 바꿔 한 번 더 읽어, 지난 값이 또렷이 읽히면 값이 바뀌지 않은 것으로 본다.
            // (한 자리를 잘못 읽은 값이 우연히 산식 위에 있으면 점수로 받아들여지고, 그 뒤로는 그 값이 "지난 값"이 되어 맞게 읽은 값을 밀어낸다:
            //  welcome to the space RIGHT 에서 453521 을 453621(확신도 0.52)로 읽은 화면. 값이 정말 바뀌었으면 화면에 지난 값이 없으므로 지난 값이 또렷이 읽힐 일이 거의 없다.)
            if (_options.ReadAgain && Trusted && _lastValue > 0 && first.Value != _lastValue && first.Confidence < SureConfidence)
            {
                Reading other = _reader.Read(band.Part(_area, Margin.RetryX, Margin.RetryY).Posed(Pose)) with { Contrast = first.Contrast };
                if (Clear(other) && other.Value == _lastValue) { Remember(other.Value); Kept++; return (other, true, true, true); }
            }
            Remember(first.Value);
            return (first, false, true, true);
        }
        if (firstClear) Lower(first);
        bool again = _options.ReadAgain && firstClear && Trusted;
        Reading second = default;
        if (!firstClear || again)
        {
            second = _reader.Read(band.Part(_area, Margin.RetryX, Margin.RetryY).Posed(Pose)) with { Contrast = first.Contrast };   // 빈 화면인지는 평소 여백으로 자른 그림으로 본다
            if (Clear(second) && Plausible(second.Value, _now))
            {
                Remember(second.Value);
                if (firstClear) Reread++;
                return (second, true, true, true);
            }
        }
        if (_options.ReadAgain && Trusted)
            foreach (var (mx, my) in MoreMargins)
            {
                Reading more = _reader.Read(band.Part(_area, mx, my).Posed(Pose)) with { Contrast = first.Contrast };
                if (Clear(more) && Plausible(more.Value, _now))
                {
                    Remember(more.Value);
                    if (firstClear) Reread++;
                    return (more, true, true, true);
                }
            }
        // 4) 바로 앞 화면까지(또는 그 앞 화면까지) 잘 읽히다가 막혔다: 자르는 자리를 조금씩 바꿔 읽어, 지난 값과 같은 값이 또렷이 읽히면 그것을 쓴다.
        //    처음에는 0.25초에 한 번만 했는데, 그 사이에 막힌 화면이 또 오면 읽힐 것을 못 읽었다(welcome to the space CENTER 의 4장, 사용자 판정 2026-11-14).
        //    그래서 시간 대신 잇달아 막힌 화면의 수로 묶는다(NudgeRun). 자리를 바로 확인해 영역이 바뀐 뒤 다시 읽을 때에도 한다.
        //    (앞뒤 화면은 어떤 여백으로도 또렷이 읽히는데 한 장만 흔들리는 화면이 있다: 값이 바뀌는 순간의 겹친 화면, 노트 이펙트가 스친 화면.
        //     그런 화면은 자르는 자리에 따라 판독이 갈린다. 지난 값과 같은 것만 받는다: 방금 또렷이 읽은 값이라 우연히 맞을 일이 가장 적다.
        //     값을 만들어 내지 않는다: 이 화면의 조각에서 판독기가 또렷이 읽은 값이다. 사용자 판정 2026-11-14 의 미통과 프레임 때문에 넣었다.)
        if (_options.ReadAgain && Trusted && _misses < NudgeRun && _lastValue > 0)
        {
            foreach (var (dx, dy, mx, my) in Nudges)
            {
                Box moved = new(_area.X0 + dx, _area.Y0 + dy, _area.X1 + dx, _area.Y1 + dy);
                Reading more = _reader.Read(band.Part(moved, mx, my).Posed(Pose)) with { Contrast = first.Contrast };
                if (Clear(more) && more.Value == _lastValue)
                {
                    Remember(more.Value);
                    Nudged++;
                    return (more, true, true, true);
                }
            }
        }
        // 점수로 볼 수 있는 값을 못 찾았다: 시제품과 같이 한다 (평소 여백의 판독이 또렷하면 그것, 아니면 좁힌 여백의 판독이 또렷할 때 그것)
        if (firstClear) return (first, false, false, true);
        if (Clear(second)) return (second, true, false, true);
        return (first, false, false, false);
    }

    /// <summary>
    /// 점수로 볼 수 있는 값인가: 고정 때 맞춘 노트 수(또는 그 두 배)의 산식에 있고, 지금까지 읽은 값보다 작지 않으며,
    /// 그 사이 시간에 오를 수 있는 만큼만 올랐다. (다시 읽을지·영역을 옮길지 정할 때만 쓴다)
    /// 두 배도 받는 까닭: 판정이 모두 10% 단위인 동안에는 노트 수의 절반도 똑같이 맞아서 고정 때 절반으로 맞춰지는 일이 많다(270개 중 225개).
    /// </summary>
    private bool Plausible(int value, double at)
    {
        if (value <= 0 || Notes <= 0 || value < _lastValue) return false;
        if (!ScoreRules.OnLattice(value, Notes) && !(Notes * 2 <= 4000 && ScoreRules.OnLattice(value, Notes * 2))) return false;
        if (_lastValue == 0) return true;
        return value - _lastValue <= (_options.MaxNotesPerSecond * Math.Max(0.0, at - _lastValueAt) + 2) * ScoreRules.MaxScore / Notes + 1;
    }

    /// <summary>지금 판에서 가장 최근에 읽은 "점수로 볼 수 있는 값"(없으면 0).</summary>
    public int LastValue => _lastValue;
    /// <summary>그 값을 읽은 시각.</summary>
    public double LastValueAt => _lastValueAt;

    /// <summary>같은 곡을 다시 시작했다(판단은 모듈이 한다): 점수 이력을 새 판의 값으로 바꾼다. 자리와 영역은 그대로 둔다.</summary>
    public void StartOver(int value, double at)
    {
        _lastValue = value; _lastValueAt = at; _lowValue = 0;
    }

    private void Remember(int value)
    {
        _lastValue = value; _lastValueAt = _now; _lowValue = 0;
    }

    /// <summary>
    /// 지난 값보다 작은 값이 또렷이 읽혔다. 자릿수가 같고 산식에 있는 같은 값이 LowFor 초 이어지면 지난 값이 잘못 읽은 것이었다고 보고 이 값으로 되돌린다
    /// (그러지 않으면 한 번의 오독이 그 뒤의 모든 값을 "줄어든 값"으로 만든다). 자릿수가 준 값은 점수판이 반쯤 벗어난 것일 수 있어 받지 않는다.
    /// </summary>
    private void Lower(Reading r)
    {
        bool same = r.Value < _lastValue && r.Text.Length == _lastValue.ToString().Length
                    && (ScoreRules.OnLattice(r.Value, Notes) || (Notes * 2 <= 4000 && ScoreRules.OnLattice(r.Value, Notes * 2)));
        if (!same) { _lowValue = 0; return; }
        if (r.Value != _lowValue) { _lowValue = r.Value; _lowSince = _now; return; }
        if (_now - _lowSince >= LowFor - 1e-6) Remember(r.Value);
    }
}
