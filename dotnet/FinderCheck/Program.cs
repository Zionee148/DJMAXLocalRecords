// 숫자 줄 탐지 모델을 .NET 에서 돌렸을 때 Python 과 같은 검출 영역이 나오는지, 띠 한 장을 보는 데 얼마나 걸리는지 확인한다.
//
//   입력: 화면 아래쪽 띠(RGB, 340x960). 출력: 4화소마다 한 칸, 칸마다 [숫자 줄 안인가(로짓), 네 가장자리까지의 거리 ÷ 64].
//   뒤처리(Python 의 find_rows 와 같다): 0.5 이상인 칸을 여덟 방향으로 이어 덩어리로 묶고, 덩어리마다 칸들이 가리키는 사각형의 가운데 값을 검출 영역으로 낸다.
//
// 실행: dotnet run -c Release -- <finder.onnx> <data\parity_finder 폴더>
using System.Diagnostics;
using System.Globalization;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

const int Stride = 4;
const float Scale = 64f;

string modelPath = args[0], folder = args[1];
using var reader = new BinaryReader(File.OpenRead(Path.Combine(folder, "bands.bin")));
int count = reader.ReadInt32(), height = reader.ReadInt32(), width = reader.ReadInt32();
var bands = new byte[count][];
for (int i = 0; i < count; i++) bands[i] = reader.ReadBytes(height * width * 3);
var expected = File.ReadAllLines(Path.Combine(folder, "expected.tsv")).Select(ParseBoxes).ToArray();

using var session = new InferenceSession(modelPath);
string inputName = session.InputMetadata.Keys.First();

// ---------- 1) 같은 검출 영역이 나오는가 ----------
int differCount = 0, boxes = 0;
double worst = 0, worstScore = 0;
for (int i = 0; i < count; i++)
{
    var found = Find(session, bands[i]);
    if (found.Count != expected[i].Count) { differCount++; continue; }
    for (int k = 0; k < found.Count; k++)
    {
        boxes++;
        for (int c = 0; c < 4; c++) worst = Math.Max(worst, Math.Abs(found[k][c] - expected[i][k][c]));
        worstScore = Math.Max(worstScore, Math.Abs(found[k][4] - expected[i][k][4]));
    }
}
Console.WriteLine($"1) 검출 영역 수가 다른 띠 {differCount} / {count}, 검출 영역 좌표의 가장 큰 차이 {worst:F4}화소, 점수의 가장 큰 차이 {worstScore:F5} (견준 검출 영역 {boxes}개)");

// ---------- 2) 속도 ----------
for (int i = 0; i < 10; i++) Find(session, bands[i % count]);
const int Rounds = 100;
var watch = Stopwatch.StartNew();
for (int i = 0; i < Rounds; i++) Find(session, bands[i % count]);
Console.WriteLine($"2) 띠 한 장({width}x{height}) 보는 시간(모델 실행 + 뒤처리): {watch.Elapsed.TotalMilliseconds / Rounds:F1} ms, 논리 코어 {Environment.ProcessorCount}개");

using var single = new InferenceSession(modelPath, new SessionOptions { IntraOpNumThreads = 1, InterOpNumThreads = 1 });
for (int i = 0; i < 10; i++) Find(single, bands[i % count]);
watch.Restart();
for (int i = 0; i < Rounds; i++) Find(single, bands[i % count]);
double one = watch.Elapsed.TotalMilliseconds / Rounds;
watch.Restart();
for (int i = 0; i < Rounds; i++) ToTensor(bands[i % count]);
double prepare = watch.Elapsed.TotalMilliseconds / Rounds;
Console.WriteLine($"   스레드 하나만 쓸 때: {one:F1} ms (그중 그림을 모델 입력으로 바꾸는 데 {prepare:F1} ms)");

// ------------------------------------------------------------------

List<float[]> ParseBoxes(string line)
{
    string[] parts = line.Split('\t');
    int n = int.Parse(parts[0]);
    var list = new List<float[]>();
    for (int k = 0; k < n; k++)
        list.Add(Enumerable.Range(0, 5).Select(c => float.Parse(parts[1 + k * 5 + c], CultureInfo.InvariantCulture)).ToArray());
    return list;
}

float[] ToTensor(byte[] rgb)
{
    var data = new float[3 * height * width];
    int plane = height * width;
    for (int p = 0; p < plane; p++)
    {
        data[p] = rgb[p * 3] / 255f;
        data[plane + p] = rgb[p * 3 + 1] / 255f;
        data[2 * plane + p] = rgb[p * 3 + 2] / 255f;
    }
    return data;
}

List<float[]> Find(InferenceSession s, byte[] rgb)
{
    var tensor = new DenseTensor<float>(ToTensor(rgb), new[] { 1, 3, height, width });
    using var results = s.Run(new[] { NamedOnnxValue.CreateFromTensor(inputName, tensor) });
    var logit = results.First(r => r.Name == "logit").AsTensor<float>();          // [1, h, w]
    var distance = results.First(r => r.Name == "distance").AsTensor<float>();    // [1, 4, h, w]
    int h = logit.Dimensions[1], w = logit.Dimensions[2];
    float[] l = logit.ToArray(), d = distance.ToArray();
    var prob = new float[h * w];
    for (int i = 0; i < prob.Length; i++) prob[i] = 1f / (1f + MathF.Exp(-l[i]));

    // 0.5 이상인 칸을 여덟 방향으로 이어 덩어리로
    var label = new int[h * w];
    var found = new List<float[]>();
    var stack = new Stack<int>();
    var cells = new List<int>();
    int next = 0;
    for (int start = 0; start < prob.Length; start++)
    {
        if (prob[start] < 0.5f || label[start] != 0) continue;
        next++; cells.Clear(); stack.Push(start); label[start] = next;
        while (stack.Count > 0)
        {
            int at = stack.Pop(); cells.Add(at);
            int y = at / w, x = at % w;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int ny = y + dy, nx = x + dx;
                    if (ny < 0 || ny >= h || nx < 0 || nx >= w) continue;
                    int to = ny * w + nx;
                    if (prob[to] >= 0.5f && label[to] == 0) { label[to] = next; stack.Push(to); }
                }
        }
        if (cells.Count < 2) continue;
        var x0 = new float[cells.Count]; var y0 = new float[cells.Count]; var x1 = new float[cells.Count]; var y1 = new float[cells.Count];
        double sum = 0;
        for (int k = 0; k < cells.Count; k++)
        {
            int at = cells[k];
            float cx = (at % w) * Stride + Stride / 2f, cy = (at / w) * Stride + Stride / 2f;
            x0[k] = cx - d[at] * Scale; y0[k] = cy - d[h * w + at] * Scale;
            x1[k] = cx + d[2 * h * w + at] * Scale; y1[k] = cy + d[3 * h * w + at] * Scale;
            sum += prob[at];
        }
        found.Add(new[] { Median(x0), Median(y0), Median(x1), Median(y1), (float)(sum / cells.Count) });
    }
    found.Sort((a, b) => b[4].CompareTo(a[4]));
    return found;
}

float Median(float[] values)
{
    Array.Sort(values);
    int n = values.Length;
    return n % 2 == 1 ? values[n / 2] : (values[n / 2 - 1] + values[n / 2]) / 2f;
}
