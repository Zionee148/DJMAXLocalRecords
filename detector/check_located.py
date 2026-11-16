"""모듈이 스스로 찾은 자리로 읽은 결과를 다른 근거와 견준다 (독립 검산, 2026-11-14).

점수 규칙은 읽은 값의 검산일 뿐이라 "규칙까지 통과한 틀린 값"은 규칙으로 잡히지 않는다. 여기서는 규칙 밖의 근거 셋과 견준다.

  labels  정답 라벨과 견준다: 우리 정답(Windows OCR + 규칙으로 만든 두 사례)과 Codex 검토 라벨(확대 그림을 직접 읽은 100장).
          고정 뒤 프레임만 센다. 라벨이 없는 프레임은 정답 미확인이다.
  cross   같은 프레임을 다른 방식으로 읽은 실행과 견준다(예: Codex 영역으로 읽은 v17, 시제품이 찾은 자리로 읽은 vid-d2-v17-r).
          두 쪽이 모두 규칙을 통과했는데 값이 다르면 어느 한쪽은 규칙을 통과한 틀린 값이다. 판독기는 같으므로 판독기의 독립 검산은 아니고,
          자리를 스스로 찾아 자른 것이 값을 바꾸지 않는지를 본다.
  sample  사례마다 규칙을 통과한 프레임 한 장을 무작위로 뽑아, 모듈이 자른 자리를 크게 한 그림을 만든다. 그림에는 번호만 적고 값은 적지 않는다.
          사람(또는 Claude)이 값을 모른 채 읽어 <그림 폴더>\\읽은값.tsv(번호, 읽은 값)에 적는다. 답은 <그림 폴더>\\key.json 에 따로 둔다.
  score   읽은값.tsv 를 key.json 과 견준다.

실행: .venv\\Scripts\\python.exe -m detector.check_located <모듈이 읽은 실행(vid-…)> labels
      .venv\\Scripts\\python.exe -m detector.check_located <모듈이 읽은 실행(vid-…)> cross <다른 실행> [출력 csv]
      .venv\\Scripts\\python.exe -m detector.check_located <모듈이 읽은 실행(vid-…)> sample <그림 폴더> [씨앗=20261114]
      .venv\\Scripts\\python.exe -m detector.check_located <모듈이 읽은 실행(vid-…)> score <그림 폴더>
"""
import csv
import json
import sys
from pathlib import Path

import cv2
import numpy as np

from .check_masks import area_at, cases_of, grab_at, names, position_of, true_times
from .full_frames import sources
from .score_frames import READ_CONF, codex_truth, own_truth, tally
from .score_rules import verify_sequence
from .selftrain import ROOT, notes_of

PER_ROW, PER_PAGE = 4, 32
TILE_W, TILE_H = 460, 120


def read(run: str, case_id: str):
    """(판독 파일, 확신 있게 읽은 값(-1 = 미판독), 규칙 통과 여부)"""
    z = np.load(ROOT / "data" / "full" / run / f"{case_id}.npz")
    raw = np.where((z["conf"] >= READ_CONF) & ~z["blank"] & (z["values"] > 0), z["values"], -1)
    keep = np.array(verify_sequence([int(v) for v in raw], notes_of(case_id)), bool)
    return z, raw, keep


def labels(run: str):
    name = names()
    total = [0, 0, 0, 0]
    for case_id in cases_of(run):
        z, raw, keep = read(run, case_id)
        checked = np.where(keep, raw, -1)
        for truth, where in (own_truth(case_id), codex_truth(case_id, z["pts"])):
            if not truth:
                if where:
                    print(f"{case_id}: {where}")
                continue
            after = {i: v for i, v in truth.items() if i - 1 < len(z["locked"]) and z["locked"][i - 1]}
            match, wrong, unread, bad = tally(checked, after)
            total[0] += len(after); total[1] += match; total[2] += wrong; total[3] += unread
            print(f"{case_id} {name.get(case_id, '')} [{where}]: 라벨 {len(truth)}장, 그중 고정 뒤 {len(after)}장 → 일치 {match}, 오독 {wrong}, 미판독 {unread}"
                  + ("" if not bad else "  | 어긋난 프레임(번호 정답 판독): " + ", ".join(f"#{i} {want} {got if got >= 0 else '-'}" for i, want, got in bad[:12])))
    print(f"합계: 고정 뒤 라벨 {total[0]}장 → 일치 {total[1]}, 오독 {total[2]}, 미판독 {total[3]}")


