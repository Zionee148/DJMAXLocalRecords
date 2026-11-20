// .NET 으로 옮긴 점수 규칙과 등록 판단이 Python 과 같은 답을 내는지 확인한다.
//
// Python(detector.export_judge)은 프레임별 판독(reads.bin)과 시나리오(키 시각, 앱을 켠 시각, 캡처 간격)별 등록 판정 결과(expected.tsv)를 저장한다.
// 이 도구는 같은 판독을 같은 시나리오로 .NET 모듈(PlayLog, RegistrationJudge)에 전달해,
// 상태·사유·점수·후보·걸린 시간·노트 수·통과한 값의 수·통과 비율·마지막 플레이 값·캡처 수를 줄마다 견준다.
//
// 실행: dotnet run -c Release -- <data\parity_judge 폴더>
using System.Diagnostics;
using System.Globalization;
using System.Text;
using DjmaxScore;

// --panel <래더 녹화> <시작 초>: 녹화를 넣는 모든 검사에서, 화면에 그 래더 녹화의 비교 정보판을 붙여서 넣는다(합성 시험. Video.Panel 의 설명)
// --panel-clip <잘라 둔 영상>: 같은 일을 정보판 부분만 미리 잘라 둔 영상으로 한다
int panelAt = Array.IndexOf(args, "--panel");
if (panelAt >= 0)
{
    Video.Panel = (args[panelAt + 1], double.Parse(args[panelAt + 2], CultureInfo.InvariantCulture), false);
    args = args.Where((_, i) => i < panelAt || i > panelAt + 2).ToArray();
}
int clipAt = Array.IndexOf(args, "--panel-clip");
if (clipAt >= 0)
{
    Video.Panel = (args[clipAt + 1], 0.0, true);
    args = args.Where((_, i) => i < clipAt || i > clipAt + 1).ToArray();
}
if (args[0] == "reader") { CheckReader(args[1], args[2]); return; }
if (args[0] == "locate") { LocateCheck.Run(args[1], args[2], args[3], args.Skip(4).ToArray()); return; }
if (args[0] == "run") { RunCheck.Run(args[1], args[2], args[3], args[4], args.Skip(5).ToArray()); return; }
if (args[0] == "module") { ModuleRun.Run(args[1], args[2], args[3], args[4], args.Skip(5).ToArray()); return; }
if (args[0] == "link") { LinkRun.Run(args[1], args[2], args[3], args[4], args.Skip(5).ToArray()); return; }
if (args[0] == "again") { ModuleRun.Again(args[1], args[2], args[3], args[4], args.Skip(5).ToArray()); return; }
if (args[0] == "calls") { ModuleRun.Calls(args[1], args[2], args[3], args[4]); return; }
if (args[0] == "restart") { Environment.ExitCode = RestartCheck.Run(); return; }
if (args[0] == "zone") { Environment.ExitCode = ZoneCheck.Run(); return; }

string folder = args[0];
var cases = new Dictionary<string, (double[] At, int[] Values, bool[] Clear, bool[] Locked, double Press)>();
using (var reader = new BinaryReader(File.OpenRead(Path.Combine(folder, "reads.bin"))))
{
    int count = reader.ReadInt32();
    for (int k = 0; k < count; k++)
    {
        string name = Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));
        int n = reader.ReadInt32();
        double press = reader.ReadDouble();
        var at = new double[n]; var values = new int[n];
        for (int i = 0; i < n; i++) at[i] = reader.ReadDouble();
        for (int i = 0; i < n; i++) values[i] = reader.ReadInt32();
        bool[] clear = reader.ReadBytes(n).Select(b => b != 0).ToArray();
        bool[] locked = reader.ReadBytes(n).Select(b => b != 0).ToArray();
        cases[name] = (at, values, clear, locked, press);
    }
}

