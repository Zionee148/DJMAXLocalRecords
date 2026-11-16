""".NET 쪽 검사가 녹화를 직접 읽을 수 있게 사례 목록을 내보낸다: 영상 파일, 구간, 키 시각, 기대 점수, 그리고 Python 이 찾은 고정 결과.

실행: .venv\\Scripts\\python.exe -m detector.export_cases <위치 탐색 실행 폴더(예: vid-d2-v17)>
출력: data\\parity_locate\\cases.tsv
      줄마다: 사례, 위치, 영상, 구간 시작(초), 구간 끝, 키 시각, 기대 점수, 고정 시각(구간 기준, 없으면 빈칸), 자세("각도 중심 높이", 없으면 빈칸),
              고정 때의 영역 x0 y0 x1 y1 (화면 전체 좌표), 마지막 영역 x0 y0 x1 y1, 움직이는 점수판인가(1/0)
"""
import json
import sys

from .build_dataset import VALIDATION
from .full_frames import sources
from .selftrain import ROOT


def main():
    folder = ROOT / "data" / "full" / sys.argv[1]
    expected = {c["id"]: c["registration"]["expected"] for c in json.loads((VALIDATION / "candidate-v4" / "all270.json").read_text(encoding="utf-8"))}
    table = sources()
    lines = []
    for case_id in sorted(table):
        src = table[case_id]
        position = "CENTER" if "-center-" in case_id else ("RIGHT" if "-right-" in case_id else "LEFT")
        row = [case_id, position, src["source"], repr(float(src["start"])), repr(float(src["end"])), repr(float(src["press"])), str(expected.get(case_id, -1))]
        path = folder / f"{case_id}.steps.json"
        if path.exists():
            d = json.loads(path.read_text(encoding="utf-8"))
            pose = "" if d["pose"] is None else " ".join(repr(float(v)) for v in d["pose"])
            row += [repr(float(d["lock_at"])), pose, " ".join(repr(float(v)) for v in d["steps"][0][1]), " ".join(repr(float(v)) for v in d["steps"][-1][1]), "1" if d["moving"] else "0"]
        else:
            row += ["", "", "", "", "0"]
        lines.append("\t".join(row))
    out = ROOT / "data" / "parity_locate"
    out.mkdir(parents=True, exist_ok=True)
    (out / "cases.tsv").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"사례 {len(lines)}개 → {out / 'cases.tsv'}")


if __name__ == "__main__":
    main()
