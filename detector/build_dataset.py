"""학습·평가용 자료 만들기.

0.5초 간격의 기준 프레임(testdata\\ladder-records 의 PNG. 읽기 전용)에서 사례마다 점수 영역을 잘라 한 파일로 묶는다.
  - 플레이 프레임(f*.png)과 등록 키 이후 프레임(r*.png, 0.2초 간격)을 함께 담는다.
  - 점수 영역 위치는 검사 설정의 사례별 결과(all270-results\\<사례>.json 의 area)에 적힌 것을 쓴다. 이전 인식기가 찾아 둔 영역이며,
    다른 도구들이 비교 기준으로 사용한다(기준 영역). 영역이 없는 사례는 담지 않는다.
출력: data\\frames05b\\<사례>.npz, data\\frames05b\\cases.json
실행: .venv\\Scripts\\python.exe -m detector.build_dataset
"""
import ctypes
import json
import os
import sys
from multiprocessing import Pool
from pathlib import Path, PureWindowsPath

import cv2
import numpy as np

from . import inputs

ROOT = Path(__file__).resolve().parents[1]
# 원본 영상 폴더 같은 로컬 경로를 적어 두는 설정 파일. git 에 넣지 않는다(.gitignore)
LOCAL = ROOT / "local-paths.json"


def local_path(key: str, env: str):
    """로컬 폴더의 경로를 돌려준다: 환경 변수, 없으면 local-paths.json 의 값. 어디에도 없으면 None.

    같은 것이 tools\\make_bundle.py 에도 있다(이쪽은 tools 를 불러오지 않는다)."""
    value = os.environ.get(env)
    if not value and LOCAL.is_file():
        value = json.loads(LOCAL.read_text(encoding="utf-8-sig")).get(key)
    return Path(value) if value else None


def local_names(key: str) -> dict[str, str]:
    """local-paths.json 에 적어 둔 이름의 대응표(옛 이름 → 지금 이름). 적어 두지 않았으면 빈 표."""
    if LOCAL.is_file():
        value = json.loads(LOCAL.read_text(encoding="utf-8-sig")).get(key)
        if isinstance(value, dict):
            return {str(old): str(new) for old, new in value.items()}
    return {}


# 검사 설정과 0.5초 PNG 는 testdata\ladder-records 에 둔다. 옛 작업 폴더의 ladder-records 를 구조 그대로 복사해 온 것이다.
# 복사해 온 JSON 안에는 옛 절대 경로가 그대로 적혀 있다. 원본을 고치지 않고, 읽을 때 here() 로 사본의 경로로 바꾼다.
# (옛 경로를 여기에 적어 두지 않는다. 경로 안의 ladder-records 폴더를 찾아 그 아래만 쓴다)
LADDER_NAME = "ladder-records"
LADDER = Path(__file__).resolve().parent.parent / "testdata" / LADDER_NAME
# 원본 녹화 영상(기어 표본)은 저장소에 없다. 있는 폴더를 환경 변수 DJMAX_SOURCE_VIDEOS 나 local-paths.json 의 source_videos 로 알려 준다.
# 검사 설정 JSON 에는 녹화했을 때의 옛 경로가 적혀 있다. 그것을 고치지 않고, 읽을 때 video() 로 알려 준 폴더의 경로로 바꾼다.
# (옛 경로를 여기에 적어 두지 않는다. 경로 안의 "기어표본" 폴더를 찾아 그 아래만 쓴다)
VIDEOS_NAME = "기어표본"
VIDEOS = local_path("source_videos", "DJMAX_SOURCE_VIDEOS")      # 알려 주지 않았으면 None
# 원본 영상의 파일 이름을 바꿨으면 local-paths.json 의 source_video_names 에 {"옛 이름": "지금 이름"} 으로 적는다.
# 검사 설정 JSON 에 적힌 이름은 고치지 않고, 읽을 때 video() 가 지금 이름으로 바꾼다. 적어 두지 않은 이름은 그대로 쓴다.
VIDEO_NAMES = local_names("source_video_names")
VALIDATION = LADDER / "score-screen-placement-probe" / "validation-20261111"
OUT = ROOT / "data" / "frames05b"            # frames05 는 1차 학습(v2)용 48x192 묶음
HEIGHT, WIDTH = 64, 256
MARGIN_X, MARGIN_Y = 14, 8


