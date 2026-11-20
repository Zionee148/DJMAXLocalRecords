// 새 판 알아채기 규칙(RestartWatch)만 따로 본다: 만든 연속 판독값을 넣어, 알아채야 할 때와 알아채면 안 될 때를 확인한다.
// 영상도 모델도 쓰지 않는다. 규칙을 고칠 때 먼저 돌린다.
//
// 실행: dotnet run -c Release -- restart
using DjmaxScore;

public static class RestartCheck
{
    /// <summary>판정 누적 합 m(1% 단위)의 점수.</summary>
    private static int Score(int m, int notes) => (int)((long)m * 10000 / notes);

    /// <summary>앞 판의 근거: 노트 수와, 규칙을 통과한 마지막 값·그 시각.</summary>
    private static PlayEvidence Known(int notes, int level, double levelAt) => new(300, notes, 280, 0.93, level, levelAt);

    private static readonly PlayEvidence Unknown = new(0, 0, 0, 0.0, null, null);

    public static int Run()
    {
        int bad = 0;
        void Check(string name, bool expected, int notes, int high, double highAt, PlayEvidence play, IEnumerable<(double At, int Value)> values)
        {
            var watch = new RestartWatch();
            int seen = 0;
            bool got = false;
            foreach (var (at, value) in values)
            {
                seen++;
                if (watch.Offer(at, value, true, high, highAt, notes, () => play, 50)) { got = true; break; }
            }
            bool ok = got == expected;
            if (!ok) bad++;
            Console.WriteLine($"{(ok ? "맞음" : "틀림 <<<<<")} | {name}: {(got ? $"새 판으로 봄(값 {seen}개째)" : "새 판으로 보지 않음")} (기대: {(expected ? "봄" : "보지 않음")})");
        }
        IEnumerable<(double, int)> Every(double from, double step, IEnumerable<int> values) => values.Select((v, k) => (from + step * k, v));
        IEnumerable<(double, int)> Rising(double from, int count, int notes, int start = 0, int perStep = 300) =>
            Enumerable.Range(1, count).Select(k => (from + 0.2 * k, Score(start + perStep * k, notes)));

        Console.WriteLine("--- 가. 2026-11-14 마지막 앱 경로 시험의 4-14 RIGHT(EXTENSION 2. 노트 798, 고정 때 399). 진단 기록에 남은 값 그대로");
        // 추적기의 지난 값이 높은 오독 865802(63.2초쯤, 진짜는 665802)로 어긋나 있었고, 66.7초부터 맨 앞자리 7 이 4 로 읽혔다.
        int[] four = { 419561, 424448, 426954, 430714, 435726, 439486 };
        Check("지난 값이 높은 오독으로 어긋난 뒤 앞자리가 4 로 읽힘, 앞 판의 근거 있음(마지막 값 719561)", false, 399, 865802, 63.2, Known(798, 719561, 66.48), Every(66.70, 0.22, four));
        Check("같은 값들, 앞 판의 근거를 모를 때(근거가 막았음을 보인다. 남는 틈)", true, 399, 865802, 63.2, Unknown, Every(66.70, 0.22, four));

        Console.WriteLine("--- 나. 맨 앞자리가 바뀌거나 빠진 값 (진짜 점수 719561 → 739486 에서 십만의 배수를 뺀 것)");
        int[] one = four.Select(v => v - 300000).ToArray(), dropped = four.Select(v => v - 400000).ToArray();
        Check("앞자리가 1 로 읽힘, 방금까지 읽던 점수 바로 뒤", false, 399, 719561, 66.48, Known(798, 719561, 66.48), Every(66.70, 0.22, one));
        Check("앞자리가 1 로 읽힘, 근거를 모를 때(시작하는 값의 한도가 막는다)", false, 399, 719561, 66.48, Unknown, Every(66.70, 0.22, one));
        Check("앞자리가 빠짐(다섯 자리), 방금까지 읽던 점수 바로 뒤", false, 399, 719561, 66.48, Known(798, 719561, 66.48), Every(66.70, 0.22, dropped));
        Check("앞자리가 빠짐, 근거를 모를 때(남는 틈)", true, 399, 719561, 66.48, Unknown, Every(66.70, 0.22, dropped));
        Check("앞자리가 빠짐, 올바른 값을 읽은 지 20초 뒤(남는 틈)", true, 399, 719561, 46.7, Known(798, 719561, 46.7), Every(66.70, 0.22, dropped));

        Console.WriteLine("--- 다. 진짜 다시 시작 (0 에서 오른다. 0.2초마다 노트 3개씩)");
        Check("5초 뒤, 근거 있음", true, 399, 719561, 66.65, Known(798, 719561, 66.65), Rising(71.65, 8, 798));
        Check("5초 뒤, 근거를 모를 때", true, 399, 719561, 66.65, Unknown, Rising(71.65, 8, 798));
        Check("5초 뒤, 진짜 노트 수로 고정된 경우", true, 798, 719561, 66.65, Known(798, 719561, 66.65), Rising(71.65, 8, 798));
        Check("새 판의 앞부분을 못 읽고 20초 뒤 큰 값부터", true, 399, 719561, 66.65, Known(798, 719561, 66.65), Rising(86.65, 8, 798, 20000));
        Check("최종 점수 뒤 8초", true, 399, 992493, 88.9, Known(798, 992493, 88.9), Rising(96.9, 8, 798, 0, 200));
        Check("지난 값이 높은 오독으로 어긋나 있어도(865802) 진짜 다시 시작은 알아챔", true, 399, 865802, 63.2, Known(798, 719561, 66.65), Rising(71.65, 8, 798));
        Check("앞 판이 짧아 근거가 이미 새 판의 값을 따라갈 때", true, 399, 80000, 20.0, Known(798, Score(1500, 798), 26.0), Rising(25.0, 8, 798));
        // 노트가 많은 곡(2000)을 2초 만에 다시 시작: 처음에는 앞자리만 바뀐 값과 가를 수 없어 미뤄지고, 몇 초 뒤에 알아챈다.
        Check("노트 2000, 2초 뒤(처음 여섯 값으로는 못 가름)", false, 2000, 500000, 60.0, Known(2000, 500000, 60.0), Rising(62.0, 6, 2000));
        Check("노트 2000, 2초 뒤(값 서른 개 안에는 알아챔)", true, 2000, 500000, 60.0, Known(2000, 500000, 60.0), Rising(62.0, 30, 2000));

        Console.WriteLine("--- 라. 그 밖");
        Check("낮은 오독 두 번", false, 399, 719561, 66.65, Unknown, new[] { (66.9, 19561), (67.1, 24448) });
        Check("오르던 낮은 값 사이에 지난 값 이상이 다시 읽힘", false, 399, 719561, 60.0, Unknown,
              new[] { (71.0, Score(300, 798)), (71.2, Score(600, 798)), (71.4, Score(900, 798)), (71.6, 720814), (71.8, Score(1200, 798)), (72.0, Score(1500, 798)), (72.2, Score(1800, 798)) });

        Console.WriteLine(bad == 0 ? "새 판 규칙 확인: 모두 맞음" : $"새 판 규칙 확인: 틀린 것 {bad}개");
        return bad;
    }
}
