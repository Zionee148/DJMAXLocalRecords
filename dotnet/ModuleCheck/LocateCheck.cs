// 위치 찾기(.NET 의 Locator)가 Python 과 같은 자리를 같은 때에 고정하는지 확인한다.
//
// Python(locate_video)과 똑같이 원본 녹화에서 화면 아래쪽 띠를 초당 10장으로 뽑아(ffmpeg), 구간 처음 45초를 차례로 넣는다.
// 탐지 모델은 세 장에 한 번 돌린다. 고정 시각, 고정한 영역, 자세를 Python 이 남긴 결과(cases.tsv)와 견준다.
//
// 실행: dotnet run -c Release -- locate <finder.onnx> <reader.onnx> <cases.tsv> [--workers N] [사례 이름에 들어 있는 글자 …]
using System.Diagnostics;
using System.Globalization;
using DjmaxScore;

public sealed record CaseInfo(string Id, string Position, string Source, double Start, double End, double Press, int Expected,
                              double? LockAt, Pose? Pose, Box? AreaFirst, Box? AreaLast, bool Moving)
{
    public const int Top = 740, BandHeight = 340, BandWidth = 960;
    public int BandX0 => Position == "LEFT" ? 0 : Position == "CENTER" ? 480 : 960;
    public GearPosition Gear => Position == "LEFT" ? GearPosition.Left : Position == "CENTER" ? GearPosition.Center : GearPosition.Right;

    public static List<CaseInfo> Load(string path)
    {
        var inv = CultureInfo.InvariantCulture;
        var list = new List<CaseInfo>();
        foreach (string line in File.ReadAllLines(path))
        {
            string[] f = line.Split('\t');
            Box? Area(string s) { if (s == "") return null; double[] v = s.Split(' ').Select(x => double.Parse(x, inv)).ToArray(); return new Box(v[0], v[1], v[2], v[3]); }
            Pose? pose = null;
            if (f[8] != "") { double[] v = f[8].Split(' ').Select(x => double.Parse(x, inv)).ToArray(); pose = new Pose(v[0], v[1], v[2]); }
            list.Add(new CaseInfo(f[0], f[1], f[2], double.Parse(f[3], inv), double.Parse(f[4], inv), double.Parse(f[5], inv), int.Parse(f[6]),
                                  f[7] == "" ? null : double.Parse(f[7], inv), pose, Area(f[9]), Area(f[10]), f[11] == "1"));
        }
        return list;
    }
}

public static class Video
{
    /// <summary>
    /// 합성 시험(--panel &lt;래더 녹화&gt; &lt;시작 초&gt;): 모든 사례의 화면에 그 래더 녹화의 비교 정보판을 프레임마다 붙여서 넣는다.
    /// 정보판의 RATE 가 실제로 변하는 녹화를 써야 한다(정지 그림의 숫자는 값이 쌓이지 않아 위치 찾기의 후보가 되지 못한다).
    /// 붙이는 것은 정보판 가운데 띠에 들어오는 높이뿐이다. LEFT·RIGHT 는 녹화의 자리(가운데) 그대로, CENTER 는 래더 CENTER 화면의 자리(왼쪽)로 옮겨 붙인다.
    /// 만든 화면이다: 실제 래더 플레이가 아니고, 정보판의 숫자는 그 사례의 점수와 상관없이 변한다.
    /// --panel-clip &lt;잘라 둔 영상&gt;: 붙일 부분만 미리 잘라 둔 영상을 쓴다(사례마다 큰 녹화를 다시 풀지 않아 빠르다. 화소는 같다). 만드는 법:
    ///   ffmpeg -ss &lt;시작 초&gt; -i &lt;래더 녹화&gt; -t &lt;길이&gt; -an -vf crop=444:162:738:740 -fps_mode passthrough -c:v ffv1 &lt;잘라 둔 영상.mkv&gt;
    /// </summary>
    public static (string Source, double Start, bool Cropped)? Panel;
    public const int PanelLeft = 738, PanelWidth = 444, PanelBottom = 902, PanelLeftAtCenter = 78;   // 1920×1080 화면에서 정보판이 놓이는 자리

