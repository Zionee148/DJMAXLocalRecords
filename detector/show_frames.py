"""채점에서 어긋난 프레임을 원본에서 꺼내 크게 보여 준다 (정답과 판독 중 어느 쪽이 틀렸는지 눈으로 확인하는 용도).

실행: .venv\\Scripts\\python.exe -m detector.show_frames <실행 이름> <저장 폴더> [방식=그대로] [사례당 최대 장수=12]
입력: data\\full\\<실행 이름>\\mismatches.csv
출력: <저장 폴더>\\<사례>.png  (칸마다: 프레임 번호 / 정답 / 판독)
"""
import csv
import sys
from collections import defaultdict
from pathlib import Path

import cv2
import numpy as np

from .full_frames import sources, stream
from .selftrain import ROOT


def main():
    run, out = sys.argv[1], Path(sys.argv[2])
    mode = sys.argv[3] if len(sys.argv) > 3 else "그대로"
    limit = int(sys.argv[4]) if len(sys.argv) > 4 else 12
    out.mkdir(parents=True, exist_ok=True)
    wanted = defaultdict(dict)
    with open(ROOT / "data" / "full" / run / "mismatches.csv", encoding="utf-8-sig") as f:
        for r in csv.DictReader(f):
            if r["방식"] == mode and len(wanted[r["사례"]]) < limit:
                wanted[r["사례"]][int(r["프레임"])] = (r["정답"], r["읽힌 글자"])
    table = sources()
    for case_id, frames in wanted.items():
        log = out / f"{case_id}.log"
        tiles = []
        for index, part in enumerate(stream(case_id, table[case_id], log), 1):
            if index in frames:
                scale = min(700 / part.shape[1], 150 / part.shape[0])
                big = cv2.resize(part, None, fx=scale, fy=scale, interpolation=cv2.INTER_CUBIC)
                tile = np.zeros((190, 768, 3), np.uint8)
                tile[36:36 + big.shape[0], 8:8 + big.shape[1]] = big
                truth, read = frames[index]
                cv2.putText(tile, f"#{index}  truth {truth}  read {read or '-'}", (8, 24), cv2.FONT_HERSHEY_SIMPLEX, 0.65, (0, 255, 255), 2)
                tiles.append(tile)
        log.unlink()
        if len(tiles) % 2:
            tiles.append(np.zeros_like(tiles[0]))
        sheet = np.vstack([np.hstack(tiles[r:r + 2]) for r in range(0, len(tiles), 2)])
        cv2.imencode(".png", sheet)[1].tofile(str(out / f"{case_id}.png"))
        print(case_id, len(frames), "장")


if __name__ == "__main__":
    main()
