"""규칙을 통과하지 못한 프레임을 원인별로 나눈다.

플레이 구간(규칙을 통과한 첫 점수 ~ 마지막 통과 프레임, 등록 키 이전)의 프레임 가운데 통과하지 못한 것을 본다.
그림으로 두 가지를 잰다.
  - 덮임   : 그 사례에서 규칙을 통과한 프레임들의 "보통 모습"(중앙값 그림)과 얼마나 다른가.
  - 안 보임 : 숫자 자리의 그림이, 점수가 뚜렷이 다른 시점의 프레임과 똑같은가. 숫자가 조금이라도 보이면 점수가 다를 때
             숫자 자리도 달라야 하므로, 똑같다면 숫자가 보이지 않는 것이다 (같은 삽화·같은 번쩍임·빈 판).
             (획 무늬의 상관으로 재는 방식은 완전히 가린 삽화에도 높은 값이 나와 버렸다)

사용자 결정(2026-11-11): 숫자를 완전히 가린 프레임만 99% 계산에서 뺀다. 희미하게라도 보이면 포함한다. 애매하면 포함.

  완전히 가림    : 덮였고 숫자 획도 남아 있지 않다                         → 따로 세고 분모에서 뺀다
  덮였지만 비침  : 덮였지만 숫자 획이 남아 있다                            → 포함
  숫자 없음      : 점수판은 보통 모습인데 숫자 획이 없다 (SCHOOL 의 분필 글씨처럼 새로 그려지는 중) → 포함 (제외는 합의 전)
  보이는데 미판독 : 점수판도 숫자도 보이는데 확신 있게 읽지 못했다
  앞자리 빠짐    : 앞에 한두 자리를 붙이면 앞뒤 통과 값 사이의 산식 값이 된다 (390476 → 90476)
  덧붙음        : 앞이나 뒤의 몇 글자를 떼면 그런 값이 된다 (988245 → 801988245, 451152 → 4511527)
  규칙에만 걸림  : 산식에 맞고 앞뒤 통과 값 사이에 드는 값 (맞게 읽었는데 규칙이 엄격해 버렸을 수 있다)
  다르게 읽음    : 그 밖의 오독

이 분류는 정답이 아니라 추정이다. 사례마다 그림 모음을 함께 만들어 눈으로 확인할 수 있게 한다.

실행: .venv\\Scripts\\python.exe -m detector.classify_frames <실행 이름> <그림 폴더> [통과율 상한=0.99 | 사례 ...] [--calibrate]
출력: data\\full\\<실행 이름>\\classes.csv, classes\\<사례>.npz (프레임별 분류), <그림 폴더>\\<사례>.png
      --calibrate 를 붙이면 덮인 프레임을 "비침" 값 순서로 늘어놓은 <사례>-calib.png 도 만든다 (기준값을 눈으로 맞추는 용도)
"""
import csv
import sys
from pathlib import Path

import cv2
import numpy as np

from .full_frames import sources, stream
from .full_summary import play_window
from .score_frames import READ_CONF
from .score_rules import on_lattice, verify_sequence
from .selftrain import MIN_DISTINCT, ROOT, notes_of

KINDS = ["완전히 가림", "덮였지만 비침", "숫자 없음", "보이는데 미판독", "앞자리 빠짐", "덧붙음", "규칙에만 걸림", "다르게 읽음"]
EXCLUDED = {"완전히 가림"}
# 숫자 자리 그림의 차이(밝기 0~255)가 이보다 작으면 "똑같은 그림"으로 본다. 그림으로 맞춘 값이다.
# 확인한 값: 멈춘 삽화(LIBERTY 2) 2.0~3.1, 움직이는 삽화(MAID, BASEBALL) 2.7~8, 숫자가 비치는 반투명 효과(LIBERTY 2) 8.5 이상.
# 3.3 으로 잡았을 때는 눈으로 보아 완전히 가린 MAID 의 삽화가 거의 다 "비침"으로 남았다.
# 경계가 좁으므로, 모델이 그 프레임에서 앞뒤 점수와 닮은 숫자열을 읽었으면 숫자가 보이는 것으로 보고 빼지 않는다 (reads_like).
# 한계: 삽화가 크게 움직여 닮은 장면이 없는 프레임(8 초과)은 완전히 가렸어도 "비침"으로 남는다.
# 7.5 로 잡았을 때는 BASEBALL 의 흰 번쩍임 위에 숫자가 아주 흐리게 남은 프레임(차이 7.0)이 "완전히 가림"으로 빠졌다. 그래서 6.0.
SAME = 6.0


