// 래더 정보판 자리(ScoreModule.PanelZone)의 좌표와 경계를 확인한다. 모델도 녹화도 쓰지 않는다.
//
//   1) 세 위치의 자리가 정한 좌표(1920x1080 화면 전체 기준, 끝은 넣지 않는다)와 같은가.
//   2) 자리가 그 위치의 띠 안에 있고, 정보판의 높이(Y 912)에서 끝나는가. 그 아래는 띠의 가로 전체가 남는가.
//   3) "네모의 가운데가 자리 안인가"의 경계: 시작 좌표는 안, 끝 좌표는 밖.
//   4) 실제로 있었던 네모: 정보판에 고정됐던 자리(래더 RIGHT·CYTUS 의 두 판)는 안, 그 판들의 실제 점수 자리와 KARTRIDER LEFT 의 점수 자리는 밖.
//
// 탐지·판독·고정이 이 자리를 실제로 건너뛰는지는 녹화를 넣는 검사(run, module, link. 정보판을 붙이는 --panel)로 본다.
//
// 실행: dotnet run -c Release -- zone      (하나라도 어긋나면 종료 코드 1)
using DjmaxScore;

public static class ZoneCheck
{
    public static int Run()
    {
        int failed = 0, total = 0;
        void Check(bool ok, string what)
        {
            total++;
            if (!ok) failed++;
            Console.WriteLine($"{(ok ? "통과" : "실패")}  {what}");
        }

        var want = new Dictionary<GearPosition, Box>
        {
            [GearPosition.Left] = new Box(736, 740, 960, 912),
            [GearPosition.Center] = new Box(480, 740, 544, 912),
            [GearPosition.Right] = new Box(960, 740, 1184, 912),
        };
        foreach (var (position, box) in want)
        {
            Box zone = ScoreModule.PanelZone(position);
            int left = ScoreModule.BandLeft(position);
            Check(zone == box, $"{position}: 자리 ({zone.X0},{zone.Y0})~({zone.X1},{zone.Y1}) = 정한 좌표 ({box.X0},{box.Y0})~({box.X1},{box.Y1})");
            Check(zone.X0 >= left && zone.X1 <= left + ScoreModule.BandWidth && zone.Y0 == ScoreModule.BandTop && zone.Y1 < ScoreModule.ScreenHeight,
                  $"{position}: 자리가 띠(X {left}~{left + ScoreModule.BandWidth}, Y {ScoreModule.BandTop}~{ScoreModule.ScreenHeight}) 안에 있고 위에서 시작해 화면 아래 끝 전에 끝난다");
            // 경계: 가운데가 시작 좌표에 있으면 안, 끝 좌표에 있으면 밖
            Check(Dot(zone.X0, zone.Y0).CenterIn(zone) && Dot(zone.X1 - 0.5, zone.Y1 - 0.5).CenterIn(zone), $"{position}: 시작 좌표와 끝 바로 안쪽은 자리 안");
            Check(!Dot(zone.X1, zone.Y0).CenterIn(zone) && !Dot(zone.X0, zone.Y1).CenterIn(zone) && !Dot(zone.X0 - 0.5, zone.Y0).CenterIn(zone), $"{position}: 끝 좌표와 시작 바로 바깥은 자리 밖");
            // 정보판 아래: 띠의 가로 어디에서도 자리 밖이다
            bool below = true;
            for (int x = left; x < left + ScoreModule.BandWidth; x += 8)
                for (int y = ScoreModule.PanelBottom; y < ScoreModule.ScreenHeight; y += 8)
                    below &= !Dot(x, y).CenterIn(zone);
            Check(below, $"{position}: Y {ScoreModule.PanelBottom} 부터 아래는 띠의 가로 전체가 자리 밖");
        }

        Box right = ScoreModule.PanelZone(GearPosition.Right), leftZone = ScoreModule.PanelZone(GearPosition.Left);
        // 진단 기록의 "area"(X, Y, 너비, 높이)
        Check(Area(1015, 771, 131, 45).CenterIn(right), "RIGHT: 정보판에 고정됐던 자리 (1015,771,131,45) (BLACK GOLD)는 자리 안");
        Check(Area(1016, 771, 134, 45).CenterIn(right), "RIGHT: 정보판에 고정됐던 자리 (1016,771,134,45) (MUSEDASH!!!!)는 자리 안");
        Check(!new Box(1592.3, 966.3, 1681.5, 1038.6).CenterIn(right), "RIGHT: 그 판의 실제 점수 자리 (1592,966)~(1682,1039) 는 자리 밖");
        Check(!Area(1689, 872, 147, 64).CenterIn(right), "RIGHT: 같은 녹화의 셋째 판에서 고정된 실제 점수 자리 (1689,872,147,64) 는 자리 밖");
        // KARTRIDER LEFT: 다시 읽을 때의 여백까지 친 범위(리뷰 20261117-래더패널-기어별탐색범위-검토). 가로로는 자리의 X 범위에 걸치지만 정보판보다 아래다
        var kart = new Box(525, 985, 766, 1053);
        Check(!kart.CenterIn(leftZone) && kart.X1 > leftZone.X0 && kart.Y0 >= leftZone.Y1, "LEFT: KARTRIDER 의 점수 범위 (525,985)~(766,1053) 는 가로로 걸쳐도 정보판 아래라 자리 밖");

        Console.WriteLine($"{total - failed}/{total} 통과");
        return failed == 0 ? 0 : 1;
    }

    private static Box Dot(double x, double y) => new(x - 1, y - 1, x + 1, y + 1);
    private static Box Area(double x, double y, double w, double h) => new(x, y, x + w, y + h);
}
