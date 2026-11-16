"""위치 탐색이 찾은 자리로 사례의 모든 프레임을 읽는다 (LEFT·CENTER 만).

읽은 뒤 보는 법:
  python -m detector.full_summary loc-<탐지>-<판독>[-all] left-center     (고정 뒤 구간의 규칙 통과율과 최종 점수)
  python -m detector.locate_compare <판독> loc-<탐지>-<판독>[-all]        (Codex 영역으로 읽은 결과와 프레임별 비교)


locate_run 의 채점은 0.5초 간격 화면의 최종 점수다. 사용자 기준은 전체 프레임이므로, 여기서는 찾은 자리로
원본 영상의 모든 프레임을 읽어 full_frames 와 같은 꼴로 남긴다. 그러면 full_summary 와 비교 도구를 그대로 쓸 수 있다.

  1) 구간 처음부터 0.1초 간격 화면을 넣어 위치를 고정한다 (locate_run.locate. 위치·기어·점수를 알려 주지 않는다).
  2) 고정 뒤 영역이 넓어지는 내역을 0.5초 간격 화면으로 구한다 (locate_run.read_whole_case).
  3) 모든 프레임을 차례로 읽는다. 고정 전의 프레임은 읽지 않은 것으로 남긴다(모듈은 고정 전에 점수를 내놓지 않는다).
     고정 뒤의 프레임은 그 시각까지 넓어진 영역으로 자른다.

한계: 영역 넓히기를 0.5초 간격 화면으로 구했다(실제 모듈은 화면이 들어오는 대로 한다). 프레임 시각은 영역을 고를 때만
"번호 ÷ 60"으로 어림하고, 저장하는 시각은 영상의 값을 쓴다.

실행: .venv\\Scripts\\python.exe -m detector.locate_full <탐지 이름> <판독 이름> [--all-model] [사례 ...]
출력: data\\full\\loc-<탐지>-<판독>[-all]\\<사례>.npz, locate.csv
"""
import csv
import json
import re
import shutil
import subprocess
import sys
import time

import numpy as np
import torch

from .build_dataset import MARGIN_X, MARGIN_Y, VALIDATION, letterbox
from .full_frames import sources
from .locate_cache import first_score_time
from .locate_model import RowFinder
from .locate_run import BAND, FPS, GROW, WATCH, locate, lock_area, posed, read_band, read_whole_case
from .locate_train import DATA, RUNS, fold_of
from .model import decode
from .score_frames import READ_CONF
from .selftrain import BLANK_STD, DATA_NAME, DEVICE, ROOT, load_model, to_tensor

VIDEO_FPS = 60.0


RETRY_MARGIN = (4, 2)     # 못 읽은 프레임을 한 번 더 읽을 때의 여백(가로, 세로). 찾은 자리는 Codex 영역보다 넉넉해 평소 여백(14, 8)까지 두면 둘레가 많이 들어간다


