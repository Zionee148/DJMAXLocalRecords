"""원본 영상만으로 한 사례를 처음부터 끝까지 읽는다: 위치 찾기 → 영역 넓히기 → 모든 프레임 읽기 → 요약.

locate_run·locate_full 은 영역 넓히기와 최종 판단에 Codex 가 뽑아 둔 0.5초 PNG 와 그것으로 만든 자료(data\\frames05b)를 쓴다.
그래서 Codex 가 위치를 못 찾아 자료가 없는 사례(3-05 CENTER, welcome to the space)는 볼 수 없었다.
여기서는 영상에서 화면 아래쪽 띠를 직접 뽑아 같은 일을 한다. Codex 폴더에서 읽는 것은 구간 시각(sources)과 기대 점수뿐이다.

  1) 구간 처음부터 0.1초 간격 띠를 넣어 위치를 고정한다 (locate_run.locate. 위치·기어·점수를 알려 주지 않는다).
  2) 구간 전체의 0.5초 간격 띠로 고정 뒤의 영역 넓히기 내역을 구한다 (locate_run.read_whole_case 와 같은 규칙).
  3) 모든 프레임을 그 시각의 영역으로 잘라 읽는다 (locate_full.read_all_frames).
  4) full_summary 의 방식으로 요약한다. 고정 전 프레임은 읽지 않은 것으로 남는다.

탐지 모델은 기어 90종 전부로 학습한 것(all.pt)을 쓴다. 3-05 CENTER 의 화면은 탐지 모델의 학습 자료에 없었다(Codex 영역이 없어서).

실행: .venv\\Scripts\\python.exe -m detector.locate_video <탐지 이름> <판독 이름> <사례 ... | left | center | right>
      .venv\\Scripts\\python.exe -m detector.locate_video <탐지 이름> <판독 이름> --summary      (위치별 요약과 locate.csv)
      .venv\\Scripts\\python.exe -m detector.locate_video <탐지 이름> <판독 이름> --normal <정상 프레임 목록의 실행> [Codex 영역으로 읽은 실행]
                                                                                    (LEFT·CENTER 를 정상 프레임 기준으로 셈. 끝에 right 를 붙이면 RIGHT.
                                                                                     Codex 영역 판독이 없는 사례는 찾은 자리만 세고 견줌 칸을 비운다)
      .venv\\Scripts\\python.exe -m detector.locate_video <탐지 이름> <판독 이름> --steps <left | center>
                                                                                    (영역 내역만 다시 구해 <사례>.steps.json 으로. 모든 프레임을 읽지는 않는다)
출력: data\\full\\vid-<탐지>-<판독>\\<사례>.npz 와 <사례>.json(그 사례의 결과 한 줄)

RIGHT(사용자 결정, 2026-11-12 밤 "RIGHT도 포함"): 탐지 모델도 판독기도 RIGHT 녹화를 본 적이 없다. 한 번 돌려 숫자만 보고,
실패한 사례를 들여다보며 고치는 것은 LEFT·CENTER 를 마무리한 뒤에 한다.
"""
import csv
import json
import shutil
import subprocess
import sys

import numpy as np
import torch

from .build_dataset import VALIDATION
from .full_frames import sources
from .full_summary import play_window, summarize
from .locate import overlap
from .locate_eval import iou
from .locate_full import read_all_frames
from .locate_model import RowFinder, find_rows
from .locate_run import BAND, BAND_RIGHT, FPS, TOP, better_area, locate, lock_area, vertical_range
from .locate_train import RUNS
from .score_frames import READ_CONF
from .score_rules import verify_sequence
from . import inputs
from .selftrain import BLANK_STD, DEVICE, ROOT, load_model, notes_of

WATCH_ALL = 45.0          # 구간 처음부터 이만큼을 위치 찾기에 넣는다 (플레이 시작 시각을 모르는 채로)
HALF = 2                  # 영역 넓히기에 쓰는 띠의 간격: 초당 2장
FOLLOW = 5                # 움직이는 점수판을 따라갈 때의 띠 간격: 초당 5장 (모듈은 탐지 모델을 0.2~0.3초마다 돌린다)


BANDS = dict(BAND, RIGHT=BAND_RIGHT)