def reads_like(text: str, a: int, b: int) -> bool:
    """읽힌 글자열(확신도와 무관)이 앞뒤 통과 값과 닮았는가: 길이가 비슷하고 앞 두 자리가 같다."""
    return len(text) >= 4 and any(abs(len(text) - len(str(t))) <= 1 and text[:2] == str(t)[:2] for t in (a, b))
FAR = 1000              # 점수가 이만큼은 달라야 "다른 점수 시점"으로 본다 (끝자리만 다른 경우를 피한다)


def digit_boxes(grays: np.ndarray, good: np.ndarray) -> np.ndarray:
    """숫자가 놓이는 영역만 잘라 작게 줄인 그림 (프레임 수 x 720)."""
    sample = grays[good[::max(1, len(good) // 300)]].astype(np.float32)
    spread = sample.std(axis=0)                                  # 통과 프레임 사이에서 많이 바뀌는 곳 = 숫자 자리
    ys, xs = np.where(spread > max(10.0, 0.5 * float(np.percentile(spread, 97))))
    h, w = grays.shape[1:]
    y0, y1, x0, x1 = (max(0, ys.min() - 2), min(h, ys.max() + 3), max(0, xs.min() - 2), min(w, xs.max() + 3)) if len(ys) >= 20 else (0, h, 0, w)
    return np.stack([cv2.resize(g[y0:y1, x0:x1], (60, 12), interpolation=cv2.INTER_AREA).reshape(-1) for g in grays]).astype(np.int16)


# 시험하고 버린 방법 (다시 시도하지 말 것):
#   - 앞뒤 통과 프레임과 획 무늬의 상관: 완전히 가린 삽화에도 0.2~0.8 이 나왔다 (기준 프레임에 효과가 섞이고 바탕 무늬가 겹친다)
#   - 숫자만의 무늬를 본으로 떠서 어긋나게 댄 값과 비교: Boom! 은 효과 중에 숫자 색이 뒤집혀 또렷한 숫자도 0 근처가 나왔다
# 지금 방법의 알려진 오류: 아주 밝은 반투명 효과 위에 숫자가 흐리게 남은 프레임은 "똑같은 그림"으로 잡혀 빠진다.
#   뺀 프레임 56장을 눈으로 본 결과 3장(모두 Boom!)에서 숫자가 흐리게 보였다.


def twin_distance(boxes: np.ndarray, frames: list, low: np.ndarray, high: np.ndarray) -> dict:
    """프레임마다, 점수가 뚜렷이 다른 시점의 프레임 가운데 숫자 자리 그림이 가장 닮은 것과의 차이.

    숫자가 조금이라도 보이면 점수가 다른 프레임과는 숫자 자리가 달라야 한다.
    점수가 다른데도 그림이 똑같다면 그 프레임들에는 숫자가 보이지 않는 것이다 (같은 삽화, 같은 번쩍임, 빈 판).
    차이는 가장 많이 다른 화소 5% 의 평균으로 잰다 (숫자 한 자리만 달라도 드러나게).
    """
    if not frames:
        return {}
    idx = np.array(frames)
    v = boxes[idx]
    lo, hi = low[idx], high[idx]
    top = max(1, v.shape[1] // 20)
    out = {}
    for n, i in enumerate(idx):
        other = (hi + FAR < lo[n]) | (lo > hi[n] + FAR)
        if not other.any():
            out[int(i)] = np.inf
            continue
        d = np.abs(v[other] - v[n])
        out[int(i)] = float(np.partition(d, -top, axis=1)[:, -top:].mean(axis=1).min())
    return out


def classify(case_id: str, z, parts):
    values, conf, blank = z["values"], z["conf"], z["blank"]
    notes = notes_of(case_id)
    raw = np.where((conf >= READ_CONF) & ~blank & (values > 0), values, -1)
    keep = verify_sequence([int(v) for v in raw], notes)
    good = np.where(keep)[0]
    if len({int(raw[i]) for i in good}) < MIN_DISTINCT:
        return None
    start, stop = play_window(z, raw, keep)
    grays = np.stack([cv2.cvtColor(p, cv2.COLOR_BGR2GRAY) for p in parts])
    thumbs = np.stack([cv2.resize(g, (96, 24), interpolation=cv2.INTER_AREA) for g in grays]).astype(np.int16)
    usual = np.median(thumbs[good[::max(1, len(good) // 400)]], axis=0)
    differ = (np.abs(thumbs - usual) > 35).mean(axis=(1, 2))                 # 보통 모습과 다른 화소의 비율
    covered = min(0.60, max(0.20, float(np.percentile(differ[good], 99)) + 0.05))
    before = np.maximum.accumulate(np.where(keep, np.arange(len(raw)), -1))           # 바로 앞의 통과 프레임
    after = np.minimum.accumulate(np.where(keep, np.arange(len(raw)), len(raw))[::-1])[::-1]
    failed = [i for i in range(start, stop) if not keep[i]]
    low = np.zeros(len(raw), np.int64); high = np.zeros(len(raw), np.int64)
    for i in failed:
        low[i], high[i] = int(raw[before[i]]), int(raw[after[i]])
    twin = twin_distance(digit_boxes(grays, good), failed, low, high)
    kinds = {}
    for i in failed:
        a, b = int(low[i]), int(high[i])
        seen = twin[i]                            # 작을수록 "점수가 달라도 똑같은 그림" = 숫자가 안 보임
        text = str(z["texts"][i])
        unread = conf[i] < READ_CONF or blank[i] or not text
        gone = seen < SAME and not reads_like(text, a, b)
        if differ[i] > covered:
            kind = "완전히 가림" if gone else "덮였지만 비침"
        elif unread:
            kind = "숫자 없음" if gone else "보이는데 미판독"
        else:                                     # 점수일 수 없는 글자열(7자리 초과 등)도 여기서 원인을 본다
            def fits(s: str) -> bool:             # 앞뒤 통과 값 사이에 들고 산식에 맞는 값인가
                return s.isdigit() and not s.startswith("0") and a <= int(s) <= b and on_lattice(int(s), notes)

            if fits(text):
                kind = "규칙에만 걸림"
            elif any(fits(str(d) + text) for d in range(1, 100)):
                kind = "앞자리 빠짐"
            elif any(fits(text[cut:]) for cut in range(1, min(5, len(text)))) or any(fits(text[:-cut]) for cut in range(1, min(3, len(text)))):
                kind = "덧붙음"
            else:
                kind = "다르게 읽음"
        kinds[i] = (kind, a, b, seen, differ[i] > covered)
    return stop - start, int(keep[start:stop].sum()), kinds


def tile_of(z, parts, i: int, first: str, second: str):
    part = parts[i]
    scale = min(370 / part.shape[1], 96 / part.shape[0])
    big = cv2.resize(part, None, fx=scale, fy=scale, interpolation=cv2.INTER_CUBIC)
    tile = np.zeros((146, 384, 3), np.uint8); tile[46:46 + big.shape[0], 4:4 + big.shape[1]] = big
    cv2.putText(tile, first, (4, 16), cv2.FONT_HERSHEY_SIMPLEX, 0.45, (0, 255, 255), 1)
    cv2.putText(tile, second, (4, 36), cv2.FONT_HERSHEY_SIMPLEX, 0.45, (200, 200, 200), 1)
    return tile


def save_sheet(tiles, path: Path):
    if not tiles:
        return
    while len(tiles) % 4:
        tiles.append(np.zeros_like(tiles[0]))
    cv2.imencode(".png", np.vstack([np.hstack(tiles[r:r + 4]) for r in range(0, len(tiles), 4)]))[1].tofile(str(path))


def sheet(case_id: str, z, parts, kinds: dict, out: Path):
    tiles = []
    for kind in KINDS:
        frames = [i for i, k in kinds.items() if k[0] == kind]
        for i in [frames[j] for j in np.linspace(0, len(frames) - 1, min(4, len(frames))).astype(int)] if frames else []:
            tiles.append(tile_of(z, parts, i, f"[{KINDS.index(kind) + 1}] #{i + 1} read {z['texts'][i] or '-'} {z['conf'][i]:.2f}",
                                 f"between {kinds[i][1]} .. {kinds[i][2]}  seen {kinds[i][3]:.2f}"))
    save_sheet(tiles, out / f"{case_id}.png")


def calibration(case_id: str, z, parts, kinds: dict, out: Path):
    """덮인 프레임을 "똑같은 그림과의 차이"가 작은 것부터 늘어놓는다."""
    frames = sorted((i for i, k in kinds.items() if k[4] and np.isfinite(k[3])), key=lambda i: kinds[i][3])
    picks = [frames[j] for j in np.linspace(0, len(frames) - 1, min(32, len(frames))).astype(int)] if frames else []
    save_sheet([tile_of(z, parts, i, f"twin {kinds[i][3]:.1f}  #{i + 1}", f"between {kinds[i][1]} .. {kinds[i][2]}") for i in picks],
               out / f"{case_id}-calib.png")


def main():
    args = [a for a in sys.argv[1:] if a != "--calibrate"]
    calibrate = "--calibrate" in sys.argv
    run, out = args[0], Path(args[1])
    out.mkdir(parents=True, exist_ok=True)
    folder = ROOT / "data" / "full" / run
    (folder / "classes").mkdir(exist_ok=True)
    rest = args[2:]
    if rest and not rest[0].replace(".", "").isdigit():
        ids = rest
    else:
        limit = float(rest[0]) if rest else 0.99
        with open(folder / "summary.csv", encoding="utf-8-sig") as f:
            ids = [r["사례"] for r in csv.DictReader(f) if r["규칙 통과율"] and float(r["규칙 통과율"]) < limit]
    table = sources()
    rows = []
    for n, case_id in enumerate(ids, 1):
        z = np.load(folder / f"{case_id}.npz")
        log = out / f"{case_id}.log"
        parts = list(stream(case_id, table[case_id], log)); log.unlink()
        if len(parts) != len(z["values"]):
            print(f"{case_id}: 프레임 수가 다름 ({len(parts)} / {len(z['values'])})"); continue
        result = classify(case_id, z, parts)
        if result is None:
            print(f"{case_id}: 규칙대로 오른 값이 적어 분류하지 않음"); continue
        window, passed, kinds = result
        row = {"사례": case_id, "플레이 구간": window, "규칙 통과": passed}
        row.update({k: sum(1 for v in kinds.values() if v[0] == k) for k in KINDS})
        rows.append(row)
        code = np.zeros(len(parts), np.int8); seen = np.full(len(parts), np.nan, np.float32)
        for i, k in kinds.items():
            code[i] = KINDS.index(k[0]) + 1; seen[i] = k[3]
        np.savez_compressed(folder / "classes" / f"{case_id}.npz", kind=code, seen=seen, names=np.array(KINDS))
        sheet(case_id, z, parts, kinds, out)
        # 뺀 프레임이 정말 숫자가 안 보이는지 눈으로 검사할 수 있게, 사례마다 아무 6장을 따로 남긴다
        hidden = sorted(i for i, k in kinds.items() if k[0] in EXCLUDED)
        if hidden:
            pick = np.random.default_rng(0).choice(hidden, size=min(6, len(hidden)), replace=False)
            (out / "audit").mkdir(exist_ok=True)
            tiles = [tile_of(z, parts, int(i), f"{case_id[:10]} #{int(i) + 1}", f"twin {kinds[int(i)][3]:.1f}  read {z['texts'][int(i)] or '-'}") for i in sorted(pick)]
            while len(tiles) < 6:
                tiles.append(np.zeros_like(tiles[0]))
            cv2.imencode(".png", np.hstack(tiles[:4]))[1].tofile(str(out / "audit" / f"{case_id}-a.png"))
            cv2.imencode(".png", np.hstack(tiles[2:6]))[1].tofile(str(out / "audit" / f"{case_id}-b.png"))
        if calibrate:
            calibration(case_id, z, parts, kinds, out)
        print(f"[{n}/{len(ids)}] {case_id}: 구간 {window}, 통과 {passed}, " + ", ".join(f"{k} {row[k]}" for k in KINDS if row[k]), flush=True)
    if rows:
        path = folder / "classes.csv"
        old = []
        if path.exists():
            with open(path, encoding="utf-8-sig") as f:
                old = [r for r in csv.DictReader(f) if r["사례"] not in {x["사례"] for x in rows} and set(r) == set(rows[0])]
        with open(path, "w", newline="", encoding="utf-8-sig") as f:
            w = csv.DictWriter(f, fieldnames=list(rows[0])); w.writeheader(); w.writerows(sorted(old + rows, key=lambda r: r["사례"]))


if __name__ == "__main__":
    main()
