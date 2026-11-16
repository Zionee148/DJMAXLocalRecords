"""찾은 자리로 자른 씨앗 라벨(truth\\seed-labels-located\\claude-read-located-*.csv)을 검사하고 학습 자료로 만든다 (LEFT·CENTER 만).

build_seed_full 의 씨앗은 Codex 영역으로 자른 조각이다. 여기의 씨앗은 같은 프레임을 **위치 탐색이 그 시각에 쓰던 영역**으로 자른다.
판독기가 Codex 영역으로는 읽는데 찾은 자리로는 못 읽은 정상 프레임(자른 범위가 달라 흔들린 것)을 가르치려는 것이다.

라벨은 Claude 가 그림을 직접 읽어 확인한 값이다. 그래도 그대로 믿지 않고, 아래를 모두 만족하는 것만 쓴다(build_seed_full 과 같은 검사).
  - 그 사례의 점수 산식으로 나올 수 있는 값이다.
  - 앞뒤로 가장 가까운 "규칙을 통과해 읽힌 값"(Codex 영역으로 읽은 실행) 사이에 있고, 증가폭이 판정의 합으로 설명된다.

실행: .venv\\Scripts\\python.exe -m detector.build_seed_located <위치 탐색 실행(예: vid-d2-v15)> <Codex 영역으로 읽은 실행(앞뒤 값을 얻을 것)>
출력: data\\seed_located.npz (images, labels, cases, frames)
"""
import csv
import shutil
import subprocess
import sys

import numpy as np

from .build_dataset import MARGIN_X, MARGIN_Y, letterbox
from .full_frames import sources
from .locate_video import load_steps
from .score_frames import READ_CONF
from .score_rules import lattice_m, step_ok, verify_sequence
from .selftrain import ROOT, notes_of


def frames_of(src: dict, numbers: list[int]) -> np.ndarray:
    """0부터 세는 프레임 번호들의 전체 화면(BGR). 번호는 full_frames·locate_video 와 같다."""
    pick = "+".join(f"eq(n\\,{k})" for k in numbers)
    command = [shutil.which("ffmpeg") or "ffmpeg", "-hide_banner", "-loglevel", "error", "-ss", str(src["start"]), "-i", src["source"],
               "-t", str(src["end"] - src["start"]), "-an", "-vf", f"select='{pick}'", "-fps_mode", "passthrough", "-frames:v", str(len(numbers)),
               "-f", "rawvideo", "-pix_fmt", "bgr24", "-"]
    raw = subprocess.run(command, capture_output=True).stdout
    return np.frombuffer(raw, np.uint8, (len(raw) // (1920 * 1080 * 3)) * 1920 * 1080 * 3).reshape(-1, 1080, 1920, 3)


def main():
    located_run, reads_run = sys.argv[1], sys.argv[2]
    wanted: dict[str, dict[int, int]] = {}
    for path in sorted((ROOT / "truth" / "seed-labels-located").glob("claude-read-located-*.csv")):
        with open(path, encoding="utf-8") as f:
            for r in csv.DictReader(f):
                if "-right-" not in r["case"]:
                    wanted.setdefault(r["case"], {})[int(r["frame"]) - 1] = int(r["value"])
    if not wanted:
        raise SystemExit(f"씨앗 라벨이 없다: {ROOT / 'truth' / 'seed-labels-located'}\\claude-read-located-*.csv\n직접 읽은 라벨은 저장소에 들어 있지 않다.")
    table = sources()
    images, labels, cases, frames = [], [], [], []
    passed = dropped = 0
    for case_id, picks in wanted.items():
        z = np.load(ROOT / "data" / "full" / reads_run / f"{case_id}.npz")
        notes = notes_of(case_id)
        raw = np.where((z["conf"] >= READ_CONF) & ~z["blank"] & (z["values"] > 0), z["values"], -1)
        keep = np.array(verify_sequence([int(v) for v in raw], notes), bool)
        known = np.flatnonzero(keep)
        found = load_steps(ROOT / "data" / "full" / located_run, case_id)
        if found is None or found[1] is not None:
            print(f"{case_id}: 영역 내역이 없거나 기울여 읽는 사례라 건너뜀"); dropped += len(picks); continue
        lock_at, _, steps = found
        good = []
        for k, value in sorted(picks.items()):
            m = lattice_m(value, notes)
            before = known[known < k]; after = known[known > k]
            lo = int(raw[before[-1]]) if len(before) else None
            hi = int(raw[after[0]]) if len(after) else None
            ok = m is not None and k / 60.0 >= lock_at
            if ok and lo is not None:
                ok = value >= lo and (value == lo or step_ok(m - lattice_m(lo, notes)))
            if ok and hi is not None:
                ok = value <= hi and (value == hi or step_ok(lattice_m(hi, notes) - m))
            if ok:
                good.append(k)
            else:
                dropped += 1; print(f"   탈락: {case_id} #{k + 1} {value} (앞 {lo}, 뒤 {hi})")
        if not good:
            continue
        pictures = frames_of(table[case_id], good)
        assert len(pictures) == len(good), (case_id, len(pictures), len(good))
        for k, frame in zip(good, pictures):
            area = next(a for t, a in reversed(steps) if t <= k / 60.0)
            xa, ya = max(0, int(round(area[0])) - MARGIN_X), max(0, int(round(area[1])) - MARGIN_Y)
            xb, yb = min(1920, int(round(area[2])) + MARGIN_X), min(1080, int(round(area[3])) + MARGIN_Y)
            images.append(letterbox(np.ascontiguousarray(frame[ya:yb, xa:xb]))[0]); labels.append(picks[k]); cases.append(case_id); frames.append(k + 1)
            passed += 1
    np.savez_compressed(ROOT / "data" / "seed_located.npz", images=np.stack(images), labels=np.array(labels), cases=np.array(cases), frames=np.array(frames))
    print(f"찾은 자리 씨앗 라벨: 통과 {passed}장, 탈락 {dropped}장 (사례 {len(set(cases))}개) → data\\seed_located.npz")


if __name__ == "__main__":
    main()
