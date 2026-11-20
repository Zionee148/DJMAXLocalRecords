// 위치 찾기: 띠 화면을 차례로 보며 점수 숫자 줄을 찾아 고정한다. Python 의 locate_run.locate 를 옮긴 것이다.
//
//   1) 탐지 모델이 낸 검출 영역(Box)을, 여러 프레임에서 같은 위치에 검출된 것끼리 하나의 추적 후보(Track)로 묶는다.
//   2) 추적 후보마다 그 자리를 판독기로 읽어 값을 쌓는다.
//   3) 쌓인 값이 어떤 노트 수의 점수 규칙대로 오르면(규칙 통과한 서로 다른 값 LockValues 개) 그 추적 후보를 고정한다.
//      콤보·배속·장식 숫자는 여기서 떨어진다. 기어 종류·좌표를 미리 알지 못한다.
//   4) 2초 넘게 지켜봤는데 거의 읽히지 않는 자리는 기운 줄일 수 있다. 여러 자세로 읽어 보고 훨씬 또렷이 읽히는 자세가 있으면
//      그 자세로 읽는 하위 추적 후보(Track.Child)를 둔다. 하위 추적 후보도 점수 규칙을 통과해야만 고정된다.
//   5) (모듈에서만. PartWait) 조건을 채운 추적 후보가 같은 줄의 다른 추적 후보가 읽는 더 긴 숫자열의 앞자리만 읽고 있으면, 그 다른 추적 후보가 조건을 채울 때까지 고정을 잠깐 미룬다.
namespace DjmaxScore;

/// <summary>고정 결과. Box 는 받은 그림(띠)의 좌표다.</summary>
public sealed record LockResult(Box Box, double At, int Notes, Pose? Pose, int Tracks);

public sealed class Locator
{
    public const int LockValues = 12;         // 규칙대로 오른 서로 다른 값이 이만큼 모이면 고정
    public const double KeepAlive = 3.0;      // 이만큼(초) 안 잡힌 추적 후보는 읽지 않는다

    private sealed class Track
    {
        public Track Root;
        public Pose? Pose;
        public readonly List<int> Values = new();
        public List<Box> Boxes = new();
        public double LastSeen, FirstSeen;
        public List<Picture> Parts = new();
        public double PoseChecked = -100.0;
        public int Slot;                                 // 안 읽히는 자리를 띄엄띄엄 읽을 때 서로 엇갈리게 하는 번호
        public int PoseTries, PoseFails;                 // 기울여 읽어 본 횟수, 그 가운데 아무 자세로도 못 읽은 횟수(잇달아)
        public string LastText = "";                     // 가장 최근에 읽은 값을 숫자열로 적은 것(또렷이 읽혔을 때만. 아니면 빈 글자)
        public int LastRead = -1;                        // 그것을 읽은 화면의 번호
        public double? ReadyAt;                          // 고정 조건을 처음 채운 때
        public double PartAt = double.NegativeInfinity;  // 다른 추적 후보가 읽는 더 긴 숫자열의 앞자리로 읽힌 가장 최근의 때
        public Track? Child;

        public Track(Box box, double at, int slot) { Root = this; Boxes.Add(box); LastSeen = at; FirstSeen = at; Slot = slot; }
        public Track(Track parent, Pose pose) { Root = parent; Pose = pose; }

        /// <summary>최근 검출 영역 15개의 가운데 값.</summary>
        public Box Box
        {
            get
            {
                var recent = Root.Boxes.Skip(Math.Max(0, Root.Boxes.Count - 15)).ToArray();
                return new Box(Numbers.Median(recent.Select(b => b.X0).ToArray()), Numbers.Median(recent.Select(b => b.Y0).ToArray()),
                               Numbers.Median(recent.Select(b => b.X1).ToArray()), Numbers.Median(recent.Select(b => b.Y1).ToArray()));
            }
        }
    }

    private readonly RowFinder _finder;
    private readonly ScoreReader _reader;
    private readonly List<Track> _tracks = new();
    private int _frame;

    /// <summary>기울여 읽어 보는 데 쓴 판독 횟수(처리 시간을 가늠하려고 센다).</summary>
    public long PoseReads { get; private set; }
    public long Reads { get; private set; }
    public int Tracks => _tracks.Count;

