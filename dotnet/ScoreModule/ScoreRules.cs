// 점수 규칙 검사. Python 의 detector\score_rules.py 를 그대로 옮긴 것이다 (같은 입력에 같은 답이 나와야 한다).
//
// DJMAX RESPECT V 의 점수는 노트마다 판정 100/90/.../10/1/0 % 가 더해진 누적 합 m 으로 정해진다.
//
//     score = floor(m * 1,000,000 / (notes * 100))
//
// 읽은 값이 이 규칙에 맞는지 "검사"만 한다. 값을 추정해서 만들어 내지 않는다.
namespace DjmaxScore;

public static class ScoreRules
{
    public const int MaxScore = 1_000_000;
    private const int BreakHeal = 3;      // 이웃한 두 값의 증가폭이 점수 규칙을 벗어난 구간: 한쪽에서 값을 이만큼까지 버려 앞뒤가 규칙대로 이어지면 그 값들을 오독으로 보고 버린다

    /// <summary>score 가 규칙으로 만들어질 수 있으면 판정 누적 합 m(1% 단위), 아니면 null.</summary>
    public static long? LatticeM(long score, long notes)
    {
        if (score < 0 || score > MaxScore) return null;
        long den = notes * 100;
        long m = (score * den + MaxScore - 1) / MaxScore;
        return (m * MaxScore) / den == score && m <= den ? m : null;
    }

    public static bool OnLattice(long score, long notes) => LatticeM(score, notes) is not null;

    /// <summary>두 값 사이의 증가량(1% 단위)이 판정들의 합으로 설명되는가. 한 구간에 1% 판정은 두 번까지만 인정한다.</summary>
    public static bool StepOk(long dm) => dm >= 1 && dm % 10 <= 2;

    /// <summary>읽힌 값들만으로 그 판의 노트 수를 맞춘다. 근거가 부족하면 null.</summary>
    public static int? FitNotes(IEnumerable<int> values, int lo = 50, int hi = 4000, int minValues = 15, double minRate = 0.7)
    {
        long[] v = values.Where(x => x > 0 && x <= MaxScore).Distinct().OrderBy(x => x).Select(x => (long)x).ToArray();
        if (v.Length < minValues) return null;
        var rate = new double[hi - lo + 1];
        int pick = 0;
        double best = double.NegativeInfinity;
        for (int n = lo; n <= hi; n++)
        {
            long den = n * 100L;
            int hits = 0;
            foreach (long x in v)
                if ((((x * den + MaxScore - 1) / MaxScore) * MaxScore) / den == x) hits++;
            rate[n - lo] = (double)hits / v.Length;
            // 노트 수가 클수록 격자가 촘촘해져 틀린 값도 우연히 맞는다. 그 몫(우연 적중률)을 빼고 견준다.
            double score = rate[n - lo] - Math.Min(1.0, n / 10000.0);
            if (score > best) { best = score; pick = n; }
        }
        int notes = pick;
        // 판정이 모두 10% 단위인 동안에는 누적 합이 항상 짝수라서 "노트 수의 절반"도 똑같이 맞는다.
        // 그 경우 절반 쪽에서는 누적 합의 끝자리가 5 인 값이 나오는데, 5% 판정은 없으므로 두 배가 맞다.
        int fives = 0;
        foreach (long x in v)
        {
            long m = (x * notes * 100 + MaxScore - 1) / MaxScore;
            if ((m * MaxScore) / (notes * 100L) == x && m % 10 == 5) fives++;
        }
        if (fives >= Math.Max(3, 0.10 * v.Length) && notes * 2 <= hi) notes *= 2;
        double finalRate = rate[notes - lo];
        double chance = Math.Min(1.0, notes / 10000.0);
        if (finalRate < minRate || finalRate < chance + 0.4) return null;     // 우연으로 설명되는 수준이면 믿지 않는다
        return notes;
    }

    /// <summary>FitNotes 가 고른 노트 수가 진짜의 1/2, 1/5, 1/10 일 때 바로잡는다. values 는 시간 순서의 판독값(-1 = 못 읽음).</summary>
    public static int RefineNotes(IReadOnlyList<int> values, int notes, int limit = 4000)
    {
        int[] scales = { 1, 2, 5, 10 };
        var kept = new Dictionary<int, int>();
        foreach (int k in scales)
            if (k == 1 || notes * k <= limit) kept[k] = VerifySequence(values, notes * k).Count(x => x);
        int best = kept.Values.Max();
        if (kept[1] >= 0.6 * best) return notes;
        return notes * scales.Where(k => kept.ContainsKey(k) && kept[k] >= 0.9 * best).Min();
    }

