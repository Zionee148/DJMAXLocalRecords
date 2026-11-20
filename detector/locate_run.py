"""위치 탐색을 처음부터 끝까지 돌려 본다 (개발용 시험. LEFT·CENTER 만).

앱이 하듯이 사례 구간의 처음부터 0.1초 간격 화면을 차례로 넣는다. 위치도, 기어도, 점수도 알려 주지 않는다.

  1) 숫자 줄 탐지 모델이 화면 아래쪽 띠에서 후보가 되는 검출 영역을 낸다 (0.3초마다).
  2) 같은 자리에서 계속 잡히는 검출 영역을 하나의 추적 후보로 묶고, 그 자리를 판독기로 읽는다 (0.1초마다).
  3) 읽힌 값들이 어떤 노트 수의 점수 규칙에 맞게 오르면 그 추적 후보를 점수 줄로 고정한다.
     콤보·배속·장식 숫자는 점수 규칙대로 오르지 않으므로 여기서 떨어진다.

채점 (찾은 자리가 맞는지는 기준 영역과의 겹침이 아니라 "그 자리를 읽은 결과"로 본다. 기준 영역은 검사 설정에 적힌 점수 영역으로, 비교에만 쓴다)
  - 고정한 자리로 그 사례의 0.5초 간격 화면 전체를 다시 읽어, 등록 시점의 최종 점수가 기대 점수와 같은지 본다.
  - 탐지 모델은 그 기어를 빼고 학습한 것을 쓴다 (처음 보는 기어 디자인).
  - 고정 시각은 "v6 판독에서 점수가 처음 읽힌 시각"으로부터의 시간으로 적는다. 실제 플레이 시작 라벨이 아직 없어서
    쓰는 참고값이며 20초 기준의 판정이 아니다.

실행: .venv\\Scripts\\python.exe -m detector.locate_run <탐지 실행 이름> <판독 실행 이름> [사례 ...]
출력: data\\locate_runs\\<탐지 이름>\\run-<판독 이름>.csv
      (열 이름 "줄기 수"는 추적 후보의 수, "네모"는 고정한 영역, "Codex 영역과 겹침"은 기준 영역과 겹친 정도다)
"""
import csv
import json
import os
import shutil
import subprocess
import sys
import time
from pathlib import Path
from types import SimpleNamespace

import cv2
import numpy as np
import torch

from .build_dataset import MARGIN_X, MARGIN_Y, VALIDATION, here, letterbox, read_image
from .full_frames import area_of, sources
from .locate import overlap
from .locate_cache import first_score_time
from .locate_eval import iou
from .locate_model import RowFinder, find_rows
from .locate_train import DATA, RUNS, fold_of
from .model import decode
from .score_rules import fit_notes, verify_sequence
from .selftrain import BLANK_STD, DATA_NAME, DEVICE, ROOT, decide_final, load_model, to_tensor, verified_reads

TOP = 740
BAND = {"LEFT": (0, 960), "CENTER": (480, 1440)}
BAND_RIGHT = (960, 1920)  # RIGHT 는 locate_video 만 쓴다. BAND 에 넣으면 옛 도구들이 RIGHT 사례까지 돌게 된다
FPS = 10
DETECT_EVERY = 3          # 탐지 모델은 0.3초마다
READ_CONF = 0.5
LOCK_VALUES = 12          # 규칙대로 오른 서로 다른 값이 이만큼 모이면 고정
WATCH = 25.0              # 첫 점수 뒤 이만큼까지 본다
EXTRA_X = int(os.environ.get("LOCATE_EXTRA_X", "0"))   # 시험용: 자를 영역 양옆에 더 주는 여유(화소)
MIN_AGREE = 0.6           # 읽힌 값 가운데 규칙을 통과한 비율이 이보다 낮으면 최종 점수를 내지 않는다
GROW = os.environ.get("LOCATE_GROW", "1") == "1"       # 고정 뒤에도 탐지 모델로 영역을 넓힐지
LOCK_PAD = float(os.environ.get("LOCATE_LOCK_PAD", "0"))   # 고정할 때 양옆에 미리 두는 여유의 배율 (0 = 두지 않음)