    /// <summary>
    /// 기울여 읽어 보기에 화면 한 장마다 쓸 수 있는 판독 횟수. 0 이면 한꺼번에 다 한다(Python 시제품과 같다. 대조용).
    ///
    /// 시제품은 안 읽히는 자리마다 3초에 한 번, 자세 31가지 x 조각 8장 = 248번을 한꺼번에 읽는다. 그래픽 카드로 묶어 읽을 때는
    /// 드러나지 않았지만 CPU 에서는 한 번에 1초가 넘고, 270개 녹화에서 판독의 86% 가 이 검사였다. 실시간으로는 쓸 수 없다.
    /// 그래서 모듈은 이렇게 한다:
    ///   - 한 번에 추적 후보 하나만 검사하고, 화면 한 장마다 PoseBudget 번만 읽는다(처리 시간의 상한).
    ///   - 먼저 모든 자세를 최근 조각 PoseQuick 장으로만 훑고(31 x 2 = 62번), 하나라도 또렷이 읽힌 자세 가운데 위쪽 PoseFinalists 개와
    ///     "그대로 읽기"만 나머지 조각까지 읽는다(많아야 4 x 4 = 16번). 고르는 규칙은 시제품과 같다(조각의 75% 이상이 또렷, 확신도가 가장 높은 것).
    ///   - 아무 자세로도 안 읽힌 자리(장식, 글자 줄)는 다시 볼 때까지의 간격을 두 배씩 늘린다(3, 6, 12, 24초).
    ///   - 한 번도 안 본 자리를 먼저, 그 가운데서는 가장 꾸준히 잡힌 자리를 먼저 본다.
    /// </summary>
    public int PoseBudget { get; init; } = 0;
    public const int PoseParts = 6;            // 한 번 검사에 쓰는 조각 수 (시제품은 모아 둔 조각 전부, 많아야 8장)
    public const int PoseQuick = 2;            // 먼저 모든 자세를 훑을 때 쓰는 조각 수
    public const int PoseFinalists = 3;        // 나머지 조각까지 읽어 볼 자세의 수
    public const double PoseBackoffMax = 24.0; // 안 읽힌 자리를 다시 보는 간격의 상한(초)

    private sealed class PoseJob
    {
        public required Track Track;
        public required List<Picture> Parts;             // 최근 조각이 앞
        public required List<(int Option, int Part)> Todo;
        public int Next;
        public bool Refining;
        public required int[] Good;
        public required double[] Sum;
        public required int[] Count;
    }

    private PoseJob? _job;

    /// <summary>
    /// 안 읽히는 자리를 몇 장에 한 번만 읽을지. 1 이면 모든 자리를 화면마다 읽는다(Python 시제품과 같다. 대조용).
    ///
    /// 시제품은 3초 안에 잡힌 모든 자리를 화면마다(초당 10번) 읽는다. 녹화 270개에서 고정 전에 화면 한 장에 가운데 9번, 많은 사례는 20번을 읽었다
    /// (한 번에 4ms 쯤이라 스레드 하나로는 0.1초 안에 못 끝나는 화면이 생긴다). 그 자리들의 대부분은 장식이나 글자 줄이다.
    /// 그래서 최근 20번 읽어 한 번도 숫자가 안 나온 자리는 ColdEvery 장에 한 번만 읽는다. 숫자가 한 번이라도 읽히면 다시 화면마다 읽는다
    /// (곡이 시작되기 전의 점수 자리는 "0" 이라 안 읽힌 것으로 치는데, 점수가 오르기 시작하면 늦어도 ColdEvery 장 안에 되돌아온다).
    /// </summary>
    public int ColdEvery { get; init; } = 1;
    public const int ColdAfter = 10;           // 최근 이만큼 읽는 동안 값이 바뀐 적이 없으면(숫자가 안 나왔거나 한 가지 값뿐이면) 띄엄띄엄 읽는다
    private int _slots;