var inv = CultureInfo.InvariantCulture;
string[] lines = File.ReadAllLines(Path.Combine(folder, "expected.tsv"));
int differ = 0, accepted = 0;
var byScenario = new Dictionary<string, (int Total, int Differ)>();
var examples = new List<string>();
var watch = Stopwatch.StartNew();
double evidenceMs = 0;
foreach (string line in lines)
{
    string[] f = line.Split('\t');
    var c = cases[f[0]];
    double pressed = c.Press + double.Parse(f[2], inv);
    double? lockFrom = f[3] == "" ? null : c.Press + double.Parse(f[3], inv);
    double capture = double.Parse(f[4], inv);
    bool Locked(int i) => c.Locked[i] && (lockFrom is null || c.At[i] >= lockFrom);

    // 플레이 중: 고정 뒤, 요청 전의 화면을 낮은 빈도로
    var log = new PlayLog();
    for (int i = 0; i < c.At.Length && c.At[i] < pressed; i++)
        if (Locked(i)) log.Offer(c.At[i], c.Values[i], c.Clear[i]);
    var timer = Stopwatch.StartNew();
    var judge = new RegistrationJudge(pressed, log.Evidence());
    evidenceMs += timer.Elapsed.TotalMilliseconds;

    // 요청 뒤: capture 간격의 캡처
    JudgeResult? result = null;
    int lastIndex = -1;
    for (double t = pressed; t <= pressed + 8.0 + 1e-6 && result is null; t += capture)
    {
        int i = LowerBound(c.At, t - 1e-6);
        if (i >= c.At.Length) break;
        if (i == lastIndex) continue;
        lastIndex = i;
        result = judge.Offer(c.At[i], c.Clear[i] ? c.Values[i] : -1, Locked(i));
    }
    result ??= judge.Expire();

    string got = string.Join("\t", result.State, result.Reason, result.Score?.ToString() ?? "", result.Candidate?.ToString() ?? "",
        result.Elapsed.ToString("F6", inv), result.Play.NoteCount, result.Play.PlayValuesVerified, result.Play.Agree.ToString("F6", inv),
        result.Play.LastPlayValue?.ToString() ?? "", result.FramesAfterRequest, result.FramesUnread, result.Basis);
    string want = string.Join("\t", f.Skip(5));
    var tally = byScenario.GetValueOrDefault(f[1]);
    if (got != want)
    {
        differ++; tally.Differ++;
        if (examples.Count < 10) examples.Add($"{f[0]} [{f[1]}]\n     Python: {want}\n     .NET  : {got}");
    }
    if (result.State == RegistrationState.Accepted) accepted++;
    tally.Total++;
    byScenario[f[1]] = tally;
}
Console.WriteLine($"판단 {lines.Length}건(사례 {cases.Count}개) 가운데 Python 과 다른 것 {differ}건 (.NET 이 점수를 낸 것 {accepted}건)");
foreach (var (name, t) in byScenario) Console.WriteLine($"   {name}: {t.Total}건 중 다른 것 {t.Differ}건");
foreach (string e in examples) Console.WriteLine("   " + e);
Console.WriteLine($"걸린 시간: 전체 {watch.Elapsed.TotalSeconds:F1}초, 그중 요청 때 플레이 근거를 만드는 데 건당 {evidenceMs / lines.Length:F1} ms");

// 판독(크기 맞추기 + 모델 + 숫자열·확신도 뽑기 + 빈 화면 판단)이 Python 과 같은가.
// 실행: dotnet run -c Release -- reader <reader.onnx> <data\parity 폴더>
static void CheckReader(string modelPath, string folder)
{
    var inv = CultureInfo.InvariantCulture;
    string[][] expected = File.ReadAllLines(Path.Combine(folder, "expected.tsv")).Select(l => l.Split('\t')).ToArray();
    using var scoreReader = new ScoreReader(modelPath);
    using var reader = new BinaryReader(File.OpenRead(Path.Combine(folder, "crops.bin")));
    int count = reader.ReadInt32();
    int differText = 0, differClear = 0, differBlank = 0, read = 0;
    double worstConfidence = 0, worstContrast = 0;
    var crops = new List<(byte[] Bgr, int H, int W)>();
    for (int i = 0; i < count; i++)
    {
        int h = reader.ReadInt32(), w = reader.ReadInt32();
        byte[] bgr = reader.ReadBytes(h * w * 3);
        crops.Add((bgr, h, w));
        Reading r = scoreReader.Read(bgr, h, w);
        string text = expected[i][0];
        double confidence = double.Parse(expected[i][2], inv), contrast = double.Parse(expected[i][3], inv);
        if (text.Length > 0) read++;
        if (r.Text != text) { differText++; continue; }
        worstConfidence = Math.Max(worstConfidence, Math.Abs(r.Confidence - confidence));
        worstContrast = Math.Max(worstContrast, Math.Abs(r.Contrast - contrast));
        bool blank = contrast < ScoreReader.BlankStd;
        if (r.Blank != blank) differBlank++;
        if (r.Clear != (confidence >= ScoreReader.ReadConfidence && !blank && ScoreReader.ToValue(text) >= 0)) differClear++;
    }
    Console.WriteLine($"판독 {count}장(숫자가 읽힌 {read}장): 숫자열이 다른 그림 {differText}, 확신도의 가장 큰 차이 {worstConfidence:F6}, 밝기 표준편차의 가장 큰 차이 {worstContrast:F4}");
    Console.WriteLine($"   \"또렷이 읽혔는가\"가 갈린 그림 {differClear}, \"빈 화면인가\"가 갈린 그림 {differBlank}");
    for (int i = 0; i < 20; i++) scoreReader.Read(crops[i].Bgr, crops[i].H, crops[i].W);
    var watch = Stopwatch.StartNew();
    const int Rounds = 400;
    for (int i = 0; i < Rounds; i++) { var c = crops[i % crops.Count]; scoreReader.Read(c.Bgr, c.H, c.W); }
    Console.WriteLine($"   한 장 읽는 시간(크기 맞추기 + 모델 + 확신도, 스레드 하나): {watch.Elapsed.TotalMilliseconds / Rounds:F1} ms");
}

static int LowerBound(double[] sorted, double value)
{
    int lo = 0, hi = sorted.Length;
    while (lo < hi)
    {
        int mid = (lo + hi) / 2;
        if (sorted[mid] < value) lo = mid + 1; else hi = mid;
    }
    return lo;
}
