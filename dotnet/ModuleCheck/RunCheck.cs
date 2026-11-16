// 녹화의 화면을 처음부터 끝까지 .NET 모듈(ScoreTracker)에 흘려 넣고, 프레임마다의 판독을 파일로 남긴다.
// 그 파일을 Python(detector.import_net)이 시제품과 같은 모양(npz)으로 바꿔, 시제품에 쓰던 채점 도구로 그대로 채점한다.
//
// 넣는 것: 원본 녹화에서 뽑은 화면 아래쪽 띠의 모든 프레임(초당 60장). 모듈은 지난 시각만 보고 그때그때 할 일을 정한다.
// 읽는 것: 고정된 뒤의 모든 프레임(채점하려는 것이다. 실제 모듈은 플레이 중에 낮은 빈도로만 읽는다).
//
// 실행: dotnet run -c Release -- run <finder.onnx> <reader.onnx> <cases.tsv> <결과 폴더> [--workers N] [--every N] [사례 이름에 들어 있는 글자 …]
//   --no-skip   래더 정보판 자리(ScoreModule.PanelZone)를 빼지 않는다(빼기 전의 동작을 다시 보는 대조용)
//   --probe 프레임,프레임,…   그 프레임(1부터 센다)에서 모듈이 쓰던 영역을 여러 자리·여백으로 잘라 읽은 것을 찍는다(들여다보는 용도. 결과에는 영향이 없다)
// 결과: <결과 폴더>\<사례>.bin (프레임별 판독), <사례>.info.json (고정 시각, 자세, 영역 내역, 처리 시간)
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using DjmaxScore;

public static class RunCheck
{
    public const double VideoFps = 60.0;
    /// <summary>--no-skip: 래더 정보판 자리를 빼지 않고 돌린다(빼기 전의 동작을 다시 보는 대조용. 모듈은 언제나 뺀다).</summary>
    public static bool NoSkip;