    /// <summary>
    /// 고정 조건을 채운 추적 후보가 "같은 줄의 다른 추적 후보가 읽는 더 긴 숫자열의 앞자리"만 읽고 있을 때, 그 다른 추적 후보가 조건을 채우기를 기다려 주는 시간(초).
    /// 0 이면 기다리지 않는다(Python 시제품과 같다. 대조용).
    ///
    /// 까닭: 탐지 모델이 "SCORE" 글자와 점수의 앞자리를 한 검출 영역으로 잡는 기어가 있다(SUMMER. 곡이 시작되기 전 점수가 0 일 때 "SCORE 0" 을 한 검출 영역으로 잡고, 첫 노트부터는 숫자만 잡는다).
    /// 그 검출 영역의 추적 후보는 점수의 앞 세 자리만 읽는데("25062" 를 "250"), 뒤의 두 자리가 잘린 값도 점수 규칙을 통과한다:
    /// 모든 판정이 100% 이면 floor(점수 / 100) 은 "모든 판정이 1% 인 판"의 점수와 같은 꼴이다. 그 추적 후보는 마지막으로 잡힌 뒤 KeepAlive 동안 더 읽히므로,
    /// 숫자만 잡은 올바른 추적 후보와 같은 노트를 보며 거의 같은 때에 조건을 채우고, 먼저 채운 쪽이 고정됐다(마지막 영상 시험의 앱 경로에서 SUMMER LEFT 가 앞자리만 읽는 추적 후보에 고정돼 등록을 못 했다).
    /// 앞자리만 읽는 추적 후보는 값만으로는 가려낼 수 없으므로 화면에서 읽은 것으로 가린다: 같은 화면에서 같은 줄의 겹치는 다른 추적 후보가
    /// 이 추적 후보의 숫자열로 시작하는 더 긴 숫자열을 또렷이 읽고 있으면 이 추적 후보는 앞자리만 보고 있는 것이다.
    /// 앞자리만 견준다: 뒤가 잘린 값만 점수 규칙을 통과한다(앞이 잘린 값은 오르내려서 통과하지 못한다). 그래야 올바른 추적 후보가
    /// "글자까지 숫자로 읽은 더 넓은 추적 후보"(올바른 숫자열 앞에 숫자가 더 붙는다) 때문에 미뤄지는 일도 없다.
    /// 끝없이 미루지는 않는다(더 넓은 추적 후보가 옆의 다른 숫자까지 읽어서 끝내 규칙을 통과하지 못할 수도 있다): 조건을 처음 채운 때부터 이만큼만 미룬다.
    /// </summary>
    public double PartWait { get; init; } = 0;

    /// <summary>
    /// 점수 자리를 찾지 않는 곳(받는 그림의 좌표). 없으면 그림 전체에서 찾는다(Python 시제품과 같다. 대조용).
    /// 추적 후보의 영역(최근 검출 영역들의 가운데 값)의 가운데가 이 안에 있는 동안 그 추적 후보는 읽지 않고, 기울여 읽어 보지 않고, 고정하지 않는다.
    /// 추적 후보 자체는 전과 같이 만든다: 다른 추적 후보의 번호(띄엄띄엄 읽는 차례)가 이 설정 때문에 달라지지 않게 하려는 것이다.
    /// </summary>
    public Box? Skip { get; init; }
    public const double PartSeen = 1.0;       // 앞자리로 읽힌 것이 이만큼(초) 안에 본 일이어야 미룬다

    /// <summary>방금 넣은 화면에서 한 판독 횟수(그대로 읽기, 기울여 읽어 보기). 처리 시간을 가늠하려고 둔다.</summary>
    public int LastReads { get; private set; }
    public int LastPoseReads { get; private set; }

    public Locator(RowFinder finder, ScoreReader reader) { _finder = finder; _reader = reader; }

    private static bool WantsPoseCheck(Track t, double at)
    {
        int recent = t.Values.Skip(Math.Max(0, t.Values.Count - 20)).Count(v => v > 0);
        return !(t.Values.Count < 20 || t.Parts.Count < 6 || at - t.PoseChecked < 3.0 || recent >= 8);
    }

    private void AdoptPose(Track t, double[] clear, double[] sure)
    {
        // 또렷이 읽히는 자세가 여럿이면 확신도가 가장 높은 것을 고른다
        int best = 0;
        for (int j = 1; j < clear.Length; j++)
        {
            bool a = clear[j] >= 0.75, b = clear[best] >= 0.75;
            if ((a && !b) || (a == b && sure[j] > sure[best])) best = j;
        }
        if (best != 0 && clear[best] >= 0.75 && clear[0] <= 0.4 && (t.Child is null || t.Child.Pose != Pose.Candidates[best - 1]))
        {
            if (t.Child is not null) _tracks.Remove(t.Child);
            t.Child = new Track(t, Pose.Candidates[best - 1]);
            _tracks.Add(t.Child);
        }
    }