@torch.no_grad()
def read_all_frames(src: dict, steps: list, lock_at: float, pose, reader, log, second: list | None = None):
    """마지막(가장 넓은) 영역을 영상에서 잘라 받고, 프레임마다 그 시각의 영역으로 다시 잘라 읽는다.

    second 에 빈 목록을 주면 "한 번 더 읽기"를 한다: 숫자열이 안 나왔거나 확신도가 READ_CONF 에 못 미친 프레임을 여백을 좁혀 다시 잘라 읽고,
    그 판독이 또렷하면(값이 있고 확신도 READ_CONF 이상) 그것을 쓴다. second 에는 프레임마다 다시 읽은 값을 썼는지가 담긴다.
    값이 맞는지는 여기서 보지 않는다(그 뒤의 규칙 검사가 본다). 또렷하게 읽힌 틀린 값은 다시 읽지 않는다."""
    every = np.array([a for _, a in steps])                  # 영역은 가로로 넓어지고 세로로는 조금 움직인다. 모두 담는 범위를 받는다
    last = (every[:, 0].min(), every[:, 1].min(), every[:, 2].max(), every[:, 3].max())
    x0, y0 = max(0, int(round(last[0])) - MARGIN_X), max(0, int(round(last[1])) - MARGIN_Y)
    x1, y1 = min(1920, int(round(last[2])) + MARGIN_X), min(1080, int(round(last[3])) + MARGIN_Y)
    w, h = x1 - x0, y1 - y0
    command = [shutil.which("ffmpeg") or "ffmpeg", "-hide_banner", "-ss", str(src["start"]), "-i", src["source"],
               "-t", str(src["end"] - src["start"]), "-an", "-vf", f"format=rgb24,crop={w}:{h}:{x0}:{y0}:exact=1,showinfo",
               "-fps_mode", "passthrough", "-enc_time_base", "demux", "-f", "rawvideo", "-pix_fmt", "rgb24", "-"]
    values, conf, texts, contrast, locked = [], [], [], [], []
    batch, slots, tight = [], [], []

    def flush():
        if batch:
            v, c, t = decode(reader(to_tensor(np.stack(batch)).to(DEVICE)).cpu())
            for slot, a, b, s in zip(slots, v, c, t):
                values[slot], conf[slot], texts[slot] = int(a), float(b), s
            again = [j for j in range(len(batch)) if second is not None and (v[j] <= 0 or c[j] < READ_CONF)]
            if again:
                v2, c2, t2 = decode(reader(to_tensor(np.stack([tight[j] for j in again])).to(DEVICE)).cpu())
                for j, a, b, s in zip(again, v2, c2, t2):
                    if a > 0 and b >= READ_CONF:
                        values[slots[j]], conf[slots[j]], texts[slots[j]] = int(a), float(b), s
                        second[slots[j]] = True
            batch.clear(); slots.clear(); tight.clear()

    with open(log, "w", encoding="utf-8") as err, subprocess.Popen(command, stdout=subprocess.PIPE, stderr=err) as proc:
        size = w * h * 3
        n = 0
        while True:
            raw = proc.stdout.read(size)
            if len(raw) < size:
                break
            at = n / VIDEO_FPS
            values.append(-1); conf.append(0.0); texts.append(""); contrast.append(255.0); locked.append(at >= lock_at)
            if second is not None:
                second.append(False)
            if at >= lock_at:
                area = next(a for t, a in reversed(steps) if t <= at)
                xa, ya = max(0, int(round(area[0])) - MARGIN_X - x0), max(0, int(round(area[1])) - MARGIN_Y - y0)
                xb, yb = min(w, int(round(area[2])) + MARGIN_X - x0), min(h, int(round(area[3])) + MARGIN_Y - y0)
                frame = np.frombuffer(raw, np.uint8).reshape(h, w, 3)[ya:yb, xa:xb, ::-1]
                image, std = letterbox(posed(np.ascontiguousarray(frame), pose))
                contrast[n] = std; batch.append(image); slots.append(n)
                if second is not None:
                    mx, my = RETRY_MARGIN
                    xa, ya = max(0, int(round(area[0])) - mx - x0), max(0, int(round(area[1])) - my - y0)
                    xb, yb = min(w, int(round(area[2])) + mx - x0), min(h, int(round(area[3])) + my - y0)
                    tight.append(letterbox(posed(np.ascontiguousarray(np.frombuffer(raw, np.uint8).reshape(h, w, 3)[ya:yb, xa:xb, ::-1]), pose))[0])
                if len(batch) == 512:
                    flush()
            n += 1
        flush()
    pts = [float(m[1]) for m in re.finditer(r" n:\s*\d+.*?pts_time:([\d.\-]+)", open(log, encoding="utf-8", errors="replace").read())]
    return values, conf, texts, contrast, locked, pts[:len(values)]


