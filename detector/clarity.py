"""프레임마다 점수판이 "얼마나 가려졌는지"를 잰다 (LEFT·CENTER).

사용자 기준(2026-11-12): 플레이 중 프레임은 "명확하게 보이는 프레임 100%"다. 조금 흐릿한 프레임을 아예 빼서는 안 되고,
어디까지를 명확하게 보이는 것으로 칠지 기준선을 정해야 한다. 그 기준선을 고를 수 있도록 척도를 만든다.

가림 정도 = 그 프레임의 점수 영역이, 시간상 가장 가까운 "규칙을 통과해 읽힌 프레임"의 점수 영역과 얼마나 다른가.
  - 앞쪽과 뒤쪽의 가장 가까운 통과 프레임과 화소마다 견줘, 더 가까운 쪽과의 밝기 차이를 쓴다. 숫자가 바뀌는 자리는
    앞이나 뒤 어느 한쪽과는 같으므로 지워지고, 어느 쪽에도 없던 것(덮인 효과)만 남는다.
  - 그 차이들의 위쪽 12% 지점을 가림 정도로 쓴다 (0~255).
  - 한계: 앞뒤의 통과 프레임도 같은 효과 속에 있으면(점수판 색이 통째로 바뀌는 Boom! 등) 가림 정도가 0 에 가깝게 나온다.
    그런 프레임은 "가려지지 않았는데 못 읽은" 쪽으로 세어진다.
  - 점수판이 반투명한 기어는 배경 영상이 비쳐 평소에도 이 값이 크다. 그래서 사례마다 "통과한 프레임들끼리의 값"(바탕값)을 함께 내고,
    바탕값보다 작은 차이는 가림으로 보지 않는다.

이 척도는 "읽혔는가"와 무관하게 그림만으로 잰다. 못 읽은 프레임이 얼마나 가려진 프레임이었는지를 보는 데 쓴다.

실행: .venv\\Scripts\\python.exe -m detector.clarity <판독 실행 이름> [그림 폴더] [사례 ...]
출력: data\\full\\<실행>\\clarity\\<사례>.npz (cover, base), clarity.csv, 그림 폴더에 가림 정도별 대표 그림
"""
import csv
import json
import sys
from pathlib import Path

import cv2
import numpy as np

from .full_frames import sources, stream
from .full_summary import play_window
from .score_frames import READ_CONF
from .score_rules import verify_sequence
from . import inputs
from .selftrain import DATA_NAME, ROOT, notes_of

GAP = 12                       # 통과한 프레임끼리 견줄 때는 이만큼(0.2초) 떨어진 것과 견준다
PERCENTILE = 88                # 처음에는 가운데 값을 썼는데, 숫자 한두 자리만 덮은 효과(MILITARY, X-MAS 풍선)를 놓쳤다
EDGES = [0, 2, 4, 6, 8, 11, 15, 20, 30, 45, 256]


