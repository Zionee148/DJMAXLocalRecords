// 녹화를 모듈 본체(ScoreModule)에 앱이 부르는 모양 그대로 넣고, 등록 요청의 답을 받는다.
//
//   플레이 중: 원본 녹화의 모든 프레임(초당 60장)을 ObserveFrame 으로 넣는다. 모듈이 스스로 자리를 찾아 고정하고, 고정 뒤에는 0.2초마다만 읽는다.
//   키 시각  : BeginRegistration (기한은 요청부터 8초).
//   요청 뒤  : 캡처 간격(기본 0.1초)마다 한 장씩 PushRegistrationFrame. 답이 나오면 멈춘다. 녹화가 먼저 끝나면 기한이 지난 것으로 친다.
//
// 화면은 BGRA 로 바꾸고, 띠 둘레에 남는 화소와 줄 끝 여백을 붙여 넘긴다(모듈이 잘라 쓰는 길을 함께 지나가게 하려는 것이다).
//
// 실행: dotnet run -c Release -- module <finder.onnx> <reader.onnx> <cases.tsv> <결과.tsv> [--workers N] [--capture 초] [--every N] [--timing] [사례 이름에 들어 있는 글자 …]
//       --timing: 호출 하나하나에 걸린 시간을 모아 끝에 적는다(--workers 1 로, 다른 무거운 작업 없이 돌려야 뜻이 있다)
//       dotnet run -c Release -- calls  <finder.onnx> <reader.onnx> <cases.tsv> <사례 이름>     (번호·순서가 어긋난 호출을 어떻게 다루는지 본다)
using System.Diagnostics;
using System.Globalization;
using DjmaxScore;

public static class ModuleRun
{
    private const long Ticks = 60000;                       // 1초. 프레임 하나가 1000
    private const int PadTop = 4, PadRight = 8, PadStride = 16;

    /// <summary>띠(BGR)를 모듈에 넘길 모양(BGRA, 위와 오른쪽에 남는 화소, 줄 끝 여백)으로 바꾼다.</summary>
    internal sealed class Feeder
    {
        public readonly int Width = CaseInfo.BandWidth + PadRight, Height = CaseInfo.BandHeight + PadTop, Stride;
        public readonly byte[] Buffer;
        private readonly CaseInfo _case;
        public Feeder(CaseInfo c) { _case = c; Stride = Width * 4 + PadStride; Buffer = new byte[Height * Stride]; Array.Fill(Buffer, (byte)0x55); }

        public ScoreFrame Frame(Picture band, long session, long frameId)
        {
            for (int y = 0; y < band.Height; y++)
            {
                int o = (y + PadTop) * Stride, i = y * band.Width * 3;
                for (int x = 0; x < band.Width; x++)
                {
                    Buffer[o + x * 4] = band.Data[i + x * 3]; Buffer[o + x * 4 + 1] = band.Data[i + x * 3 + 1]; Buffer[o + x * 4 + 2] = band.Data[i + x * 3 + 2]; Buffer[o + x * 4 + 3] = 255;
                }
            }
            long at = frameId * (Ticks / 60);
            return new ScoreFrame(session, frameId, at, at, Width, Height, Stride, ScoreModule.ScreenWidth, ScoreModule.ScreenHeight, _case.BandX0, CaseInfo.Top - PadTop);
        }
    }

    private static GearPosition PositionOf(CaseInfo c) => c.Position == "LEFT" ? GearPosition.Left : c.Position == "CENTER" ? GearPosition.Center : GearPosition.Right;

    private static ScoreModule Create(string finderPath, string readerPath)
    {
        ScoreModule? module = ScoreModule.TryCreate(finderPath, readerPath, new ScoreOptions { TicksPerSecond = Ticks }, out string? error);
        return module ?? throw new InvalidOperationException(error);
    }