MOVING = 0.45             # 잡힌 검출 영역들의 가운데 높이가 이만큼(검출 영역 높이의 배율) 넘게 오르내리면 "점수판이 위아래로 움직인다"고 본다
MOVING_SEEN = 30          # 그 판단에 필요한 검출 영역의 수. 탐지 모델이 숫자 줄을 가끔만 잡는 기어는 몇 안 되는 검출 영역이 흔들려 잘못 걸린다:
                          # EXTENSION 3 CENTER 는 160장 가운데 16장만 잡혔고 그 검출 영역들이 10화소쯤 흔들려(배율 0.37) 움직이는 것으로 걸렸고,
                          # 따라가다가 규칙 통과율이 100% → 1.6% 로 무너졌다. welcome to the space 는 158장이 잡히고 배율 0.58, 가만히 있는 기어는 0.02 다.


def vertical_range(tops, bottoms) -> tuple[float, float, bool]:
    """고정 뒤 영역의 세로 범위 (위, 아래)와, 점수판이 위아래로 움직이는 기어인지.

    세로는 잡힌 검출 영역들의 가운데 값이다(숫자 줄의 높이는 변하지 않는다).
    기어 전체가 위아래로 움직여 점수판이 따라 움직이는 기어가 있다(welcome to the space). 가운데 값으로 두면 점수판이 올라가거나
    내려앉은 때 숫자가 잘린다. 이전 인식기의 위치 탐색은 이 움직임을 알아채고 세로 범위를 넓게 잡았다.
    같은 식으로 오르내린 범위(5%~95%)를 모두 담아 봤더니 오히려 나빠졌다(3-05 LEFT 의 규칙 통과율 96.3% → 93.0%):
    영역이 높아지면 숫자가 작게 들어가고, 판독기는 위아래로 넓게 자른 조각을 배운 적이 없다.
    그래서 여기서는 움직이는지만 알려 주고, 움직이는 점수판은 따라가며 자른다 (locate_video.follow_steps).
    """
    tops, bottoms = np.asarray(tops, float), np.asarray(bottoms, float)
    moving = False
    if len(tops) >= MOVING_SEEN:
        centers = (tops + bottoms) / 2
        moving = bool(np.percentile(centers, 95) - np.percentile(centers, 5) >= MOVING * float(np.median(bottoms - tops)))
    return float(np.median(tops)), float(np.median(bottoms)), moving


def read_band(src: dict, position: str, seconds: float) -> np.ndarray:
    x0, x1 = BAND[position]
    w, h = x1 - x0, 1080 - TOP
    command = [shutil.which("ffmpeg") or "ffmpeg", "-hide_banner", "-loglevel", "error", "-ss", str(src["start"]), "-i", src["source"],
               "-t", str(seconds), "-an", "-vf", f"crop={w}:{h}:{x0}:{TOP},fps={FPS}", "-f", "rawvideo", "-pix_fmt", "bgr24", "-"]
    raw = subprocess.run(command, capture_output=True).stdout
    n = len(raw) // (w * h * 3)
    return np.frombuffer(raw, np.uint8, n * w * h * 3).reshape(n, h, w, 3)


# 기운 숫자 줄(CYTUS 등)을 위한 "자세" 가설: (돌릴 각도, 띠의 중심, 띠의 높이). 기어 이름으로 고르지 않고,
# 바로 읽히지 않는 자리에서만 여러 자세로 읽어 보아 판독기가 가장 또렷이 읽는 자세를 고른다.
# 판독기는 각도가 10도쯤 어긋나도 읽고, 띠의 중심은 높이의 6%쯤만 어긋나도 못 읽는다 (CYTUS LEFT 에서 잰 것).
POSES = [(angle, center, 0.36) for angle in (-45, -30, -15, 15, 30, 45) for center in (0.32, 0.41, 0.5, 0.59, 0.68)]


def part_of(image, box):
    """주어진 영역 둘레에 판독기가 학습 때 본 것과 같은 여백을 두고 자른 조각 (BGR)."""
    x0, y0, x1, y1 = box
    xa, ya = max(0, int(round(x0)) - MARGIN_X - EXTRA_X), max(0, int(round(y0)) - MARGIN_Y)
    xb, yb = min(image.shape[1], int(round(x1)) + MARGIN_X + EXTRA_X), min(image.shape[0], int(round(y1)) + MARGIN_Y)
    return image[ya:yb, xa:xb]


