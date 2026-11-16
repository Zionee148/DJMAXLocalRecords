// 판독 모델을 .NET 에서 돌렸을 때 Python 과 같은 숫자열이 나오는지, 한 장 읽는 데 얼마나 걸리는지 확인한다.
//
//   1) 모델만: Python 이 크기를 맞춰 둔 그림(letterboxed.bin)을 그대로 넣는다.
//   2) 크기 맞추기까지: 원본 크기 그림(crops.bin)을 C# 으로 직접 크기를 맞춘 뒤 넣는다. 앱이 실제로 하게 될 일이다.
//
// 실행: dotnet run -c Release -- <reader.onnx> <data\parity 폴더>
using System.Diagnostics;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

const int Height = 64, Width = 256, Blank = 10;

string modelPath = args[0], folder = args[1];
string[] expected = File.ReadAllLines(Path.Combine(folder, "expected.tsv")).Select(l => l.Split('\t')[0]).ToArray();

using var session = new InferenceSession(modelPath);
string inputName = session.InputMetadata.Keys.First();

// ---------- 1) 모델만 ----------
byte[] boxed = File.ReadAllBytes(Path.Combine(folder, "letterboxed.bin"));
int count = boxed.Length / (Height * Width * 3);
int differ = 0;
for (int i = 0; i < count; i++)
{
    string text = Read(boxed.AsSpan(i * Height * Width * 3, Height * Width * 3));
    if (text != expected[i]) differ++;
}
Console.WriteLine($"1) 모델만: 숫자열이 다른 그림 {differ} / {count}");

// ---------- 2) 크기 맞추기까지 ----------
using var reader = new BinaryReader(File.OpenRead(Path.Combine(folder, "crops.bin")));
int crops = reader.ReadInt32();
int differResize = 0, read = 0, differRead = 0;
var examples = new List<string>();
byte[] canvas = new byte[Height * Width * 3];
for (int i = 0; i < crops; i++)
{
    int h = reader.ReadInt32(), w = reader.ReadInt32();
    byte[] bgr = reader.ReadBytes(h * w * 3);
    Letterbox(bgr, h, w, canvas);
    string text = Read(canvas);
    if (expected[i].Length > 0) read++;
    if (text != expected[i])
    {
        differResize++;
        if (expected[i].Length > 0) differRead++;
        if (examples.Count < 8) examples.Add($"{i}: Python '{expected[i]}' / .NET '{text}' ({w}x{h})");
    }
}
Console.WriteLine($"2) 크기 맞추기까지 C# 으로: 숫자열이 다른 그림 {differResize} / {crops} (숫자가 읽힌 {read}장 중에서는 {differRead}장)");
foreach (string e in examples) Console.WriteLine("     " + e);

// ---------- 3) 속도 ----------
for (int i = 0; i < 20; i++) Read(boxed.AsSpan(0, Height * Width * 3));       // 예열
var watch = Stopwatch.StartNew();
const int Rounds = 300;
for (int i = 0; i < Rounds; i++) Read(boxed.AsSpan((i % count) * Height * Width * 3, Height * Width * 3));
Console.WriteLine($"3) 한 장 읽는 시간(모델 실행 + 숫자열 뽑기): {watch.Elapsed.TotalMilliseconds / Rounds:F1} ms, 논리 코어 {Environment.ProcessorCount}개");

using var single = new InferenceSession(modelPath, new SessionOptions { IntraOpNumThreads = 1, InterOpNumThreads = 1 });
float[] buffer = ToTensor(boxed.AsSpan(0, Height * Width * 3));
for (int i = 0; i < 20; i++) Run(single, buffer);
watch.Restart();
for (int i = 0; i < Rounds; i++) Run(single, buffer);
Console.WriteLine($"   스레드 하나만 쓸 때: {watch.Elapsed.TotalMilliseconds / Rounds:F1} ms");

// ------------------------------------------------------------------

string Read(ReadOnlySpan<byte> rgb) => Decode(Run(session, ToTensor(rgb)));

float[] ToTensor(ReadOnlySpan<byte> rgb)
{
    // 그림은 높이·너비·색(RGB) 순서, 모델 입력은 색·높이·너비 순서에 0~1 값
    var data = new float[3 * Height * Width];
    for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
            for (int c = 0; c < 3; c++)
                data[c * Height * Width + y * Width + x] = rgb[(y * Width + x) * 3 + c] / 255f;
    return data;
}

float[] Run(InferenceSession s, float[] data)
{
    var tensor = new DenseTensor<float>(data, new[] { 1, 3, Height, Width });
    using var results = s.Run(new[] { NamedOnnxValue.CreateFromTensor(inputName, tensor) });
    return results.First().AsEnumerable<float>().ToArray();          // [1, 너비/4, 11]
}

string Decode(float[] logits)
{
    // 칸마다 가장 큰 값을 고르고, 같은 글자가 이어지면 하나로, 빈칸은 버린다
    int steps = logits.Length / 11;
    var text = new System.Text.StringBuilder();
    int last = Blank;
    for (int t = 0; t < steps; t++)
    {
        int best = 0;
        for (int c = 1; c < 11; c++)
            if (logits[t * 11 + c] > logits[t * 11 + best]) best = c;
        if (best != last && best != Blank) text.Append((char)('0' + best));
        last = best;
    }
    return text.ToString();
}