    /// <summary>
    /// 띄엄띄엄 읽을 자리인가. 처음 잡힌 화면에서는 한 번 읽는다. 그 뒤로 숫자가 한 번도 안 나온 자리와,
    /// 최근 ColdAfter 번 읽는 동안 값이 바뀐 적이 없는 자리가 그렇다.
    /// "한 번만 잡힌 자리는 아예 읽지 않기"도 해 봤는데, 탐지 모델이 숫자 줄을 드물게만 잡는 기어(EXTENSION 3)에서
    /// 읽을 기회가 줄어 CENTER 의 고정이 17.8초 늦어지고 한 사례는 45초 안에 고정하지 못했다.
    /// </summary>
    private static bool IsCold(Track t)
    {
        if (t.Values.Count == 0) return false;
        if (t.Values.Count < ColdAfter) return !t.Values.Any(v => v > 0);
        return t.Values.Skip(t.Values.Count - ColdAfter).Where(v => v > 0).Distinct().Count() <= 1;
    }

    /// <summary>나눠서 하는 방식에서의 "기울여 읽어 볼 자리인가". 띄엄띄엄 읽는 자리는 읽은 횟수가 천천히 쌓이므로 읽은 횟수 대신 지켜본 시간으로 본다.</summary>
    private bool WantsPoseStep(Track t, double at)
    {
        int recent = t.Values.Skip(Math.Max(0, t.Values.Count - 20)).Count(v => v > 0);
        return at - t.FirstSeen >= 2.0 && t.Parts.Count >= 6 && recent < 8 && at - t.PoseChecked >= PoseInterval(t);
    }

    private double PoseInterval(Track t) => Math.Min(PoseBackoffMax, 3.0 * Math.Pow(2, t.PoseFails));

    /// <summary>기울여 읽어 보기를 화면마다 조금씩 한다.</summary>
    private void PoseStep(List<Track> live, double at)
    {
        if (_job is not null && !live.Contains(_job.Track)) _job = null;          // 그 자리가 더는 잡히지 않는다
        if (_job is null)
        {
            Track? pick = live.Where(t => t.Root == t && WantsPoseStep(t, at))
                              .OrderBy(t => t.PoseTries).ThenByDescending(t => t.Boxes.Count).FirstOrDefault();
            if (pick is null) return;
            pick.PoseChecked = at;
            int height = pick.Parts[^1].Height, width = pick.Parts[^1].Width;
            if (height < 8 || width < 8) return;
            List<Picture> parts = pick.Parts.Where(p => !p.Empty).TakeLast(PoseParts).Select(p => p.Resized(height, width)).Reverse().ToList();
            if (parts.Count < PoseParts) return;
            int options = Pose.Candidates.Length + 1;
            var todo = new List<(int, int)>();
            for (int part = 0; part < PoseQuick; part++)
                for (int j = 0; j < options; j++) todo.Add((j, part));
            _job = new PoseJob { Track = pick, Parts = parts, Todo = todo, Good = new int[options], Sum = new double[options], Count = new int[options] };
        }
        for (int k = 0; k < PoseBudget && _job.Next < _job.Todo.Count; k++, _job.Next++)
        {
            var (j, part) = _job.Todo[_job.Next];
            Reading r = _reader.Read(_job.Parts[part].Posed(j == 0 ? null : Pose.Candidates[j - 1]));
            PoseReads++; LastPoseReads++;
            _job.Count[j]++;
            if (r.Value > 0 && r.Confidence >= 0.8) _job.Good[j]++;
            _job.Sum[j] += r.Value > 0 ? r.Confidence : 0.0;
        }
        if (_job.Next < _job.Todo.Count) return;
        Track track = _job.Track;
        int n = _job.Parts.Count;
        if (!_job.Refining)
        {
            // 훑어본 결과: 하나라도 또렷이 읽힌 자세만 남긴다
            List<int> finalists = Enumerable.Range(1, Pose.Candidates.Length).Where(j => _job.Good[j] >= 1)
                                            .OrderByDescending(j => _job.Good[j]).ThenByDescending(j => _job.Sum[j]).Take(PoseFinalists).ToList();
            if (finalists.Count == 0)
            {
                track.PoseTries++; track.PoseFails++; track.PoseChecked = at; _job = null;
                return;
            }
            _job.Refining = true; _job.Next = 0; _job.Todo.Clear();
            foreach (int j in finalists.Prepend(0))
                for (int part = PoseQuick; part < n; part++) _job.Todo.Add((j, part));
            return;
        }
        var clear = new double[_job.Good.Length]; var sure = new double[_job.Good.Length];
        for (int j = 0; j < clear.Length; j++)
            if (_job.Count[j] == n) { clear[j] = (double)_job.Good[j] / n; sure[j] = _job.Sum[j] / n; }
        Track? before = track.Child;
        AdoptPose(track, clear, sure);
        track.PoseTries++; track.PoseChecked = at;
        track.PoseFails = track.Child is not null && (track.Child != before || track.Child.Pose is not null) ? 0 : track.PoseFails + 1;
        _job = null;
    }

