// 앱이 실제로 쓰는 연결 코드(ScoreLink)를 가짜 시계로 돌린다 (기획서 v3 완료 기준 1의 "개발 중 빠른 시험").
//
// 앱이 하는 것처럼 한다: 일정한 간격(기본 0.1초)으로 "캡처"해서 ScoreLink.Offer 로 넘기고, 키 시각에 BeginRequest, 매 캡처마다 Poll.
// 녹화의 60장 가운데 캡처 시각에 해당하는 한 장만 넘긴다(나머지는 앱이 보지 못한 화면이다).
// 시계는 가짜다: 모듈이 일하는 데 걸린 시간만큼 "바쁜" 것으로 치고, 그동안 온 플레이 중의 화면은 ScoreLink 가 버린다(요청 뒤의 화면은 줄을 선다).
// 일이 끝났다는 알림은 가짜 시계가 그 일이 끝나는 시각에 닿았을 때 간다(앱의 ScoreRunner 가 일이 끝난 뒤에야 화면 스레드에 알리는 것과 같다).
// 그래서 호출이 느리면(--busy-ms) 요청 뒤의 화면이 줄에 쌓이고 답이 늦어지는 것이 그대로 나타난다.
//
// 실행: dotnet run -c Release -- link <finder.onnx> <reader.onnx> <cases.tsv> <결과.tsv> [--workers N] [--interval 초] [--busy-ms X] [--busy-scale K]
//                                      [--ask-busy-ms X] [--miss N] [--stall 초] [--cancel-after 초 [--reask-after 초]] [--log <폴더>] [사례…]
//   --interval   캡처 간격(앱의 타이머는 0.1초)
//   --busy-ms    모듈 호출 하나가 걸리는 시간을 이 값으로 고정한다(안 주면 실제로 걸린 시간 × --busy-scale)
//   --ask-busy-ms X   첫 등록 키부터는 모듈 호출 하나가 걸리는 시간을 이 값으로 고정한다(그 앞의 플레이 중에는 --busy-ms 나 실제 시간).
//                캡처 간격보다 크게 주면 요청 뒤의 화면이 줄에 쌓인다(플레이 중의 근거는 그대로 둔 채 등록 중에만 일이 밀린 상태를 만든다)
//   --miss N     캡처 N 번에 한 번은 화면을 못 얻은 것으로 친다(캡처 실패, 곡 정보가 엇갈림)
//   --stall 초   키를 누른 직후 이만큼은 캡처가 멈춘 것으로 친다(앱이 잠깐 멈춤)
//   --cancel-after 초   키를 누르고 이만큼 뒤에 요청을 취소한다(그때까지 답이 안 났을 때). --reask-after 를 안 주면 그 취소가 이 사례의 답이다
//   --reask-after 초    취소하고 이만큼 뒤에 등록 키를 다시 누른다. 이 사례의 답은 두 번째 요청의 답이다
// 기한 확인: 답이 전해진 시각(deliveredMs. 등록 키부터 답의 알림이 온 때까지 가짜 시계로 흐른 시간. 이 도구가 잰다)이 기한 8초 + 기한 알림 하나를 처리하는 여유 0.5초 + 캡처 간격을 넘으면 "기한 넘김",
//           8초를 넘겨 전해진 답이 Accepted 이면 "기한 뒤 수락"으로 센다. 하나라도 있으면 종료 코드 1.
//   --phase 초   첫 캡처를 이만큼 늦춘다(어느 화면이 캡처되는지가 달라진다. 캡처 시점에 따라 갈리는 일을 다시 만들 때 쓴다)
//   --log        진단 기록을 이 폴더 아래 작업자별 폴더에 남긴다
//   --detect-every N, --part-wait 초   위치 찾기의 설정을 바꾼다(TrackerOptions. 고정 전 탐지 모델을 도는 간격, 일부만 읽는 자리의 고정을 미루는 시간).
//                모듈의 기본값이 아니므로 "어느 줄기가 먼저 조건을 채우는가"를 일부러 바꿔 보는 시험에만 쓴다.
using System.Diagnostics;
using System.Globalization;
using DjmaxScore;

public static class LinkRun
{
    private const long Ticks = 60000;