def low_priority():
    try:
        ctypes.windll.kernel32.SetPriorityClass(ctypes.windll.kernel32.GetCurrentProcess(), 0x4000)
    except Exception:
        pass


def read_image(path: Path):
    data = np.fromfile(str(path), dtype=np.uint8)
    return cv2.imdecode(data, cv2.IMREAD_COLOR)   # BGR


# 숫자가 기울어진 기어: (오른쪽 위로 기운 각도, 바로 세운 뒤 남길 세로 비율, 남길 띠의 중심 높이 비율).
# 손으로 잰 값이며 기준 영역으로 자르는 도구들이 쓴다. 위치 찾기(locate_run, 모듈의 Locator)는 이 값을 쓰지 않고 여러 자세로 읽어 보아 기울기를 고른다.
ORIENTED = {"3-08": (29.0, 0.40, 0.56)}


def cut(image, area, gear: str = ""):
    """원본 화면에서 점수 영역을 원본 크기 그대로 잘라 낸다 (기울어진 기어는 바로 세워서)."""
    offset = 740 if image.shape[0] == 340 else 0
    left, top, right, bottom = area
    x0 = max(0, left - MARGIN_X); x1 = min(image.shape[1], right + MARGIN_X)
    y0 = max(0, top - MARGIN_Y - offset); y1 = min(image.shape[0], bottom + MARGIN_Y - offset)
    return straighten(image[y0:y1, x0:x1], gear)


def straighten(part, gear: str = ""):
    """기울어진 기어의 점수 영역을 바로 세운다. 그 밖의 기어는 그대로 돌려준다."""
    if gear in ORIENTED and part.size:
        angle, band, center = ORIENTED[gear]
        h, w = part.shape[:2]
        turn = cv2.getRotationMatrix2D((w / 2, h * center), -angle, 1.0)
        part = cv2.warpAffine(part, turn, (w, h), flags=cv2.INTER_CUBIC, borderMode=cv2.BORDER_REPLICATE)
        keep = int(h * band); y = max(0, int(h * center - keep / 2))
        part = part[y:y + keep]
    return part


def crop(image, area, gear: str = ""):
    return letterbox(cut(image, area, gear))


def here(path) -> Path:
    """검사 설정 JSON 에 적힌 옛 작업 폴더의 경로를 이 저장소에 있는 사본(testdata\\ladder-records)의 경로로 바꾼다.

    절대 경로에서 처음 나오는 ladder-records 폴더의 아래만 쓴다. 그 폴더가 없는 경로(원본 영상 등)와 상대 경로는 그대로 둔다."""
    text = str(path).replace("/", "\\")
    parts = text.split("\\") if PureWindowsPath(text).is_absolute() else []
    for i, part in enumerate(parts[1:], 1):
        if part.lower() == LADDER_NAME:
            return LADDER.joinpath(*[p for p in parts[i + 1:] if p])
    return Path(path)


def video(path) -> str:
    """검사 설정에 적힌 원본 영상의 옛 경로를 지금 영상이 있는 폴더(VIDEOS)의 경로로 바꾼다.

    경로에서 마지막으로 나오는 "기어표본" 폴더의 아래만 쓴다. 그 폴더가 없는 경로는 그대로 둔다.
    파일 이름이 이름의 대응표(VIDEO_NAMES)에 있으면 지금 이름으로 바꾼다.
    영상이 있는 폴더를 알려 주지 않았으면 무엇이 필요한지 알리고 끝낸다."""
    text = str(path).replace("/", "\\")
    parts = text.split("\\")
    for i in range(len(parts) - 2, 0, -1):
        if parts[i] == VIDEOS_NAME:
            if VIDEOS is None:
                raise SystemExit("원본 녹화 영상(기어 표본)이 있는 폴더를 알려 주지 않았다.\n"
                                 "환경 변수 DJMAX_SOURCE_VIDEOS 나 저장소 맨 위 local-paths.json 의 source_videos 로 알려 준다.\n"
                                 "원본 녹화는 저장소에 들어 있지 않다. 없으면 영상을 읽는 도구는 돌릴 수 없다.")
            rest = [p for p in parts[i + 1:] if p]
            if rest:
                rest[-1] = VIDEO_NAMES.get(rest[-1], rest[-1])
            return str(VIDEOS.joinpath(*rest))
    return str(path)


