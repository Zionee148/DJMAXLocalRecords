"""씨앗 라벨용 그림 모음.

모델이 스스로 라벨을 얻지 못하는 글꼴은 누군가 몇 장을 직접 읽어 줘야 한다.
사례의 플레이 프레임 몇 장을 원본 화면에서 잘라 크게 키우고 번호를 붙여 늘어놓는다.
읽은 값은 truth\\seed-labels\\*.csv (id,file,value) 에 적고, 학습에서는 점수 규칙을 통과한 것만 쓴다.

실행: .venv\\Scripts\\python.exe -m detector.seed_sheet <저장 폴더> <장수> <사례> [사례 ...]
출력: <폴더>\\<사례>.png, <폴더>\\<사례>.json (번호 순서대로의 파일 이름)
"""
import json
import sys
from pathlib import Path

import cv2
import numpy as np

from .build_dataset import VALIDATION, cut, here, read_image


def main():
    out, count = Path(sys.argv[1]), int(sys.argv[2])
    out.mkdir(parents=True, exist_ok=True)
    cases = {c["id"]: c for c in json.loads((VALIDATION / "candidate-v4" / "all270.json").read_text(encoding="utf-8"))}
    for case_id in sys.argv[3:]:
        case = cases[case_id]
        area = json.loads((VALIDATION / "candidate-v4" / "all270-results" / f"{case_id}.json").read_text(encoding="utf-8"))["area"]
        gear = case_id.rsplit("-", 2)[0]
        play = sorted(here(case["frames"]).glob("f*.png"))
        picks = np.linspace(len(play) * 0.08, len(play) - 3, count).astype(int)
        tiles, names = [], []
        for number, k in enumerate(picks, 1):
            part = cut(read_image(play[k]), area, gear)
            scale = min(700 / part.shape[1], 170 / part.shape[0])
            big = cv2.resize(part, None, fx=scale, fy=scale, interpolation=cv2.INTER_CUBIC)
            tile = np.zeros((180, 768, 3), np.uint8)
            tile[5:5 + big.shape[0], 62:62 + big.shape[1]] = big
            cv2.putText(tile, f"{number:02d}", (4, 100), cv2.FONT_HERSHEY_SIMPLEX, 0.9, (0, 255, 255), 2)
            tiles.append(tile); names.append(play[k].name)
        sheet = np.vstack([np.hstack(tiles[r:r + 2]) for r in range(0, len(tiles), 2)])
        cv2.imencode(".png", sheet)[1].tofile(str(out / f"{case_id}.png"))
        (out / f"{case_id}.json").write_text(json.dumps(names), encoding="utf-8")
        print(case_id, len(names), "장")


if __name__ == "__main__":
    main()
