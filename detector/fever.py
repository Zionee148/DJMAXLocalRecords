"""피버 연출 구간을 가려내고, 연출이 아닌 프레임(정상 프레임)만 놓고 채점한다 (LEFT·CENTER).

채점 기준(2026-11-12)
  - 플레이 구간의 정상 프레임은 모두 읽어야 한다(100%). 가려진 프레임이란 게임 중 피버 연출이 숫자를 가린 프레임을 말한다.
    등록 키를 누를 때는 피버 연출이 없으므로 그런 프레임은 분모에서 뺀다. 연출 구간은 통째로 뺀다.
  - 흐릿한 것은 괜찮다: 연출 뒤 숫자가 어느 정도 명확해진 시점부터는 읽어야 하고, 평소 기어 효과로 흐린 프레임도 정상이다.
  - 이 기준(99% 가 아니라 100%)도 위치가 고정된 뒤의 프레임에 적용한다.

연출 구간을 찾는 법 (판독 결과를 쓰지 않고 그림만으로)
  1) 평소 점수판 = 플레이 구간 모든 프레임의 화소별 가운데 값. 연출은 구간의 3할을 넘지 않으므로 가운데 값은 평소 모습이다.
     (처음에는 "규칙을 통과해 읽힌 프레임"으로 만들었는데, 연출 중에도 읽힌 프레임이 섞여 EXTENSION 2 의 줄무늬가 지나는 자리를 못 봤다.)
  2) 견주는 자리 = 대부분의 프레임에서 평소 값과 거의 같은 화소 (숫자가 놓이는 자리, 늘 움직이는 장식, 비치는 배경은 빠진다).
  3) 프레임의 달라짐 = 그 자리들에서 평소 점수판과의 밝기 차이의 위쪽 12% 지점 (0~255).
  4) 달라짐이 HIGH 이상인 프레임이 하나라도 있는, LOW 이상이 이어진 구간을 연출 구간으로 묶는다. 짧은 틈은 잇는다.
     LOW 는 "연출이 걷히는 중인데 숫자는 이미 다 보이는" 흐린 프레임을 정상으로 남기려고 0 이 아니라 조금 높게 둔다.
     HIGH·LOW 는 그림을 보고 정한 출발값이며, 사람이 결과 그림으로 확인해야 한다.
  견줄 자리가 너무 적은 사례(점수판이 반투명한 기어)는 연출 구간을 찾지 못하므로 전부 정상 프레임으로 센다.

실행: .venv\\Scripts\\python.exe -m detector.fever <판독 실행 이름> [그림 폴더] [사례 ...]
      .venv\\Scripts\\python.exe -m detector.fever <목록의 실행 이름> --window-only <판독 실행 이름> <사례 ...>
                                (플레이 구간만 담은 목록. 숫자를 가리는 것이 없는 기어인데 목록을 만들 수 없는 사례에 쓴다: 3-05 CENTER)
출력: data\\full\\<실행>\\fever\\<사례>.npz (change, fever), fever.csv, 그림 폴더에 "연출이 끝난 직후" 그림
"""
import csv
import os
import json
import sys
from pathlib import Path

import cv2
import numpy as np
import torch

from .build_dataset import letterbox
from .full_frames import sources, stream
from .full_summary import play_window
from .model import BLANK, HEIGHT, WIDTH
from .score_frames import READ_CONF
from .score_rules import verify_sequence
from . import inputs
from .selftrain import DATA_NAME, DEVICE, ROOT, load_model, notes_of, to_tensor

VISIBLE_BELOW = 0.05           # 가장 덜 보이는 자리의 닮음이 (그 사례 평소 닮음의 아래 1% 지점 - 이 값) 이상이면 "모든 자릿수가 또렷이 보임"
ONE_DIGIT_DROP = 0.3           # 가장 덜 보이는 자리가 다른 자리들보다 이만큼 넘게 낮으면 "한 자리가 가려짐".
                               # 사람이 그림을 보고 판정한 예(O = 정상, X = 가려짐)에 맞춘 값: EXTENSION 2 에서 줄무늬에 덮인 프레임(X)은 0.37~0.66,
                               # Boom! 에서 연출 중 점수판 색이 바뀌었지만 숫자가 다 보이는 프레임(O)은 0.20 이었다 (0.2 로 두었을 때 이것을 가려짐으로 냈다)
LOWEST_RATIO = 0.5             # 가장 덜 보이는 자리의 닮음이 그 사례 평소(가운데 값)의 절반에 못 미치면 "그 자리는 안 보임".
                               # MYSTIC SUMMER 에서 큰 "FEVER" 글자가 숫자를 가린 프레임(직접 확인)은 최저 0.32·가운데 0.59 라서
                               # 위의 두 기준(뚝 떨어짐 0.3, 줄 전체 0.6배)을 아슬아슬하게 비켜 갔다. 글자의 획이 숫자 본과 우연히 닮아 가운데 값이 높게 나온다.
                               # 그림을 보고 O 로 판정한 Boom! 의 프레임들은 최저 0.61~0.78 이라 이 기준에 걸리지 않는다