def measure(case_id: str, z, grays: np.ndarray):
    """(가림 정도, 바탕값, 통과 여부, 플레이 구간)"""
    raw = np.where((z["conf"] >= READ_CONF) & ~z["blank"] & (z["values"] > 0), z["values"], -1)
    keep = np.array(verify_sequence([int(v) for v in raw], notes_of(case_id)), bool)
    if keep.sum() < 8:
        return None
    start, stop = play_window(z, raw, keep)
    n = len(grays)
    good = np.where(keep)[0]
    cover = np.full(n, np.nan, np.float32)
    signed = np.stack([cv2.GaussianBlur(g, (3, 3), 0) for g in grays]).astype(np.int16)      # 영상 압축 잡음을 조금 눌러 둔다

    for i in range(start, min(stop, n)):
        k = int(np.searchsorted(good, i))
        gap = GAP if keep[i] else 1
        before = [j for j in good[max(0, k - 40):k] if i - j >= gap]
        after = [j for j in good[k:k + 40] if j - i >= gap]
        maps = []
        if before:
            maps.append(np.abs(signed[i] - signed[before[-1]]))
        if after:
            maps.append(np.abs(signed[i] - signed[after[0]]))
        if maps:
            # 화소마다 앞·뒤 가운데 더 가까운 쪽과의 차이. 숫자가 바뀌는 자리는 앞이나 뒤 어느 한쪽과는 같으므로 지워지고,
            # 어느 쪽에도 없던 것(덮인 효과)만 남는다. 그 위쪽 12% 지점을 쓴다: 숫자 한두 자리만 덮여도 잡히도록.
            cover[i] = float(np.percentile(np.minimum.reduce(maps), PERCENTILE))
    # 바탕값: 통과한 프레임들의 값 가운데 아래에서 4분의 3 지점. 처음에는 위에서 1% 지점을 썼는데, 효과가 덮였어도 읽힌 프레임이
    # 섞여 있어 효과 기어의 바탕값이 70 까지 올라가 버렸다 (BASEBALL, LOL).
    # 위의 값은 "이웃한 통과 프레임과의 차이"라서, 이웃도 같은 효과 속에 있으면 0 에 가깝게 나온다 (효과 속에서도 읽히는 프레임이 있다).
    # 그래서 그 사례의 "평소 점수판"을 따로 만들어 그것과 견준다.
    #   평소 점수판 = 통과한 프레임 가운데 이웃과의 차이가 작은 절반의, 화소별 가운데 값
    #   견주는 자리 = 그 프레임들에서 거의 변하지 않는 화소(숫자가 놓이는 자리와 비치는 배경은 빠진다)
    # 변하지 않는 화소가 너무 적은 사례(점수판이 반투명한 기어)는 잴 수 없으므로 전부 0 으로 둔다(= 모두 명확한 프레임으로 센다).
    usable = np.where(keep[:n] & ~np.isnan(cover))[0]
    calm = usable[cover[usable] <= np.median(cover[usable])]
    calm = calm[np.linspace(0, len(calm) - 1, min(400, len(calm))).round().astype(int)]
    stack = signed[calm]
    panel = np.median(stack, axis=0)
    still = stack.std(axis=0) < 6
    cover2 = np.zeros(n, np.float32)
    if still.mean() >= 0.15:
        for i in range(start, min(stop, n)):
            cover2[i] = float(np.percentile(np.abs(signed[i] - panel)[still], PERCENTILE))
    base = float(np.percentile(cover2[usable], 75)) if len(usable) else 0.0
    cover2[np.isnan(cover)] = np.nan

    # 사용자 정의(2026-11-12): 정상 프레임 = 모든 자릿수가 보이고, 하나라도 가려지면 안 된다. 흐릿한 것은 괜찮다.
    # 그래서 점수판 전체가 아니라 "숫자 한 자리 너비의 칸마다" 본다.
    #   숫자 줄 = 평소에 변하는 화소가 몰려 있는 가로 띠, 그 안에서 변하는 화소가 있는 세로 줄들.
    #   칸마다, 평소에 변하지 않는 화소(숫자 사이와 둘레의 바탕)에서 "평소 점수판과의 차이"가 얼마나 얼룩덜룩한지를 잰다.
    #     - 흐릿해진 것(옅게 덮임, 번쩍임)은 차이가 고르게 생기므로 얼룩이 작다.
    #     - 무엇이 그 자리를 가리면(풍선, 삽화, 글자) 차이에 무늬가 생겨 얼룩이 크다.
    #   프레임의 값 = 칸들 가운데 가장 큰 얼룩 (한 자리라도 가려지면 걸리도록).
    # 따로, 숫자 줄의 밝기 대비가 평소의 몇 배인지도 낸다 (흐릿함의 정도).
    spots = np.zeros(n, np.float32); contrast = np.ones(n, np.float32)
    moving = ~still
    rows = np.where(moving.mean(axis=1) >= 0.2)[0]
    if still.mean() >= 0.15 and len(rows) >= 4:
        r0, r1 = int(rows.min()), int(rows.max()) + 1
        cols = np.where(moving[r0:r1].mean(axis=0) >= 0.2)[0]
        if len(cols) >= 4:
            c0, c1 = int(cols.min()), int(cols.max()) + 1
            width = max(6, int(0.62 * (r1 - r0)))
            ra, rb = max(0, r0 - 3), min(still.shape[0], r1 + 3)
            windows = []
            for a in range(c0, c1, width):
                mask = np.zeros_like(still); mask[ra:rb, a:min(c1, a + width)] = True
                mask &= still
                if mask.sum() >= 12:
                    windows.append(mask)
            zone = np.zeros_like(still); zone[r0:r1, c0:c1] = True
            usual = float(np.median([signed[j][zone].std() for j in calm[:: max(1, len(calm) // 60)]])) or 1.0
            for i in range(start, min(stop, n)):
                difference = signed[i] - panel
                if windows:
                    spots[i] = max(float(difference[m].std()) for m in windows)
                contrast[i] = float(signed[i][zone].std()) / usual
    return cover2, base, keep[:n], (start, min(stop, n)), raw[:n], spots, contrast


def main():
    run = sys.argv[1]
    pictures = Path(sys.argv[2]) if len(sys.argv) > 2 else None
    folder = ROOT / "data" / "full" / run
    out = folder / "clarity"; out.mkdir(exist_ok=True)
    names = {c["id"]: c["name"] for c in json.loads((ROOT / "data" / DATA_NAME / "cases.json").read_text(encoding="utf-8"))}
    table = sources()
    wanted = sys.argv[3:] or [p.stem for p in inputs(folder, "*.npz", "사례별 판독 결과") if "-right-" not in p.stem]
    rows, samples = [], {}
    for n, case_id in enumerate(wanted, 1):
        z = np.load(folder / f"{case_id}.npz")
        log = out / f"{case_id}.log"
        parts = list(stream(case_id, table[case_id], log)); log.unlink()
        if len(parts) != len(z["values"]):
            print(f"{case_id}: 프레임 수가 다름"); continue
        grays = np.stack([cv2.cvtColor(p, cv2.COLOR_BGR2GRAY) for p in parts])
        result = measure(case_id, z, grays)
        if result is None:
            continue
        cover, base, keep, (start, stop), raw, spots, contrast = result
        np.savez_compressed(out / f"{case_id}.npz", cover=cover, base=np.float32(base), spots=spots, contrast=contrast)
        inside = np.zeros(len(cover), bool); inside[start:stop] = True
        failed = inside & ~keep & ~np.isnan(cover)
        spot_base = float(np.percentile(spots[inside & keep], 75)) if (inside & keep).any() else 0.0
        over = np.maximum(spots - spot_base, 0)               # 척도: 가장 얼룩진 칸의 얼룩 (평소 값을 넘는 만큼)
        row = {"사례": case_id, "기어": names[case_id], "플레이 구간": int(inside.sum()), "규칙 통과": int((inside & keep).sum()),
               "못 읽거나 어긋남": int(failed.sum()), "바탕값": f"{spot_base:.1f}"}
        for a, b in zip(EDGES, EDGES[1:]):
            row[f"가림 {a}~{b}"] = int((failed & (over >= a) & (over < b)).sum())
        rows.append(row)
        if pictures is not None:
            for a, b in zip(EDGES, EDGES[1:]):
                picks = np.where(failed & (over >= a) & (over < b))[0]
                if len(picks):
                    i = int(picks[len(picks) // 2])
                    samples.setdefault((a, b), []).append((case_id, i, float(over[i]), parts[i], str(z["texts"][i]), float(z["conf"][i])))
        print(f"[{n}/{len(wanted)}] {case_id}: 구간 {row['플레이 구간']}, 못 읽거나 어긋남 {row['못 읽거나 어긋남']}, 바탕값 {base:.1f}", flush=True)
    with open(folder / "clarity.csv", "w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0])); w.writeheader(); w.writerows(rows)
    print("기준선(바탕값을 넘는 가림 정도)을 이 값으로 잡으면, 그보다 덜 가려졌는데 못 읽거나 어긋난 프레임 수와 그런 프레임이 있는 사례 수:")
    for line in EDGES[1:-1]:
        counts = [sum(r[f"가림 {a}~{b}"] for a, b in zip(EDGES, EDGES[1:]) if b <= line) for r in rows]
        print(f"  기준선 {line:>2}: 프레임 {sum(counts):>6}장, 사례 {sum(c > 0 for c in counts):>3}개 / {len(rows)}")
    if pictures is not None and samples:
        pictures.mkdir(parents=True, exist_ok=True)
        for (a, b), items in sorted(samples.items()):
            tiles = []
            for case_id, i, value, part, text, conf in items[:: max(1, len(items) // 8)][:8]:
                tile = cv2.resize(part, (360, 110), interpolation=cv2.INTER_CUBIC)
                head = np.zeros((40, 360, 3), np.uint8)
                cv2.putText(head, f"{case_id[:14]} #{i + 1} cover {value:.0f}", (4, 15), cv2.FONT_HERSHEY_SIMPLEX, 0.42, (0, 255, 255), 1, cv2.LINE_AA)
                cv2.putText(head, f"read '{text}' conf {conf:.2f}", (4, 33), cv2.FONT_HERSHEY_SIMPLEX, 0.42, (255, 255, 255), 1, cv2.LINE_AA)
                tiles.append(np.vstack([head, tile]))
            while len(tiles) % 4:
                tiles.append(np.zeros_like(tiles[0]))
            grid = np.vstack([np.hstack(tiles[k:k + 4]) for k in range(0, len(tiles), 4)])
            cv2.imencode(".png", grid)[1].tofile(str(pictures / f"cover-{a:02d}-{b:02d}.png"))


if __name__ == "__main__":
    main()
