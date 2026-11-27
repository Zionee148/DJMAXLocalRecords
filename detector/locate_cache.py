"""위치 탐색 개발용 자료: 사례마다 플레이 초반의 탐색 범위 화면을 0.1초 간격으로 모아 둔다 (흑백).

앱은 0.1초쯤마다 화면을 잡아 모듈에 넘긴다. 위치 탐색은 그 화면들만 보고 점수 줄을 찾아야 하므로,
같은 간격으로 원본 영상에서 뽑아 둔다. 그림이 크므로 흑백만 저장하고, 색이 필요한 판독용 조각은 그때그때 영상에서 자른다.

  - 탐색 범위: 화면 아래쪽 y >= TOP, 가로는 위치별로 넓게 (LEFT 0~960, CENTER 480~1440, RIGHT 960~1920).
    기어별 좌표는 쓰지 않는다. 이 범위는 개발용 출발값이다.
  - 구간: "점수가 처음 읽힌 시각"의 3초 전부터 20초 뒤까지. 그 시각은 v6 전체 프레임 판독에서 가져온다
    (개발 편의를 위한 기준일 뿐이다. 실제 플레이 시작 라벨은 없다).
  - RIGHT 는 만들지 않는다 (RIGHT 는 학습에 쓰지 않는 평가용이라, LEFT·CENTER 가 기준을 통과한 뒤에 본다).

실행: .venv\\Scripts\\python.exe -m detector.locate_cache [사례 ...]
출력: data\\band\\<사례>.npy (프레임 x 높이 x 너비, uint8), data\\band\\<사례>.json
"""
import json
import shutil
import subprocess
import sys
import time

import numpy as np

from .full_frames import sources
from .full_summary import play_window
from .score_frames import READ_CONF
from .score_rules import verify_sequence
from .selftrain import ROOT, notes_of

OUT = ROOT / "data" / "band"
BAND = {"left": (0, 960), "center": (480, 1440), "right": (960, 1920)}
TOP = 700
FPS = 10
BEFORE, AFTER = 3.0, 20.0
REFERENCE_RUN = "v6"


def first_score_time(case_id: str) -> float | None:
    """v6 판독에서 규칙을 통과한 첫 점수가 나온 시각(구간 시작 기준, 초)."""
    path = ROOT / "data" / "full" / REFERENCE_RUN / f"{case_id}.npz"
    if not path.exists():
        return None
    z = np.load(path)
    raw = np.where((z["conf"] >= READ_CONF) & ~z["blank"] & (z["values"] > 0), z["values"], -1)
    keep = np.array(verify_sequence([int(v) for v in raw], notes_of(case_id)), bool)
    if keep.sum() < 8:
        return None
    start, _ = play_window(z, raw, keep)
    return float(z["pts"][start])


def read_band(src: dict, position: str, at: float, seconds: float, fps: int = FPS, color: bool = False) -> np.ndarray:
    """구간 시작에서 at 초 지난 곳부터 seconds 초 동안의 탐색 범위 화면."""
    x0, x1 = BAND[position]
    w, h = x1 - x0, 1080 - TOP
    command = [shutil.which("ffmpeg") or "ffmpeg", "-hide_banner", "-loglevel", "error", "-ss", str(src["start"] + at), "-i", src["source"],
               "-t", str(seconds), "-an", "-vf", f"crop={w}:{h}:{x0}:{TOP},fps={fps}", "-f", "rawvideo", "-pix_fmt", "bgr24" if color else "gray", "-"]
    raw = subprocess.run(command, capture_output=True).stdout
    ch = 3 if color else 1
    n = len(raw) // (w * h * ch)
    return np.frombuffer(raw, np.uint8, n * w * h * ch).reshape((n, h, w, 3) if color else (n, h, w))


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    table = sources()
    wanted = sys.argv[1:] or sorted(i for i in table if "-right-" not in i)
    started = time.time()
    for n, case_id in enumerate(wanted, 1):
        if (OUT / f"{case_id}.json").exists():
            continue
        position = case_id.rsplit("-", 2)[1]
        first = first_score_time(case_id)
        if first is None:                      # 판독 자료가 없는 사례(3-05 CENTER): 구간 처음부터 모은다
            first, at = None, 0.0
        else:
            at = max(0.0, first - BEFORE)
        frames = read_band(table[case_id], position, at, BEFORE + AFTER)
        np.save(OUT / f"{case_id}.npy", frames)
        (OUT / f"{case_id}.json").write_text(json.dumps({"case": case_id, "position": position, "at": at, "first_score": first, "fps": FPS,
                                                         "x0": BAND[position][0], "top": TOP, "frames": len(frames)}), encoding="utf-8")
        print(f"[{n}/{len(wanted)}] {case_id}: {len(frames)}장, 첫 점수 {first}, 누적 {time.time() - started:.0f}초", flush=True)


if __name__ == "__main__":
    main()