    /// <summary>가장 긴 "줄지 않는" 부분열의 위치.</summary>
    public static List<int> Lnds(IReadOnlyList<long> values)
    {
        var tails = new List<long>();
        var tailIndex = new List<int>();
        var prev = new int[values.Count];
        for (int i = 0; i < values.Count; i++)
        {
            long x = values[i];
            int lo = 0, hi = tails.Count;                 // bisect_right: x 보다 큰 첫 자리
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (x < tails[mid]) hi = mid; else lo = mid + 1;
            }
            int k = lo;
            prev[i] = k > 0 ? tailIndex[k - 1] : -1;
            if (k == tails.Count) { tails.Add(x); tailIndex.Add(i); }
            else { tails[k] = x; tailIndex[k] = i; }
        }
        var result = new List<int>();
        int at = tailIndex.Count > 0 ? tailIndex[^1] : -1;
        while (at >= 0) { result.Add(at); at = prev[at]; }
        result.Reverse();
        return result;
    }

    /// <summary>
    /// 시간 순서로 늘어선 판독값(-1 = 못 읽음) 중 규칙을 모두 통과한 것을 true 로 표시한다.
    /// 1) 산식 격자 위  2) 시간 순서대로 줄지 않음  3) 이웃 값과의 증가폭이 판정 합으로 설명됨.
    /// 통과하지 못한 값은 버릴 뿐, 다른 값으로 바꾸지 않는다.
    /// tolerant(등록 판단의 검산 근거에만 쓴다. 2026-11-14 에 정함): 이웃한 두 값의 증가폭이 점수 규칙을 벗어났는데 둘 중 어느 한 값을 빼도 규칙에 맞지 않을 때,
    /// 한쪽에서 값을 BreakHeal 개까지 버려 앞뒤가 규칙대로 이어지면 그 값들을 오독으로 보고 버리고, 그래도 맞지 않으면 그 구간을 "끊긴 자리"로 받아들여
    /// 양쪽을 모두 둔다(받아들이는 수에 상한은 없다: 맥스 판정으로 플레이하면 1% 판정이 자주 나온다). 0.2초마다 뽑은 값 사이에는 1% 판정이 세 번 이상 몰릴 수 있고(Boom!),
    /// tolerant 가 아니면 그런 구간 하나 때문에 곡의 절반이 버려진다. 노트 수를 고를 때(RefineNotes)에는 쓰지 않는다: 거기서는 그 "크게 버려짐"이 신호다.
    /// </summary>
    public static bool[] VerifySequence(IReadOnlyList<int> values, int notes, bool tolerant = false)
    {
        var keep = new bool[values.Count];
        var index = new List<int>();
        for (int i = 0; i < values.Count; i++)
            if (values[i] >= 0 && OnLattice(values[i], notes)) index.Add(i);
        if (index.Count == 0) return keep;
        List<int> chain = Lnds(index.Select(i => (long)values[i]).ToArray()).Select(k => index[k]).ToList();
        var support = new Dictionary<int, int>();
        foreach (int i in chain) support[values[i]] = support.GetValueOrDefault(values[i]) + 1;
        var dropped = new HashSet<int>();
        var accepted = new HashSet<(int, int)>();
        while (true)
        {
            int[] vals = support.Keys.Where(x => !dropped.Contains(x)).OrderBy(x => x).ToArray();
            long[] ms = vals.Select(x => LatticeM(x, notes)!.Value).ToArray();
            bool PairOk(int a, int b) => StepOk(ms[b] - ms[a]);
            int bad = -1;
            for (int k = 1; k < vals.Length; k++)
                if (!PairOk(k - 1, k) && !accepted.Contains((vals[k - 1], vals[k]))) { bad = k; break; }
            if (bad < 0) break;
            int va = vals[bad - 1], vb = vals[bad];
            bool okWithoutB = bad + 1 >= vals.Length || PairOk(bad - 1, bad + 1);
            bool okWithoutA = bad - 2 < 0 || PairOk(bad - 2, bad);
            if (okWithoutB && !okWithoutA) dropped.Add(vb);
            else if (okWithoutA && !okWithoutB) dropped.Add(va);
            else if (tolerant && !okWithoutA && !okWithoutB)
            {
                int right = 0, left = 0;                 // 이어 붙이려고 버릴 값의 수(0 = 못 이음)
                for (int j = 2; j <= BreakHeal && right == 0; j++)
                    if (bad + j >= vals.Length || PairOk(bad - 1, bad + j)) right = j;
                for (int j = 2; j <= BreakHeal && left == 0; j++)
                    if (bad - 1 - j < 0 || PairOk(bad - 1 - j, bad)) left = j;
                if (right == 0 && left == 0) { accepted.Add((va, vb)); continue; }
                int[] after = right > 0 ? vals[bad..Math.Min(vals.Length, bad + right)] : Array.Empty<int>();
                int[] before = left > 0 ? vals[Math.Max(0, bad - left)..bad] : Array.Empty<int>();
                bool dropAfter = left == 0
                    || (right > 0 && (after.Length, after.Sum(x => support[x])).CompareTo((before.Length, before.Sum(x => support[x]))) <= 0);   // 버릴 값이 적은 쪽, 같으면 그 값들이 읽힌 횟수(support)가 적은 쪽, 그것도 같으면 뒤쪽
                foreach (int x in dropAfter ? after : before) dropped.Add(x);
            }
            else if (support[va] < support[vb]) dropped.Add(va);
            else if (support[vb] < support[va]) dropped.Add(vb);
            else { dropped.Add(va); dropped.Add(vb); }
        }
        foreach (int i in chain)
            if (!dropped.Contains(values[i])) keep[i] = true;
        return keep;
    }
}
