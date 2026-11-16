// 같은 곡을 다시 시작했는지를 읽은 점수의 흐름만으로 알아챈다 (기획서 v3 기능 2절. 화면을 따로 인식하지 않는다).
//
// 다시 시작하면 점수가 0 부터 다시 오른다. 플레이 중 0.2초마다 읽어 두는 값에서 아래가 모두 맞으면 새 판으로 본다.
//   1) 지난 값(추적기가 지금 판에서 마지막으로 읽은 "점수로 볼 수 있는 값")의 절반보다 낮은 값에서 시작한다.
//   2) 그 뒤로 또렷이 읽힌 값이 줄지 않고 이어지며, 모두 지난 값보다 낮고 산식에 있고, 그 사이 시간에 오를 수 있는 만큼만 오른다.
//   3) 그렇게 이어진 서로 다른 값이 Need 개가 된다.
//   4) 앞 판에서 맞춘 노트 수를 알면, 이어진 값들이 그 노트 수의 점수 규칙(산식, 증가폭)을 모두 통과한다.
//   5) 시작하는 값이, 지난 값을 읽은 때부터 그 사이 시간에 0 에서 오를 수 있는 만큼을 넘지 않는다(새 판은 0 에서 시작한다).
//   6) 앞 판의 근거(플레이 중 읽어 둔 값 가운데 점수 규칙을 통과한 마지막 값과 그 시각)를 알면, 그것으로 1)과 5)를 다시 보고,
//      시작하는 값이 "앞 판의 점수에서 맨 앞자리만 바뀌거나 빠진 값"으로 설명되지 않아야 한다.
// 못 읽은 화면은 흐름을 끊지 않는다. 또렷이 읽힌 값이 2)를 어기면(지난 값 이상이 다시 읽힘, 줄어듦, 산식에 없음, 너무 많이 오름) 처음부터 다시 센다.
//
// 까닭:
//   - 한두 번의 낮은 오독은 3)을 채우지 못한다.
//   - 맨 앞자리가 가려져 빠지거나 다른 숫자로 읽힌 값("725701" → "425701", "25701")은 낮고, 오르고, 산식에도 그대로 있다
//     (값에서 10,000 의 배수를 빼도 산식 위다: floor((m - kN)·10000/N) = floor(m·10000/N) - 10000k). 2)~4)로는 걸러지지 않는다.
//   - 추적기의 지난 값은 한 번의 높은 오독으로 어긋날 수 있다(값이 한동안 안 읽힌 뒤에는 오를 수 있는 한도가 넓어져 높은 오독이 받아들여진다.
//     그 뒤의 올바른 값은 "줄어든 값"이 되어 지난 값이 한참 고쳐지지 않는다). 그래서 1)과 5)만으로는 모자라고 6)을 둔다.
//     2026-11-14 마지막 앱 경로 시험의 4-14 RIGHT(EXTENSION 2): 로고 사선이 맨 앞자리를 가리는 동안 "665802"가 "865802"로 읽혀 지난 값이 됐고,
//     3초 뒤 다시 가려져 "7xxxxx"가 "4xxxxx"로 1초쯤 읽히자(865802 의 절반 아래) 새 판으로 잘못 봤다. 같은 녹화를 다섯 번 넣어 두 번 그랬다.
//     근거의 마지막 값은 줄지 않는 가장 긴 줄에서 나오므로 한두 번의 높은 오독에 끌려가지 않는다.
//   - 남는 틈: 앞자리가 가려진 값이, 올바른 값을 마지막으로 읽은 뒤 한참(노트 수에 따라 1~4초) 지나서야 읽히기 시작하면 6)으로 가를 수 없다.
//   - 6)의 값: 노트 수가 많은 곡을 아주 빨리 다시 시작하면 처음 몇 초의 값이 "앞자리만 바뀐 값"과 구별되지 않아 알아채는 것이 몇 초 늦어질 수 있다.
//   - 규칙에 맞는다는 것만으로 올바른 판독이나 재시작이 증명되지는 않는다. 그래서 잘못 알아챘을 때의 피해가 작게 했다:
//     새 판으로 보면 그때까지의 점수 이력만 버리고 자리는 그대로 둔다. 근거는 몇 초 안에 다시 쌓인다.
namespace DjmaxScore;