def read_bands(src: dict, position: str, per_second: int = HALF, seconds: float | None = None) -> np.ndarray:
    """화면 아래쪽 띠(BGR)를 초당 per_second 장씩. seconds 를 주면 구간 처음부터 그만큼만."""
    x0, x1 = BANDS[position]
    w, h = x1 - x0, 1080 - TOP
    command = [shutil.which("ffmpeg") or "ffmpeg", "-hide_banner", "-loglevel", "error", "-ss", str(src["start"]), "-i", src["source"],
               "-t", str(seconds if seconds is not None else src["end"] - src["start"]), "-an", "-vf", f"crop={w}:{h}:{x0}:{TOP},fps={per_second}", "-f", "rawvideo", "-pix_fmt", "bgr24", "-"]
    raw = subprocess.run(command, capture_output=True).stdout
    n = len(raw) // (w * h * 3)
    return np.frombuffer(raw, np.uint8, n * w * h * 3).reshape(n, h, w, 3)


@torch.no_grad()
def grow_steps(bands: np.ndarray, box_abs, lock_at: float, finder, reader, band_x0: int):
    """고정 뒤의 영역 넓히기 내역 [(그때부터, 영역)]과, 점수판이 위아래로 움직이는지. 규칙은 locate_run.read_whole_case 와 같다."""
    area = [float(v) for v in box_abs]
    first_w = area[2] - area[0]
    steps = [(-1.0, tuple(area))]
    tops, bottoms = [area[1]], [area[3]]
    seen_tops, seen_bottoms = [], []                          # 고정 뒤 영역 가까이에서 잡힌 모든 네모의 세로 (움직임을 알아보는 데 쓴다)
    waiting = None

    def in_band(a):
        return (a[0] - band_x0, a[1] - TOP, a[2] - band_x0, a[3] - TOP)

    for k, image in enumerate(bands):
        at = k / HALF
        if at < lock_at:
            continue
        near = None
        for b in find_rows(finder, np.ascontiguousarray(image[:, :, ::-1])[None], DEVICE)[0]:
            box = (b[0] + band_x0, b[1] + TOP, b[2] + band_x0, b[3] + TOP)
            if overlap(box, area) >= 0.5 and 0.6 <= (box[3] - box[1]) / (area[3] - area[1]) <= 1.6:
                near = box; break
        if near is not None and waiting is not None and iou(near, waiting) >= 0.7:
            tops.append(near[1]); bottoms.append(near[3])
            top, bottom, _ = vertical_range(tops, bottoms)
            grown = [min(area[0], near[0]), top, max(area[2], near[2]), bottom]
            if grown[2] - grown[0] <= 2.5 * first_w and grown != area and better_area(image, 0, in_band(area), in_band(grown), reader):
                area = grown; steps.append((at, tuple(area)))
        waiting = near
        if near is not None:
            seen_tops.append(near[1]); seen_bottoms.append(near[3])
    return steps, (vertical_range(seen_tops, seen_bottoms)[2] if seen_tops else False), (seen_tops, seen_bottoms)


@torch.no_grad()
def follow_steps(bands: np.ndarray, steps: list, lock_at: float, finder, band_x0: int, seen) -> list:
    """움직이는 점수판을 따라간다: 탐지 모델이 숫자 줄을 잡을 때마다 영역의 세로를 그 네모에 맞춘다. 가로는 grow_steps 가 정한 대로.

    읽어서 확인하지는 않는다(점수판이 옮겨 간 화면에서는 지금 영역이 안 읽혀 견줄 수 없다). 대신 옮겨 갈 수 있는 범위를 묶어 둔다:
    고정 뒤에 잡힌 네모들이 오르내린 범위에서 네모 높이의 반 넘게 벗어난 네모는 따르지 않는다(위의 글자 줄로 옮겨 가지 않도록).
    """
    tops, bottoms = np.asarray(seen[0], float), np.asarray(seen[1], float)
    height = float(np.median(bottoms - tops))
    low, high = float(np.percentile(tops, 2)) - 0.5 * height, float(np.percentile(bottoms, 98)) + 0.5 * height
    out = [steps[0]]
    area = list(steps[0][1])
    for k, image in enumerate(bands):
        at = k / FOLLOW
        if at < lock_at:
            continue
        wide = next(a for t, a in reversed(steps) if t <= at)             # 그 시각까지 넓어진 가로
        best = None
        for b in find_rows(finder, np.ascontiguousarray(image[:, :, ::-1])[None], DEVICE)[0]:
            box = (b[0] + band_x0, b[1] + TOP, b[2] + band_x0, b[3] + TOP)
            across = (min(box[2], wide[2]) - max(box[0], wide[0])) / max(1.0, box[2] - box[0])
            if across >= 0.6 and 0.6 <= (box[3] - box[1]) / height <= 1.6 and box[1] >= low and box[3] <= high:
                if best is None or abs((box[1] + box[3]) - (area[1] + area[3])) < abs((best[1] + best[3]) - (area[1] + area[3])):
                    best = box
        new = [wide[0], area[1], wide[2], area[3]] if best is None else [wide[0], best[1], wide[2], best[3]]
        if new != area:
            area = new; out.append((at, tuple(area)))
    return out