void Letterbox(byte[] bgr, int h, int w, byte[] output)
{
    // Python 쪽과 같은 규칙: 비율을 지키며 64x256 안에 맞추고 가운데 놓는다. 늘릴 때는 3차 보간, 줄일 때는 면적 평균.
    Array.Clear(output);
    double scale = Math.Min((double)Height / h, (double)Width / w);
    int nw = Math.Max(1, (int)Math.Round(w * scale, MidpointRounding.ToEven));
    int nh = Math.Max(1, (int)Math.Round(h * scale, MidpointRounding.ToEven));
    float[] resized = scale > 1 ? Cubic(bgr, h, w, nh, nw) : Area(bgr, h, w, nh, nw);
    int top = (Height - nh) / 2, left = (Width - nw) / 2;
    for (int y = 0; y < nh; y++)
        for (int x = 0; x < nw; x++)
            for (int c = 0; c < 3; c++)
            {
                float v = resized[(y * nw + x) * 3 + c];
                output[((top + y) * Width + left + x) * 3 + (2 - c)] = (byte)Math.Clamp((int)MathF.Round(v), 0, 255);   // BGR → RGB
            }
}

float[] Cubic(byte[] src, int h, int w, int nh, int nw)
{
    // OpenCV 의 INTER_CUBIC 과 같은 계수(a = -0.75)와 같은 좌표 규칙. 가장자리는 끝 화소를 되풀이한다.
    (int[] index, float[] weight) Taps(int from, int to)
    {
        var index = new int[to * 4]; var weight = new float[to * 4];
        double ratio = (double)from / to;
        for (int d = 0; d < to; d++)
        {
            double pos = (d + 0.5) * ratio - 0.5;
            int floor = (int)Math.Floor(pos);
            float f = (float)(pos - floor);
            const float A = -0.75f;
            float[] k =
            {
                ((A * (f + 1) - 5 * A) * (f + 1) + 8 * A) * (f + 1) - 4 * A,
                ((A + 2) * f - (A + 3)) * f * f + 1,
                ((A + 2) * (1 - f) - (A + 3)) * (1 - f) * (1 - f) + 1,
                0f,
            };
            k[3] = 1f - k[0] - k[1] - k[2];
            for (int t = 0; t < 4; t++)
            {
                index[d * 4 + t] = Math.Clamp(floor - 1 + t, 0, from - 1);
                weight[d * 4 + t] = k[t];
            }
        }
        return (index, weight);
    }

    var (xi, xw) = Taps(w, nw);
    var (yi, yw) = Taps(h, nh);
    var rows = new float[h * nw * 3];
    for (int y = 0; y < h; y++)
        for (int x = 0; x < nw; x++)
            for (int c = 0; c < 3; c++)
            {
                float sum = 0;
                for (int t = 0; t < 4; t++) sum += src[(y * w + xi[x * 4 + t]) * 3 + c] * xw[x * 4 + t];
                rows[(y * nw + x) * 3 + c] = sum;
            }
    var result = new float[nh * nw * 3];
    for (int y = 0; y < nh; y++)
        for (int x = 0; x < nw; x++)
            for (int c = 0; c < 3; c++)
            {
                float sum = 0;
                for (int t = 0; t < 4; t++) sum += rows[(yi[y * 4 + t] * nw + x) * 3 + c] * yw[y * 4 + t];
                result[(y * nw + x) * 3 + c] = sum;
            }
    return result;
}

float[] Area(byte[] src, int h, int w, int nh, int nw)
{
    // 줄일 때: 새 화소 하나가 덮는 원본 영역의 평균 (걸친 화소는 걸친 만큼만)
    (int[] start, float[][] weight) Spans(int from, int to)
    {
        var start = new int[to]; var weight = new float[to][];
        double ratio = (double)from / to;
        for (int d = 0; d < to; d++)
        {
            double a = d * ratio, b = Math.Min((d + 1) * ratio, from);
            int first = (int)Math.Floor(a), last = Math.Min(from - 1, (int)Math.Ceiling(b) - 1);
            var ws = new float[last - first + 1];
            for (int s = first; s <= last; s++)
                ws[s - first] = (float)((Math.Min(b, s + 1) - Math.Max(a, s)) / (b - a));
            start[d] = first; weight[d] = ws;
        }
        return (start, weight);
    }

    var (xs, xw) = Spans(w, nw);
    var (ys, yw) = Spans(h, nh);
    var rows = new float[h * nw * 3];
    for (int y = 0; y < h; y++)
        for (int x = 0; x < nw; x++)
            for (int c = 0; c < 3; c++)
            {
                float sum = 0;
                for (int t = 0; t < xw[x].Length; t++) sum += src[(y * w + xs[x] + t) * 3 + c] * xw[x][t];
                rows[(y * nw + x) * 3 + c] = sum;
            }
    var result = new float[nh * nw * 3];
    for (int y = 0; y < nh; y++)
        for (int x = 0; x < nw; x++)
            for (int c = 0; c < 3; c++)
            {
                float sum = 0;
                for (int t = 0; t < yw[y].Length; t++) sum += rows[((ys[y] + t) * nw + x) * 3 + c] * yw[y][t];
                result[(y * nw + x) * 3 + c] = sum;
            }
    return result;
}
