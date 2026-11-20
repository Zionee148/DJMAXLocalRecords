"""숫자 줄 탐지 모델을, 학습 때 빼 두었던 기어들에서 시험한다 (그 모델이 한 번도 보지 못한 기어 디자인).

여기서 견주는 대상은 비교 기준으로 사용한 영역(기준 영역. 검사 설정에 적힌 점수 영역)이다. 가장자리까지 정답이라는 뜻이 아니므로, 이 숫자는
"후보를 제대로 내는가"를 보는 개발용 지표다. 위치 탐색의 채점은 찾은 자리를 읽은 결과로 따로 한다.

  - 찾음        : 모델이 낸 검출 영역 가운데 기준 영역과 충분히 겹치는 것이 있다 (겹친 넓이 ÷ 합친 넓이 >= 0.5)
  - 첫째로 찾음 : 가장 점수 높은 검출 영역이 그것이다
  - 겹침 0.1 미만인 검출 영역 : 기준 영역과 겹치지 않거나 조금만 겹치는 검출 영역의 수. 결과 표의 열 이름은 "엉뚱한 네모(그림당)"이다 (콤보·배속·글자 등. 뒤에서 판독으로 걸러야 할 것들)

실행: .venv\\Scripts\\python.exe -m detector.locate_eval <이름> [그림 폴더]
출력: data\\locate_runs\\<이름>\\eval.csv
"""
import csv
import json
import sys
from pathlib import Path

import cv2
import numpy as np
import torch

from .locate_model import RowFinder, find_rows
from .locate_train import DATA, FOLDS, RUNS, fold_of
from .selftrain import DATA_NAME, DEVICE, ROOT


def iou(a, b) -> float:
    w = min(a[2], b[2]) - max(a[0], b[0]); h = min(a[3], b[3]) - max(a[1], b[1])
    if w <= 0 or h <= 0:
        return 0.0
    inter = w * h
    return inter / ((a[2] - a[0]) * (a[3] - a[1]) + (b[2] - b[0]) * (b[3] - b[1]) - inter)


def main():
    name = sys.argv[1]
    pictures = Path(sys.argv[2]) if len(sys.argv) > 2 else None
    if pictures:
        pictures.mkdir(parents=True, exist_ok=True)
    index = json.loads((DATA / "index.json").read_text(encoding="utf-8"))
    gears = sorted({i["gear"] for i in index})
    names = {c["id"]: c["name"] for c in json.loads((ROOT / "data" / DATA_NAME / "cases.json").read_text(encoding="utf-8"))}
    rows = []
    for fold in range(FOLDS):
        path = RUNS / name / f"fold{fold}.pt"
        if not path.exists():
            continue
        model = RowFinder().to(DEVICE); model.load_state_dict(torch.load(path, map_location=DEVICE)); model.eval()
        for item in index:
            if fold_of(item["gear"], gears) != fold:
                continue
            images = np.load(DATA / f"{item['case']}.npy")
            found = []
            for s in range(0, len(images), 8):
                found.extend(find_rows(model, images[s:s + 8], DEVICE))
            ref = item["box"]
            best = [max((iou(b, ref) for b in boxes), default=0.0) for boxes in found]
            first = [iou(boxes[0], ref) if boxes else 0.0 for boxes in found]
            stray = [sum(1 for b in boxes if iou(b, ref) < 0.1) for boxes in found]
            row = {"사례": item["case"], "기어": names.get(item["case"], ""), "묶음": fold, "그림": len(images),
                   "찾음": sum(v >= 0.5 for v in best), "첫째로 찾음": sum(v >= 0.5 for v in first),
                   "겹침 중앙값": f"{np.median(best):.2f}", "엉뚱한 네모(그림당)": f"{np.mean(stray):.2f}"}
            rows.append(row)
            if pictures and row["찾음"] < len(images) * 0.9:
                worst = int(np.argmin(best))
                img = cv2.cvtColor(images[worst], cv2.COLOR_RGB2BGR).copy()
                cv2.rectangle(img, (int(ref[0]), int(ref[1])), (int(ref[2]), int(ref[3])), (0, 255, 0), 1)
                for b in found[worst]:
                    cv2.rectangle(img, (int(b[0]), int(b[1])), (int(b[2]), int(b[3])), (0, 255, 255), 1)
                    cv2.putText(img, f"{b[4]:.2f}", (int(b[0]), int(b[1]) - 3), cv2.FONT_HERSHEY_SIMPLEX, 0.4, (0, 255, 255), 1)
                cv2.putText(img, f"{item['case']} {row['기어']} found {row['찾음']}/{len(images)}", (6, 16), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 0), 1)
                cv2.imencode(".png", img)[1].tofile(str(pictures / f"{item['case']}.png"))
    with open(RUNS / name / "eval.csv", "w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0])); w.writeheader(); w.writerows(rows)
    total = sum(r["그림"] for r in rows)
    print(f"사례 {len(rows)}개, 그림 {total}장 (모두 학습 때 빼 둔 기어)")
    print(f"찾음 {sum(r['찾음'] for r in rows)} / {total}, 첫째로 찾음 {sum(r['첫째로 찾음'] for r in rows)} / {total}")
    print(f"모든 그림에서 찾은 사례 {sum(r['찾음'] == r['그림'] for r in rows)}, 90% 이상 {sum(r['찾음'] >= 0.9 * r['그림'] for r in rows)}, 한 장도 못 찾은 사례 {sum(r['찾음'] == 0 for r in rows)}")
    print(f"기준 영역과 거의 겹치지 않는 검출 영역: 그림당 평균 {np.mean([float(r['엉뚱한 네모(그림당)']) for r in rows]):.2f}")
    low = sorted((r for r in rows if r["찾음"] < 0.9 * r["그림"]), key=lambda r: r["찾음"] / r["그림"])
    print("90% 못 미친 사례:")
    for r in low:
        print(f"  {r['사례']:<22} {r['기어'][:20]:<20} {r['찾음']:>2}/{r['그림']}  겹침 {r['겹침 중앙값']}  겹침 0.1 미만인 검출 영역 그림당 평균 {r['엉뚱한 네모(그림당)']}")


if __name__ == "__main__":
    main()