def posed(part, pose):
    """자세 가설대로 돌리고 띠만 남긴다. pose 가 None 이면 그대로."""
    if pose is None or part.size == 0:
        return part
    angle, center, band = pose
    h, w = part.shape[:2]
    turn = cv2.getRotationMatrix2D((w / 2, h * center), -angle, 1.0)
    out = cv2.warpAffine(part, turn, (w, h), flags=cv2.INTER_CUBIC, borderMode=cv2.BORDER_REPLICATE)
    keep = max(4, int(h * band)); y = max(0, int(h * center - keep / 2))
    return out[y:y + keep]


def crop_for_reader(image, box, pose=None):
    """BGR 그림 → 모델 입력, 밝기 변화량."""
    return letterbox(posed(part_of(image, box), pose))


def lock_area(box, band_x0: int):
    """고정한 검출 영역(띠 좌표) → 화면 전체 좌표의 고정 영역. 양옆에 숫자 한두 자리만큼의 여유를 미리 둔다.

    영역 넓히기는 숫자 줄이 넓게 잡힌 것을 두 번 본 뒤에야 넓힌다. 그 사이에 자릿수가 늘면 끝자리가 잘린 채 읽힌다
    (welcome to the space LEFT 의 전체 프레임에서 110장). 판독기 v8 은 양옆 40화소까지 넓게 잘라도 결과가 같았으므로
    처음부터 검출 영역 높이의 0.8배(20~40화소)를 양옆에 더 둔다.
    """
    pad = float(np.clip(0.8 * (box[3] - box[1]), 20, 40)) * LOCK_PAD
    return (max(0.0, box[0] + band_x0 - pad), box[1] + TOP, min(1920.0, box[2] + band_x0 + pad), box[3] + TOP)


class Track:
    """한 자리에서 계속 잡히는 검출 영역을 묶은 추적 후보. 기운 줄 가설은 같은 자리를 다른 자세로 읽는 "하위 추적 후보"(child)로 따로 둔다.

    처음에는 안 읽히는 자리의 자세를 통째로 바꿨는데, 점수가 아직 뜨지 않은 곡 초반에 멀쩡한 자리를 기운 줄로
    바꿔 버려 KARTRIDER·PIXEL ART·TECHNIKA 3 를 놓쳤다. 그래서 원래 읽기는 건드리지 않는다.
    """

    def __init__(self, box, at, parent=None, pose=None):
        self.root = parent or self
        self.pose = pose
        self.times = []; self.values = []
        if parent is None:
            self.boxes = [box]; self.last_seen = at; self.parts = []; self.pose_checked = -100.0; self.child = None

    @property
    def box(self):
        return tuple(float(v) for v in np.median(np.array(self.root.boxes[-15:]), axis=0))


