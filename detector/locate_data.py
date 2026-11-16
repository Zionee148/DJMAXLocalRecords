"""숫자 줄 탐지 모델의 학습 자료를 만든다 (LEFT·CENTER 만. RIGHT 는 쓰지 않는다).

"자주 바뀌는 작은 덩어리"만으로는 점수 줄을 못 찾는 기어가 있다 (TECHNIKA 3 처럼 점수판이 반투명해 배경 영상이 비치는 기어).
그래서 숫자 줄의 생김새를 보고 찾는 작은 모델을 둔다.

  - 그림: Codex 가 0.5초 간격으로 뽑아 둔 화면 아래쪽 띠(1920x340, y >= 740). 사례마다 점수가 읽힌 프레임을 고르게 고른다.
  - 라벨: 그 사례의 점수 영역 네모. Codex 가 찾았던 영역이지만, 그 영역을 읽은 최종 점수가 179개 모두 맞았으므로
    "그 안에 점수 숫자가 있다"는 것은 판독으로 확인된 셈이다. 다만 네모의 가장자리가 정확하다는 보장은 없다.
  - 모델은 화면의 어디인지(좌표)를 입력으로 받지 않고, 학습할 때 가로로 아무 데나 잘라 보여 준다.
    기어별 좌표를 외우는 길을 막기 위해서다. 정말 외우지 않았는지는 기어를 통째로 빼고 학습해 그 기어에서 시험해 본다.

실행: .venv\\Scripts\\python.exe -m detector.locate_data
출력: data\\locate\\<사례>.npy (그림 x 340 x 너비 x 3, RGB), data\\locate\\index.json
"""
import json
from pathlib import Path

import numpy as np

from .build_dataset import VALIDATION, here, read_image
from .full_frames import area_of
from .selftrain import DATA_NAME, ROOT

OUT = ROOT / "data" / "locate"
TOP = 740
WINDOW = {"LEFT": (0, 1100), "CENTER": (410, 1510)}     # 저장만 이 범위로 한다 (용량). 학습 때는 이 안에서 아무 데나 자른다
PER_CASE = 24
REFERENCE_RUN = "v6"


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    cases = [c for c in json.loads((VALIDATION / "candidate-v4" / "all270.json").read_text(encoding="utf-8")) if c["position"] in WINDOW]
    predictions = np.load(ROOT / "data" / "runs" / REFERENCE_RUN / "predictions.npz")
    index = []
    for n, c in enumerate(cases, 1):
        case_id = c["id"]
        area = area_of(case_id)
        if area is None:
            continue
        data = np.load(ROOT / "data" / DATA_NAME / f"{case_id}.npz")
        mine = predictions["ids"] == case_id
        values, conf = predictions["values"][mine], predictions["conf"][mine]
        if len(values) != len(data["names"]):
            print(f"{case_id}: 판독 수와 그림 수가 다름"); continue
        seen = np.where((data["kinds"] == 0) & (conf >= 0.5) & (values > 0))[0]       # 플레이 중이고 점수가 읽힌 프레임
        if len(seen) == 0:
            continue
        picks = seen[np.linspace(0, len(seen) - 1, min(PER_CASE, len(seen))).round().astype(int)]
        x0, x1 = WINDOW[c["position"]]
        images = []
        for i in picks:
            strip = read_image(here(c["frames"]) / str(data["names"][i]))                # 1920x340, BGR
            images.append(strip[:, x0:x1, ::-1])
        np.save(OUT / f"{case_id}.npy", np.ascontiguousarray(np.stack(images)))
        index.append({"case": case_id, "gear": case_id.rsplit("-", 2)[0], "position": c["position"], "x0": x0, "top": TOP, "count": len(images),
                      "box": [area[0] - x0, area[1] - TOP, area[2] - x0, area[3] - TOP], "values": [int(values[i]) for i in picks]})
        print(f"[{n}/{len(cases)}] {case_id}: {len(images)}장", flush=True)
    (OUT / "index.json").write_text(json.dumps(index, ensure_ascii=False), encoding="utf-8")
    print(f"사례 {len(index)}개, 그림 {sum(i['count'] for i in index)}장")


if __name__ == "__main__":
    main()