    /// <summary>
    /// 가짜 시계. 일은 맡은 자리에서 돌리되(모듈의 상태는 그때 바뀐다), 끝났다는 알림은 걸린 시간이 지난 시각에 한다.
    /// 시계를 앞으로 옮길 때(Now 에 값을 넣을 때) 그 사이에 끝난 일의 알림이 끝난 시각 순서대로 간다. 알림 안에서 보는 Now 는 그 일이 끝난 시각이다.
    /// </summary>
    private sealed class VirtualRunner : IScoreRunner
    {
        private long _now;
        private (long At, Action Done)? _pending;
        public long Now { get => _now; set => Advance(value); }
        public long TicksPerSecond => Ticks;
        public double? FixedMs;
        public double Scale = 1.0;
        public long BusyUntil { get; private set; }
        public double WorkMs { get; private set; }
        public bool Busy => _pending is not null;

        public void Run(Action work, Action done)
        {
            if (_pending is not null) throw new InvalidOperationException("앞서 맡긴 일이 끝나기 전에 다음 일을 맡겼다");
            var watch = Stopwatch.StartNew();
            work();
            double ms = FixedMs ?? watch.Elapsed.TotalMilliseconds * Scale;
            WorkMs += ms;
            BusyUntil = _now + (long)(ms * Ticks / 1000);
            _pending = (BusyUntil, done);
        }

        private void Advance(long to)
        {
            // 알림 안에서 다음 일을 맡기면(줄에 선 일) 그 일은 앞의 일이 끝난 시각에 시작한다.
            while (_pending is { } p && p.At <= to)
            {
                _pending = null;
                _now = Math.Max(_now, p.At);
                p.Done();
            }
            _now = Math.Max(_now, to);
        }
    }

    /// <summary>등록 요청의 기한(초)과, 기한을 지켰다고 보는 전달 시각의 한계에 더하는 여유(초. 기한 알림 하나를 모듈이 처리하는 시간).</summary>
    private const double DeadlineSeconds = 8.0, GraceSeconds = 0.5;