    /// <summary>녹화의 한 구간에서 띠(BGR)를 차례로 뽑는다. fps 가 0 이면 원본의 모든 프레임.</summary>
    public static IEnumerable<Picture> Bands(CaseInfo c, double seconds, int fps)
    {
        var inv = CultureInfo.InvariantCulture;
        string crop = $"crop={CaseInfo.BandWidth}:{CaseInfo.BandHeight}:{c.BandX0}:{CaseInfo.Top}" + (fps > 0 ? $",fps={fps}" : "");
        var inputs = new List<string> { "-ss", c.Start.ToString("R", inv), "-i", c.Source };
        string[] filter = { "-vf", crop };
        if (Panel is var (panelSource, panelStart, cropped))
        {
            inputs.AddRange(new[] { "-ss", panelStart.ToString("R", inv), "-i", panelSource });
            string cut = cropped ? "" : $"crop={PanelWidth}:{PanelBottom - CaseInfo.Top}:{PanelLeft}:{CaseInfo.Top},";
            int to = c.Position == "CENTER" ? PanelLeftAtCenter : PanelLeft;
            // 정보판 쪽은 끝난 뒤에도 마지막 화면을 이어 붙이고(tpad), 사례의 녹화가 끝나면 함께 끝낸다(shortest):
            // 그러지 않으면 사례의 녹화가 구간보다 먼저 끝날 때 그 마지막 화면에 정보판만 이어 붙인 화면이 더 나온다
            filter = new[] { "-filter_complex", $"[1:v]{cut}tpad=stop=-1:stop_mode=clone[p];" +
                                                $"[0:v][p]overlay={to}:{CaseInfo.Top}:shortest=1,{crop}" };
        }
        var info = new ProcessStartInfo("ffmpeg") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (string a in new[] { "-hide_banner", "-loglevel", "error" }.Concat(inputs).Concat(new[] { "-t", seconds.ToString("0.0###", inv), "-an" }).Concat(filter)
                                     .Concat(fps > 0 ? Array.Empty<string>() : new[] { "-fps_mode", "passthrough" })
                                     .Concat(new[] { "-f", "rawvideo", "-pix_fmt", "bgr24", "-" }))
            info.ArgumentList.Add(a);
        using var process = Process.Start(info)!;
        process.ErrorDataReceived += (_, _) => { };              // 일찍 그만 읽으면 ffmpeg 가 "Broken pipe" 를 쏟아낸다. 버린다
        process.BeginErrorReadLine();
        try
        {
            Stream output = process.StandardOutput.BaseStream;
            int size = CaseInfo.BandWidth * CaseInfo.BandHeight * 3;
            while (true)
            {
                var buffer = new byte[size];
                int got = 0;
                while (got < size)
                {
                    int n = output.Read(buffer, got, size - got);
                    if (n <= 0) break;
                    got += n;
                }
                if (got < size) break;
                yield return new Picture(buffer, CaseInfo.BandHeight, CaseInfo.BandWidth);
            }
        }
        finally
        {
            if (!process.HasExited) { try { process.Kill(); } catch (InvalidOperationException) { } }
            process.WaitForExit();
        }
    }
}

public static class LocateCheck
{
    public const int Fps = 10, DetectEvery = 3;
    public const double Watch = 45.0;