    public static void Run(string finderPath, string readerPath, string casesPath, string outPath, string[] rest)
    {
        var inv = CultureInfo.InvariantCulture;
        int workers = 1, every = 1;
        bool timing = false;
        double capture = 0.1;
        var spent = new Dictionary<string, List<double>> { ["찾는 중"] = new(), ["고정 뒤"] = new(), ["요청 뒤"] = new() };
        var seconds = new Dictionary<string, double> { ["찾는 중"] = 0, ["고정 뒤"] = 0, ["요청 뒤"] = 0 };
        var filters = new List<string>();
        for (int k = 0; k < rest.Length; k++)
            if (rest[k] == "--workers") workers = int.Parse(rest[++k]);
            else if (rest[k] == "--capture") capture = double.Parse(rest[++k], inv);
            else if (rest[k] == "--every") every = int.Parse(rest[++k]);
            else if (rest[k] == "--timing") timing = true;
            else filters.Add(rest[k]);
        List<CaseInfo> cases = CaseInfo.Load(casesPath).Where(c => filters.Count == 0 || filters.Any(c.Id.Contains)).Where((c, k) => k % every == 0).ToList();
        var rows = new string[cases.Count];
        int done = 0, right = 0, wrong = 0, none = 0, ignored = 0, restarted = 0;
        var gate = new object();
        var watch = Stopwatch.StartNew();
        Parallel.For(0, cases.Count, new ParallelOptions { MaxDegreeOfParallelism = workers }, () => Create(finderPath, readerPath), (k, _, module) =>
        {
            CaseInfo c = cases[k];
            var feeder = new Feeder(c);
            long session = k + 1, request = k + 1;
            double pressAt = c.Press - c.Start, next = pressAt;
            long pressTicks = (long)Math.Round(pressAt * Ticks), deadline = pressTicks + 8 * Ticks;
            int ignoredBefore = module.IgnoredCalls, updates = 0, pushed = 0;
            void Count(LocationUpdate _) => updates++;
            module.LocationUpdated += Count;
            module.BeginSession(session, PositionOf(c), 0);
            bool asked = false;
            long n = 0;
            var mine = new Dictionary<string, List<double>> { ["찾는 중"] = new(), ["고정 뒤"] = new(), ["요청 뒤"] = new() };
            var timer = new Stopwatch();
            double answeredAt = double.NaN;
            foreach (Picture band in Video.Bands(c, c.End - c.Start, 0))
            {
                double at = n / RunCheck.VideoFps;
                if (at < pressAt - 1e-6)
                {
                    ScoreFrame frame = feeder.Frame(band, session, n);
                    string phase = module.Location.State == LocationState.Locked ? "고정 뒤" : "찾는 중";
                    timer.Restart();
                    module.ObserveFrame(frame, feeder.Buffer);
                    mine[phase].Add(timer.Elapsed.TotalMilliseconds);
                }
                else
                {
                    if (!asked) { module.BeginRegistration(request, session, pressTicks, deadline); asked = true; }
                    if (at >= next - 1e-6)
                    {
                        ScoreFrame frame = feeder.Frame(band, session, n);
                        timer.Restart();
                        module.PushRegistrationFrame(request, frame, feeder.Buffer);
                        mine["요청 뒤"].Add(timer.Elapsed.TotalMilliseconds);
                        pushed++;
                        next += capture;
                        if (module.Registration.State is RegistrationStatus.Accepted or RegistrationStatus.Failed) { answeredAt = at; break; }
                    }
                }
                n++;
            }
            if (!asked) module.BeginRegistration(request, session, pressTicks, deadline);
            module.Poll(deadline + Ticks);                    // 녹화가 먼저 끝났다: 기한이 지난 것으로 친다 (이미 답이 났으면 아무 일도 없다)
            module.LocationUpdated -= Count;
            LocationUpdate where = module.Location;
            RegistrationResult r = module.Registration;
            int restarts = module.Restarts;
            module.EndSession(session);
            string verdict = r.State != RegistrationStatus.Accepted ? "none" : r.RawScore == c.Expected ? "right" : "wrong";
            rows[k] = string.Join("\t", c.Id, verdict, r.State, r.Reason, r.RawScore?.ToString() ?? "", r.Candidate?.ToString() ?? "", c.Expected, r.ElapsedMs.ToString("F0", inv),
                                  where.State, where.LockedAt is long t ? ((double)t / Ticks).ToString("F2", inv) : "", where.NoteCount,
                                  r.Play?.PlayReads ?? 0, r.Play?.NoteCount ?? 0, r.Play?.PlayValuesVerified ?? 0, (r.Play?.Agree ?? 0).ToString("F3", inv),
                                  r.Play?.LastPlayValue?.ToString() ?? "", r.FramesAfterRequest, r.FramesUnread, pushed, updates, module.IgnoredCalls - ignoredBefore, restarts);
            lock (gate)
            {
                done++;
                if (verdict == "right") right++; else if (verdict == "wrong") wrong++; else none++;
                ignored += module.IgnoredCalls - ignoredBefore;
                restarted += restarts;
                foreach (var (phase, list) in mine) spent[phase].AddRange(list);
                seconds["찾는 중"] += mine["찾는 중"].Count / RunCheck.VideoFps; seconds["고정 뒤"] += mine["고정 뒤"].Count / RunCheck.VideoFps;
                seconds["요청 뒤"] += (double.IsNaN(answeredAt) ? c.End - c.Start - pressAt : answeredAt - pressAt) + capture;
                Console.WriteLine($"[{done}/{cases.Count}] {c.Id}: {r.State} {r.Reason} {r.RawScore?.ToString() ?? "-"} (기대 {c.Expected}) {r.ElapsedMs / 1000:F1}초 | " +
                                  $"위치 {where.State}{(where.LockedAt is long l ? $" {(double)l / Ticks:F1}초" : "")}, 플레이 중 읽은 값 {r.Play?.PlayReads ?? 0}개, " +
                                  $"노트 수 {r.Play?.NoteCount ?? 0}, 통과한 값 {r.Play?.PlayValuesVerified ?? 0}개({r.Play?.Agree ?? 0:P0}), 요청 뒤 화면 {r.FramesAfterRequest}장" +
                                  (restarts > 0 ? $", 새 판으로 본 횟수 {restarts}   <<<<<" : "") + (verdict == "right" ? "" : "   <<<<<"));
            }
            return module;
        }, module => module.Dispose());
        File.WriteAllLines(outPath, new[] { "case\tverdict\tstate\treason\tscore\tcandidate\texpected\telapsedMs\tlocation\tlockAt\tlockNotes\tplayReads\tnotes\tverified\tagree\tlastPlay\tframesAfter\tframesUnread\tpushed\tlocationUpdates\tignoredCalls\trestarts" }.Concat(rows));
        if (timing)
        {
            Console.WriteLine($"처리 시간 (호출 하나에 걸린 시간. 작업자 {workers}, 플레이 중에는 초당 60장을 모두 넣었고 요청 뒤에는 {capture.ToString(inv)}초마다 한 장):");
            foreach (var (phase, list) in spent)
            {
                if (list.Count == 0) continue;
                list.Sort();
                double At(double q) => list[Math.Min(list.Count - 1, (int)(list.Count * q))];
                Console.WriteLine($"   {phase}: 호출 {list.Count:N0}번(녹화 {seconds[phase]:N0}초), 합계 {list.Sum() / 1000:F1}초 = 코어 하나의 {list.Sum() / 10 / seconds[phase]:F1}% | " +
                                  $"평균 {list.Average():F2} ms, 가운데 {At(0.5):F2}, 95% {At(0.95):F1}, 99% {At(0.99):F1}, 99.9% {At(0.999):F1}, 가장 긴 {list[^1]:F1} | " +
                                  $"16.7ms 넘음 {list.Count(v => v > 16.7):N0}번({100.0 * list.Count(v => v > 16.7) / list.Count:F2}%), 33ms 넘음 {list.Count(v => v > 33.3):N0}번, 50ms 넘음 {list.Count(v => v > 50):N0}번, 100ms 넘음 {list.Count(v => v > 100):N0}번");
            }
        }
        Console.WriteLine($"사례 {cases.Count}개 (요청 뒤 캡처 간격 {capture.ToString(inv)}초): 맞음 {right}, 틀림 {wrong}, 못 냄 {none} | 무시된 호출 {ignored}, 새 판으로 잘못 본 횟수 {restarted} | 전체 {watch.Elapsed.TotalSeconds:F0}초 → {outPath}");
    }