    public static void Run(string finderPath, string readerPath, string casesPath, string outPath, string[] rest)
    {
        var inv = CultureInfo.InvariantCulture;
        int workers = 1, miss = 0;
        double interval = 0.1, stall = 0, scale = 1.0, phase = 0;
        double? busyMs = null, askBusyMs = null, cancelAfter = null, reaskAfter = null;
        string? logRoot = null;
        var tracker = new TrackerOptions();
        var filters = new List<string>();
        for (int k = 0; k < rest.Length; k++)
            if (rest[k] == "--workers") workers = int.Parse(rest[++k]);
            else if (rest[k] == "--interval") interval = double.Parse(rest[++k], inv);
            else if (rest[k] == "--busy-ms") busyMs = double.Parse(rest[++k], inv);
            else if (rest[k] == "--busy-scale") scale = double.Parse(rest[++k], inv);
            else if (rest[k] == "--ask-busy-ms") askBusyMs = double.Parse(rest[++k], inv);
            else if (rest[k] == "--miss") miss = int.Parse(rest[++k]);
            else if (rest[k] == "--stall") stall = double.Parse(rest[++k], inv);
            else if (rest[k] == "--phase") phase = double.Parse(rest[++k], inv);
            else if (rest[k] == "--cancel-after") cancelAfter = double.Parse(rest[++k], inv);
            else if (rest[k] == "--reask-after") reaskAfter = double.Parse(rest[++k], inv);
            else if (rest[k] == "--log") logRoot = rest[++k];
            else if (rest[k] == "--detect-every") tracker = tracker with { DetectEvery = int.Parse(rest[++k]) };
            else if (rest[k] == "--part-wait") tracker = tracker with { PartWait = double.Parse(rest[++k], inv) };
            else filters.Add(rest[k]);
        List<CaseInfo> cases = CaseInfo.Load(casesPath).Where(c => filters.Count == 0 || filters.Any(c.Id.Contains)).ToList();
        var rows = new string[cases.Count];
        int done = 0, right = 0, wrong = 0, none = 0, restarted = 0, worker = 0, overdue = 0, lateAccepted = 0;
        long observedAll = 0, droppedAll = 0;
        var gate = new object();
        var watch = Stopwatch.StartNew();
        Parallel.For(0, cases.Count, new ParallelOptions { MaxDegreeOfParallelism = workers }, () =>
        {
            var runner = new VirtualRunner { FixedMs = busyMs, Scale = scale };
            ScoreDiagnostics? log = logRoot is null ? null : new ScoreDiagnostics(Path.Combine(logRoot, "worker-" + Interlocked.Increment(ref worker)), () => DateTimeOffset.UnixEpoch.AddSeconds((double)runner.Now / Ticks));
            var link = new ScoreLink(finderPath, readerPath, runner, log, new ScoreOptions { Tracker = tracker });
            if (!link.Available) throw new InvalidOperationException(link.Unavailable);
            return (link, runner);
        }, (k, _, state) =>
        {
            var (link, runner) = state;
            CaseInfo c = cases[k];
            var feeder = new ModuleRun.Feeder(c);
            GearPosition gear = c.Position == "LEFT" ? GearPosition.Left : c.Position == "CENTER" ? GearPosition.Center : GearPosition.Right;
            long origin = runner.Now = Math.Max(runner.Now, runner.BusyUntil) + 10 * Ticks;       // 작업자가 이어서 쓰는 시계: 사례마다 뒤로 민다
            runner.FixedMs = busyMs;
            double pressAt = c.Press - c.Start;
            ScoreAnswer? answer = null;
            int serialBefore = link.PlaySerial + 1, captures = 0, missed = 0;
            link.NewPlay(c.Id, gear, "case");
            bool asked = false;
            double next = phase;
            double? cancelAt = null, reaskAt = null;
            string firstAnswer = "";
            long pressedTicks = 0;
            double deliveredMs = 0;                                    // 등록 키부터 답의 알림이 온 때까지(가짜 시계)
            void Ask(long pressed)
            {
                pressedTicks = pressed;
                if (askBusyMs is double slow) runner.FixedMs = slow;
                link.BeginRequest(pressed, DeadlineSeconds, a => { answer = a; deliveredMs = (runner.Now - pressedTicks) * 1000.0 / Ticks; });
            }
            long n = 0;
            long? lockedAt = null;
            foreach (Picture band in Video.Bands(c, c.End - c.Start, 0))
            {
                double at = n / RunCheck.VideoFps;
                n++;
                if (at < next - 1e-6) continue;                        // 앱이 캡처하지 않은 화면
                next += interval;
                runner.Now = origin + (long)Math.Round(at * Ticks);
                if (!asked && at >= pressAt - 1e-6)
                {
                    Ask(origin + (long)Math.Round(pressAt * Ticks));
                    asked = true;
                    if (cancelAfter is double ca) cancelAt = pressAt + ca;
                    if (stall > 0) { next = at + stall; link.Poll(); continue; }
                }
                if (cancelAt is double x && at >= x - 1e-6)
                {
                    cancelAt = null;
                    link.CancelRequest();                              // 이미 답이 났으면 아무 일도 없다
                    if (reaskAfter is double ra)
                    {
                        firstAnswer = answer is null ? "" : $"{answer.Result.State}/{answer.Result.Reason}/{deliveredMs.ToString("F0", inv)}";
                        answer = null;
                        reaskAt = at + ra;
                    }
                }
                if (reaskAt is double y && at >= y - 1e-6)
                {
                    reaskAt = null;
                    Ask(runner.Now);
                }
                captures++;
                if (miss > 0 && captures % miss == 0) { missed++; link.Poll(); continue; }
                ScoreFrame f = feeder.Frame(band, 0, 0);
                long now = runner.Now;
                // 화소는 화면마다 사본을 넘긴다. 일이 줄에 서 있는 동안 feeder 가 같은 버퍼에 다음 화면을 쓰기 때문이다
                // (앱도 넘긴 화면을 일이 끝날 때까지 바꾸지 않는다. 일을 맡은 자리에서 바로 돌리던 때에는 사본이 필요 없었다).
                link.Offer(new CapturedFrame(now, now, f.Width, f.Height, f.Stride, f.SourceWidth, f.SourceHeight, f.CropX, f.CropY), (byte[])feeder.Buffer.Clone(), c.Id);
                link.Poll();
                lockedAt ??= link.Location?.LockedAt;
                if (answer is not null && cancelAt is null && reaskAt is null) break;
            }
            if (!asked) Ask(origin + (long)Math.Round(pressAt * Ticks));
            // 녹화가 끝났다. 화면 없이 시계만 캡처 간격으로 흘려보낸다(돌던 일과 줄에 선 일이 끝나고, 기한이 지나 답이 나올 때까지).
            // 일이 밀려 있으면 기한을 한참 넘겨서야 답이 온다. 그것을 그대로 적으려고 넉넉히(가짜 시계로 10분) 기다린다.
            for (int k2 = 0; answer is null && k2 < 600 / interval; k2++) { runner.Now += (long)Math.Round(interval * Ticks); link.Poll(); }
            if (answer is null) throw new InvalidOperationException($"{c.Id}: 10분을 기다려도 답이 오지 않았다");
            int observed = link.Observed, dropped = link.Dropped, restarts = link.PlaySerial - serialBefore;
            link.EndPlay("case-end");
            RegistrationResult r = answer.Result;
            string verdict = r.State != RegistrationStatus.Accepted ? "none" : r.RawScore == c.Expected ? "right" : "wrong";
            bool isOverdue = deliveredMs > (DeadlineSeconds + GraceSeconds + interval) * 1000 + 1;
            bool isLateAccepted = r.State == RegistrationStatus.Accepted && deliveredMs > DeadlineSeconds * 1000 + 1;
            double lockSec = lockedAt is long l ? (double)(l - origin) / Ticks : double.NaN;
            rows[k] = string.Join("\t", c.Id, verdict, r.State, r.Reason, r.RawScore?.ToString() ?? "", c.Expected, r.ElapsedMs.ToString("F0", inv),
                                  double.IsNaN(lockSec) ? "" : lockSec.ToString("F2", inv), observed, dropped, missed, answer.FramesOffered, answer.MaxFrameGapMs.ToString("F0", inv),
                                  r.FramesAfterRequest, r.FramesUnread, r.Play?.PlayReads ?? 0, r.Play?.NoteCount ?? 0, r.Play?.PlayValuesVerified ?? 0, (r.Play?.Agree ?? 0).ToString("F3", inv), restarts,
                                  deliveredMs.ToString("F0", inv), isOverdue ? 1 : 0, isLateAccepted ? 1 : 0, firstAnswer);
            lock (gate)
            {
                done++;
                if (verdict == "right") right++; else if (verdict == "wrong") wrong++; else none++;
                restarted += restarts; observedAll += observed; droppedAll += dropped;
                if (isOverdue) overdue++;
                if (isLateAccepted) lateAccepted++;
                Console.WriteLine($"[{done}/{cases.Count}] {c.Id}: {r.State} {r.Reason} {r.RawScore?.ToString() ?? "-"} (기대 {c.Expected}) 판단 {r.ElapsedMs / 1000:F1}초·전달 {deliveredMs / 1000:F1}초 | " +
                                  (firstAnswer != "" ? $"첫 요청 {firstAnswer}, " : "") + (isOverdue ? "기한 넘김   <<<<<, " : "") + (isLateAccepted ? "기한 뒤 수락   <<<<<, " : "") +
                                  $"고정 {(double.IsNaN(lockSec) ? "못 함" : lockSec.ToString("F1", inv) + "초")}, 넘긴 화면 {observed}·버린 화면 {dropped}·못 얻은 화면 {missed}, " +
                                  $"요청 뒤 화면 {answer.FramesOffered}장(가장 긴 간격 {answer.MaxFrameGapMs:F0}ms), 플레이 중 읽은 값 {r.Play?.PlayReads ?? 0}개" +
                                  (restarts > 0 ? $", 새 판으로 본 횟수 {restarts}   <<<<<" : "") + (verdict == "right" ? "" : "   <<<<<"));
            }
            return state;
        }, state => state.link.Dispose());
        File.WriteAllLines(outPath, new[] { "case\tverdict\tstate\treason\tscore\texpected\telapsedMs\tlockAt\tobserved\tdropped\tmissed\tframesOffered\tmaxGapMs\tframesJudged\tframesUnread\tplayReads\tnotes\tverified\tagree\trestarts\tdeliveredMs\toverdue\tlateAccepted\tfirstAnswer" }.Concat(rows));
        Console.WriteLine($"사례 {cases.Count}개 (연결 코드, 가짜 시계. 캡처 간격 {interval.ToString(inv)}초" + (miss > 0 ? $", {miss}번에 한 번 화면을 못 얻음" : "") + (stall > 0 ? $", 키 직후 {stall.ToString(inv)}초 멈춤" : "") +
                          (busyMs is double b ? $", 호출마다 {b.ToString(inv)}ms" : scale != 1.0 ? $", 걸린 시간 ×{scale.ToString(inv)}" : "") +
                          (askBusyMs is double ab ? $", 등록 키부터 호출마다 {ab.ToString(inv)}ms" : "") +
                          (cancelAfter is double cancel ? $", 키 {cancel.ToString(inv)}초 뒤 취소" + (reaskAfter is double again ? $"·{again.ToString(inv)}초 뒤 다시 요청" : "") : "") +
                          $"): 맞음 {right}, 틀림 {wrong}, 못 냄 {none} | 기한 넘김 {overdue}, 기한 뒤 수락 {lateAccepted} | 넘긴 화면 {observedAll:N0}, 바빠서 버린 화면 {droppedAll:N0}, 새 판으로 잘못 본 횟수 {restarted} | 전체 {watch.Elapsed.TotalSeconds:F0}초 → {outPath}");
        if (overdue + lateAccepted > 0) Environment.ExitCode = 1;
    }
}