    /// <summary>
    /// 이 화면에서 이 추적 후보가 "같은 줄의 겹치는 다른 추적 후보가 읽은 더 긴 숫자열의 앞자리"를 읽었으면 그때를 적어 둔다(PartWait 의 설명).
    /// 두 추적 후보 모두 이 화면에서 또렷이 읽혔을 때만 견준다(다른 화면의 값은 점수가 그새 올라서 견줄 수 없다).
    /// </summary>
    private static void NotePart(Track t, List<Track> live, int frame, double at)
    {
        if (t.LastRead != frame || t.LastText.Length == 0) return;
        Box mine = t.Box;
        foreach (Track u in live)
        {
            if (u.Root == t.Root || u.LastRead != frame || u.LastText.Length <= t.LastText.Length || !u.LastText.StartsWith(t.LastText, StringComparison.Ordinal)) continue;
            Box other = u.Box;
            if (Math.Min(mine.X1, other.X1) <= Math.Max(mine.X0, other.X0)) continue;                                               // 가로로 겹치지 않는다
            if (Math.Abs((mine.Y0 + mine.Y1) - (other.Y0 + other.Y1)) / 2 >= 0.5 * Math.Min(mine.Height, other.Height)) continue;  // 같은 줄이 아니다
            t.PartAt = at;
            return;
        }
    }

    /// <summary>조건을 채운 추적 후보의 고정을 미룰 것인가: 최근 PartSeen 초 안에 더 긴 숫자열의 앞자리로 읽혔고, 조건을 처음 채운 지 PartWait 초가 안 됐다.</summary>
    private bool Held(Track t, double at)
    {
        t.ReadyAt ??= at;
        bool held = at - t.ReadyAt.Value < PartWait && at - t.PartAt <= PartSeen;
        if (held) Holds++;
        return held;
    }

    /// <summary>조건을 채운 추적 후보의 고정을 미룬 횟수(화면 수. 들여다보는 용도).</summary>
    public int Holds { get; private set; }

    /// <summary>추적 후보마다 무엇이 읽혔는지(들여다보는 용도): 영역, 잡힌 횟수, 읽은 횟수, 숫자가 나온 횟수, 서로 다른 값의 수.</summary>
    public IEnumerable<string> Describe() =>
        _tracks.OrderByDescending(t => t.Values.Count).Select(t =>
            $"[{t.Box.X0:F0},{t.Box.Y0:F0},{t.Box.X1:F0},{t.Box.Y1:F0}] 잡힘 {t.Root.Boxes.Count} 읽음 {t.Values.Count} 숫자 {t.Values.Count(v => v > 0)} " +
            $"서로 다른 값 {t.Values.Where(v => v > 0).Distinct().Count()}{(t.Pose is null ? "" : $" 자세 {t.Pose.Value.Angle}")} 끝 값 {(t.Values.Count > 0 ? t.Values[^1] : 0)}");