@torch.no_grad()
def locate(frames: np.ndarray, finder, reader, debug: list | None = None):
    """프레임을 차례로 보며 점수 줄을 고정한다. (고정한 영역 또는 None, 고정 프레임 번호, 추적 후보 수, 고정 때의 노트 수 추정, 읽은 자세)"""
    tracks: list[Track] = []
    for i, frame in enumerate(frames):
        at = i / FPS
        if i % DETECT_EVERY == 0:
            for b in find_rows(finder, frame[None, :, :, ::-1].copy(), DEVICE)[0]:
                box = b[:4]
                if box[2] - box[0] < 8 or box[3] - box[1] < 6:
                    continue
                roots = [t for t in tracks if t.root is t]
                best = max(roots, key=lambda t: iou(t.box, box), default=None)
                if best is not None and iou(best.box, box) >= 0.4:
                    best.boxes.append(box); best.last_seen = at
                else:
                    tracks.append(Track(box, at))
        live = [t for t in tracks if at - t.root.last_seen <= 3.0]
        if not live:
            continue
        crops = [crop_for_reader(frame, t.box, t.pose) for t in live]
        values, conf, _ = decode(reader(to_tensor(np.stack([c[0] for c in crops])).to(DEVICE)).cpu())
        for t, (image, std), v, c in zip(live, crops, values, conf):
            t.times.append(at); t.values.append(int(v) if (v > 0 and c >= READ_CONF and std >= BLANK_STD) else -1)
            if i % 3 == 0 and t.root is t:
                t.parts = (t.parts + [part_of(frame, t.box).copy()])[-8:]
        for t in [t for t in live if t.root is t]:
            # 2초 넘게 지켜봤는데 거의 읽히지 않는 자리는 기운 줄일 수 있다. 여러 자세로 읽어 보고, 훨씬 또렷이 읽히는 자세가 있으면
            # 그 자세로 읽는 하위 추적 후보를 하나 둔다. 그 추적 후보도 점수 규칙을 통과해야만 고정된다.
            recent = t.values[-20:]
            if len(t.values) < 20 or len(t.parts) < 6 or at - t.pose_checked < 3.0 or sum(v > 0 for v in recent) >= 8:
                continue
            t.pose_checked = at
            # 조각들은 그때그때의 검출 영역으로 잘라 둔 것이라 크기가 한두 화소씩 다르다. 예전에는 크기가 다르면 건너뛰었는데,
            # 그래서 이 검사가 한 번도 돌지 않았다: CYTUS 는 탐지 모델이 숫자 줄을 잘 잡고(168번) 30도 기울여 읽으면 전부 읽히는데도
            # 세 위치 모두 고정하지 못했다(2026-11-13 에 찾음). 가장 최근 조각의 크기에 맞춘다.
            height, width = t.parts[-1].shape[:2]
            if height < 8 or width < 8:
                continue
            same = [p if p.shape[:2] == (height, width) else cv2.resize(p, (width, height), interpolation=cv2.INTER_LINEAR) for p in t.parts if p.size]
            if len(same) < 6:
                continue
            options = [None] + POSES
            batch = np.stack([letterbox(posed(p, o))[0] for o in options for p in same])
            v, c, _ = decode(reader(to_tensor(batch).to(DEVICE)).cpu())
            n = len(same)
            clear = [sum(1 for k in range(j * n, j * n + n) if v[k] > 0 and c[k] >= 0.8) / n for j in range(len(options))]
            # 또렷이 읽히는 자세가 여럿이면 확신도가 가장 높은 것을 고른다. 처음에는 "또렷한 조각의 비율"이 가장 높은 첫 자세를 골랐는데,
            # CYTUS LEFT 에서 15도와 30도가 둘 다 전부 또렷해 15도가 골라졌고, 그 자세로는 끝자리 3 을 0 으로, 앞자리 7 을 1 로 읽는 일이 잦았다
            # (고정 뒤 규칙 통과율 90%. 30도로 읽는 CENTER 는 100%).
            sure = [float(np.mean([c[k] if v[k] > 0 else 0.0 for k in range(j * n, j * n + n)])) for j in range(len(options))]
            best = max(range(len(options)), key=lambda j: (clear[j] >= 0.75, sure[j]))
            if best != 0 and clear[best] >= 0.75 and clear[0] <= 0.4 and (t.child is None or t.child.pose != options[best]):
                if t.child is not None:
                    tracks.remove(t.child)
                t.child = Track(None, at, parent=t, pose=options[best]); tracks.append(t.child)
        locked = []
        for t in live:
            seen = {v for v in t.values if v > 0}
            if len(seen) < 15:
                continue
            fit = fit_notes(seen)
            if fit is None:
                continue
            keep = verify_sequence(t.values, fit.notes)
            good = {v for v, k in zip(t.values, keep) if k}
            if len(good) >= LOCK_VALUES:
                locked.append((max(good), t, fit.notes))
        if locked:
            # 바로 읽히는 추적 후보가 고정 조건을 채웠으면 기울여 읽는 하위 추적 후보보다 그것을 먼저 쓴다 (15도쯤 기울여도 읽히는 글꼴이 있어,
            # 같은 자리의 하위 추적 후보가 함께 조건을 채울 수 있다. 기울여 읽은 쪽은 한 장씩의 판독이 더 나쁘다).
            straight = [x for x in locked if x[1].pose is None]
            _, t, notes = max(straight or locked, key=lambda x: x[0])
            return t.box, i, len(tracks), notes, t.pose
    if debug is not None:                                    # 고정하지 못했을 때: 추적 후보마다 무엇이 읽혔는지
        for t in sorted(tracks, key=lambda t: -len(t.root.boxes))[:8]:
            seen = [v for v in t.values if v > 0]
            fit = fit_notes(set(seen)) if len(set(seen)) >= 15 else None
            debug.append({"box": [round(v) for v in t.box], "boxes": len(t.root.boxes), "reads": len(t.values), "confident": len(seen),
                          "distinct": len(set(seen)), "fit": None if fit is None else (fit.notes, round(fit.rate, 2)), "pose": t.pose,
                          "sample": seen[:: max(1, len(seen) // 12)][:12]})
    return None, len(frames), len(tracks), 0, None


@torch.no_grad()
def better_area(image, offset: int, area, proposed, reader) -> bool:
    """영역을 바꾸기 전에 두 영역을 같은 화면에서 읽어 본다. 바꿔도 되는 경우만 True.

    고정 영역은 "읽힌 값이 점수 규칙대로 오른다"는 것으로 확인된 자리다. 탐지 모델의 검출 영역만 믿고 옮기면 숫자 위의 글자 줄로
    옮겨 가기도 한다 (TECHNIKA 2·MUSE DASH·Don't Die 의 전체 프레임 통과율이 0% 로 무너졌다). 그래서
      - 지금 영역이 읽히고 새 영역도 같은 값을 그만큼 또렷이 읽으면 바꾼다 (자리를 다듬는 경우)
      - 지금 영역이 읽은 숫자열이 새 영역이 읽은 숫자열의 일부면 바꾼다 (잘렸던 자리가 들어온 경우)
      - 그 밖에는 바꾸지 않는다. 지금 영역이 안 읽히는 화면에서는 판단하지 않는다.
    """
    pair = [crop_for_reader(image, (a[0], a[1] - offset, a[2], a[3] - offset))[0] for a in (area, proposed)]
    values, conf, texts = decode(reader(to_tensor(np.stack(pair)).to(DEVICE)).cpu())
    if conf[0] < READ_CONF or conf[1] < READ_CONF or values[0] <= 0 or values[1] <= 0:
        return False
    if values[0] == values[1]:
        return conf[1] >= conf[0] - 0.02
    return len(texts[1]) > len(texts[0]) and texts[0] in texts[1]


@torch.no_grad()
def read_whole_case(case, box_abs, reader, pose=None, finder=None, band_x0: int = 0):
    """고정한 자리(화면 전체 좌표)로 0.5초 간격 화면 전체를 읽어 최종 점수를 판단한다.

    finder 를 주면 고정한 뒤에도 탐지 모델을 계속 돌려, 고정 영역과 겹치는 숫자 줄이 더 넓게 잡히면 영역을 넓힌다.
    가운데 정렬 기어는 자릿수가 늘면서 숫자 줄이 고정 때의 영역 밖으로 나가기 때문이다 (welcome to the space LEFT 에서
    끝자리가 잘려 틀린 점수가 나왔다). 영역은 넓어지기만 하고, 두 번 잇달아 같은 자리에서 잡혀야 넓힌다.

    돌려주는 값: (최종 점수 또는 None, 플레이 프레임 중 규칙 통과 비율, 보류 이유, 읽힌 값 가운데 규칙 통과 비율, 마지막 영역,
                 영역이 넓어진 내역)
    """
    data = np.load(ROOT / "data" / DATA_NAME / f"{case['id']}.npz")
    order = np.argsort(data["seconds"], kind="stable")
    area = [float(v) for v in box_abs]
    first_w, first_h = area[2] - area[0], area[3] - area[1]
    waiting = None
    steps = [(-1.0, tuple(area))]                           # (그때부터, 영역): 영역이 바뀐 시각들
    tops, bottoms = [area[1]], [area[3]]
    images, stds = [None] * len(order), [0.0] * len(order)
    for i in order:
        folder = here(case["frames"] if data["kinds"][i] == 0 else case["registration"]["frames"])
        image = read_image(Path(folder) / str(data["names"][i]))
        offset = TOP if image.shape[0] == 340 else 0
        if finder is not None and pose is None and image.shape[0] == 340:
            strip = image[:, band_x0:band_x0 + 960, ::-1]
            near = None
            for b in find_rows(finder, np.ascontiguousarray(strip)[None], DEVICE)[0]:
                box = (b[0] + band_x0, b[1] + TOP, b[2] + band_x0, b[3] + TOP)
                if overlap(box, area) >= 0.5 and 0.6 <= (box[3] - box[1]) / (area[3] - area[1]) <= 1.6:
                    near = box; break
            if near is not None and waiting is not None and iou(near, waiting) >= 0.7:
                # 가로는 넓어지기만 한다(자릿수가 는다). 세로는 숫자 줄의 높이가 변하지 않으므로, 지금까지 잡힌 검출 영역들의 가운데 값을 쓴다.
                # 세로를 고정 때의 영역 그대로 두면 아래가 잘려(welcome to the space LEFT: 3·8·9 혼동) 나빠졌고, 합쳐서 넓히기만 하면
                # 조각이 커져 글자가 작게 들어간다.
                tops.append(near[1]); bottoms.append(near[3])
                top, bottom, _ = vertical_range(tops, bottoms)
                grown = [min(area[0], near[0]), top, max(area[2], near[2]), bottom]
                if grown[2] - grown[0] <= 2.5 * first_w and grown != area and better_area(image, offset, area, grown, reader):
                    area = grown; steps.append((float(data["seconds"][i]), tuple(area)))
            waiting = near
        images[i], stds[i] = crop_for_reader(image, (area[0], area[1] - offset, area[2], area[3] - offset), pose)
    values, conf = [], []
    for s in range(0, len(images), 256):
        v, c, _ = decode(reader(to_tensor(np.stack(images[s:s + 256])).to(DEVICE)).cpu())
        values += v; conf += c
    values, conf = np.array(values), np.array(conf)
    d = SimpleNamespace(cases=[{"id": case["id"]}], kinds=data["kinds"], seconds=data["seconds"], blank=np.array(stds) < BLANK_STD, order=[order])
    final, coverage, why = decide_final(d, 0, values, conf)
    # 읽힌 값 가운데 점수 규칙을 통과한 비율. 숫자 줄의 한쪽이 잘린 자리를 읽으면 값은 또렷이 읽히지만 대부분 규칙에 어긋난다.
    # 그런데도 우연히 규칙에 맞는 값이 8개쯤은 생겨서 기존 최종 판단을 통과할 수 있다. 그래서 이 비율이 낮으면 점수를 내지 않는다.
    _, reads, keep = verified_reads(d, 0, values, conf)
    read = sum(1 for r in reads if r > 0)
    agree = float(keep.sum() / read) if read else 0.0
    if final is not None and agree < MIN_AGREE:
        final, why = None, f"읽힌 값의 규칙 통과 비율이 낮음({agree:.2f})"
    return final, coverage, why, agree, area, steps


def main():
    finder_name, reader_name = sys.argv[1], sys.argv[2]
    wanted = set(sys.argv[3:])
    use_codex = "--codex-area" in wanted                    # 시험 도구 자체의 점검: 위치를 찾지 않고 기준 영역을 그대로 넣어 읽어 본다
    wanted.discard("--codex-area")
    use_all = "--all-model" in wanted                       # 기어 90종 전부로 학습한 탐지 모델 (실제로 쓸 버전. 처음 보는 기어 시험이 아니다)
    wanted.discard("--all-model")
    cases = [c for c in json.loads((VALIDATION / "candidate-v4" / "all270.json").read_text(encoding="utf-8")) if c["position"] in BAND]
    if wanted:
        cases = [c for c in cases if c["id"] in wanted]
    index = json.loads((DATA / "index.json").read_text(encoding="utf-8"))
    gears = sorted({i["gear"] for i in index})
    names = {c["id"]: c["name"] for c in json.loads((ROOT / "data" / DATA_NAME / "cases.json").read_text(encoding="utf-8"))}
    reader, _ = load_model(reader_name); reader.eval()
    finders = {}
    table = sources()
    rows = []
    started = time.time()
    for n, c in enumerate(cases, 1):
        case_id = c["id"]; gear = case_id.rsplit("-", 2)[0]
        if not (ROOT / "data" / DATA_NAME / f"{case_id}.npz").exists():
            continue                                         # 0.5초 자료가 없는 사례(3-05 CENTER)는 이 채점 방식으로 못 본다
        fold = "all" if use_all else fold_of(gear, gears)
        if fold not in finders and not use_codex:
            m = RowFinder().to(DEVICE)
            m.load_state_dict(torch.load(RUNS / finder_name / ("all.pt" if use_all else f"fold{fold}.pt"), map_location=DEVICE)); m.eval()
            finders[fold] = m
        first = first_score_time(case_id)
        x0 = BAND[c["position"]][0]
        ref = area_of(case_id)
        row = {"사례": case_id, "기어": names.get(case_id, ""), "묶음": fold, "첫 점수(참고)": "" if first is None else f"{first:.1f}"}
        if use_codex:
            box_abs, lock_frame, tracks, notes, pose = tuple(ref), 0, 0, 0, None
        else:
            frames = read_band(table[case_id], c["position"], (first or 10.0) + WATCH)
            notes_debug = [] if wanted else None
            box, lock_frame, tracks, notes, pose = locate(frames, finders[fold], reader, notes_debug)
            box_abs = None if box is None else lock_area(box, x0)
            if notes_debug:
                print(f"  기준 영역(띠 좌표): {[ref[0] - x0, ref[1] - TOP, ref[2] - x0, ref[3] - TOP] if ref else None}")
                for line in notes_debug:
                    print("  추적 후보", line)
        row.update({"줄기 수": tracks, "고정": "예" if box_abs else "아니오", "고정 시각": "", "첫 점수 뒤(초)": "", "노트 수 추정": notes or "",
                    "네모": "", "마지막 영역": "", "읽힌 값의 규칙 통과 비율": "",
                    "자세(각도·띠 중심·띠 높이)": "" if pose is None else " ".join(str(v) for v in pose),
                    "Codex 영역과 겹침": "", "최종 점수": "", "기대 점수": c["registration"]["expected"], "판정": "고정 못 함", "통과 비율": "", "보류 이유": ""})
        if box_abs:
            grow_with = finders.get(fold) if (GROW and not use_codex) else None
            final, coverage, why, agree, area, _ = read_whole_case(c, box_abs, reader, pose, grow_with, x0)
            row.update({"고정 시각": f"{lock_frame / FPS:.1f}", "첫 점수 뒤(초)": "" if first is None else f"{lock_frame / FPS - first:.1f}",
                        "네모": " ".join(str(int(round(v))) for v in box_abs), "마지막 영역": " ".join(str(int(round(v))) for v in area),
                        "읽힌 값의 규칙 통과 비율": f"{agree:.3f}", "Codex 영역과 겹침": f"{iou(box_abs, ref):.2f}" if ref else "",
                        "최종 점수": "" if final is None else final, "통과 비율": f"{coverage:.3f}", "보류 이유": why,
                        "판정": "보류" if final is None else ("맞음" if final == c["registration"]["expected"] else "틀림")})
        rows.append(row)
        print(f"[{n}/{len(cases)}] {case_id} {row['기어'][:16]:<16} 고정 {row['고정']} 첫 점수 뒤 {row['첫 점수 뒤(초)'] or '-':>5}초 겹침 {row['Codex 영역과 겹침'] or '-':>4} "
              f"{row['판정']} {row['보류 이유']} 처음 고정한 영역 [{row['네모']}] → 마지막 영역 [{row['마지막 영역']}] 기준 영역 {ref} 규칙 통과 {row['읽힌 값의 규칙 통과 비율']} "
              f"최종 {row['최종 점수']}/{row['기대 점수']} ({time.time() - started:.0f}초)", flush=True)
    out = RUNS / finder_name / (f"run-{reader_name}{'-codex-area' if use_codex else ''}{'-all-model' if use_all else ''}{f'-extra{EXTRA_X}' if EXTRA_X else ''}.csv")
    if not wanted:
        with open(out, "w", newline="", encoding="utf-8-sig") as f:
            w = csv.DictWriter(f, fieldnames=list(rows[0])); w.writeheader(); w.writerows(rows)
    verdict = {k: sum(r["판정"] == k for r in rows) for k in ("맞음", "틀림", "보류", "고정 못 함")}
    print(f"사례 {len(rows)}개:", verdict)
    waits = [float(r["첫 점수 뒤(초)"]) for r in rows if r["첫 점수 뒤(초)"]]
    if waits:
        print(f"고정까지(첫 점수 뒤, 참고): 중앙값 {np.median(waits):.1f}초, 가장 늦은 것 {max(waits):.1f}초, 15초 넘는 사례 {sum(w > 15 for w in waits)}개")


if __name__ == "__main__":
    main()