internal sealed class RestartWatch
{
    public const int Need = 6;                            // 이어진 서로 다른 값의 수
    private const int LeadingStep = 100_000;              // 맨 앞자리(십만 자리) 하나의 값

    private readonly List<(double At, int Value)> _run = new();
    private int _distinct;

    /// <summary>새 판으로 보게 한 값들(시각, 값). 새 판의 근거로 넘긴다.</summary>
    public IReadOnlyList<(double At, int Value)> Run => _run;

    public void Reset() { _run.Clear(); _distinct = 0; }

    /// <summary>
    /// 플레이 중 읽어 둔 값 하나. high 는 지금 판의 지난 값(없으면 0), highAt 은 그것을 읽은 시각, notes 는 고정 때 맞춘 노트 수
    /// (진짜 노트 수이거나 그 1/2, 1/5 이다. 그래서 이것으로 잰 한도는 진짜 한도보다 좁지 않다),
    /// evidence 는 앞 판의 값들로 만든 검산 근거를 알려 주는 함수(계산이 무거워 새 판으로 보기 직전에만 부른다). 새 판으로 볼 때 true.
    /// </summary>
    public bool Offer(double at, int value, bool clear, int high, double highAt, int notes, Func<PlayEvidence> evidence, double maxNotesPerSecond)
    {
        if (!clear || value <= 0) return false;
        if (high <= 0 || notes <= 0 || value >= high) { Reset(); return false; }
        bool OnLattice(int v) => ScoreRules.OnLattice(v, notes) || (notes * 2 <= 4000 && ScoreRules.OnLattice(v, notes * 2));
        double fromZero = (maxNotesPerSecond * Math.Max(0.0, at - highAt) + 2) * ScoreRules.MaxScore / notes + 1;
        bool Starts(int v) => (long)v * 2 < high && OnLattice(v) && v <= fromZero;
        if (_run.Count == 0)
        {
            if (Starts(value)) { _run.Add((at, value)); _distinct = 1; }
            return false;
        }
        var (lastAt, last) = _run[^1];
        double limit = (maxNotesPerSecond * Math.Max(0.0, at - lastAt) + 2) * ScoreRules.MaxScore / notes + 1;
        if (value < last || !OnLattice(value) || value - last > limit)
        {
            Reset();
            if (Starts(value)) { _run.Add((at, value)); _distinct = 1; }
            return false;
        }
        if (value > last) _distinct++;
        _run.Add((at, value));
        if (_distinct < Need) return false;
        PlayEvidence play = evidence();
        if (play.NoteCount > 0)
        {
            if (ScoreRules.VerifySequence(_run.Select(x => x.Value).ToArray(), play.NoteCount).Any(k => !k)) { Reset(); return false; }
            if (!NewAgainst(play, notes, maxNotesPerSecond)) { Reset(); return false; }
        }
        return true;
    }

    /// <summary>6) 앞 판의 근거에 견주어도 새 판의 시작인가.</summary>
    private bool NewAgainst(PlayEvidence play, int notes, double maxNotesPerSecond)
    {
        var (firstAt, first) = _run[0];
        // 근거의 마지막 값이 이미 이 줄 안의 것이면(앞 판이 짧아 새 판의 값들이 근거가 됨) 견줄 앞 판의 값이 없다.
        if (play.LastPlayValue is not int level || play.LastPlayAt is not double levelAt || levelAt >= firstAt) return true;
        double gap = firstAt - levelAt;
        if ((long)first * 2 >= level) return false;
        if (first > (maxNotesPerSecond * gap + 2) * ScoreRules.MaxScore / notes + 1) return false;
        // 맨 앞자리만 바뀌거나 빠진 값인가: 십만의 배수를 더하면 앞 판의 점수가 그 사이에 오를 수 있는 범위 안에 들어온다.
        // 그 범위가 십만보다 좁을 때만 본다(넓으면 어떤 값이든 들어맞아 가를 수 없다. 진짜 다시 시작은 불러오는 시간 때문에 대개 여기에 든다).
        double rise = (maxNotesPerSecond * gap + 2) * ScoreRules.MaxScore / play.NoteCount + 1;
        if (rise < LeadingStep)
        {
            long over = (((long)first - level) % LeadingStep + LeadingStep) % LeadingStep;
            if (over <= rise) return false;
        }
        return true;
    }
}