    /// <summary>띠 화면 하나. detect 면 탐지 모델도 돌린다. 고정되면 결과를 돌려준다.</summary>
    public LockResult? Offer(Picture frame, double at, bool detect)
    {
        int i = _frame++;
        LastReads = 0; LastPoseReads = 0;
        if (detect)
        {
            foreach (var (box, _) in _finder.Find(frame.Data, frame.Height, frame.Width))
            {
                if (box.Width < 8 || box.Height < 6) continue;
                Track? best = null; double bestIou = double.NegativeInfinity;
                foreach (Track t in _tracks)
                {
                    if (t.Root != t) continue;
                    double v = Box.Iou(t.Box, box);
                    if (v > bestIou) { bestIou = v; best = t; }
                }
                if (best is not null && bestIou >= 0.4) { best.Boxes.Add(box); best.LastSeen = at; }
                else _tracks.Add(new Track(box, at, _slots++));
            }
        }
        List<Track> live = _tracks.Where(t => at - t.Root.LastSeen <= KeepAlive && !(Skip is Box zone && t.Box.CenterIn(zone))).ToList();
        if (live.Count == 0) return null;
        foreach (Track t in live)
        {
            Picture part = frame.Part(t.Box);
            bool cold = ColdEvery > 1 && IsCold(t);
            if (!cold || (i + t.Root.Slot) % ColdEvery == 0)
            {
                Reading r = _reader.Read(part.Posed(t.Pose));
                Reads++; LastReads++;
                bool clear = r.Value > 0 && r.Confidence >= ScoreReader.ReadConfidence && r.Contrast >= ScoreReader.BlankStd;
                t.Values.Add(clear ? r.Value : -1);
                t.LastText = clear ? r.Value.ToString() : ""; t.LastRead = i;
            }
            if (i % 3 == 0 && t.Root == t)
            {
                t.Parts.Add(part);
                if (t.Parts.Count > 8) t.Parts.RemoveAt(0);
            }
        }
        if (PartWait > 0)
            foreach (Track t in live) NotePart(t, live, i, at);
        if (PoseBudget > 0) PoseStep(live, at);
        else
            foreach (Track t in live.Where(t => t.Root == t).ToList())
            {
                if (!WantsPoseCheck(t, at)) continue;
                t.PoseChecked = at;
                int height = t.Parts[^1].Height, width = t.Parts[^1].Width;
                if (height < 8 || width < 8) continue;
                List<Picture> same = t.Parts.Where(p => !p.Empty).Select(p => p.Resized(height, width)).ToList();
                if (same.Count < 6) continue;
                int n = same.Count, options = Pose.Candidates.Length + 1;
                var clear = new double[options]; var sure = new double[options];
                for (int j = 0; j < options; j++)
                {
                    Pose? pose = j == 0 ? null : Pose.Candidates[j - 1];
                    int good = 0; double sum = 0;
                    foreach (Picture p in same)
                    {
                        Reading r = _reader.Read(p.Posed(pose));
                        PoseReads++;
                        if (r.Value > 0 && r.Confidence >= 0.8) good++;
                        sum += r.Value > 0 ? r.Confidence : 0.0;
                    }
                    clear[j] = (double)good / n; sure[j] = sum / n;
                }
                AdoptPose(t, clear, sure);
            }
        (int Top, Track Track, int Notes)? pick = null;
        bool pickStraight = false;
        foreach (Track t in live)
        {
            var seen = new HashSet<int>(t.Values.Where(v => v > 0));
            if (seen.Count < 15) continue;
            int? fit = ScoreRules.FitNotes(seen);
            if (fit is null) continue;
            bool[] keep = ScoreRules.VerifySequence(t.Values, fit.Value);
            var good = new HashSet<int>();
            for (int k = 0; k < keep.Length; k++)
                if (keep[k]) good.Add(t.Values[k]);
            if (good.Count < LockValues) continue;
            if (PartWait > 0 && Held(t, at)) continue;
            // 바로 읽히는 추적 후보가 조건을 채웠으면 기울여 읽는 하위 추적 후보보다 그것을 먼저 쓴다
            bool straight = t.Pose is null;
            int top = good.Max();
            if (pick is null || (straight && !pickStraight) || (straight == pickStraight && top > pick.Value.Top))
            {
                pick = (top, t, fit.Value); pickStraight = straight;
            }
        }
        return pick is null ? null : new LockResult(pick.Value.Track.Box, at, PoseBudget > 0 || ColdEvery > 1 ? ScoreRules.RefineNotes(pick.Value.Track.Values, pick.Value.Notes) : pick.Value.Notes, pick.Value.Track.Pose, _tracks.Count);
    }
}
