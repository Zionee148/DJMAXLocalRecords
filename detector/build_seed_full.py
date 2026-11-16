"""전체 프레임에서 직접 읽은 씨앗 라벨(truth\\seed-labels-full\\claude-read-full-*.csv)을 검사하고 학습 자료로 만든다.

읽은 값은 그대로 믿지 않는다. 아래를 모두 만족하는 것만 쓴다.
  - 그 사례의 점수 산식으로 나올 수 있는 값이다.
  - 앞뒤로 가장 가까운 "규칙을 통과해 읽힌 값" 사이에 있고, 그 값들과의 증가폭이 판정의 합으로 설명된다.

라벨을 붙인 프레임의 바로 앞뒤 두 장도, 그림이 거의 똑같으면(같은 값이 떠 있는 동안이면) 같은 라벨로 함께 쓴다.

실행: .venv\\Scripts\\python.exe -m detector.build_seed_full <판독 실행 이름(앞뒤 값을 얻을 것)>
출력: data\\seed_full.npz (images, labels, cases, frames)
"""
import csv
import sys

import cv2
import numpy as np

from .build_dataset import letterbox
from .full_frames import sources, stream
from .score_frames import READ_CONF
from .score_rules import lattice_m, step_ok, verify_sequence
from .selftrain import ROOT, notes_of

SAME = 3.0        # 이웃 프레임과의 밝기 차이(평균)가 이보다 작으면 같은 그림으로 본다


def main():
    run = sys.argv[1]
    wanted: dict[str, dict[int, int]] = {}
    for path in sorted((ROOT / "truth" / "seed-labels-full").glob("claude-read-full-*.csv")):
        with open(path, encoding="utf-8") as f:
            for r in csv.DictReader(f):
                wanted.setdefault(r["case"], {})[int(r["frame"]) - 1] = int(r["value"])
    if not wanted:
        raise SystemExit(f"씨앗 라벨이 없다: {ROOT / 'truth' / 'seed-labels-full'}\\claude-read-full-*.csv\n직접 읽은 라벨은 저장소에 들어 있지 않다.")
    table = sources()
    images, labels, cases, frames = [], [], [], []
    passed = dropped = extra = 0
    for case_id, picks in wanted.items():
        z = np.load(ROOT / "data" / "full" / run / f"{case_id}.npz")
        notes = notes_of(case_id)
        raw = np.where((z["conf"] >= READ_CONF) & ~z["blank"] & (z["values"] > 0), z["values"], -1)
        keep = np.array(verify_sequence([int(v) for v in raw], notes), bool)
        n = len(keep); order = np.arange(n)
        before = np.maximum.accumulate(np.where(keep, order, -1)); after = np.minimum.accumulate(np.where(keep, order, n)[::-1])[::-1]
        good = {}
        for i, v in picks.items():
            a = int(raw[before[i]]) if before[i] >= 0 else None
            b = int(raw[after[i]]) if after[i] < n else None
            mv = lattice_m(v, notes)
            ok = mv is not None and a is not None and b is not None and a <= v <= b \
                and (v == a or step_ok(mv - lattice_m(a, notes))) and (v == b or step_ok(lattice_m(b, notes) - mv))
            if ok:
                good[i] = v; passed += 1
            else:
                dropped += 1; print(f"  탈락: {case_id} #{i + 1} {v} (앞뒤 {a} .. {b})")
        need = {j for i in good for j in range(i - 2, i + 3) if 0 <= j < n}
        log = ROOT / "data" / f"{case_id}.seed.log"
        parts = {i: p.copy() for i, p in enumerate(stream(case_id, table[case_id], log)) if i in need}
        log.unlink()
        for i, v in good.items():
            base = cv2.cvtColor(parts[i], cv2.COLOR_BGR2GRAY).astype(np.float32)
            for j in range(i - 2, i + 3):
                if j not in parts:
                    continue
                if j != i:
                    if float(np.abs(cv2.cvtColor(parts[j], cv2.COLOR_BGR2GRAY).astype(np.float32) - base).mean()) >= SAME or j in good:
                        continue
                    extra += 1
                images.append(letterbox(parts[j])[0]); labels.append(v); cases.append(case_id); frames.append(j + 1)
    np.savez_compressed(ROOT / "data" / "seed_full.npz", images=np.stack(images), labels=np.array(labels, np.int32),
                        cases=np.array(cases), frames=np.array(frames, np.int32))
    print(f"읽은 라벨 {passed + dropped}장: 규칙 통과 {passed}, 탈락 {dropped}. 그림이 같은 이웃 프레임 {extra}장을 더해 학습 자료 {len(images)}장")


if __name__ == "__main__":
    main()