    public static void Run(string finderPath, string readerPath, string casesPath, string outDir, string[] rest)
    {
        int workers = 1, every = 1;
        var filters = new List<string>();
        var probe = new HashSet<int>();
        for (int k = 0; k < rest.Length; k++)
            if (rest[k] == "--workers") workers = int.Parse(rest[++k]);
            else if (rest[k] == "--probe") probe.UnionWith(rest[++k].Split(',').Select(int.Parse));
            else if (rest[k] == "--every") every = int.Parse(rest[++k]);
            else if (rest[k] == "--no-skip") NoSkip = true;
            else filters.Add(rest[k]);
        Directory.CreateDirectory(outDir);
        List<CaseInfo> cases = CaseInfo.Load(casesPath).Where(c => filters.Count == 0 || filters.Any(c.Id.Contains)).Where((c, k) => k % every == 0)
                                       .Where(c => !File.Exists(Path.Combine(outDir, c.Id + ".info.json"))).ToList();     // 이미 돌린 사례는 건너뛴다
        int done = 0;
        var gate = new object();
        var watch = Stopwatch.StartNew();
        Parallel.For(0, cases.Count, new ParallelOptions { MaxDegreeOfParallelism = workers }, () => (new RowFinder(finderPath), new ScoreReader(readerPath)), (k, _, models) =>
        {
            CaseInfo c = cases[k];
            var tracker = new ScoreTracker(models.Item1, models.Item2, c.BandX0, CaseInfo.Top, skip: NoSkip ? null : ScoreModule.PanelZone(c.Gear));
            var values = new List<int>(); var conf = new List<float>(); var contrast = new List<float>();
            var locked = new List<byte>(); var second = new List<byte>(); var texts = new List<string>();
            var times = new List<double>();
            int n = 0;
            foreach (Picture band in Video.Bands(c, c.End - c.Start, 0))
            {
                double at = n / VideoFps;
                string inside = probe.Contains(n + 1) ? tracker.Inside : "";
                var timer = Stopwatch.StartNew();
                tracker.Observe(band, at);
                var read = tracker.Read(band);
                times.Add(timer.Elapsed.TotalMilliseconds);
                if (read is var (r, retried))
                {
                    values.Add(r.Value); conf.Add((float)r.Confidence); contrast.Add((float)r.Contrast); locked.Add(1); second.Add((byte)(retried ? 1 : 0)); texts.Add(r.Text);
                }
                else
                {
                    values.Add(-1); conf.Add(0f); contrast.Add(255f); locked.Add(0); second.Add(0); texts.Add("");
                }
                n++;
                if (probe.Contains(n) && tracker.Area is Box whole) Probe(c.Id, n, band, whole.Shift(-c.BandX0, -CaseInfo.Top), models.Item2, read?.Reading, inside, tracker.Inside, gate);
            }
            using (var w = new BinaryWriter(File.Create(Path.Combine(outDir, c.Id + ".bin"))))
            {
                w.Write(n);
                foreach (int v in values) w.Write(v);
                foreach (float v in conf) w.Write(v);
                foreach (float v in contrast) w.Write(v);
                w.Write(locked.ToArray()); w.Write(second.ToArray());
                foreach (string t in texts) w.Write(Encoding.ASCII.GetBytes(t.PadRight(8).Substring(0, 8)));
            }
            times.Sort();
            var info = new
            {
                id = c.Id, frames = n, locked = tracker.Locked, lockAt = tracker.Locked ? tracker.LockAt : -1.0, notes = tracker.Notes,
                pose = tracker.Pose is Pose p ? new[] { p.Angle, p.Center, p.Band } : null, moving = tracker.IsMoving, movingAt = tracker.IsMoving ? tracker.MovingAt : -1.0,
                steps = tracker.Steps.Select(s => new[] { s.At, s.Area.X0, s.Area.Y0, s.Area.X1, s.Area.Y1 }).ToArray(),
                finderRuns = tracker.FinderRuns, compareReads = tracker.CompareReads, shifts = tracker.Shifts, reread = tracker.Reread, checks = tracker.Checks, nudged = tracker.Nudged, kept = tracker.Kept, lockHolds = tracker.LockHolds, workers,
                frameMsMean = times.Count > 0 ? times.Average() : 0, frameMsP95 = times.Count > 0 ? times[(int)(times.Count * 0.95)] : 0, frameMsMax = times.Count > 0 ? times[^1] : 0,
            };
            File.WriteAllText(Path.Combine(outDir, c.Id + ".info.json"), JsonSerializer.Serialize(info));
            lock (gate)
            {
                done++;
                string where = tracker.Locked ? $"고정 {tracker.LockAt:F1}초 (Python {c.LockAt?.ToString("F1", CultureInfo.InvariantCulture) ?? "없음"}초), 영역 바뀜 {tracker.Steps.Count - 1}번" +
                                                (tracker.IsMoving ? $", 움직이는 점수판으로 봄({tracker.MovingAt:F1}초부터)" : "") + (tracker.Shifts > 0 ? $", 읽어서 옮김 {tracker.Shifts}번" : "") : "고정 못 함";
                Console.WriteLine($"[{done}/{cases.Count}] {c.Id}: 프레임 {n}, {where}, 읽은 프레임 {locked.Count(b => b == 1)}, 다시 읽은 값을 쓴 프레임 {second.Count(b => b == 1)} | " +
                                  $"프레임당 평균 {info.frameMsMean:F1} ms");
            }
            return models;
        }, models => { models.Item1.Dispose(); models.Item2.Dispose(); });
        Console.WriteLine($"사례 {cases.Count}개, 전체 {watch.Elapsed.TotalSeconds:F0}초 → {outDir}");
    }

    private static readonly (int X, int Y)[] ProbeMargins = { (14, 8), (4, 2), (8, 4), (0, 0), (20, 4), (14, 2) };
    private static readonly int[] ProbeDy = { 0, -3, 3, -6, 6, -9, 9, -12, 12, -18, -24, -30 };
    private static readonly int[] ProbeDx = { 0, -4, 4 };

    /// <summary>한 프레임에서 모듈이 쓰던 영역(띠 좌표)을 여러 자리·여백으로 잘라 읽어 찍는다.</summary>
    private static void Probe(string id, int n, Picture band, Box area, ScoreReader reader, Reading? got, string before, string after, object gate)
    {
        var lines = new List<string> { $"== {id} #{n}: 모듈의 판독 '{got?.Text}'({got?.Confidence:F2}), 영역(띠) [{area.X0:F1}, {area.Y0:F1}, {area.X1:F1}, {area.Y1:F1}]. 줄마다 여백 {string.Join(" ", ProbeMargins.Select(m => $"({m.X},{m.Y})"))}",
                                        $"   읽기 전: {before}", $"   읽은 뒤: {after}" };
        foreach (int dy in ProbeDy)
            foreach (int dx in ProbeDx)
            {
                if (dx != 0 && dy != 0) continue;
                Box moved = new(area.X0 + dx, area.Y0 + dy, area.X1 + dx, area.Y1 + dy);
                lines.Add($"   밀기({dx},{dy}): " + string.Join(" ", ProbeMargins.Select(m => { Reading r = reader.Read(band.Part(moved, m.X, m.Y)); return $"{(r.Text.Length > 0 ? r.Text : "-")}({r.Confidence:F2})"; })));
            }
        lock (gate) foreach (string line in lines) Console.WriteLine(line);
    }
}