def cross(run: str, other: str, out: Path | None):
    name = names()
    rows, differ = [], []
    for case_id in cases_of(run):
        if not (ROOT / "data" / "full" / other / f"{case_id}.npz").exists():
            continue
        z, raw, keep = read(run, case_id)
        _, raw2, keep2 = read(other, case_id)
        n = min(len(raw), len(raw2))
        locked = z["locked"][:n]
        both = locked & keep[:n] & keep2[:n]
        diff = np.flatnonzero(both & (raw[:n] != raw2[:n]))
        rows.append({"사례": case_id, "기어": name.get(case_id, ""), "위치": position_of(case_id), "고정 뒤 프레임": int(locked.sum()),
                     "둘 다 규칙 통과": int(both.sum()), "그중 값이 다름": len(diff),
                     "모듈만 통과": int((locked & keep[:n] & ~keep2[:n]).sum()), "다른 쪽만 통과": int((locked & ~keep[:n] & keep2[:n]).sum())})
        differ += [(case_id, int(i) + 1, int(raw[i]), int(raw2[i])) for i in diff]
    if out:
        with open(out, "w", newline="", encoding="utf-8-sig") as f:
            w = csv.DictWriter(f, fieldnames=list(rows[0])); w.writeheader(); w.writerows(rows)
    print(f"{run} 와 {other}: 사례 {len(rows)}개")
    for position in ("LEFT", "CENTER", "RIGHT"):
        part = [r for r in rows if r["위치"] == position]
        total = lambda key: sum(r[key] for r in part)
        print(f"  {position:6} 사례 {len(part)} | 고정 뒤 프레임 {total('고정 뒤 프레임'):,} | 둘 다 규칙 통과 {total('둘 다 규칙 통과'):,}, 그중 값이 다름 {total('그중 값이 다름')} | "
              f"모듈만 통과 {total('모듈만 통과'):,}, 다른 쪽만 통과 {total('다른 쪽만 통과'):,}")
    for case_id, index, a, b in differ[:40]:
        print(f"  값이 다름: {case_id} #{index} 모듈 {a} / 다른 쪽 {b}")


def sample(run: str, folder: Path, seed: int):
    folder.mkdir(parents=True, exist_ok=True)
    rng = np.random.default_rng(seed)
    table = sources()
    chosen = []
    for case_id in cases_of(run):
        z, raw, keep = read(run, case_id)
        times = true_times(case_id)
        if times is None:
            continue
        pool = np.flatnonzero(z["locked"] & keep & (z["pts"] < float(z["press"])))
        pool = pool[pool < len(times)]
        if len(pool):
            chosen.append((case_id, int(rng.choice(pool)), times))
    order = rng.permutation(len(chosen))                       # 번호가 기어 순서를 따르지 않게 섞는다
    key, tiles = {}, []
    for serial, k in enumerate(order, 1):
        case_id, i, times = chosen[k]
        z, raw, keep = read(run, case_id)
        steps = json.loads((ROOT / "data" / "full" / run / f"{case_id}.steps.json").read_text(encoding="utf-8"))["steps"]
        frame = grab_at(table[case_id]["source"], table[case_id]["start"] + float(times[i]) - 0.004)
        tile = np.zeros((TILE_H + 22, TILE_W, 3), np.uint8)
        cv2.putText(tile, f"{serial}", (4, 17), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 255, 255), 1, cv2.LINE_AA)
        if frame is not None:
            x0, y0, x1, y1 = area_at(steps, i / 60.0)
            left, right = int(max(0, x0 - 24)), int(min(1920, x1 + 24)); top, bottom = int(max(0, y0 - 12)), int(min(1080, y1 + 12))
            part = frame[top:bottom, left:right]
            scale = min(TILE_W / part.shape[1], TILE_H / part.shape[0])
            big = cv2.resize(part, None, fx=scale, fy=scale, interpolation=cv2.INTER_CUBIC)
            tile[22:22 + big.shape[0], :big.shape[1]] = big
        key[serial] = {"case": case_id, "frame": i + 1, "value": int(raw[i]), "grabbed": frame is not None}
        tiles.append(tile)
        print(f"{serial}/{len(order)}", end="\r", flush=True)
    for page in range(0, len(tiles), PER_PAGE):
        part = tiles[page:page + PER_PAGE]
        while len(part) % PER_ROW:
            part.append(np.zeros_like(part[0]))
        cv2.imencode(".png", np.vstack([np.hstack(part[r:r + PER_ROW]) for r in range(0, len(part), PER_ROW)]))[1].tofile(str(folder / f"sheet-{page // PER_PAGE + 1:02d}.png"))
    (folder / "key.json").write_text(json.dumps({"run": run, "seed": seed, "items": key}, ensure_ascii=False, indent=1), encoding="utf-8")
    print(f"사례 {len(chosen)}개에서 한 장씩, 그림 {(len(tiles) + PER_PAGE - 1) // PER_PAGE}장 → {folder}")


def score(folder: Path):
    key = json.loads((folder / "key.json").read_text(encoding="utf-8"))["items"]
    with open(folder / "읽은값.tsv", encoding="utf-8-sig", newline="") as f:
        mine = {r[0]: r[1].strip() for r in csv.reader(f, delimiter="\t") if len(r) >= 2 and r[0].isdigit()}
    same = diff = skipped = 0
    for serial, item in key.items():
        got = mine.get(serial, "")
        if not got.isdigit():
            skipped += 1
            print(f"  읽지 못함: {serial} {item['case']} #{item['frame']} 모듈 {item['value']} / 적은 것 '{got}'")
        elif int(got) == item["value"]:
            same += 1
        else:
            diff += 1
            print(f"  다름: {serial} {item['case']} #{item['frame']} 모듈 {item['value']} / 읽은 값 {got}")
    print(f"뽑은 {len(key)}장: 모듈과 같음 {same}, 다름 {diff}, 읽지 못함 {skipped}")


def main():
    run, mode = sys.argv[1], sys.argv[2]
    if mode == "labels":
        labels(run)
    elif mode == "cross":
        cross(run, sys.argv[3], ROOT / sys.argv[4] if len(sys.argv) > 4 else None)
    elif mode == "sample":
        sample(run, ROOT / sys.argv[3], int(sys.argv[4]) if len(sys.argv) > 4 else 20261114)
    elif mode == "score":
        score(ROOT / sys.argv[3])
    else:
        raise SystemExit(__doc__)


if __name__ == "__main__":
    main()