def main():
    finder_name, reader_name = sys.argv[1], sys.argv[2]
    wanted = set(sys.argv[3:])
    use_all = "--all-model" in wanted
    wanted.discard("--all-model")
    cases = [c for c in json.loads((VALIDATION / "candidate-v4" / "all270.json").read_text(encoding="utf-8")) if c["position"] in BAND]
    if wanted:
        cases = [c for c in cases if c["id"] in wanted]
    index = json.loads((DATA / "index.json").read_text(encoding="utf-8"))
    gears = sorted({i["gear"] for i in index})
    reader, _ = load_model(reader_name); reader.eval()
    out = ROOT / "data" / "full" / f"loc-{finder_name}-{reader_name}{'-all' if use_all else ''}"
    out.mkdir(parents=True, exist_ok=True)
    table = sources()
    finders = {}
    rows = []
    started = time.time()
    for n, c in enumerate(cases, 1):
        case_id = c["id"]; gear = case_id.rsplit("-", 2)[0]
        if not (ROOT / "data" / DATA_NAME / f"{case_id}.npz").exists():
            continue
        fold = "all" if use_all else fold_of(gear, gears)
        if fold not in finders:
            m = RowFinder().to(DEVICE)
            m.load_state_dict(torch.load(RUNS / finder_name / ("all.pt" if use_all else f"fold{fold}.pt"), map_location=DEVICE)); m.eval()
            finders[fold] = m
        src = table[case_id]
        first = first_score_time(case_id)
        x0 = BAND[c["position"]][0]
        box, lock_frame, _, _, pose = locate(read_band(src, c["position"], (first or 10.0) + WATCH), finders[fold], reader)
        row = {"사례": case_id, "고정": "예" if box else "아니오", "고정 시각": "", "프레임": 0, "고정 전 프레임": 0, "영역(고정 때)": "", "영역(마지막)": "",
               "0.5초 화면 최종 점수": "", "보류 이유": ""}
        if box is not None:
            box_abs = lock_area(box, x0)
            lock_at = lock_frame / FPS
            final, _, why, _, _, steps = read_whole_case(c, box_abs, reader, pose, finders[fold] if GROW else None, x0)
            log = out / f"{case_id}.ffmpeg.log"
            values, conf, texts, contrast, locked, pts = read_all_frames(src, steps, lock_at, pose, reader, log)
            log.unlink()
            np.savez_compressed(out / f"{case_id}.npz", index=np.arange(1, len(values) + 1), pts=np.array(pts, np.float32), values=np.array(values),
                                conf=np.array(conf, np.float32), texts=np.array(texts), blank=np.array(contrast, np.float32) < BLANK_STD,
                                press=np.float32(src["press"] - src["start"]), locked=np.array(locked), lock_at=np.float32(lock_at),
                                area_first=np.array(steps[0][1], np.float32), area_last=np.array(steps[-1][1], np.float32))
            row.update({"고정 시각": f"{lock_at:.1f}", "프레임": len(values), "고정 전 프레임": int(len(locked) - sum(locked)),
                        "영역(고정 때)": " ".join(str(int(round(v))) for v in steps[0][1]), "영역(마지막)": " ".join(str(int(round(v))) for v in steps[-1][1]),
                        "0.5초 화면 최종 점수": "" if final is None else final, "보류 이유": why})
        rows.append(row)
        print(f"[{n}/{len(cases)}] {case_id}: 고정 {row['고정']} {row['고정 시각']}초, 프레임 {row['프레임']} ({time.time() - started:.0f}초)", flush=True)
    if not wanted:
        with open(out / "locate.csv", "w", newline="", encoding="utf-8-sig") as f:
            w = csv.DictWriter(f, fieldnames=list(rows[0])); w.writeheader(); w.writerows(rows)
    print(f"사례 {len(rows)}개, 고정 {sum(r['고정'] == '예' for r in rows)}개, 프레임 {sum(r['프레임'] for r in rows):,}장")


if __name__ == "__main__":
    main()