    public static void Run(string finderPath, string readerPath, string casesPath, string[] rest)
    {
        int workers = 1, budget = 0, cold = 1, every = 1;
        var filters = new List<string>();
        for (int k = 0; k < rest.Length; k++)
            if (rest[k] == "--workers") workers = int.Parse(rest[++k]);
            else if (rest[k] == "--cold-every") cold = int.Parse(rest[++k]);                // 안 읽히는 자리는 이만큼의 화면에 한 번만 읽는다 (1 = 시제품처럼 화면마다)
            else if (rest[k] == "--every") every = int.Parse(rest[++k]);                    // 사례를 이만큼 건너 하나씩만 (처리 시간을 잴 때)
            else if (rest[k] == "--pose-budget") budget = int.Parse(rest[++k]);       // 기울여 읽어 보기를 화면마다 이만큼씩 나눠 한다 (0 = 시제품처럼 한꺼번에)
            else filters.Add(rest[k]);
        List<CaseInfo> cases = CaseInfo.Load(casesPath).Where(c => filters.Count == 0 || filters.Any(c.Id.Contains)).Where((c, k) => k % every == 0).ToList();
        var rows = new string[cases.Count];
        int sameLock = 0, differLock = 0, differTime = 0, differPose = 0, done = 0;
        double worstBox = 0;
        var frameMs = new List<double>();
        long poseReads = 0, reads = 0;
        var frameReads = new List<int>();
        var gate = new object();
        var watch = Stopwatch.StartNew();
        Parallel.For(0, cases.Count, new ParallelOptions { MaxDegreeOfParallelism = workers }, () => (new RowFinder(finderPath), new ScoreReader(readerPath)), (k, _, models) =>
        {
            CaseInfo c = cases[k];
            var locator = new Locator(models.Item1, models.Item2) { PoseBudget = budget, ColdEvery = cold };
            LockResult? found = null;
            int i = 0;
            var times = new List<double>();
            var counts = new List<int>();
            foreach (Picture band in Video.Bands(c, Watch, Fps))
            {
                var timer = Stopwatch.StartNew();
                found = locator.Offer(band, (double)i / Fps, i % DetectEvery == 0);
                times.Add(timer.Elapsed.TotalMilliseconds);
                counts.Add(locator.LastReads + locator.LastPoseReads);
                if (found is not null) break;
                i++;
            }
            string verdict;
            double boxDiff = 0;
            bool wantLock = c.LockAt is not null;
            if (found is null) verdict = wantLock ? $"다름: .NET 은 고정 못 함 (Python {c.LockAt:F1}초)" : "같음: 둘 다 고정 못 함";
            else if (!wantLock) verdict = $"다름: .NET 만 고정 ({found.At:F1}초)";
            else
            {
                // 띠 좌표 → 화면 전체 좌표. Python 의 lock_area 처럼 화면 밖으로 나간 가로는 화면 끝(0, 1920)에서 자른다
                Box shifted = found.Box.Shift(c.BandX0, CaseInfo.Top), theirs = c.AreaFirst!.Value;
                Box mine = new Box(Math.Max(0.0, shifted.X0), shifted.Y0, Math.Min(1920.0, shifted.X1), shifted.Y1);
                boxDiff = new[] { Math.Abs(mine.X0 - theirs.X0), Math.Abs(mine.Y0 - theirs.Y0), Math.Abs(mine.X1 - theirs.X1), Math.Abs(mine.Y1 - theirs.Y1) }.Max();
                bool timeSame = Math.Abs(found.At - c.LockAt!.Value) < 1e-6, poseSame = found.Pose == c.Pose;
                verdict = (timeSame && poseSame && boxDiff < 1.0 ? "같음" : "다름") + $": 고정 {found.At:F1}초 (Python {c.LockAt:F1}초), 영역 차이 {boxDiff:F2}화소" +
                          (found.Pose is null && c.Pose is null ? "" : $", 자세 {found.Pose?.Angle}/{found.Pose?.Center} (Python {c.Pose?.Angle}/{c.Pose?.Center})") + $", 노트 수 {found.Notes}" +
                          (boxDiff >= 1.0 ? $" | .NET [{mine.X0:F1}, {mine.Y0:F1}, {mine.X1:F1}, {mine.Y1:F1}] / Python [{theirs.X0:F1}, {theirs.Y0:F1}, {theirs.X1:F1}, {theirs.Y1:F1}]" : "");
            }
            lock (gate)
            {
                if (verdict.StartsWith("같음")) sameLock++; else differLock++;
                if (found is not null && wantLock)
                {
                    worstBox = Math.Max(worstBox, boxDiff);
                    if (Math.Abs(found.At - c.LockAt!.Value) >= 1e-6) differTime++;
                    if (found.Pose != c.Pose) differPose++;
                }
                frameMs.AddRange(times); poseReads += locator.PoseReads; reads += locator.Reads; frameReads.AddRange(counts);
                times.Sort();
                rows[k] = $"{c.Id}\t{verdict}\t추적 후보 {locator.Tracks}\t판독 {locator.Reads}\t기울여 읽기 {locator.PoseReads}\t화면 {times.Count}장\t" +
                          $"가운데 {times[times.Count / 2]:F0} ms\t95% {times[(int)(times.Count * 0.95)]:F0} ms\t가장 긴 {times[^1]:F0} ms";
                done++;
                if (!verdict.StartsWith("같음") || cases.Count <= 12) Console.WriteLine($"[{done}/{cases.Count}] {c.Id}: {verdict}");
                if (cases.Count <= 3) foreach (string d in locator.Describe()) Console.WriteLine("      " + d);
                else if (done % 30 == 0) Console.WriteLine($"[{done}/{cases.Count}] …");
            }
            return models;
        }, models => { models.Item1.Dispose(); models.Item2.Dispose(); });
        frameMs.Sort();
        Console.WriteLine($"사례 {cases.Count}개: Python 과 같은 고정 {sameLock}개, 다른 것 {differLock}개 (고정 시각이 다른 것 {differTime}, 자세가 다른 것 {differPose}, 영역의 가장 큰 차이 {worstBox:F2}화소)");
        if (frameMs.Count > 0)
            Console.WriteLine($"화면 한 장 처리 시간(작업자 {workers}개로 함께 돌릴 때): 가운데 {frameMs[frameMs.Count / 2]:F1} ms, 95% {frameMs[(int)(frameMs.Count * 0.95)]:F1} ms, 가장 긴 {frameMs[^1]:F1} ms " +
                              $"| 판독 {reads:N0}번 + 기울여 읽어 보기 {poseReads:N0}번, 전체 {watch.Elapsed.TotalSeconds:F0}초");
            Console.WriteLine($"   평균 {frameMs.Average():F1} ms, 99% {frameMs[(int)(frameMs.Count * 0.99)]:F1} ms, 100ms 를 넘은 화면 {frameMs.Count(v => v > 100)}장 / {frameMs.Count}장 ({100.0 * frameMs.Count(v => v > 100) / frameMs.Count:F2}%), 150ms 를 넘은 화면 {frameMs.Count(v => v > 150)}장");
        if (frameReads.Count > 0)
        {
            frameReads.Sort();
            Console.WriteLine($"화면 한 장에서 한 판독 횟수(그대로 읽기 + 기울여 읽어 보기): 가운데 {frameReads[frameReads.Count / 2]}, 95% {frameReads[(int)(frameReads.Count * 0.95)]}, " +
                              $"99% {frameReads[(int)(frameReads.Count * 0.99)]}, 가장 많은 {frameReads[^1]} (화면 {frameReads.Count:N0}장)");
        }
        File.WriteAllLines(Path.Combine(Path.GetDirectoryName(casesPath)!, budget > 0 || cold > 1 ? $"locate-dotnet-budget{budget}-cold{cold}.tsv" : "locate-dotnet.tsv"), rows);
    }
}
