"""화면 효과가 점수판을 완전히 가린 프레임의 그림을 모아 둔다 (LEFT·CENTER 만).

화면 효과 기어에서 99% 에 못 미치는 까닭의 큰 몫은 "덮였지만 숫자가 비치는" 프레임이다. 효과가 나타나고 사라지는 동안
점수판 위에 효과가 반쯤 겹친다. 사람은 읽을 수 있고 채점 기준으로도 세는 프레임인데, 판독기는 이런 그림을 거의 배우지 못했다.

완전히 가린 프레임은 "숫자 없이 효과만 찍힌 그림"이다. 이것을 깨끗한 프레임 위에 반투명하게 겹치면 효과가 나타나는 도중의
그림과 닮은 학습 자료를 얼마든지 만들 수 있고, 라벨은 깨끗한 프레임의 것이라 틀릴 일이 없다 (train_wide 의 "효과를 겹친 그림").

숫자가 조금이라도 비치는 그림이 섞이면 겹친 그림에 숫자가 두 벌 생기므로, "완전히 가림" 판정 가운데 아주 확실한 것만 쓴다.

실행: .venv\\Scripts\\python.exe -m detector.build_overlays <판독 실행 이름(분류가 있는 것)>
출력: data\\overlays\\<사례>.npy (그림 x 64 x 256 x 3, RGB)
"""
import sys

import numpy as np

from .build_dataset import letterbox
from .build_full_train import SURE
from .full_frames import sources, stream
from . import inputs
from .selftrain import ROOT

OUT = ROOT / "data" / "overlays"
PER_CASE = 240


def main():
    run = sys.argv[1]
    folder = ROOT / "data" / "full" / run / "classes"
    OUT.mkdir(parents=True, exist_ok=True)
    table = sources()
    total = 0
    for path in inputs(folder, "*.npz", "프레임 분류 결과"):
        case_id = path.stem
        if "-right-" in case_id or (OUT / f"{case_id}.npy").exists():
            continue
        z = np.load(path)
        hidden = np.where((z["kind"] == list(z["names"]).index("완전히 가림") + 1) & (z["seen"] < SURE))[0]
        if len(hidden) < 20:
            continue
        picks = set(int(i) for i in hidden[np.linspace(0, len(hidden) - 1, min(PER_CASE, len(hidden))).round().astype(int)])
        log = OUT / f"{case_id}.log"
        kept = [letterbox(part)[0] for i, part in enumerate(stream(case_id, table[case_id], log)) if i in picks]
        log.unlink()
        np.save(OUT / f"{case_id}.npy", np.stack(kept))
        total += len(kept)
        print(f"{case_id}: 확실히 가린 프레임 {len(hidden)}장 가운데 {len(kept)}장", flush=True)
    print(f"완료: {total}장")


if __name__ == "__main__":
    main()
