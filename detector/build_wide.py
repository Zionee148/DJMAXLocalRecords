"""판독기를 "자르는 위치가 달라져도 읽게" 가르치기 위한 넓은 조각 자료 (LEFT·CENTER 만).

비교 기준으로 사용한 영역(기준 영역. 검사 설정에 적힌 점수 영역)에 여백 14x8 을 둔 조각으로만 배운 판독기는 양옆을
12화소만 더 넓게 잘라도 179개 사례 중 5개가, 40화소 넓히면 20개가 최종 점수를 내지 못한다 (틀린 점수는 없었다). 위치 탐색이 내는
영역은 기준 영역과 똑같지 않으므로, 판독기가 넓게 잘린 조각에서도 점수 숫자만 읽어야 한다. RIGHT 에서 "SCORE" 글자가 같이 잘려
숫자로 읽히던 문제도 원인이 같다.

여기서는 0.5초 간격 화면마다 점수 영역을 넉넉히(양옆 WIDE_X, 위아래 WIDE_Y) 잘라 원본 크기 그대로 모아 둔다.
학습 때 이 안에서 자르는 위치를 흔든다. 기운 기어(CYTUS)는 손으로 잰 각도로 세운 기존 조각을 그대로 쓰므로 여기서 뺀다.

실행: .venv\\Scripts\\python.exe -m detector.build_wide
출력: data\\wide\\<사례>.npz (images: 그림 x 높이 x 너비 x 3 BGR, inner: 넓은 조각 안에서의 기준 영역 좌표)
"""
import json
from pathlib import Path

import numpy as np

from .build_dataset import ORIENTED, VALIDATION, here, read_image
from .full_frames import area_of
from .selftrain import DATA_NAME, ROOT

OUT = ROOT / "data" / "wide"
WIDE_X, WIDE_Y = 64, 20


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    cases = [c for c in json.loads((VALIDATION / "candidate-v4" / "all270.json").read_text(encoding="utf-8")) if c["position"] != "RIGHT"]
    total = 0
    for n, c in enumerate(cases, 1):
        case_id = c["id"]; gear = case_id.rsplit("-", 2)[0]
        area = area_of(case_id)
        path = ROOT / "data" / DATA_NAME / f"{case_id}.npz"
        if area is None or gear in ORIENTED or not path.exists() or (OUT / f"{case_id}.npz").exists():
            continue
        data = np.load(path)
        images, inner = [], None
        for name, kind in zip(data["names"], data["kinds"]):
            folder = here(c["frames"] if kind == 0 else c["registration"]["frames"])
            image = read_image(Path(folder) / str(name))
            offset = 740 if image.shape[0] == 340 else 0
            x0, y0 = max(0, area[0] - WIDE_X), max(0, area[1] - offset - WIDE_Y)
            x1, y1 = min(image.shape[1], area[2] + WIDE_X), min(image.shape[0], area[3] - offset + WIDE_Y)
            images.append(image[y0:y1, x0:x1])
            inner = [area[0] - x0, area[1] - offset - y0, area[2] - x0, area[3] - offset - y0]
        if len({i.shape for i in images}) != 1:
            print(f"{case_id}: 그림 크기가 섞여 있어 건너뜀"); continue
        np.savez_compressed(OUT / f"{case_id}.npz", images=np.stack(images), inner=np.array(inner, np.int32))
        total += len(images)
        if n % 20 == 0:
            print(f"[{n}/{len(cases)}] {case_id}: 누적 {total}장", flush=True)
    print(f"완료: {total}장")


if __name__ == "__main__":
    main()
