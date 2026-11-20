"""못 읽은 정상 프레임을 확대 이미지로 뽑아, 직접 판독해 초기 학습 라벨을 붙일 수 있게 한다 (전체 프레임 기준. LEFT·CENTER 만).

새 기준에서 남은 실패는 대부분 "숫자가 보이는데 판독기가 못 읽는" 프레임이다(연출 중 색이 바뀐 점수판의 옅은 숫자, 빛줄기 속의 흐린 숫자 등).
이 프레임들은 앞뒤의 통과 값 사이가 넓어(연출 1~2초 동안 통과한 판독이 없다) 앞뒤 값만으로는 라벨이 정해지지 않는다:
2,763장 가운데 앞뒤가 같은 값이라 확정되는 것은 140장뿐이었다. 그래서 0.5초 자료 때처럼 그림을 직접 읽는다.

읽은 값은 그대로 믿지 않는다. 점수 산식에 맞고, 앞뒤의 통과 값 사이에 있고, 같은 사례 안에서 시간 순서로 줄지 않는 것만 쓴다.

실행: .venv\\Scripts\\python.exe -m detector.seed_full <판독 실행(정상 프레임 목록이 있는 것)> <그림 폴더> <사례당 장수> <사례 ...>
출력: 그림 폴더에 <사례>-<번호>.png (12장씩), index.json (그림마다 프레임 번호와 앞뒤 통과 값)
"""
import csv
import json
import sys
from pathlib import Path

import cv2
import numpy as np

from .fever import normal_frames
from .full_frames import sources, stream
from .score_frames import READ_CONF
from .score_rules import verify_sequence
from .selftrain import ROOT, notes_of


def main():
    run, out, per_case, wanted = sys.argv[1], Path(sys.argv[2]), int(sys.argv[3]), sys.argv[4:]
    out.mkdir(parents=True, exist_ok=True)
    run, _, masks_run = run.partition("@")
    folder = ROOT / "data" / "full" / run
    masks = ROOT / "data" / "full" / (masks_run or run) / "fever"
    done: dict[str, set[int]] = {}
    for path in sorted((ROOT / "truth" / "seed-labels-full").glob("claude-read-full-*.csv")):
        with open(path, encoding="utf-8") as f:
            for r in csv.DictReader(f):
                done.setdefault(r["case"], set()).update(range(int(r["frame"]) - 3, int(r["frame"]) + 2))
    table = sources()
    index = json.loads((out / "index.json").read_text(encoding="utf-8")) if (out / "index.json").exists() else {}
    for case_id in wanted:
        z = np.load(folder / f"{case_id}.npz"); m = np.load(masks / f"{case_id}.npz")
        raw = np.where((z["conf"] >= READ_CONF) & ~z["blank"] & (z["values"] > 0), z["values"], -1)
        keep = np.array(verify_sequence([int(v) for v in raw], notes_of(case_id)), bool)
        n = len(keep)
        inside = np.zeros(n, bool); inside[int(m["window"][0]):int(m["window"][1])] = True
        miss = np.where(normal_frames(case_id, inside, m["fever"][:n], m["covered"][:n]) & ~keep)[0]
        miss = np.array([i for i in miss if int(i) not in done.get(case_id, ())], int)
        if not len(miss):
            continue
        picks = sorted({int(miss[k]) for k in np.linspace(0, len(miss) - 1, min(per_case, len(miss))).round().astype(int)})
        order = np.arange(n)
        before = np.maximum.accumulate(np.where(keep, order, -1)); after = np.minimum.accumulate(np.where(keep, order, n)[::-1])[::-1]
        log = out / f"{case_id}.log"
        want = set(picks)
        crops = {i: part.copy() for i, part in enumerate(stream(case_id, table[case_id], log)) if i in want}
        log.unlink()
        for page, s in enumerate(range(0, len(picks), 12)):
            tiles, entries = [], []
            for slot, i in enumerate(picks[s:s + 12]):
                a = int(raw[before[i]]) if before[i] >= 0 else -1
                b = int(raw[after[i]]) if after[i] < n else -1
                part = crops[i]
                scale = 400 / part.shape[1]
                tile = cv2.resize(part, (400, max(40, int(part.shape[0] * scale))), interpolation=cv2.INTER_CUBIC)
                head = np.zeros((22, 400, 3), np.uint8)
                cv2.putText(head, f"[{slot + 1}] #{i + 1}  {a} .. {b}", (4, 16), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 255, 255), 1, cv2.LINE_AA)
                tiles.append(np.vstack([head, tile]))
                entries.append({"slot": slot + 1, "frame": i + 1, "after": a, "before": b})
            height = max(t.shape[0] for t in tiles)
            tiles = [np.pad(t, ((0, height - t.shape[0]), (0, 0), (0, 0))) for t in tiles]
            while len(tiles) % 3:
                tiles.append(np.zeros_like(tiles[0]))
            grid = np.vstack([np.hstack(tiles[k:k + 3]) for k in range(0, len(tiles), 3)])
            name = f"{case_id}-{page + 1}.png"
            cv2.imencode(".png", grid)[1].tofile(str(out / name))
            index[name] = {"case": case_id, "frames": entries}
        print(f"{case_id}: 못 읽은 정상 프레임 {len(miss)}장 가운데 {len(picks)}장", flush=True)
    (out / "index.json").write_text(json.dumps(index, ensure_ascii=False, indent=1), encoding="utf-8")


if __name__ == "__main__":
    main()