    /// <summary>
    /// 같은 곡을 다시 시작한 것을 만들어 본다(만든 시험이다. 실제 재시작 녹화가 아니다): 한 녹화를 넣어 등록 답까지 받은 뒤,
    /// 세션을 새로 열지 않고 같은 녹화를 처음부터 다시 넣는다. 모듈이 새 판으로 알아채는지, 둘째 판의 등록 답이 맞는지 본다.
    ///
    /// 실행: dotnet run -c Release -- again &lt;finder.onnx&gt; &lt;reader.onnx&gt; &lt;cases.tsv&gt; &lt;결과.tsv&gt; [--workers N] [--gap 초] [--capture 초]
    ///                                    [--press &lt;사례·키 시각·기대 값 목록&gt;] [--short 초] [사례…]
    ///   --press : 둘째 판에서 등록 키를 누를 시각(구간 기준, 초)과 그때의 기대 값. 노트가 없는 중간 지점을 주면 "앞 판의 최종 점수보다 낮은 후보"가 된다.
    ///   --short : 둘째 판에서 새 판으로 알아챈 뒤 이만큼(초) 지나 등록 키를 누른다. 새 판의 근거가 모자라 답을 내지 않아야 한다.
    /// </summary>
    public static void Again(string finderPath, string readerPath, string casesPath, string outPath, string[] rest)
    {
        var inv = CultureInfo.InvariantCulture;
        int workers = 1;
        double capture = 0.1, gap = 3.0;
        double? shortAfter = null;
        bool verbose = false;
        Dictionary<string, (double At, int Expected)>? presses = null;
        var filters = new List<string>();
        for (int k = 0; k < rest.Length; k++)
            if (rest[k] == "--workers") workers = int.Parse(rest[++k]);
            else if (rest[k] == "--capture") capture = double.Parse(rest[++k], inv);
            else if (rest[k] == "--gap") gap = double.Parse(rest[++k], inv);
            else if (rest[k] == "--verbose") verbose = true;
            else if (rest[k] == "--short") shortAfter = double.Parse(rest[++k], inv);
            else if (rest[k] == "--press") presses = File.ReadAllLines(rest[++k]).Where(l => l.Length > 0).Select(l => l.Split('\t')).ToDictionary(f => f[0], f => (double.Parse(f[1], inv), int.Parse(f[2])));
            else filters.Add(rest[k]);
        List<CaseInfo> cases = CaseInfo.Load(casesPath).Where(c => filters.Count == 0 || filters.Any(c.Id.Contains)).Where(c => presses is null || presses.ContainsKey(c.Id)).ToList();
        var rows = new string[cases.Count];
        int done = 0, good = 0;
        var gate = new object();
        var watch = Stopwatch.StartNew();
        Parallel.For(0, cases.Count, new ParallelOptions { MaxDegreeOfParallelism = workers }, () => Create(finderPath, readerPath), (k, _, module) =>
        {
            CaseInfo c = cases[k];
            var feeder = new Feeder(c);
            long session = k + 1;
            int restarts = 0;
            double restartAt = double.NaN, firstSeen = double.NaN;
            void OnRestart(PlayRestart e) { restarts++; restartAt = (double)e.At / Ticks; firstSeen = (double)e.FirstSeenAt / Ticks; }
            module.PlayRestarted += OnRestart;
            ScoreArea? shown = null;
            void OnLocation(LocationUpdate u)
            {
                if (u.Area is not ScoreArea a) return;
                if (shown is ScoreArea b && Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) + Math.Abs(a.Width - b.Width) + Math.Abs(a.Height - b.Height) < 6) return;
                shown = a;
                Console.WriteLine($"      {c.Id} {(double)u.At / Ticks:F1}초 영역 X {a.X} Y {a.Y} {a.Width}x{a.Height}");
            }
            if (verbose) module.LocationUpdated += OnLocation;
            module.BeginSession(session, PositionOf(c), 0);

            // 한 판: offset 번째 프레임부터 녹화를 처음부터 넣고, pressAt(구간 기준 초)에 등록 요청을 열어 답을 받는다. 넣은 프레임 수를 돌려준다.
            (RegistrationResult Result, long Fed) Play(long offset, double pressAt, long request, bool pressAfterRestart)
            {
                bool asked = false;
                int seen = restarts;
                double next = pressAt;
                long pressTicks = 0, deadline = 0, n = 0;
                foreach (Picture band in Video.Bands(c, c.End - c.Start, 0))
                {
                    double at = n / RunCheck.VideoFps;
                    if (pressAfterRestart && !asked && restarts > seen && double.IsPositiveInfinity(pressAt)) { pressAt = restartAt - offset / RunCheck.VideoFps + shortAfter!.Value; next = pressAt; }
                    if (at < pressAt - 1e-6) module.ObserveFrame(feeder.Frame(band, session, offset + n), feeder.Buffer);
                    else
                    {
                        if (!asked)
                        {
                            pressTicks = (long)Math.Round((offset / RunCheck.VideoFps + pressAt) * Ticks); deadline = pressTicks + 8 * Ticks;
                            module.BeginRegistration(request, session, pressTicks, deadline); asked = true;
                        }
                        if (at >= next - 1e-6)
                        {
                            module.PushRegistrationFrame(request, feeder.Frame(band, session, offset + n), feeder.Buffer);
                            next += capture;
                            if (module.Registration.RequestId == request && module.Registration.State is RegistrationStatus.Accepted or RegistrationStatus.Failed) { n++; break; }
                        }
                    }
                    n++;
                }
                if (!asked)
                {
                    pressTicks = (offset + n) * (Ticks / 60); deadline = pressTicks + 8 * Ticks;
                    module.BeginRegistration(request, session, pressTicks, deadline);
                }
                module.Poll(deadline + Ticks);
                return (module.Registration, n);
            }

            var (first, fed) = Play(0, c.Press - c.Start, 2 * k + 1, false);
            int restartsInFirst = restarts;
            long offset = fed + (long)Math.Round(gap * RunCheck.VideoFps);
            double press2 = shortAfter is not null ? double.PositiveInfinity : presses is not null ? presses[c.Id].At : c.Press - c.Start;
            int expected2 = presses is not null ? presses[c.Id].Expected : c.Expected;
            var (second, _) = Play(offset, press2, 2 * k + 2, shortAfter is not null);
            module.PlayRestarted -= OnRestart;
            if (verbose) module.LocationUpdated -= OnLocation;
            module.EndSession(session);

            bool firstRight = first.State == RegistrationStatus.Accepted && first.RawScore == c.Expected;
            bool secondRight = shortAfter is not null ? second.State == RegistrationStatus.Failed && second.Reason == "NotEnoughPlayEvidence"
                                                      : second.State == RegistrationStatus.Accepted && second.RawScore == expected2;
            bool ok = firstRight && secondRight && restartsInFirst == 0 && restarts == 1;
            double since = double.IsNaN(restartAt) ? double.NaN : restartAt - offset / RunCheck.VideoFps, sinceFirst = double.IsNaN(restartAt) ? double.NaN : restartAt - firstSeen;
            rows[k] = string.Join("\t", c.Id, ok ? "ok" : "bad", firstRight ? "right" : "bad", second.State, second.Reason, second.RawScore?.ToString() ?? "", second.Candidate?.ToString() ?? "",
                                  shortAfter is not null ? "" : expected2.ToString(), restartsInFirst, restarts, since.ToString("F2", inv), sinceFirst.ToString("F2", inv),
                                  second.Play?.PlayReads ?? 0, second.Play?.NoteCount ?? 0, second.Play?.PlayValuesVerified ?? 0, second.Play?.LastPlayValue?.ToString() ?? "", second.ElapsedMs.ToString("F0", inv));
            lock (gate)
            {
                done++;
                if (ok) good++;
                Console.WriteLine($"[{done}/{cases.Count}] {c.Id}: 첫 판 {(firstRight ? "맞음" : "다름")}, 새 판으로 본 횟수 {restarts}(첫 판 중 {restartsInFirst})" +
                                  (double.IsNaN(since) ? "" : $", 둘째 판 시작 뒤 {since:F1}초(새 판의 값이 처음 읽힌 뒤 {sinceFirst:F1}초)") +
                                  $" | 둘째 판 {second.State} {second.Reason} {second.RawScore?.ToString() ?? "-"}" + (shortAfter is not null ? " (기대: 근거 부족)" : $" (기대 {expected2})") +
                                  $", 근거: 읽은 값 {second.Play?.PlayReads ?? 0}개·통과 {second.Play?.PlayValuesVerified ?? 0}개" + (ok ? "" : "   <<<<<"));
            }
            return module;
        }, module => module.Dispose());
        File.WriteAllLines(outPath, new[] { "case\tverdict\tfirst\tstate2\treason2\tscore2\tcandidate2\texpected2\trestartsInFirst\trestarts\trestartAfterStart\trestartAfterFirstValue\tplayReads2\tnotes2\tverified2\tlastPlay2\telapsedMs2" }.Concat(rows));
        Console.WriteLine($"사례 {cases.Count}개 (만든 재시작 시험{(shortAfter is not null ? $", 알아챈 뒤 {shortAfter.Value.ToString(inv)}초에 키" : presses is not null ? ", 중간 지점에서 키" : ", 끝에서 키")}): 기대대로 {good}, 아닌 것 {cases.Count - good} | 전체 {watch.Elapsed.TotalSeconds:F0}초 → {outPath}");
    }

    /// <summary>번호가 안 맞거나 순서가 어긋난 호출, 취소, 화면이 바뀜, 요청 중의 새 세션을 한 녹화로 차례로 해 본다.</summary>
    public static void Calls(string finderPath, string readerPath, string casesPath, string name)
    {
        CaseInfo c = CaseInfo.Load(casesPath).First(x => x.Id.Contains(name));
        using ScoreModule module = Create(finderPath, readerPath);
        var feeder = new Feeder(c);
        int fail = 0;
        void Check(string what, bool ok, string got = "")
        {
            if (!ok) fail++;
            Console.WriteLine($"   {(ok ? "맞음" : "다름 <<<<<")}  {what}{(got == "" ? "" : "  → " + got)}");
        }
        string Now() => $"{module.Registration.RequestId}:{module.Registration.State}:{module.Registration.Reason}:{module.Registration.RawScore?.ToString() ?? "-"}";
        bool Ignored(Action call) { int before = module.IgnoredCalls; call(); return module.IgnoredCalls == before + 1; }

        double pressAt = c.Press - c.Start;
        using IEnumerator<Picture> frames = Video.Bands(c, c.End - c.Start, 0).GetEnumerator();
        long n = -1;
        Picture Next(int skip = 1) { Picture? p = null; for (int i = 0; i < skip; i++) { if (!frames.MoveNext()) throw new InvalidOperationException("녹화가 끝났다"); p = frames.Current; n++; } return p!; }
        long At() => n * (Ticks / 60);

        Console.WriteLine($"{c.Id} (기대 점수 {c.Expected})");
        Picture first = Next();
        Check("세션을 열기 전의 화면은 무시한다", Ignored(() => module.ObserveFrame(feeder.Frame(first, 1, n), feeder.Buffer)));
        module.BeginSession(1, PositionOf(c), 0);
        Check("세션을 열면 Searching", module.Location is { SessionId: 1, State: LocationState.Searching });
        module.ObserveFrame(feeder.Frame(first, 1, n), feeder.Buffer);
        Check("같은 프레임 번호를 다시 넣으면 무시한다", Ignored(() => module.ObserveFrame(feeder.Frame(first, 1, n), feeder.Buffer)));
        Check("다른 세션 번호의 화면은 무시한다", Ignored(() => module.ObserveFrame(feeder.Frame(first, 7, n + 1), feeder.Buffer)));
        Check("세션 번호가 다른 등록 요청은 무시한다", Ignored(() => module.BeginRegistration(90, 7, At(), At() + 8 * Ticks)));
        while ((n + 1) / RunCheck.VideoFps < pressAt - 1e-6) { Picture p = Next(); module.ObserveFrame(feeder.Frame(p, 1, n), feeder.Buffer); }
        Check("키 시각 전에 고정", module.Location.State == LocationState.Locked, $"{module.Location.State}, 고정 {(double)(module.Location.LockedAt ?? -1) / Ticks:F1}초, 노트 수 {module.Location.NoteCount}, 영역 {module.Location.Area}");

        // 취소
        module.BeginRegistration(1, 1, At(), At() + 8 * Ticks);
        Check("요청을 열면 WaitingForFrames", module.Registration is { RequestId: 1, State: RegistrationStatus.WaitingForFrames }, Now());
        Check("요청이 열려 있으면 다른 요청은 무시한다", Ignored(() => module.BeginRegistration(2, 1, At(), At() + 8 * Ticks)));
        module.CancelRegistration(1);
        Check("취소하면 Cancelled", module.Registration is { RequestId: 1, State: RegistrationStatus.Cancelled }, Now());
        Picture q = Next(6);
        Check("취소한 요청에 넣는 화면은 무시한다", Ignored(() => module.PushRegistrationFrame(1, feeder.Frame(q, 1, n), feeder.Buffer)));
        Check("쓴 요청 번호는 다시 못 쓴다", Ignored(() => module.BeginRegistration(1, 1, At(), At() + 8 * Ticks)));
        Check("무시된 호출은 지난 답을 바꾸지 않는다", module.Registration is { RequestId: 1, State: RegistrationStatus.Cancelled }, Now());

        // 화면을 하나도 못 받고 화면이 바뀜
        module.BeginRegistration(3, 1, At(), At() + 8 * Ticks);
        module.NotifyContextLeft(3, n, At());
        Check("화면 없이 화면이 바뀌면 Failed·ContextChangedNoEvidence", module.Registration is { RequestId: 3, State: RegistrationStatus.Failed, Reason: "ContextChangedNoEvidence" }, Now());

        // 두 장만 넣고 그만 보냄
        module.BeginRegistration(4, 1, At(), At() + 8 * Ticks);
        for (int i = 0; i < 2; i++) { q = Next(6); module.PushRegistrationFrame(4, feeder.Frame(q, 1, n), feeder.Buffer); }
        Check("두 장 넣는 동안은 Reading", module.Registration is { RequestId: 4, State: RegistrationStatus.Reading }, Now());
        module.CompleteRegistration(4);
        Check("두 장만 넣고 마치면 Failed·NotEnoughFrames", module.Registration is { RequestId: 4, State: RegistrationStatus.Failed, Reason: "NotEnoughFrames" }, Now());
        Check("끝난 요청에 넣는 화면은 무시한다", Ignored(() => module.PushRegistrationFrame(4, feeder.Frame(q, 1, n + 1), feeder.Buffer)));

        // 사라지면 직전 값(사용자 결정, 2026-11-14): 같은 값이 0.3초(네 장) 이어진 뒤 0.5초를 채우기 전에 점수가 사라진다
        module.BeginRegistration(7, 1, At(), At() + 8 * Ticks);
        for (int i = 0; i < 4; i++) { q = Next(6); module.PushRegistrationFrame(7, feeder.Frame(q, 1, n), feeder.Buffer); }
        Check("같은 값 네 장(0.3초)까지는 아직 Reading", module.Registration is { RequestId: 7, State: RegistrationStatus.Reading }, Now());
        module.NotifyContextLeft(7, n, At());
        Check("그때 곡이 끝났다고 알리면 기다리지 않고 직전 값으로 Accepted(근거 Gone)",
              module.Registration is { RequestId: 7, State: RegistrationStatus.Accepted, Basis: "Gone" } && module.Registration.RawScore == c.Expected, Now() + " " + module.Registration.Basis);
        module.BeginRegistration(8, 1, At(), At() + 8 * Ticks);
        for (int i = 0; i < 2; i++) { q = Next(6); module.PushRegistrationFrame(8, feeder.Frame(q, 1, n), feeder.Buffer); }
        module.NotifyContextLeft(8, n, At());
        Check("두 장(0.1초)뿐이면 곡이 끝났다고 알려도 값을 내지 않는다", module.Registration is { RequestId: 8, State: RegistrationStatus.Failed, Reason: "NotEnoughFrames" }, Now());
        module.BeginRegistration(9, 1, At(), At() + 8 * Ticks);
        for (int i = 0; i < 4; i++) { q = Next(6); module.PushRegistrationFrame(9, feeder.Frame(q, 1, n), feeder.Buffer); }
        var black = new Picture(new byte[CaseInfo.BandHeight * CaseInfo.BandWidth * 3], CaseInfo.BandHeight, CaseInfo.BandWidth);
        int blanks = 0;
        while (module.Registration is { RequestId: 9, State: RegistrationStatus.Reading } && blanks < 40)
        {
            n += 6; blanks++;                                 // 녹화는 그대로 두고 시각만 0.1초씩 보낸다: 점수가 없는(검은) 화면이 들어온다
            module.PushRegistrationFrame(9, feeder.Frame(black, 1, n), feeder.Buffer);
        }
        Check("점수가 없는 화면이 2초 이어지면 직전 값으로 Accepted(근거 Gone). 그 전에는 내지 않는다",
              module.Registration is { RequestId: 9, State: RegistrationStatus.Accepted, Basis: "Gone" } && module.Registration.RawScore == c.Expected && blanks == 21,
              Now() + $", 빈 화면 {blanks}장, 요청부터 {module.Registration.ElapsedMs:F0}ms");

        // 한 장 넣은 뒤 새 세션이 먼저 옴
        module.BeginRegistration(5, 1, At(), At() + 8 * Ticks);
        q = Next(6); module.PushRegistrationFrame(5, feeder.Frame(q, 1, n), feeder.Buffer);
        module.BeginSession(2, PositionOf(c), At());
        Check("요청 중에 새 세션이 오면 요청은 받은 것으로 마친다(취소가 아니다)", module.Registration is { RequestId: 5, State: RegistrationStatus.Failed, Reason: "NotEnoughFrames" }, Now());
        Check("새 세션은 Searching 부터", module.Location is { SessionId: 2, State: LocationState.Searching });
        Check("옛 세션 번호의 화면은 무시한다", Ignored(() => module.ObserveFrame(feeder.Frame(q, 1, n + 1), feeder.Buffer)));

        // 새 세션(고정 전)에서 온 요청: 플레이 중 근거가 없다
        module.BeginRegistration(6, 2, At(), At() + 8 * Ticks);
        Check("고정 전에 온 요청은 화면을 기다리지 않고 바로 Failed·LocationUncertain", module.Registration is { RequestId: 6, State: RegistrationStatus.Failed, Reason: "LocationUncertain" }, Now());
        q = Next(6);
        Check("그 요청에 넣는 화면은 무시한다", Ignored(() => module.PushRegistrationFrame(6, feeder.Frame(q, 2, n), feeder.Buffer)));

        // 좌표계가 다른 화면
        ScoreFrame bad = feeder.Frame(q, 2, n + 1) with { SourceWidth = 1280, SourceHeight = 720 };
        module.ObserveFrame(bad, feeder.Buffer);
        Check("1920x1080 이 아닌 화면은 Unsupported", module.Location is { SessionId: 2, State: LocationState.Unsupported });
        module.EndSession(2);
        Check("끝난 세션의 화면은 무시한다", Ignored(() => module.ObserveFrame(feeder.Frame(q, 2, n + 2), feeder.Buffer)));
        Console.WriteLine(fail == 0 ? "모두 맞음" : $"다른 것 {fail}개");
    }
}