def save_steps(out, case_id: str, lock_at: float, pose, steps: list, moving: bool):
    """고정 뒤 영역이 바뀐 내역을 남긴다. 판독기 학습 때 "모듈이 실제로 자르는 자리"로 조각을 만드는 데 쓴다(train_wide. LEFT·CENTER 만)."""
    (out / f"{case_id}.steps.json").write_text(json.dumps(
        {"lock_at": lock_at, "pose": pose, "moving": bool(moving), "steps": [[float(t), [float(v) for v in a]] for t, a in steps]}), encoding="utf-8")


def load_steps(out, case_id: str):
    path = out / f"{case_id}.steps.json"
    if not path.exists():
        return None
    d = json.loads(path.read_text(encoding="utf-8"))
    return d["lock_at"], d["pose"], [(t, tuple(a)) for t, a in d["steps"]]


def first_score(case_id: str):
    """참고용: Codex 영역으로 읽은 옛 판독(v5, 269개 사례)에서 규칙을 통과한 첫 점수가 나온 시각(구간 시작 기준, 초).

    플레이가 시작된 시각의 라벨은 없다. 이것은 "고정이 첫 점수 뒤 몇 초였나"를 어림하는 데만 쓰고, 20초 기준의 판정이 아니다.
    """
    path = ROOT / "data" / "full" / "v5" / f"{case_id}.npz"
    if not path.exists():
        return None
    z = np.load(path)
    raw = np.where((z["conf"] >= READ_CONF) & ~z["blank"] & (z["values"] > 0), z["values"], -1)
    keep = np.array(verify_sequence([int(v) for v in raw], notes_of(case_id)), bool)
    if keep.sum() < 8:
        return None
    return float(z["pts"][play_window(z, raw, keep)[0]])