BLURRY_TEMPLATES = 0.75        # 깨끗한 프레임의 닮음(가운데 값)이 이보다 낮으면 본이 흐린 사례로 본다
WHOLE_ROW_RATIO = 0.6          # 자리들의 가운데 값이 그 사례 평소(가운데 값)의 이 배율에 못 미치면 "숫자 줄 전체가 안 보임".
                               # 처음에는 평소의 아래 1% 지점에서 0.35 를 뺀 값을 썼는데, 본이 흐린 기어(LIBERTY 2)는 아래 1% 지점이 0.36 이라
                               # 기준이 0 에 가까워져 삽화가 다 덮은 프레임도 통과했다
                               # 또렷하지도 가려지지도 않은 프레임은 정상으로 센다. 값들은 그림을 보고 정한 출발값이며, 사람이 결과 그림으로 확인해야 한다

WHITE_OUT, WHITE_SPREAD = 240.0, 16.0   # 숫자 한 자리 너비의 조각이 통째로 하얗고(어두운 쪽 5% 가 이 밝기 이상) 밝기 폭이 이보다 좁으면 "그 자리는 하얗게 날아감".
                               # LOL 의 흰 빛줄기가 앞 두 자리를 덮은 프레임 30장은 밝기 240~248·폭 7~15, 숫자가 다 보이는 8장은 129~169·85~126 이었다.
                               # Boom! 의 노란 점수판(옅은 숫자, 그림을 보고 O 로 판정)은 밝기 233~235 라 걸리지 않는다. BASEBALL 의 옅은 숫자는 181~207.
                               # 직접 읽어 라벨을 붙인 Boom! 17장 가운데 2장(#5988, #8407: 앞자리가 흰 삽화 위에 겹친 것)이 여기 걸린다.
HIGH, LOW = 45.0, 20.0
BRIDGE = 18                    # 이만큼(0.3초)보다 짧은 틈은 같은 연출로 잇는다
START_LOW, EARLY = 8.0, 30     # 연출의 시작은 달라짐이 이 값을 넘은 때부터로 앞당긴다 (최대 0.5초)
MAX_FEVER = 240                # 4초 넘게 이어진 달라짐은 연출로 보지 않는다. 열두 사례에서 잰 연출은 0.9~2.0초였고, 그보다 훨씬 긴 것은
                               # 점수판에 비치는 배경 영상이 바뀐 것이었다 (TECHNIKA 3 10.7초, So Happy 7.7초). 연출이 아니면 정상으로 센다
SPLIT_PEAK = 90.0              # 4초 넘는 달라짐 구간 안에서도, 달라짐이 HIGH 를 넘는 4초 이하의 토막이 이 값까지 솟으면 그 토막은 연출로 본다.
                               # So Happy 는 무대 조명 때문에 달라짐 26~44 가 7.7초씩 이어지고 그 안에 진짜 연출(달라짐 140~180)이 들어 있었다.
                               # 긴 구간을 통째로 버리면서 진짜 연출까지 버렸다. TECHNIKA 3 의 배경 영상은 가장 높아야 79 라 걸리지 않는다.
PEAK_MIN, PEAK_RATIO = 53.0, 0.6   # 연출로 치려면 달라짐이 이 값까지 솟아야 하고, 그 사례의 다른 연출들(솟은 값의 위 1/4 지점)의 이 배율은 돼야 한다.
                               # 기어끼리 연출 시각을 견줘 보니 정상적인 기어는 모두 같은 17번의 시각에 잡혔고, 어긋난 것은 연출이 아니었다(그림으로 확인):
                               #   So Happy 33번 가운데 16번은 노트를 칠 때 터지는 불빛(솟은 값 45~66, 진짜 연출은 163 이상),
                               #   Overload 는 2.1초마다 점수판 바탕색이 보라로 번쩍이는 것(45~48), DIEIN CENTER 65번 가운데 48번은 불빛(45~51, 진짜는 55~68).
                               # 솟은 값이 낮은 진짜 연출(TEKKEN 46~55)도 빠지지만, 그 프레임은 정상으로 세어지므로 판독기에 불리한 쪽이다.
NOTHING_COVERS = {"4-32", "3-05"}   # 연출도 기어 장식도 숫자를 가리지 않는 기어: 플레이 구간의 모든 프레임이 정상 프레임이다.
                               # SUMMER ADVENTURE(4-32): 실제 플레이에서 구름과 피버 연출이 숫자를 전혀 가리지 않는 것을 확인했다(2026-11-12 밤).
                               # welcome to the space(3-05): 2026-11-14 에 이렇게 세기로 정했다. 이 기어는 연출이 숫자를 가리지 않고 기어 전체가
                               #   움직이며, 목록의 "연출 구간"은 점수판이 움직인 구간이었다. 그 안에서 숫자가 보이는데 못 읽은 15장은 그림을 직접 보고
                               #   미통과로 판정했다(docs\기록\독립검산.md). CENTER 는 기준 영역(full_frames.area_of)이 없어 목록을 만들 수 없으므로 --window-only 로 만든다.
VIDEO_FPS = 60.0


def normal_frames(case_id: str, inside: np.ndarray, fever: np.ndarray, covered: np.ndarray) -> np.ndarray:
    """정상 프레임: 플레이 구간 안, 연출 구간 밖, 가려지지 않음 (2026-11-12 밤에 정한 기준). NOTHING_COVERS 의 기어는 플레이 구간 전부."""
    if case_id.rsplit("-", 2)[0] in NOTHING_COVERS:
        return inside.copy()
    return inside & ~fever & ~covered


