// 숫자열 판독. 점수 영역을 잘라 낸 그림 하나를 받아 숫자열, 값, 확신도를 낸다. Python 의 build_dataset.letterbox + model.decode 와 같다.
//
//   - 크기 맞추기: 비율을 지키며 64x256 안에 맞추고 가운데 놓는다. 늘릴 때는 3차 보간(a = -0.75), 줄일 때는 면적 평균.
//     Python(OpenCV)과 같은 방식이어야 한다. 선형 보간으로 바꾸면 숫자열이 달라지는 그림이 생긴다.
//   - 확신도: 모델이 그 숫자열에 준 확률(글자 경계가 한 칸 밀리는 경우까지 모두 더한 값). 정답일 확률로 보정한 수치가 아니다.
//   - 빈 화면: 자른 그림의 밝기 표준편차가 BlankStd 보다 작으면 아무것도 없는 화면으로 본다.
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace DjmaxScore;

/// <summary>판독 하나. Value 는 점수일 수 없으면 -1.</summary>
public readonly record struct Reading(string Text, int Value, double Confidence, double Contrast)
{
    public bool Blank => Contrast < ScoreReader.BlankStd;
    /// <summary>또렷이 읽혔는가(값 0 포함). 맞는 값인지는 규칙 검사가 따로 본다.</summary>
    public bool Clear => Confidence >= ScoreReader.ReadConfidence && !Blank && Value >= 0;
}

public sealed class ScoreReader : IDisposable
{
    public const int Height = 64, Width = 256, Blank = 10, Classes = 11;
    public const double ReadConfidence = 0.5;
    public const double BlankStd = 6.0;

    private readonly InferenceSession _session;
    private readonly string _input;
    private readonly float[] _tensor = new float[3 * Height * Width];
    private readonly byte[] _canvas = new byte[Height * Width * 3];

    public ScoreReader(string modelPath, int threads = 1)
    {
        var options = new SessionOptions();
        if (threads > 0) { options.IntraOpNumThreads = threads; options.InterOpNumThreads = threads; }
        _session = new InferenceSession(modelPath, options);
        _input = _session.InputMetadata.Keys.First();
    }

    public void Dispose() => _session.Dispose();

    public Reading Read(Picture part) => Read(part.Data, part.Height, part.Width);

    /// <summary>잘라 낸 그림(BGR, 높이 h, 너비 w)을 읽는다.</summary>
    public Reading Read(ReadOnlySpan<byte> bgr, int h, int w)
    {
        if (h <= 0 || w <= 0) return new Reading("", -1, 0.0, 0.0);
        double contrast = Contrast(bgr, h, w);
        Letterbox(bgr, h, w, _canvas);
        int plane = Height * Width;
        for (int p = 0; p < plane; p++)
        {
            _tensor[p] = _canvas[p * 3] / 255f;
            _tensor[plane + p] = _canvas[p * 3 + 1] / 255f;
            _tensor[2 * plane + p] = _canvas[p * 3 + 2] / 255f;
        }
        var tensor = new DenseTensor<float>(_tensor, new[] { 1, 3, Height, Width });
        using var results = _session.Run(new[] { NamedOnnxValue.CreateFromTensor(_input, tensor) });
        float[] logits = results.First().AsEnumerable<float>().ToArray();          // [1, 너비/4, 11]
        (string text, double confidence) = Decode(logits);
        return new Reading(text, ToValue(text), confidence, contrast);
    }

    /// <summary>읽힌 숫자열을 점수로. 점수일 수 없으면 -1.</summary>
    public static int ToValue(string text)
    {
        if (text.Length == 0 || text.Length > 7 || (text[0] == '0' && text.Length > 1)) return -1;
        int v = int.Parse(text);
        return v <= ScoreRules.MaxScore ? v : -1;
    }

