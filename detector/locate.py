"""SCORE 위치 탐색의 후보 찾기(밝기 변화 방식). 위치 찾기 도구(locate_run·locate_video)는 숫자 줄 탐지 모델(locate_model)로 후보를 내고, 이 파일에서는 overlap 만 가져다 쓴다.

탐색 원리
  1) 점수는 플레이하는 동안 계속 바뀌는 숫자 줄이다. 탐색 범위에서 "뚝뚝 끊겨 크게 바뀌는 작은 덩어리"를 후보로 잡는다.
     배경 영상은 넓게, 기어 장식은 부드럽게 바뀌므로 덩어리의 크기로 거르고, 겹치는 후보 가운데서는 후보 영역 주변의 밝기 변화가
     적은 쪽을 남긴다. 기어별 좌표나 기어 구분은 쓰지 않는다.
  2) 후보가 점수 줄인지는 판독기로 읽어 본 값이 점수 규칙대로 오르는지로 정한다 (콤보·배속·장식 숫자는 여기서 떨어진다).
  3) 고정할 영역은 숫자 줄 전체와 7자리까지의 여유를 담아야 한다.

이 파일은 1)의 후보 찾기까지다.
"""
from dataclasses import dataclass

import cv2
import numpy as np

T_SMALL = 8      # 이보다 작은 밝기 차이는 영상 압축 잡음으로 본다
T_BIG = 28       # 획이 생기거나 사라질 때의 큰 차이
LEVELS = (0.012, 0.05, 0.15)   # "자주 바뀜"의 기준: 프레임 쌍 가운데 이 비율 이상에서 크게 바뀐 화소
QUIET = 0.06     # 프레임 쌍 가운데 이 비율 이하에서만 밝기가 바뀐 화소는 "변화가 적은 화소"로 본다


@dataclass
class Candidate:
    x: int
    y: int
    w: int
    h: int
    level: float      # 어느 기준에서 잡혔는지
    quiet_ring: float  # 후보 영역 주변에서 밝기 변화가 적은 화소의 비율
    fill: float       # 후보 영역 안에서 자주 바뀐 화소의 비율

    @property
    def box(self):
        return self.x, self.y, self.x + self.w, self.y + self.h


def activity(frames: np.ndarray):
    """프레임 묶음(흑백) → 화소마다 크게 바뀐 횟수, 조금이라도 바뀐 횟수, 프레임 쌍의 수."""
    big = np.zeros(frames.shape[1:], np.int32); some = np.zeros(frames.shape[1:], np.int32)
    previous = frames[0].astype(np.int16)
    for frame in frames[1:]:
        current = frame.astype(np.int16)
        d = np.abs(current - previous)
        big += d > T_BIG; some += d > T_SMALL
        previous = current
    return big, some, len(frames) - 1


def propose(big: np.ndarray, some: np.ndarray, pairs: int) -> list[Candidate]:
    """자주 크게 바뀌는 작은 덩어리를 후보로 낸다. 숫자들이 한 덩어리가 되도록 가로로만 이어 붙인다."""
    quiet = some <= max(1, int(QUIET * pairs))
    integral = cv2.integral(quiet.astype(np.uint8))

    def quiet_sum(x0, y0, x1, y1):
        return int(integral[y1, x1] - integral[y0, x1] - integral[y1, x0] + integral[y0, x0])

    height, width = big.shape
    found: list[Candidate] = []
    for level in LEVELS:
        mask = (big >= max(2, int(level * pairs))).astype(np.uint8)
        mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_RECT, (13, 3)))
        count, _, stats, _ = cv2.connectedComponentsWithStats(mask, 8)
        for i in range(1, count):
            x, y, w, h, area = (int(v) for v in stats[i])
            if not (7 <= h <= 80 and 10 <= w <= 420 and w >= 0.8 * h):
                continue
            r = max(5, h // 2)
            x0, y0, x1, y1 = max(0, x - r), max(0, y - r), min(width, x + w + r), min(height, y + h + r)
            ring_area = (x1 - x0) * (y1 - y0) - w * h
            ring = (quiet_sum(x0, y0, x1, y1) - quiet_sum(x, y, x + w, y + h)) / max(1, ring_area)
            found.append(Candidate(x, y, w, h, level, ring, area / (w * h)))
    # 같은 자리에서 여러 기준으로 잡힌 것은 주변의 밝기 변화가 더 적은 쪽만 남긴다
    found.sort(key=lambda c: -c.quiet_ring)
    kept: list[Candidate] = []
    for c in found:
        if all(overlap(c.box, k.box) < 0.5 for k in kept):
            kept.append(c)
    return kept


def overlap(a, b) -> float:
    """두 사각형이 겹친 넓이 ÷ 작은 쪽 넓이."""
    w = min(a[2], b[2]) - max(a[0], b[0]); h = min(a[3], b[3]) - max(a[1], b[1])
    if w <= 0 or h <= 0:
        return 0.0
    return w * h / min((a[2] - a[0]) * (a[3] - a[1]), (b[2] - b[0]) * (b[3] - b[1]))