def find_fever(grays: np.ndarray, start: int, stop: int):
    """(달라짐, 연출 여부, 견줄 자리의 비율). 플레이 구간 밖은 0·False."""
    n = len(grays)
    smooth = np.stack([cv2.GaussianBlur(g, (3, 3), 0) for g in grays[start:stop]]).astype(np.int16)
    panel = np.median(smooth[:: max(1, len(smooth) // 600)], axis=0)
    change = np.zeros(n, np.float32); fever = np.zeros(n, bool)
    # 견줄 자리 = 대부분의 프레임에서 평소 값과 거의 같은 화소. "거의 같다"의 폭은 사례에 맞춘다:
    # 점수판이 은은하게 일렁이는 기어(Hanz up! Girls 의 금속 원판, LOL)는 폭 8 로는 견줄 자리가 1% 도 안 나와
    # 연출을 하나도 못 잡았다(화면을 다 덮는 삽화인데도). 폭을 넓히면 그만큼 연출로 보는 문턱도 올린다.
    # 연출이 구간의 3할을 차지하는 기어가 있으므로 "6할 넘게 같은 화소"로 고른다(8할로 두면 그런 기어에서 화소가 안 남는다).
    still, tolerance = None, 0
    for tolerance in (8, 14, 20):
        near = (np.abs(smooth[:: max(1, len(smooth) // 600)] - panel) <= tolerance).mean(axis=0)
        still = near >= 0.6
        if still.mean() >= 0.15:
            break
    if still.mean() < 0.15:
        return change, fever, float(still.mean())
    change[start:stop] = np.percentile(np.abs(smooth - panel)[:, still], 88, axis=1)
    low_line, start_line = max(LOW, tolerance + 10.0), max(START_LOW, float(tolerance))
    low = change >= low_line
    # 짧은 틈 잇기
    gap = 0
    for i in range(start, stop):
        if low[i]:
            if 0 < gap < BRIDGE:
                low[i - gap:i] = True
            gap = 0
        else:
            gap += 1
    found: list[tuple[int, int]] = []
    i = start
    while i < stop:
        if low[i]:
            j = i
            while j < stop and low[j]:
                j += 1
            if change[i:j].max() >= SPLIT_PEAK and (j - i) > MAX_FEVER:
                # 긴 구간 안의 솟은 토막을 찾는다 (짧은 틈은 잇는다)
                high = change[i:j] >= HIGH
                gap = 0
                for k in range(len(high)):
                    if high[k]:
                        if 0 < gap < BRIDGE:
                            high[k - gap:k] = True
                        gap = 0
                    else:
                        gap += 1
                for a, b in episodes(high):
                    if b - a <= MAX_FEVER and change[i + a:i + b].max() >= SPLIT_PEAK:
                        found.append((i + a, i + b))
            elif change[i:j].max() >= HIGH and (j - i) <= MAX_FEVER:
                # 시작 쪽은 앞으로 조금 넓힌다: 연출은 무엇이 밀려 들어오며 시작해서, 전체로는 아직 조금만 달라졌어도
                # 숫자 한 자리가 먼저 덮인다 (EXTENSION 2 의 줄무늬). 끝 쪽은 넓히지 않는다: 걷히는 중의 흐린 프레임은 읽어야 한다.
                a = i
                while a > start and i - a < EARLY and change[a - 1] >= start_line:
                    a -= 1
                found.append((a, j))
            i = j
        else:
            i += 1
    if found:
        peaks = np.array([float(change[a:b].max()) for a, b in found])
        line = max(PEAK_MIN, PEAK_RATIO * float(np.percentile(peaks, 75)))
        for (a, b), peak in zip(found, peaks):
            if peak >= line:
                fever[a:b] = True
    return change, fever, float(still.mean())


def digit_centers(row_logits: np.ndarray, width: int, height: int) -> list[tuple[int, float]]:
    """판독기 출력 한 줄(64칸 x 11) → [(숫자, 그 숫자의 가로 중심(조각 좌표))]. 판독기가 그 숫자를 어느 칸에서 냈는지로 자리를 안다."""
    best = row_logits.argmax(-1)
    scale = min(HEIGHT / height, WIDTH / width)
    offset = (WIDTH - max(1, round(width * scale))) // 2
    out, t = [], 0
    while t < len(best):
        if best[t] != BLANK:
            u = t
            while u + 1 < len(best) and best[u + 1] == best[t]:
                u += 1
            out.append((int(best[t]), (((t + u) / 2 + 0.5) * (WIDTH / len(best)) - offset) / scale))
            t = u + 1
        else:
            t += 1
    return out


def patch_vector(gray: np.ndarray, x: float, half: float, rows: tuple[int, int]) -> np.ndarray | None:
    """숫자 한 자리 조각을 같은 크기로 맞추고, 밝기와 대비를 지운 벡터로 만든다 (통째로 흐려져도 닮음이 유지되도록)."""
    a, b = int(round(x - half)), int(round(x + half))
    if a < 0 or b > gray.shape[1] or b - a < 3:
        return None
    v = cv2.resize(gray[rows[0]:rows[1], a:b].astype(np.float32), (16, 24), interpolation=cv2.INTER_AREA).ravel()
    v = v - v.mean()
    norm = float(np.linalg.norm(v))
    return v / norm if norm > 1e-3 else None


@torch.no_grad()
def judge_visibility(parts: list, grays: np.ndarray, fever: np.ndarray, keep: np.ndarray, raw: np.ndarray, start: int, stop: int, reader,
                     extra: np.ndarray | None = None):
    """연출 구간의 프레임마다 "모든 자릿수가 보이는가"를 잰다. 돌려주는 값: 프레임별 점수(가장 덜 보이는 자리의 닮음. 연출 밖은 nan).

    2026-11-12 의 기준: 연출이 걸려 있어도 숫자가 또렷하면 정상 프레임이다.
      1) 그 사례에서 연출 밖이고 규칙을 통과해 읽힌 프레임으로 0~9 의 본(숫자별 평균 그림)을 뜬다. 숫자의 자리는 판독기가 그 숫자를 낸 칸에서 얻는다.
      2) 연출 프레임에서는, 연출 직전·직후의 깨끗한 프레임에서 숫자가 놓였던 자리들을 본다(연출 2초 안에 자리가 옮겨지는 일은 드물다).
         자리마다 0~9 본 가운데 가장 닮은 것과의 닮음을 재고, 가장 낮은 자리의 값을 그 프레임의 점수로 삼는다.
    닮음은 밝기와 대비를 지우고 재므로 통째로 흐려진 것에는 덜 떨어지고, 획이 가려지면 떨어진다.

    extra: 연출로 잡히지 않았어도 함께 잴 프레임(못 읽은 프레임). 연출 찾기가 놓친 가림을 여기서 잡는다.
    """
    n = len(parts)
    score = np.full(n, np.nan, np.float32)
    clean = np.where(keep & ~fever)[0]
    clean = clean[(clean >= start) & (clean < stop)]
    target = fever if extra is None else (fever | extra)
    if len(clean) < 30 or not target.any():
        return (score, score.copy()), None
    sample = clean[np.linspace(0, len(clean) - 1, min(300, len(clean))).round().astype(int)]
    h, w = parts[0].shape[:2]
    logits = reader(to_tensor(np.stack([letterbox(parts[i])[0] for i in sample])).to(DEVICE)).cpu().numpy()
    found = {int(i): digit_centers(row, w, h) for i, row in zip(sample, logits)}
    found = {i: c for i, c in found.items() if "".join(str(d) for d, _ in c) == str(int(raw[i]))}
    gaps = [b[1] - a[1] for c in found.values() for a, b in zip(c, c[1:])]
    if len(found) < 20 or not gaps:
        return (score, score.copy()), None
    half = max(3.0, 0.5 * float(np.median(gaps)))
    # 숫자가 놓이는 가로 띠만 본다: 깨끗한 프레임들 사이에서 값이 많이 바뀌는 줄들.
    # 처음에는 조각의 위아래를 거의 다 봤는데, EXTENSION 2 에서 숫자 위쪽을 지나는 줄무늬 때문에 숫자가 멀쩡한 프레임(#1800 "58897")을
    # 가려졌다고 냈다. 그림을 직접 보고 찾은 오판이다.
    lo = int(max(0, min(c[0][1] for c in found.values()) - half)); hi = int(min(w, max(c[-1][1] for c in found.values()) + half))
    wobble = grays[sorted(found)][:, :, lo:hi].astype(np.float32).std(axis=0).mean(axis=1)
    strong = np.where(wobble >= 0.35 * wobble.max())[0]
    rows = (max(0, int(strong.min()) - 1), min(h, int(strong.max()) + 2)) if len(strong) >= 4 else (2, h - 2)
    # 0~9 의 본 뜨기. 판독기가 알려 주는 숫자 자리는 몇 화소씩 어긋나서, 그대로 평균하면 본이 흐려진다
    # (LIBERTY 2 처럼 기울고 번진 글꼴에서는 깨끗한 프레임조차 본과 0.3 밖에 안 닮아, 삽화가 다 덮은 프레임을 "또렷"으로 냈다).
    # 그래서 한 번 뜬 본에 각 조각을 좌우로 조금씩 옮겨 가장 잘 맞는 자리로 다시 맞추고 본을 새로 뜬다. 두 번 되풀이한다.
    spots = [(i, digit, x) for i, centers in found.items() for digit, x in centers]

    def make(shift: dict) -> dict:
        bank: dict[int, list] = {}
        for k, (i, digit, x) in enumerate(spots):
            v = patch_vector(grays[i], x + shift.get(k, 0.0), half, rows)
            if v is not None:
                bank.setdefault(digit, []).append(v)
        return {d: np.mean(v, axis=0) / np.linalg.norm(np.mean(v, axis=0)) for d, v in bank.items() if len(v) >= 5}

    by_digit, shift = make({}), {}
    for _ in range(2):
        for k, (i, digit, x) in enumerate(spots):
            if digit not in by_digit:
                continue
            best = (-1.0, 0.0)
            for dx in np.arange(-0.5 * half, 0.5 * half + 0.01, 1.0):
                v = patch_vector(grays[i], x + dx, half, rows)
                if v is not None:
                    best = max(best, (float(by_digit[digit] @ v), float(dx)))
            shift[k] = best[1]
        by_digit = make(shift)
    if not by_digit:
        return (score, score.copy()), None
    templates = np.stack(list(by_digit.values()))
    flat_values: list[float] = []                             # row_score 를 부를 때마다 "가장 밋밋한 자리의 밝기 폭"이 하나씩 쌓인다
    white_values: list[float] = []
    def row_score(gray, centers) -> float:
        """숫자 줄(centers 의 왼쪽 끝~오른쪽 끝)을 한 화소씩 훑어 "여기에 어떤 숫자가 또렷이 있다"는 정도를 재고,
        숫자 한 자리 너비의 구간마다 가장 높은 값을 뽑아 그 가운데 가장 낮은 것을 돌려준다.

        처음에는 직전 프레임의 숫자 자리에 그대로 대 보았는데, 숫자마다 너비가 다른 글꼴(BASEBALL: 1 이 좁다)에서는 값이 바뀌면
        자리가 밀려, 아주 또렷한 프레임도 닮음이 0.6 으로 나왔다. 그래서 자리를 정해 두지 않고 훑는다.
        닮음은 절댓값으로 잰다: 효과 중에 밝고 어두움이 뒤집히는 점수판이 있다(Boom!).
        """
        left, right = centers[0][1], centers[-1][1]
        xs = np.arange(int(np.floor(left - half)), int(np.ceil(right + half)) + 1)
        likeness = np.zeros(len(xs), np.float32)
        for k, x in enumerate(xs):
            v = patch_vector(gray, float(x), half, rows)
            if v is not None:
                likeness[k] = float(np.abs(templates @ v).max())
        each, contrast, darkest = [], [], []
        for c in np.arange(left, right + 0.01, half):         # 구간의 중심을 반 자리씩 옮겨 가며
            near = likeness[(xs >= c - half) & (xs <= c + half)]
            each.append(float(near.max()) if len(near) else 0.0)
            # 그 자리에 획이 있기는 한가: 자리 너비보다 조금 좁은 조각의 밝기 폭(위 5% - 아래 5%).
            # 닮음은 아무 무늬에나 0.5 쯤 나와서(본 10개 x 자리 옮김 x 절댓값) 흰 빛줄기나 민무늬 삽화가 덮은 자리를 못 가렸다.
            a, b = max(0, int(round(c - 0.7 * half))), min(gray.shape[1], int(round(c + 0.7 * half)))
            if b - a >= 2:
                piece = gray[rows[0]:rows[1], a:b]
                contrast.append(float(np.percentile(piece, 95) - np.percentile(piece, 5)))
                darkest.append(float(np.percentile(piece, 5)))
        flat_values.append(min(contrast) if contrast else np.nan)
        white_values.append(max(darkest) if darkest else np.nan)   # 가장 하얗게 날아간 자리의 "어두운 쪽 5%" 밝기
        return min(each), float(np.median(each))              # 가장 덜 보이는 자리, 자리들의 가운데 값

    usual = [row_score(grays[i], c) for i, c in list(found.items())[::3]]
    usual_flat = float(np.nanpercentile(flat_values, 1)) if flat_values else 0.0
    usual_white = float(np.nanpercentile(white_values, 99)) if white_values else 255.0
    order = np.array(sorted(found))
    middle = np.full(n, np.nan, np.float32); flat = np.full(n, np.nan, np.float32); white = np.full(n, np.nan, np.float32)
    for a, b in episodes(target):
        k = int(np.searchsorted(order, a))
        references = [found[int(order[j])] for j in (k - 1, int(np.searchsorted(order, b))) if 0 <= j < len(order)]
        for i in range(a, b):
            # 직전과 직후의 숫자 줄 범위를 각각 대 보고 나은 쪽 (자릿수가 연출 중에 늘 수 있다)
            both = []
            for c in references:
                both.append(row_score(grays[i], c) + (flat_values[-1], white_values[-1]))
            if both:
                score[i], middle[i], flat[i], white[i] = max(both)
    lows = [u[0] for u in usual]; mids = [u[1] for u in usual]
    judge_visibility.white = white
    judge_visibility.flat = flat                              # 프레임별 "가장 밋밋한 자리의 밝기 폭" (main 이 저장한다)
    return (score, middle), (float(np.percentile(lows, 1)), float(np.median(lows)), len(templates), float(np.percentile(mids, 1)), float(np.median(mids)), usual_flat, usual_white)


def episodes(fever: np.ndarray) -> list[tuple[int, int]]:
    edges = np.flatnonzero(np.diff(np.concatenate([[0], fever.astype(np.int8), [0]])))
    return [(int(a), int(b)) for a, b in zip(edges[::2], edges[1::2])]


def compare(run: str, masks_run: str):
    """다른 실행에서 정해 둔 "정상 프레임" 목록을 그대로 두고, 이 실행의 판독이 그 프레임들을 읽었는지만 센다.

    정상 프레임을 가려내는 방법은 그 실행의 판독 결과로 숫자의 본을 뜨기 때문에, 판독기가 바뀌면 정상 프레임의 목록이 조금씩 달라진다.
    판독기끼리 견줄 때는 목록을 한 실행의 것으로 고정해야 공정하다.
    실행: python -m detector.fever <다른 판독 실행> --masks-from <정상 프레임 목록을 정한 실행>
    """
    folder, masks = ROOT / "data" / "full" / run, ROOT / "data" / "full" / masks_run / "fever"
    meta = {c["id"]: c for c in json.loads((ROOT / "data" / DATA_NAME / "cases.json").read_text(encoding="utf-8"))}
    rows = []
    for path in inputs(masks, "*.npz", "정상 프레임 목록"):
        case_id = path.stem
        if not (folder / path.name).exists():
            continue
        m = np.load(path)
        if "covered" not in m.files:
            continue
        z = np.load(folder / path.name)
        raw = np.where((z["conf"] >= READ_CONF) & ~z["blank"] & (z["values"] > 0), z["values"], -1)
        keep = np.array(verify_sequence([int(v) for v in raw], notes_of(case_id)), bool)
        n = min(len(keep), len(m["covered"]))
        inside = np.zeros(n, bool); inside[int(m["window"][0]):int(m["window"][1])] = True
        normal = normal_frames(case_id, inside, m["fever"][:n], m["covered"][:n])
        rows.append({"사례": case_id, "기어": meta[case_id]["name"], "플레이 구간": int(inside.sum()), "정상 프레임": int(normal.sum()),
                     "정상인데 못 읽음": int((normal & ~keep[:n]).sum()),
                     "(참고) 연출 중 안 가려진 것까지 세면": int((inside & ~m["covered"][:n] & ~keep[:n]).sum())})
    with open(folder / f"fever-as-{masks_run}.csv", "w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0])); w.writeheader(); w.writerows(rows)
    miss = [r["정상인데 못 읽음"] for r in rows]
    print(f"{run} (정상 프레임 목록은 {masks_run} 의 것): 사례 {len(rows)}개, 정상 프레임을 모두 읽은 사례 {sum(x == 0 for x in miss)}개, "
          f"1~5장 {sum(1 <= x <= 5 for x in miss)}개, 6장 이상 {sum(x > 5 for x in miss)}개, 못 읽은 정상 프레임 합계 {sum(miss):,}장 "
          f"(정상 프레임 {sum(r['정상 프레임'] for r in rows):,}장 / 플레이 구간 {sum(r['플레이 구간'] for r in rows):,}장. "
          f"참고: 연출 중 안 가려진 것까지 세면 {sum(r['(참고) 연출 중 안 가려진 것까지 세면'] for r in rows):,}장)")
    by = {}
    for r in rows:
        by[r["기어"]] = by.get(r["기어"], 0) + r["정상인데 못 읽음"]
    print("  기어별:", ", ".join(f"{k} {v}" for k, v in sorted(by.items(), key=lambda x: -x[1]) if v))


def window_only(masks_run: str, read_run: str, cases: list[str]):
    """플레이 구간만 담은 목록을 만든다(연출·가려짐 없음). NOTHING_COVERS 의 기어에만 쓴다.

    기준 영역(full_frames.area_of)이 없어 목록을 만들 수 없는 사례(3-05 CENTER)를 정상 프레임 집계에 넣기 위한 것이다.
    플레이 구간은 다른 목록과 같은 규칙(play_window)으로, 주어진 판독 실행에서 잡는다.
    """
    out = ROOT / "data" / "full" / masks_run / "fever"
    out.mkdir(parents=True, exist_ok=True)
    for case_id in cases:
        if case_id.rsplit("-", 2)[0] not in NOTHING_COVERS:
            print(f"{case_id}: NOTHING_COVERS 의 기어가 아니라 만들지 않음"); continue
        if (out / f"{case_id}.npz").exists():
            print(f"{case_id}: 목록이 이미 있어 그대로 둠"); continue
        z = np.load(ROOT / "data" / "full" / read_run / f"{case_id}.npz")
        raw = np.where((z["conf"] >= READ_CONF) & ~z["blank"] & (z["values"] > 0), z["values"], -1)
        keep = np.array(verify_sequence([int(v) for v in raw], notes_of(case_id)), bool)
        start, stop = play_window(z, raw, keep)
        n = len(raw)
        none, nothing = np.zeros(n, bool), np.full(n, np.nan, np.float32)
        np.savez_compressed(out / f"{case_id}.npz", change=np.zeros(n, np.float32), fever=none, seen=nothing, middle=nothing, covered=none,
                            flat=nothing, white=nothing, window=np.array([start, stop]), usual=np.array([], np.float32))
        print(f"{case_id}: 플레이 구간 {start}~{stop} ({read_run} 의 판독으로 잡음) → {out / (case_id + '.npz')}")


def main():
    if len(sys.argv) > 3 and sys.argv[2] == "--masks-from":
        return compare(sys.argv[1], sys.argv[3])
    if len(sys.argv) > 4 and sys.argv[2] == "--window-only":
        return window_only(sys.argv[1], sys.argv[3], sys.argv[4:])
    run = sys.argv[1]
    pictures = Path(sys.argv[2]) if len(sys.argv) > 2 else None
    folder = ROOT / "data" / "full" / run
    out = folder / "fever"; out.mkdir(exist_ok=True)
    meta = {c["id"]: c for c in json.loads((ROOT / "data" / DATA_NAME / "cases.json").read_text(encoding="utf-8"))}
    table = sources()
    wanted = sys.argv[3:] or [p.stem for p in inputs(folder, "*.npz", "사례별 판독 결과") if "-right-" not in p.stem]
    reader, _ = load_model(run); reader.eval()
    rows = []
    for n, case_id in enumerate(wanted, 1):
        z = np.load(folder / f"{case_id}.npz")
        raw = np.where((z["conf"] >= READ_CONF) & ~z["blank"] & (z["values"] > 0), z["values"], -1)
        keep = np.array(verify_sequence([int(v) for v in raw], notes_of(case_id)), bool)
        if keep.sum() < 8:
            continue
        start, stop = play_window(z, raw, keep)
        log = out / f"{case_id}.log"
        parts = list(stream(case_id, table[case_id], log)); log.unlink()
        if len(parts) != len(raw):
            print(f"{case_id}: 프레임 수가 다름"); continue
        grays = np.stack([cv2.cvtColor(p, cv2.COLOR_BGR2GRAY) for p in parts])
        change, fever, still = find_fever(grays, start, stop)
        inside = np.zeros(len(raw), bool); inside[start:stop] = True
        # 연출로 잡히지 않은 못 읽은 프레임도 가림을 잰다 (2026-11-12 밤). 연출 찾기는 "평소 점수판과 달라진 구간"에 기대는데,
        # 그것이 놓친 가림이 "정상인데 못 읽음"에 많이 섞여 있었다. 못 읽은 프레임을 뽑아 직접 보니:
        #   So Happy: 캐릭터 머리가 점수판을 다 덮은 프레임(무대 조명 때문에 달라짐이 4초 넘게 이어져 연출에서 빠졌다),
        #   LOL: 흰 빛줄기가 앞 두 자리를 덮은 프레임, LEFT BEHIND: 삽화가 끝자리를 덮은 프레임,
        #   PIECE OF LIBERTY: 연출 앞뒤로 점수판에 숫자가 아예 안 떠 있는 프레임.
        # 반대로 GROOVE COASTER·BASEBALL 의 못 읽은 프레임은 숫자가 다 보였다(판독기 잘못). 이것들은 정상으로 남아야 한다.
        # 견줄 다른 실행(FEVER_ALSO=v8,v13 처럼 준다)이 못 읽은 프레임도 함께 잰다: 목록을 정한 실행에만 유리해지지 않도록.
        unread = inside & ~keep
        for other in filter(None, os.environ.get("FEVER_ALSO", "").split(",")):
            path = ROOT / "data" / "full" / other / f"{case_id}.npz"
            if path.exists():
                zo = np.load(path)
                ro = np.where((zo["conf"] >= READ_CONF) & ~zo["blank"] & (zo["values"] > 0), zo["values"], -1)
                ko = np.array(verify_sequence([int(v) for v in ro], notes_of(case_id)), bool)
                if len(ko) == len(keep):
                    unread |= inside & ~ko
        judge_visibility.flat = judge_visibility.white = None
        (seen, middle), info = judge_visibility(parts, grays, fever, keep, raw, start, stop, reader, unread)
        flat = judge_visibility.flat if judge_visibility.flat is not None else np.full(len(raw), np.nan, np.float32)
        white = judge_visibility.white if judge_visibility.white is not None else np.full(len(raw), np.nan, np.float32)
        judged = (fever | unread) & ~np.isnan(seen)
        # 가려짐은 두 가지다.
        #   (가) 한 자리가 유독 안 보인다: 가장 덜 보이는 자리가 다른 자리들(가운데 값)보다 뚝 떨어진다.
        #        EXTENSION 2 에서 줄무늬가 앞자리를 덮은 프레임은 다른 자리 0.93, 그 자리 0.59 였다.
        #   (나) 숫자 줄 전체가 안 보인다: 자리들의 가운데 값이 그 사례의 평소보다 크게 낮다.
        # 통째로 흐려진 프레임은 모든 자리가 비슷하게 조금씩 떨어지므로 (가)에 걸리지 않는다. 정상 프레임의 기준(흐릿한 것은 괜찮다)대로 정상이다.
        # 기준선은 그 사례의 평소 값에 맞춘다(글꼴마다 본이 얼마나 또렷한지가 다르다). 숫자는 그림을 보고 정한 출발값이다.
        usual_low, usual_mid = (info[0], info[4]) if info else (1.0, 1.0)
        covered = judged & (((middle - seen) >= ONE_DIGIT_DROP) | (middle < usual_mid * WHOLE_ROW_RATIO)
                            | (seen < (info[1] if info else 1.0) * LOWEST_RATIO)
                            | ((white >= WHITE_OUT) & (flat < WHITE_SPREAD)))
        # 본이 흐리게 떠진 사례(깨끗한 프레임조차 본과 잘 안 닮음)에서는 위의 판단을 믿을 수 없다. LIBERTY 2 가 그렇다: 글꼴이 기울고 번져
        # 평소 닮음의 가운데 값이 0.63 이고, 삽화가 다 덮은 프레임도 0.3 대라 갈리지 않는다.
        # 그런 사례는 "점수판이 평소와 크게 달라졌는가"로만 가른다. 숫자 한 자리만 가려진 프레임은 못 잡으므로 정상으로 세어진다.
        blurry = info is None or info[1] < BLURRY_TEMPLATES
        if blurry:
            covered = fever & (change >= HIGH)
        clear = judged & ~covered & (seen >= max(0.3, usual_low - VISIBLE_BELOW))
        np.savez_compressed(out / f"{case_id}.npz", change=change, fever=fever, seen=seen, middle=middle, covered=covered, flat=flat, white=white,
                            window=np.array([start, stop]), usual=np.array(info if info else [], np.float32))   # usual: 기준을 바꿔 다시 가를 때 쓴다
        unsure = fever & ~clear & ~covered                    # 애매하거나 잴 수 없었던 연출 프레임: 정상으로 센다
        # 2026-11-12 밤에 정한 기준: 연출 구간의 프레임은 분모에서 통째로 뺀다. 연출 밖에서 가려진 프레임(연출 앞뒤의 빈 점수판 등)도 뺀다.
        normal = normal_frames(case_id, inside, fever, covered)
        runs = episodes(fever)
        lengths = [(b - a) / VIDEO_FPS for a, b in runs]
        # 최종 점수가 떠 있는 동안(등록 구간) 연출이 걸렸는가
        final = np.where((z["values"] == meta[case_id]["expected"]) & (z["conf"] >= READ_CONF))[0]
        at_end = bool(len(final) and fever[final[0]:final[-1] + 1].any())
        row = {"사례": case_id, "기어": meta[case_id]["name"], "플레이 구간": int(inside.sum()), "견줄 자리 비율": f"{still:.2f}",
               "연출 횟수": len(runs), "연출 프레임": int(fever.sum()), "가장 긴 연출(초)": f"{max(lengths, default=0):.2f}",
               "연출 중 또렷": int((clear & fever).sum()), "연출 중 애매": int(unsure.sum()), "연출 중 가려짐(제외)": int((covered & fever).sum()),
               "연출 밖 가려짐(제외)": int((covered & ~fever).sum()),
               "정상 프레임": int(normal.sum()), "정상인데 못 읽음": int((normal & ~keep).sum()),
               "(참고) 연출 중 안 가려진 것까지 세면": int((inside & ~covered & ~keep).sum()),
               "연출 밖 못 읽음": int((inside & ~fever & ~keep).sum()), "또렷한데 못 읽음": int((clear & ~keep).sum()), "애매한데 못 읽음": int((unsure & ~keep).sum()),
               "가려졌는데 읽힘": int((covered & keep).sum()), "평소 닮음(아래 1%·가운데)": "" if info is None else f"{info[0]:.2f} {info[1]:.2f}",
               "끝에 연출이 걸림": "예" if at_end else "", "가른 방법": "" if not runs else ("달라짐만" if blurry else "숫자 본")}
        rows.append(row)
        if pictures is not None and runs:
            pictures.mkdir(parents=True, exist_ok=True)

            def strip(picks, tag):
                tiles = []
                for i in picks:
                    tile = cv2.resize(parts[i], (330, 100), interpolation=cv2.INTER_CUBIC)
                    head = np.zeros((38, 330, 3), np.uint8)
                    cv2.putText(head, f"{tag} #{i + 1} seen {seen[i]:.2f} change {change[i]:.0f}", (4, 15), cv2.FONT_HERSHEY_SIMPLEX, 0.42, (0, 255, 255), 1, cv2.LINE_AA)
                    cv2.putText(head, f"read '{z['texts'][i]}' {'ok' if keep[i] else 'FAIL'}", (4, 32), cv2.FONT_HERSHEY_SIMPLEX, 0.42, (255, 255, 255) if keep[i] else (80, 80, 255), 1, cv2.LINE_AA)
                    tiles.append(np.vstack([head, tile]))
                while len(tiles) < 4:
                    tiles.append(np.zeros((138, 330, 3), np.uint8))
                return np.hstack(tiles)

            def spread(mask, count=4):
                idx = np.where(mask)[0]
                return [int(idx[k]) for k in np.linspace(0, len(idx) - 1, min(count, len(idx))).round().astype(int)] if len(idx) else []

            edge = np.where(covered)[0]
            edge = [int(i) for i in edge[np.argsort(-seen[edge])][:4]]                 # 가려짐으로 본 것 가운데 닮음이 가장 높았던 것(경계)
            grid = np.vstack([strip(spread(clear & ~keep), "CLEAR"), strip(spread(unsure & ~keep), "UNSURE"), strip(edge, "COVERED"),
                              strip(spread(covered), "COVERED")])
            cv2.imencode(".png", grid)[1].tofile(str(pictures / f"{case_id}.png"))
        print(f"[{n}/{len(wanted)}] {case_id} {row['기어'][:12]:<12} 연출 {len(runs):>2}번 {row['연출 프레임']:>5}장(또렷 {row['연출 중 또렷']:>4} 애매 {row['연출 중 애매']:>4} 가려짐 {row['연출 중 가려짐(제외)']:>4}) "
              f"가장 긴 {row['가장 긴 연출(초)']}초 | 정상 {row['정상 프레임']:>5} 가운데 못 읽음 {row['정상인데 못 읽음']:>4} (연출 밖 {row['연출 밖 못 읽음']}, 또렷 {row['또렷한데 못 읽음']}, 애매 {row['애매한데 못 읽음']}) "
              f"| 가려졌는데 읽힘 {row['가려졌는데 읽힘']} | 평소 닮음 {row['평소 닮음(아래 1%·가운데)']} {row['가른 방법']} {'끝에 걸림' if at_end else ''}", flush=True)
    with open(folder / "fever.csv", "w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0])); w.writeheader(); w.writerows(rows)
    miss = [r["정상인데 못 읽음"] for r in rows]
    print(f"사례 {len(rows)}개: 정상 프레임을 모두 읽은 사례 {sum(m == 0 for m in miss)}개, 못 읽은 정상 프레임 합계 {sum(miss):,}장 (정상 프레임 {sum(r['정상 프레임'] for r in rows):,}장 가운데)")
    longest = max(float(r["가장 긴 연출(초)"]) for r in rows)
    print(f"가장 긴 연출 {longest:.2f}초, 끝에 연출이 걸린 사례 {sum(r['끝에 연출이 걸림'] == '예' for r in rows)}개")


if __name__ == "__main__":
    main()