    /// <summary>가장 그럴듯한 숫자열과, 모델이 그 숫자열에 준 확률.</summary>
    public static (string Text, double Confidence) Decode(float[] logits)
    {
        int steps = logits.Length / Classes;
        var logp = new double[steps * Classes];
        var digits = new List<int>();
        int last = Blank;
        for (int t = 0; t < steps; t++)
        {
            double max = double.NegativeInfinity;
            int best = 0;
            for (int c = 0; c < Classes; c++)
            {
                double v = logits[t * Classes + c];
                if (v > max) { max = v; best = c; }
            }
            double sum = 0;
            for (int c = 0; c < Classes; c++) sum += Math.Exp(logits[t * Classes + c] - max);
            double norm = max + Math.Log(sum);
            for (int c = 0; c < Classes; c++) logp[t * Classes + c] = logits[t * Classes + c] - norm;
            if (best != last && best != Blank) digits.Add(best);
            last = best;
        }
        // 그 숫자열이 나오는 모든 경로의 확률을 더한다 (사이사이에 빈칸을 끼운 줄을 따라가는 앞으로 셈)
        int n = digits.Count, size = 2 * n + 1;
        var alpha = new double[size];
        var next = new double[size];
        Array.Fill(alpha, double.NegativeInfinity);
        alpha[0] = logp[Blank];
        if (size > 1) alpha[1] = logp[digits[0]];
        for (int t = 1; t < steps; t++)
        {
            for (int s = 0; s < size; s++)
            {
                int label = s % 2 == 0 ? Blank : digits[s / 2];
                double a = alpha[s];
                if (s > 0) a = LogAdd(a, alpha[s - 1]);
                if (s > 1 && s % 2 == 1 && digits[s / 2] != digits[s / 2 - 1]) a = LogAdd(a, alpha[s - 2]);
                next[s] = a + logp[t * Classes + label];
            }
            (alpha, next) = (next, alpha);
        }
        double total = size > 1 ? LogAdd(alpha[size - 1], alpha[size - 2]) : alpha[0];
        var text = new string(digits.Select(d => (char)('0' + d)).ToArray());
        return (text, Math.Exp(total));
    }

    private static double LogAdd(double a, double b)
    {
        if (double.IsNegativeInfinity(a)) return b;
        if (double.IsNegativeInfinity(b)) return a;
        double hi = Math.Max(a, b);
        return hi + Math.Log(Math.Exp(a - hi) + Math.Exp(b - hi));
    }

    /// <summary>밝기(회색) 값의 표준편차. OpenCV 의 BGR→회색 변환과 같은 정수 계산을 쓴다.</summary>
    public static double Contrast(ReadOnlySpan<byte> bgr, int h, int w)
    {
        int n = h * w;
        double sum = 0, squares = 0;
        for (int p = 0; p < n; p++)
        {
            int gray = (bgr[p * 3] * 1868 + bgr[p * 3 + 1] * 9617 + bgr[p * 3 + 2] * 4899 + 8192) >> 14;
            sum += gray; squares += (double)gray * gray;
        }
        double mean = sum / n;
        return Math.Sqrt(Math.Max(0.0, squares / n - mean * mean));
    }

    /// <summary>비율을 지키며 64x256 안에 맞추고 가운데 놓는다. 출력은 RGB.</summary>
    public static void Letterbox(ReadOnlySpan<byte> bgr, int h, int w, byte[] output)
    {
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

    // OpenCV 의 INTER_CUBIC 과 같은 계수(a = -0.75)와 같은 좌표 규칙. 가장자리는 끝 화소를 되풀이한다.
    private static float[] Cubic(ReadOnlySpan<byte> src, int h, int w, int nh, int nw)
    {
        static (int[] Index, float[] Weight) Taps(int from, int to)
        {
            var index = new int[to * 4]; var weight = new float[to * 4];
            double ratio = (double)from / to;
            for (int d = 0; d < to; d++)
            {
                double pos = (d + 0.5) * ratio - 0.5;
                int floor = (int)Math.Floor(pos);
                float f = (float)(pos - floor);
                const float A = -0.75f;
                float k0 = ((A * (f + 1) - 5 * A) * (f + 1) + 8 * A) * (f + 1) - 4 * A;
                float k1 = ((A + 2) * f - (A + 3)) * f * f + 1;
                float k2 = ((A + 2) * (1 - f) - (A + 3)) * (1 - f) * (1 - f) + 1;
                float[] k = { k0, k1, k2, 1f - k0 - k1 - k2 };
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

    // 줄일 때: 새 화소 하나가 덮는 원본 영역의 평균 (걸친 화소는 걸친 만큼만)
    private static float[] Area(ReadOnlySpan<byte> src, int h, int w, int nh, int nw)
    {
        static (int[] Start, float[][] Weight) Spans(int from, int to)
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
}
