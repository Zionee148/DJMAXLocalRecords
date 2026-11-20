// 그림 다루기: 자르기, 기운 줄 바로 세우기, 크기 바꾸기. Python 쪽(locate_run.part_of·posed, OpenCV)과 같은 규칙을 쓴다.
namespace DjmaxScore;

/// <summary>BGR 그림 한 장(높이 x 너비 x 3).</summary>
public sealed class Picture
{
    public readonly byte[] Data;
    public readonly int Height, Width;

    public Picture(byte[] data, int height, int width) { Data = data; Height = height; Width = width; }

    public bool Empty => Height <= 0 || Width <= 0;

    /// <summary>영역(Box) 둘레에 여백을 두고 자른 조각. 그림 밖은 잘라 낸다.</summary>
    public Picture Part(Box box, int marginX = Margin.X, int marginY = Margin.Y)
    {
        int xa = Math.Max(0, Numbers.Round(box.X0) - marginX), ya = Math.Max(0, Numbers.Round(box.Y0) - marginY);
        int xb = Math.Min(Width, Numbers.Round(box.X1) + marginX), yb = Math.Min(Height, Numbers.Round(box.Y1) + marginY);
        int w = Math.Max(0, xb - xa), h = Math.Max(0, yb - ya);
        var data = new byte[h * w * 3];
        for (int y = 0; y < h; y++)
            Buffer.BlockCopy(Data, ((ya + y) * Width + xa) * 3, data, y * w * 3, w * 3);
        return new Picture(data, h, w);
    }

    /// <summary>자세 가설대로 돌리고 띠만 남긴다. pose 가 없으면 그대로.</summary>
    public Picture Posed(Pose? pose)
    {
        if (pose is null || Empty) return this;
        Pose p = pose.Value;
        int h = Height, w = Width;
        // OpenCV: getRotationMatrix2D((w/2, h*center), -angle, 1) 로 돌린 그림. 결과의 (x, y) 는 원본의 아래 자리에서 가져온다.
        double theta = -p.Angle * Math.PI / 180.0, a = Math.Cos(theta), b = Math.Sin(theta);
        double cx = w / 2.0, cy = h * p.Center;
        double tx = (1 - a) * cx + b * cy, ty = -b * cx + (1 - a) * cy;
        var turned = new byte[h * w * 3];
        Span<float> kx = stackalloc float[4];
        Span<float> ky = stackalloc float[4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double sx = a * x - b * y + tx, sy = b * x + a * y + ty;
                long qx = (long)Math.Round(sx * 32), qy = (long)Math.Round(sy * 32);      // OpenCV 는 소수 부분을 1/32 단위로 끊는다
                int ix = (int)(qx >> 5), iy = (int)(qy >> 5);
                Cubic((qx & 31) / 32f, kx); Cubic((qy & 31) / 32f, ky);
                for (int c = 0; c < 3; c++)
                {
                    float sum = 0;
                    for (int j = 0; j < 4; j++)
                    {
                        int yy = Math.Clamp(iy - 1 + j, 0, h - 1);
                        float row = 0;
                        for (int i = 0; i < 4; i++)
                            row += Data[(yy * w + Math.Clamp(ix - 1 + i, 0, w - 1)) * 3 + c] * kx[i];
                        sum += row * ky[j];
                    }
                    turned[(y * w + x) * 3 + c] = (byte)Math.Clamp((int)MathF.Round(sum), 0, 255);
                }
            }
        int keep = Math.Max(4, (int)(h * p.Band)), top = Math.Max(0, (int)(h * p.Center - keep / 2.0));
        int rows = Math.Max(0, Math.Min(h, top + keep) - top);
        var data = new byte[rows * w * 3];
        Buffer.BlockCopy(turned, top * w * 3, data, 0, rows * w * 3);
        return new Picture(data, rows, w);
    }

    private static void Cubic(float f, Span<float> k)
    {
        const float A = -0.75f;
        k[0] = ((A * (f + 1) - 5 * A) * (f + 1) + 8 * A) * (f + 1) - 4 * A;
        k[1] = ((A + 2) * f - (A + 3)) * f * f + 1;
        k[2] = ((A + 2) * (1 - f) - (A + 3)) * (1 - f) * (1 - f) + 1;
        k[3] = 1f - k[0] - k[1] - k[2];
    }

    /// <summary>크기를 바꾼다(선형 보간, OpenCV 의 INTER_LINEAR 와 같은 좌표 규칙).</summary>
    public Picture Resized(int height, int width)
    {
        if (height == Height && width == Width) return this;
        var data = new byte[height * width * 3];
        double ry = (double)Height / height, rx = (double)Width / width;
        for (int y = 0; y < height; y++)
        {
            double sy = Math.Max(0.0, (y + 0.5) * ry - 0.5);
            int y0 = Math.Min(Height - 1, (int)sy), y1 = Math.Min(Height - 1, y0 + 1);
            double fy = sy - y0;
            for (int x = 0; x < width; x++)
            {
                double sx = Math.Max(0.0, (x + 0.5) * rx - 0.5);
                int x0 = Math.Min(Width - 1, (int)sx), x1 = Math.Min(Width - 1, x0 + 1);
                double fx = sx - x0;
                for (int c = 0; c < 3; c++)
                {
                    double top = Data[(y0 * Width + x0) * 3 + c] * (1 - fx) + Data[(y0 * Width + x1) * 3 + c] * fx;
                    double bottom = Data[(y1 * Width + x0) * 3 + c] * (1 - fx) + Data[(y1 * Width + x1) * 3 + c] * fx;
                    data[(y * width + x) * 3 + c] = (byte)Math.Clamp((int)Math.Round(top * (1 - fy) + bottom * fy), 0, 255);
                }
            }
        }
        return new Picture(data, height, width);
    }
}

/// <summary>판독기에 넣을 조각을 자를 때 영역(Box) 둘레에 두는 여백(화소). 판독기를 이 여백으로 자른 조각으로 학습했다.</summary>
public static class Margin
{
    public const int X = 14, Y = 8;
    public const int RetryX = 4, RetryY = 2;     // 못 읽었을 때 한 번 더 읽는 여백
}

/// <summary>기운 숫자 줄을 위한 자세 가설: 돌릴 각도(도), 띠의 중심(조각 높이의 비율), 띠의 높이(비율).</summary>
public readonly record struct Pose(double Angle, double Center, double Band)
{
    /// <summary>바로 읽히지 않는 자리에서 읽어 보는 자세들. 기어 이름으로 고르지 않는다.</summary>
    public static readonly Pose[] Candidates =
        (from angle in new double[] { -45, -30, -15, 15, 30, 45 } from center in new[] { 0.32, 0.41, 0.5, 0.59, 0.68 } select new Pose(angle, center, 0.36)).ToArray();
}