def summary(out):
    rows = [json.loads(p.read_text(encoding="utf-8")) for p in inputs(out, "*.json", "사례별 결과") if not p.name.endswith(".steps.json")]
    with open(out / "locate.csv", "w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0])); w.writeheader(); w.writerows(rows)
    print(f"사례 {len(rows)}개 ({out.name})")
    print("위치 | 사례 | 고정 | 최종 점수 맞음 / 틀림 / 보류 | 고정 뒤 규칙 통과율 99% 이상 / 95~99 / 95 미만 | 고정까지(첫 점수 뒤, 참고) 가운데·가장 늦은·15초 넘는 것 | 움직이는 점수판")
    for position in ("LEFT", "CENTER", "RIGHT"):
        part = [r for r in rows if r["위치"] == position]
        if not part:
            continue
        locked = [r for r in part if r["고정"] == "예"]
        rate = [float(r["규칙 통과율"]) for r in locked if r["규칙 통과율"] != ""]
        wait = [float(r["첫 점수 뒤(초)"]) for r in locked if r["첫 점수 뒤(초)"] != ""]
        verdict = {k: sum(r["최종 판정"] == k for r in part) for k in ("맞음", "틀림", "보류")}
        print(f"{position:6} | {len(part)} | {len(locked)} | {verdict['맞음']} / {verdict['틀림']} / {verdict['보류'] - (len(part) - len(locked))} (+고정 못 함 {len(part) - len(locked)}) | "
              f"{sum(v >= 0.99 for v in rate)} / {sum(0.95 <= v < 0.99 for v in rate)} / {sum(v < 0.95 for v in rate)} | "
              + (f"{np.median(wait):.1f}초 · {max(wait):.1f}초 · {sum(v > 15 for v in wait)}개" if wait else "-")
              + f" | {sum(r['움직이는 점수판'] == '예' for r in part)}개")


def normal_check(out, masks_run: str, area_run: str, right: bool = False):
    """찾은 자리로 읽은 결과를 정상 프레임 기준으로 센다 (LEFT·CENTER. 가림 판정은 Codex 영역으로 읽은 실행에서 만든 목록을 빌린다).

    프레임 번호는 full_frames 와 같으므로(같은 구간을 같은 방식으로 뽑는다) 목록을 그대로 댈 수 있다. 고정 전의 프레임은 세지 않고 따로 적는다.
    같은 프레임을 Codex 영역으로 읽은 실행(area_run)이 못 읽은 수도 나란히 낸다.

    세는 사례: 정상 프레임 목록이 있는 사례 전부. Codex 영역으로 읽은 판독이 없는 사례(3-05 CENTER: Codex 영역이 없던 사례)는
    찾은 자리의 수만 세고 견줌 칸을 비운다. 전에는 그런 사례를 통째로 건너뛰어 LEFT·CENTER 가 179개로만 세어졌다
    (2026-11-17 리뷰 지적). 목록이 없어 세지 못한 사례와 견줌이 없는 사례는 이름을 찍는다.
    """
    from .fever import normal_frames
    names = {c["id"]: c["name"] for c in json.loads((ROOT / "data" / "frames05b" / "cases.json").read_text(encoding="utf-8"))} \
        if (ROOT / "data" / "frames05b" / "cases.json").exists() else {}
    rows, no_mask = [], []
    for path in inputs(out, "*.npz", "사례별 판독 결과"):
        case_id = path.stem
        mask = ROOT / "data" / "full" / masks_run / "fever" / f"{case_id}.npz"
        other = ROOT / "data" / "full" / area_run / f"{case_id}.npz"
        if ("-right-" in case_id) != right:
            continue
        if not mask.exists():
            no_mask.append(case_id)
            continue
        z, m, c = np.load(path), np.load(mask), np.load(other) if other.exists() else None
        n = min(len(z["values"]), len(m["covered"]), *([len(c["values"])] if c is not None else []))

        def kept(run):
            raw = np.where((run["conf"] >= READ_CONF) & ~run["blank"] & (run["values"] > 0), run["values"], -1)
            return np.array(verify_sequence([int(v) for v in raw], notes_of(case_id)), bool)[:n]

        inside = np.zeros(n, bool); inside[int(m["window"][0]):int(m["window"][1])] = True
        normal = normal_frames(case_id, inside, m["fever"][:n], m["covered"][:n])
        after = normal & z["locked"][:n]
        rows.append({"사례": case_id, "기어": names.get(case_id, ""), "정상 프레임": int(normal.sum()), "그중 고정 전": int((normal & ~z["locked"][:n]).sum()),
                     "고정 뒤 정상 프레임": int(after.sum()), "찾은 자리로 못 읽음": int((after & ~kept(z)).sum()),
                     "Codex 영역으로 못 읽음": int((after & ~kept(c)).sum()) if c is not None else ""})
    if no_mask:
        print(f"정상 프레임 목록({masks_run})이 없어 세지 못한 사례 {len(no_mask)}개: {', '.join(no_mask)}")
    if not rows:
        sys.exit(f"셀 사례가 없다: {out} 에 {'RIGHT' if right else 'LEFT·CENTER'} 판독이 없거나 정상 프레임 목록({masks_run})이 없다.")
    with open(out / f"normal-as-{masks_run}{'-right' if right else ''}.csv", "w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0])); w.writeheader(); w.writerows(rows)
    found = [r["찾은 자리로 못 읽음"] for r in rows]
    paired = [r for r in rows if r["Codex 영역으로 못 읽음"] != ""]
    alone = [r["사례"] for r in rows if r["Codex 영역으로 못 읽음"] == ""]
    print(f"사례 {len(rows)}개 (정상 프레임 목록 {masks_run}, 견줌 {area_run}): 정상 프레임 {sum(r['정상 프레임'] for r in rows):,}장, 그중 고정 전 {sum(r['그중 고정 전'] for r in rows):,}장")
    print(f"  고정 뒤 정상 프레임 {sum(r['고정 뒤 정상 프레임'] for r in rows):,}장 가운데 찾은 자리로 못 읽은 것 {sum(found):,}장")
    print(f"  Codex 영역과의 견줌(견줄 판독이 있는 {len(paired)}개, 고정 뒤 정상 프레임 {sum(r['고정 뒤 정상 프레임'] for r in paired):,}장): "
          f"찾은 자리 {sum(r['찾은 자리로 못 읽음'] for r in paired):,}장 / Codex 영역 {sum(r['Codex 영역으로 못 읽음'] for r in paired):,}장")
    if alone:
        print(f"  견줄 판독({area_run})이 없어 찾은 자리만 센 사례 {len(alone)}개: {', '.join(alone)}")
    print(f"  찾은 자리로 모두 읽은 사례 {sum(x == 0 for x in found)}개, 1~5장 {sum(1 <= x <= 5 for x in found)}개, 6~50장 {sum(6 <= x <= 50 for x in found)}개, 51장 이상 {sum(x > 50 for x in found)}개")
    worst = sorted(rows, key=lambda r: -r["찾은 자리로 못 읽음"])
    print("  많은 순:", ", ".join(f"{r['사례'][:-7]} {r['기어'][:14]} {r['찾은 자리로 못 읽음']}(Codex 영역 {'견줌 없음' if r['Codex 영역으로 못 읽음'] == '' else r['Codex 영역으로 못 읽음']})"
                                for r in worst if r["찾은 자리로 못 읽음"] > 0))


def main():
    finder_name, reader_name, wanted = sys.argv[1], sys.argv[2], [a for a in sys.argv[3:] if a != "--retry"]
    retry = "--retry" in sys.argv[3:]                             # 못 읽은 프레임을 여백을 좁혀 한 번 더 읽는다. 결과는 vid-…-r 에 따로 둔다
    base = ROOT / "data" / "full" / f"vid-{finder_name}-{reader_name}"
    if retry:
        reader_name += "-r"
    if wanted and wanted[0] == "--summary":
        return summary(ROOT / "data" / "full" / f"vid-{finder_name}-{reader_name}")
    if wanted and wanted[0] == "--normal":
        return normal_check(ROOT / "data" / "full" / f"vid-{finder_name}-{reader_name}", wanted[1], wanted[2] if len(wanted) > 2 else reader_name, "right" in wanted[3:])
    expected = {c["id"]: c["registration"]["expected"] for c in json.loads((VALIDATION / "candidate-v4" / "all270.json").read_text(encoding="utf-8"))}
    reader, _ = load_model(reader_name.removesuffix("-r")); reader.eval()
    finder = RowFinder().to(DEVICE)
    finder.load_state_dict(torch.load(RUNS / finder_name / "all.pt", map_location=DEVICE)); finder.eval()
    out = ROOT / "data" / "full" / f"vid-{finder_name}-{reader_name}"
    out.mkdir(parents=True, exist_ok=True)
    table = sources()
    if wanted and wanted[0] in ("left", "center", "right"):       # 그 위치의 사례 전부. 이미 읽은 사례는 건너뛴다
        wanted = [i for i in sorted(table) if f"-{wanted[0]}-" in i and not (out / f"{i}.json").exists()]
    only_steps = bool(wanted) and wanted[0] == "--steps"
    if only_steps:                                                # 이미 읽은 사례의 영역 내역만 다시 구한다 (같은 입력, 같은 규칙)
        wanted = [i for i in sorted(table, reverse=len(wanted) > 2 and wanted[2] == "reverse")
                  if f"-{wanted[1]}-" in i and (out / f"{i}.json").exists() and not (out / f"{i}.steps.json").exists()]
    for n, case_id in enumerate(wanted, 1):
        if only_steps and (out / f"{case_id}.steps.json").exists():      # 다른 프로세스가 그사이에 구했다 (뒤에서부터 도는 것을 함께 돌릴 때)
            continue
        position = "CENTER" if "-center-" in case_id else ("RIGHT" if "-right-" in case_id else "LEFT")
        src = table[case_id]; x0 = BANDS[position][0]
        notes = []
        known = load_steps(base, case_id) if retry else None      # 위치 찾기는 한 번 더 읽기와 상관없으므로, 이미 구한 영역 내역이 있으면 그대로 쓴다
        if known is not None:
            box, lock_frame, tracks, fitted, pose = known[2][0][1], known[0] * FPS, 0, "(앞의 실행)", known[1]
        else:
            box, lock_frame, tracks, fitted, pose = locate(read_bands(src, position, FPS, WATCH_ALL), finder, reader, notes)
        first = first_score(case_id)
        record = {"사례": case_id, "위치": position, "고정": "예" if box is not None else "아니오", "고정 시각": "", "첫 점수 뒤(초)": "", "고정 전 프레임": "",
                  "플레이 구간": "", "규칙 통과": "", "규칙 통과율": "", "최종 점수": "", "기대 점수": expected.get(case_id, -1), "최종 판정": "보류",
                  "움직이는 점수판": "", "자세": "" if pose is None else " ".join(str(v) for v in pose), "영역(마지막)": ""}
        if box is None:
            if only_steps:
                continue
            (out / f"{case_id}.json").write_text(json.dumps(record, ensure_ascii=False), encoding="utf-8")
            print(f"[{n}/{len(wanted)}] {case_id}: 고정 못 함 (줄기 {tracks}개)", flush=True)
            continue
        box_abs = lock_area(box, x0) if known is None else box; lock_at = lock_frame / FPS
        steps, moving = [(-1.0, tuple(box_abs))], False
        if known is not None:
            steps = known[2]; moving = json.loads((base / f"{case_id}.steps.json").read_text(encoding="utf-8"))["moving"]
        elif pose is None:
            steps, moving, seen = grow_steps(read_bands(src, position), box_abs, lock_at, finder, reader, x0)
            if moving:
                steps = follow_steps(read_bands(src, position, FOLLOW), steps, lock_at, finder, x0, seen)
        save_steps(out, case_id, lock_at, pose, steps, moving)
        if only_steps:
            print(f"[{n}/{len(wanted)}] {case_id}: 고정 {lock_at:.1f}초, 영역 바뀜 {len(steps) - 1}번", flush=True)
            continue
        log = out / f"{case_id}.ffmpeg.log"
        second = [] if retry else None
        values, conf, texts, contrast, locked, pts = read_all_frames(src, steps, lock_at, pose, reader, log, second)
        log.unlink()
        np.savez_compressed(out / f"{case_id}.npz", index=np.arange(1, len(values) + 1), pts=np.array(pts, np.float32), values=np.array(values),
                            conf=np.array(conf, np.float32), texts=np.array(texts), blank=np.array(contrast, np.float32) < BLANK_STD,
                            press=np.float32(src["press"] - src["start"]), locked=np.array(locked), lock_at=np.float32(lock_at),
                            area_first=np.array(steps[0][1], np.float32), area_last=np.array(steps[-1][1], np.float32),
                            second=np.array(second if retry else np.zeros(len(values), bool)))
        row = summarize(case_id, np.load(out / f"{case_id}.npz"), expected.get(case_id, -1))
        record.update({"고정 시각": f"{lock_at:.1f}", "첫 점수 뒤(초)": "" if first is None else f"{lock_at - first:.1f}",
                       "고정 전 프레임": int(len(locked) - sum(locked)), "플레이 구간": row["플레이 구간"], "규칙 통과": row["규칙 통과"],
                       "규칙 통과율": row["규칙 통과율"], "최종 점수": row["최종 점수"], "최종 판정": row["최종 판정"],
                       "움직이는 점수판": "예" if moving else "", "영역(마지막)": " ".join(str(int(round(v))) for v in steps[-1][1])})
        (out / f"{case_id}.json").write_text(json.dumps(record, ensure_ascii=False), encoding="utf-8")
        print(f"[{n}/{len(wanted)}] {case_id}: 고정 {lock_at:.1f}초(노트 수 추정 {fitted}), 영역 {[int(round(v)) for v in steps[0][1]]} → {[int(round(v)) for v in steps[-1][1]]} "
              f"(바뀜 {len(steps) - 1}번{', 움직이는 점수판을 따라감' if moving else ''}) | 전체 {row['전체 프레임']}장, 고정 전 {int(len(locked) - sum(locked))}장 | 플레이 구간 {row['플레이 구간']} "
              f"규칙 통과 {row['규칙 통과']} 걸림 {row['규칙에 걸림']} 미판독 {row['미판독']} 통과율 {row['규칙 통과율']} | "
              f"최종 {row['최종 점수']} / 기대 {row['기대 점수']} → {row['최종 판정']}", flush=True)


if __name__ == "__main__":
    main()
