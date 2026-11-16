// 숫자 줄 탐지. 화면 아래쪽 띠를 보고 "점수 숫자 줄이 여기 있다"는 네모들을 낸다. Python 의 locate_model.find_rows 와 같다.
//
//   입력: 띠 그림(BGR). 모델에는 RGB 0~1 로 넣는다. 좌표를 입력으로 받지 않고, 기어를 가리지 않는다.
//   출력: 4화소마다 한 칸, 칸마다 [숫자 줄 안인가(로짓), 왼쪽·위·오른쪽·아래 가장자리까지의 거리 ÷ 64].
//   뒤처리: 0.5 이상인 칸을 여덟 방향으로 이어 덩어리로 묶고, 덩어리마다 칸들이 가리키는 네모의 가운데 값을 낸다. 점수가 높은 순서.
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace DjmaxScore;

/// <summary>네모(화소). 어느 좌표계인지는 쓰는 쪽이 정한다(띠 좌표 또는 화면 전체 좌표).</summary>
public readonly record struct Box(double X0, double Y0, double X1, double Y1)
{
    public double Width => X1 - X0;
    public double Height => Y1 - Y0;
    public Box Shift(double dx, double dy) => new(X0 + dx, Y0 + dy, X1 + dx, Y1 + dy);

    /// <summary>이 네모의 가운데가 zone 안에 있는가. zone 의 오른쪽·아래 끝은 넣지 않는다.</summary>
    public bool CenterIn(Box zone)
    {
        double x = (X0 + X1) / 2, y = (Y0 + Y1) / 2;
        return x >= zone.X0 && x < zone.X1 && y >= zone.Y0 && y < zone.Y1;
    }

    /// <summary>겹친 넓이 ÷ 합친 넓이.</summary>
    public static double Iou(Box a, Box b)
    {
        double w = Math.Min(a.X1, b.X1) - Math.Max(a.X0, b.X0), h = Math.Min(a.Y1, b.Y1) - Math.Max(a.Y0, b.Y0);
        if (w <= 0 || h <= 0) return 0.0;
        double inter = w * h;
        return inter / (a.Width * a.Height + b.Width * b.Height - inter);
    }

    /// <summary>겹친 넓이 ÷ 작은 쪽 넓이.</summary>
    public static double Overlap(Box a, Box b)
    {
        double w = Math.Min(a.X1, b.X1) - Math.Max(a.X0, b.X0), h = Math.Min(a.Y1, b.Y1) - Math.Max(a.Y0, b.Y0);
        if (w <= 0 || h <= 0) return 0.0;
        return w * h / Math.Min(a.Width * a.Height, b.Width * b.Height);
    }
}

public sealed class RowFinder : IDisposable
{
    public const int Stride = 4;
    public const float Scale = 64f;

    private readonly InferenceSession _session;
    private readonly string _input;
    private float[] _tensor = Array.Empty<float>();

    public RowFinder(string modelPath, int threads = 1)
    {
        var options = new SessionOptions();
        if (threads > 0) { options.IntraOpNumThreads = threads; options.InterOpNumThreads = threads; }
        _session = new InferenceSession(modelPath, options);
        _input = _session.InputMetadata.Keys.First();
    }

    public void Dispose() => _session.Dispose();

    /// <summary>띠 그림(BGR, 높이 h, 너비 w) → (네모(띠 좌표), 점수) 목록. 점수가 높은 순서.</summary>
    public List<(Box Box, double Score)> Find(ReadOnlySpan<byte> bgr, int height, int width, float threshold = 0.5f)
    {
        int plane = height * width;
        if (_tensor.Length != 3 * plane) _tensor = new float[3 * plane];
        for (int p = 0; p < plane; p++)
        {
            _tensor[p] = bgr[p * 3 + 2] / 255f;
            _tensor[plane + p] = bgr[p * 3 + 1] / 255f;
            _tensor[2 * plane + p] = bgr[p * 3] / 255f;
        }
        var tensor = new DenseTensor<float>(_tensor, new[] { 1, 3, height, width });
        using var results = _session.Run(new[] { NamedOnnxValue.CreateFromTensor(_input, tensor) });
        var logit = results.First(r => r.Name == "logit").AsTensor<float>();          // [1, h, w]
        var distance = results.First(r => r.Name == "distance").AsTensor<float>();    // [1, 4, h, w]
        int h = logit.Dimensions[1], w = logit.Dimensions[2], cellsTotal = h * w;
        float[] l = logit.ToArray(), d = distance.ToArray();
        var prob = new float[cellsTotal];
        for (int i = 0; i < cellsTotal; i++) prob[i] = 1f / (1f + MathF.Exp(-l[i]));

        var seen = new bool[cellsTotal];
        var found = new List<(Box, double)>();
        var stack = new Stack<int>();
        var cells = new List<int>();
        for (int start = 0; start < cellsTotal; start++)
        {
            if (prob[start] < threshold || seen[start]) continue;
            cells.Clear(); stack.Push(start); seen[start] = true;
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
                        if (prob[to] >= threshold && !seen[to]) { seen[to] = true; stack.Push(to); }
                    }
            }
            if (cells.Count < 2) continue;
            int n = cells.Count;
            var x0 = new double[n]; var y0 = new double[n]; var x1 = new double[n]; var y1 = new double[n];
            double sum = 0;
            for (int k = 0; k < n; k++)
            {
                int at = cells[k];
                double cx = (at % w) * Stride + Stride / 2.0, cy = (at / w) * Stride + Stride / 2.0;
                x0[k] = cx - d[at] * Scale; y0[k] = cy - d[cellsTotal + at] * Scale;
                x1[k] = cx + d[2 * cellsTotal + at] * Scale; y1[k] = cy + d[3 * cellsTotal + at] * Scale;
                sum += prob[at];
            }
            found.Add((new Box(Numbers.Median(x0), Numbers.Median(y0), Numbers.Median(x1), Numbers.Median(y1)), sum / n));
        }
        found.Sort((a, b) => b.Item2.CompareTo(a.Item2));
        return found;
    }
}

public static class Numbers
{
    /// <summary>가운데 값(짝수 개면 가운데 둘의 평균). 배열을 정렬한다.</summary>
    public static double Median(double[] values)
    {
        Array.Sort(values);
        int n = values.Length;
        return n % 2 == 1 ? values[n / 2] : (values[n / 2 - 1] + values[n / 2]) / 2.0;
    }

    /// <summary>numpy.percentile 과 같은 방식(선형 보간)의 백분위.</summary>
    public static double Percentile(IEnumerable<double> values, double percent)
    {
        double[] sorted = values.OrderBy(v => v).ToArray();
        if (sorted.Length == 0) return double.NaN;
        double rank = (sorted.Length - 1) * percent / 100.0;
        int lo = (int)Math.Floor(rank), hi = Math.Min(sorted.Length - 1, lo + 1);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (rank - lo);
    }

    /// <summary>Python 의 round 와 같은 반올림(딱 절반이면 짝수 쪽).</summary>
    public static int Round(double value) => (int)Math.Round(value, MidpointRounding.ToEven);
}