def letterbox(part):
    """잘라 낸 그림(BGR)을 모델 입력 크기의 RGB 그림으로 맞추고, 밝기 변화량도 함께 돌려준다."""
    if part.size == 0:
        return np.zeros((HEIGHT, WIDTH, 3), np.uint8), 0.0
    contrast = float(cv2.cvtColor(part, cv2.COLOR_BGR2GRAY).std())   # 거의 0 이면 아무것도 없는 화면
    scale = min(HEIGHT / part.shape[0], WIDTH / part.shape[1])
    w = max(1, round(part.shape[1] * scale)); h = max(1, round(part.shape[0] * scale))
    small = cv2.resize(part, (w, h), interpolation=cv2.INTER_CUBIC if scale > 1 else cv2.INTER_AREA)
    canvas = np.zeros((HEIGHT, WIDTH, 3), np.uint8)
    y = (HEIGHT - h) // 2; x = (WIDTH - w) // 2
    canvas[y:y + h, x:x + w] = small
    return canvas[:, :, ::-1].copy(), contrast    # RGB


def build(case):
    low_priority()
    out = OUT / f"{case['id']}.npz"
    if out.exists():
        return case["id"], -1
    result = json.loads((VALIDATION / "candidate-v4" / "all270-results" / f"{case['id']}.json").read_text(encoding="utf-8"))
    area = result.get("area")
    if area is None:
        return case["id"], 0
    images, seconds, kinds, names, contrast = [], [], [], [], []
    gear = case["id"].rsplit("-", 2)[0]
    play = inputs(here(case["frames"]), "f*.png", f"{case['id']} 의 플레이 프레임 그림")
    for k, path in enumerate(play):
        image, c = crop(read_image(path), area, gear)
        images.append(image); contrast.append(c); seconds.append(k * case["interval"]); kinds.append(0); names.append(path.name)
    reg = case["registration"]
    for k, path in enumerate(sorted(here(reg["frames"]).glob("r*.png"))):
        image, c = crop(read_image(path), area, gear)
        images.append(image); contrast.append(c); seconds.append(reg["pressedAt"] + k * reg["interval"]); kinds.append(1); names.append(path.name)
    OUT.mkdir(parents=True, exist_ok=True)
    np.savez_compressed(out, images=np.stack(images), seconds=np.array(seconds, np.float32),
                        kinds=np.array(kinds, np.uint8), names=np.array(names), contrast=np.array(contrast, np.float32))
    return case["id"], len(images)


def main():
    cases = json.loads((VALIDATION / "candidate-v4" / "all270.json").read_text(encoding="utf-8"))
    names = {g["id"]: g["name"] for g in json.loads((LADDER / "score-all-gears-20261107" / "manifest.json").read_text(encoding="utf-8"))}
    OUT.mkdir(parents=True, exist_ok=True)
    meta = []
    for c in cases:
        gear = c["id"].rsplit("-", 2)[0]
        meta.append({"id": c["id"], "gear": gear, "name": names.get(gear, ""), "position": c["position"],
                     "pressedAt": c["registration"]["pressedAt"], "expected": c["registration"]["expected"]})
    (OUT / "cases.json").write_text(json.dumps(meta, ensure_ascii=False, indent=1), encoding="utf-8")
    workers = int(sys.argv[1]) if len(sys.argv) > 1 else 4
    total = 0
    with Pool(workers) as pool:
        for n, (case_id, count) in enumerate(pool.imap_unordered(build, cases), 1):
            total += max(0, count)
            if n % 30 == 0 or count == 0:
                print(f"[{n}/{len(cases)}] {case_id}: {count}", flush=True)
    print(f"완료: 사례 {len(cases)}개, 새로 담은 프레임 {total}장 → {OUT}", flush=True)


if __name__ == "__main__":
    main()
