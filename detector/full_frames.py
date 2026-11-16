"""사례의 원본 영상 구간을 처음부터 끝까지, 모든 프레임을 읽는다.

0.5초 간격 검사는 예비 검사일 뿐이다. 사용자 기준(사례별 전체 프레임 99% 일치)은 원본의 모든 프레임으로 잰다.
그림은 저장하지 않고(사례당 5천 장이 넘는다) 프레임별 판독만 남긴다.

  - 영상 구간(파일·시작·끝)은 Codex 가 정리해 둔 목록을 읽는다 (읽기 전용).
  - 프레임 번호와 시각은 Codex 의 전체 프레임 추출과 같은 방식(ffmpeg -ss 시작 -t 길이, 프레임 그대로)이라 서로 맞는다.
  - 점수 영역 위치는 아직 Codex 인식기가 찾은 것을 쓴다.

실행: .venv\\Scripts\\python.exe -m detector.full_frames <실행 이름> <사례> [사례 ...]   (사례 자리에 all 을 쓰면 전부)
출력: data\\full\\<실행 이름>\\<사례>.npz  (index, pts, values, conf, texts, blank)
"""
import json
import re
import shutil
import subprocess
import sys
import time
from pathlib import Path

import numpy as np
import torch

from .build_dataset import LADDER, MARGIN_X, MARGIN_Y, VALIDATION, here, letterbox, straighten
from .build_dataset import video
from .model import decode
from .selftrain import BLANK_STD, DEVICE, ROOT, load_model, to_tensor

PROBE = LADDER / "score-screen-placement-probe"


def sources() -> dict[str, dict]:
    """사례 → 원본 영상 파일과 구간(초). LEFT 는 사례 폴더의 capture.json, CENTER·RIGHT 는 추출 목록.
    영상의 경로는 설정에 적힌 옛 자리를 지금의 자리로 바꿔서 돌려준다(build_dataset.video)."""
    out = {}
    for name in ("center-nov10/full-extract.json", "right-nov11/priority-extract.json", "right-nov11/remaining-extract.json"):
        for s in json.loads((PROBE / name).read_text(encoding="utf-8")):
            out[s["id"]] = {"source": video(s["source"]), "start": s["start"], "end": s["end"], "press": s["registration"]["press"]}
    for c in json.loads((VALIDATION / "candidate-v4" / "all270.json").read_text(encoding="utf-8")):
        if c["position"] == "LEFT":
            cap = json.loads((here(c["frames"]) / "capture.json").read_text(encoding="utf-8"))
            press = cap["start"] + c["registration"]["pressedAt"]
            out[c["id"]] = {"source": video(cap["source"]), "start": cap["start"], "end": press + 5, "press": press}
    return out


def area_of(case_id: str):
    return json.loads((VALIDATION / "candidate-v4" / "all270-results" / f"{case_id}.json").read_text(encoding="utf-8")).get("area")


def stream(case_id: str, src: dict, log: Path):
    """사례 구간의 모든 프레임에서 점수 영역(BGR, 바로 세운 것)을 차례로 내놓는다. ffmpeg 기록은 log 에 남긴다."""
    gear = case_id.rsplit("-", 2)[0]
    left, top, right, bottom = area_of(case_id)
    x0, y0 = max(0, left - MARGIN_X), max(0, top - MARGIN_Y)
    x1, y1 = min(1920, right + MARGIN_X), min(1080, bottom + MARGIN_Y)
    w, h = x1 - x0, y1 - y0
    command = [shutil.which("ffmpeg") or "ffmpeg", "-hide_banner", "-ss", str(src["start"]), "-i", src["source"],
               "-t", str(src["end"] - src["start"]), "-an", "-vf", f"format=rgb24,crop={w}:{h}:{x0}:{y0}:exact=1,showinfo",
               "-fps_mode", "passthrough", "-enc_time_base", "demux", "-f", "rawvideo", "-pix_fmt", "rgb24", "-"]
    with open(log, "w", encoding="utf-8") as err, subprocess.Popen(command, stdout=subprocess.PIPE, stderr=err) as proc:
        size = w * h * 3
        while True:
            raw = proc.stdout.read(size)
            if len(raw) < size:
                break
            frame = np.frombuffer(raw, np.uint8).reshape(h, w, 3)[:, :, ::-1]          # RGB → BGR
            yield straighten(np.ascontiguousarray(frame), gear)


def read_case(case_id: str, src: dict, model, out: Path) -> int:
    if area_of(case_id) is None:
        return 0
    log = out / f"{case_id}.ffmpeg.log"
    values, conf, texts, contrast = [], [], [], []
    batch = []

    def flush():
        if batch:
            v, c, t = decode(model(to_tensor(np.stack(batch)).to(DEVICE)).cpu())
            values.extend(v); conf.extend(c); texts.extend(t); batch.clear()

    for part in stream(case_id, src, log):
        image, std = letterbox(part)
        batch.append(image); contrast.append(std)
        if len(batch) == 512:
            flush()
    flush()
    pts = [float(m[1]) for m in re.finditer(r" n:\s*\d+.*?pts_time:([\d.\-]+)", log.read_text(encoding="utf-8", errors="replace"))]
    log.unlink()
    count = len(values)
    if len(pts) < count:
        raise RuntimeError(f"{case_id}: 프레임 {count}장인데 시각은 {len(pts)}개")
    np.savez_compressed(out / f"{case_id}.npz", index=np.arange(1, count + 1), pts=np.array(pts[:count], np.float32),
                        values=np.array(values), conf=np.array(conf, np.float32), texts=np.array(texts),
                        blank=np.array(contrast, np.float32) < BLANK_STD, press=np.float32(src["press"] - src["start"]))
    return count


@torch.no_grad()
def main():
    run, wanted = sys.argv[1], sys.argv[2:]
    model, _ = load_model(run)
    model.eval()
    out = ROOT / "data" / "full" / run
    out.mkdir(parents=True, exist_ok=True)
    table = sources()
    if wanted == ["all"]:
        ids = sorted(table)
    elif wanted == ["left-center"]:                 # RIGHT 는 LEFT·CENTER 가 기준을 통과한 뒤에 본다 (사용자 결정)
        ids = sorted(i for i in table if "-right-" not in i)
    else:
        ids = wanted
    started = time.time(); total = 0
    for n, case_id in enumerate(ids, 1):
        if (out / f"{case_id}.npz").exists():
            continue
        t = time.time()
        count = read_case(case_id, table[case_id], model, out)
        total += count
        print(f"[{n}/{len(ids)}] {case_id}: {count}장, {time.time() - t:.0f}초", flush=True)
    print(f"완료: {total}장, {time.time() - started:.0f}초", flush=True)


if __name__ == "__main__":
    main()
