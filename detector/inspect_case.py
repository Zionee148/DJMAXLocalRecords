"""사례 하나를 자세히 들여다본다: 플레이·등록 프레임의 판독값과 확신도, 잘라 낸 그림 모음.

실행: .venv\\Scripts\\python.exe -m detector.inspect_case <실행 이름> <그림 저장 폴더> <사례> [사례 ...]
"""
import sys
from collections import Counter
from pathlib import Path

import cv2
import numpy as np

from .score_rules import on_lattice, verify_sequence
from .selftrain import ROOT, load_model, notes_of, predict


def main():
    run, out = sys.argv[1], Path(sys.argv[2])
    out.mkdir(parents=True, exist_ok=True)
    model, cfg = load_model(run)
    for case_id in sys.argv[3:]:
        z = np.load(ROOT / "data" / cfg["data"] / f"{case_id}.npz")
        images, kinds = z["images"], z["kinds"]
        values, conf, texts = predict(model, images)
        notes = notes_of(case_id)
        play = np.where(kinds == 0)[0]; reg = np.where(kinds == 1)[0]
        reads = [int(values[i]) if conf[i] >= 0.3 and values[i] > 0 else -1 for i in play]
        keep = verify_sequence(reads, notes)
        verified = [reads[j] for j in range(len(reads)) if keep[j]]
        print(f"== {case_id}  플레이 {len(play)}장, 규칙 통과 {int(keep.sum())}장, 서로 다른 통과 값 {len(set(verified))}개, 최대 {max(verified, default=None)}")
        print("   플레이 판독 상위:", Counter(reads).most_common(5))
        print("   플레이 끝 8장:", [(texts[i], round(float(conf[i]), 2)) for i in play[-8:]])
        print("   등록 프레임  :", [(int(values[i]), round(float(conf[i]), 2), "격자" if on_lattice(int(values[i]), notes) else "x") for i in reg])
        pick = list(play[np.linspace(0, len(play) - 1, 12).astype(int)]) + list(reg[:12])
        tiles = []
        for i in pick:
            tile = cv2.resize(images[i][:, :, ::-1], (384, 96), interpolation=cv2.INTER_NEAREST)
            cv2.putText(tile, f"{'R' if kinds[i] else 'P'} {int(values[i])} {conf[i]:.2f}", (4, 14), cv2.FONT_HERSHEY_SIMPLEX, 0.45, (0, 255, 255), 1)
            tiles.append(tile)
        while len(tiles) % 4:
            tiles.append(np.zeros_like(tiles[0]))
        sheet = np.vstack([np.hstack(tiles[r:r + 4]) for r in range(0, len(tiles), 4)])
        cv2.imencode(".png", sheet)[1].tofile(str(out / f"{case_id}.png"))


if __name__ == "__main__":
    main()
