"""점수 규칙 검사.

DJMAX RESPECT V 의 점수는 노트마다 판정 100/90/.../10/1/0 % 가 더해진 누적 합 m 으로 정해진다.

    score = floor(m * 1,000,000 / (notes * 100))

이 모듈은 읽은 값이 이 규칙에 맞는지 "검사"만 한다. 값을 추정해서 만들어 내지 않는다.
"""
from __future__ import annotations

from bisect import bisect_right
from dataclasses import dataclass

import numpy as np

MAX_SCORE = 1_000_000


def lattice_m(score: int, notes: int) -> int | None:
    """score 가 규칙으로 만들어질 수 있으면 판정 누적 합 m(1% 단위), 아니면 None."""
    if score < 0 or score > MAX_SCORE:
        return None
    den = notes * 100
    m = (score * den + MAX_SCORE - 1) // MAX_SCORE
    return m if (m * MAX_SCORE) // den == score and m <= den else None


def on_lattice(score: int, notes: int) -> bool:
    return lattice_m(score, notes) is not None


def step_ok(dm: int) -> bool:
    """두 값 사이의 증가량(1% 단위)이 판정들의 합으로 설명되는가.

    10~100% 판정은 10 의 배수를, 1% 판정은 1 을 더한다. 한 구간에 1% 판정은 두 번까지만 인정한다.
    (1% 판정이 여러 번 나온 판이 실제로 있다: TAPSONIC TOP LEFT, G&K CENTER, Boom!)
    잘못 읽은 값이 끼어들면 들어갈 때와 나올 때 끝자리가 모두 맞아야 하므로 여전히 대부분 걸러진다.
    """
    return dm >= 1 and dm % 10 <= 2


@dataclass
class NotesFit:
    notes: int          # 맞춰진 노트 수
    rate: float         # 그 노트 수에서 규칙에 맞는 값의 비율
    chance: float       # 엉터리 값이 우연히 맞을 비율
    values: int         # 맞추는 데 쓴 서로 다른 값의 수


def fit_notes(values, lo: int = 50, hi: int = 4000, tolerance: float = 0.03,
              min_values: int = 15, min_rate: float = 0.7) -> NotesFit | None:
    """읽힌 값들만으로 그 판의 노트 수를 맞춘다. 근거가 부족하면 None.

    배수(예: 798 의 3배인 2394)도 똑같이 맞으므로, 최고 적중률에 가까운 것 중 가장 작은 수를 고른다.
    """
    v = np.unique(np.asarray([x for x in values if 0 < x <= MAX_SCORE], dtype=np.int64))
    if v.size < min_values:
        return None
    n = np.arange(lo, hi + 1, dtype=np.int64)
    den = n[:, None] * 100
    m = (v[None, :] * den + MAX_SCORE - 1) // MAX_SCORE
    rate = (((m * MAX_SCORE) // den) == v[None, :]).mean(axis=1)
    # 노트 수가 클수록 격자가 촘촘해져 틀린 값도 우연히 맞는다. 그 몫(우연 적중률)을 빼고 비교한다.
    pick = int(np.argmax(rate - np.minimum(1.0, n / 10000)))
    notes = int(n[pick])
    # 판정이 모두 10% 단위인 동안에는 누적 합이 항상 짝수라서 "노트 수의 절반"도 똑같이 맞는다.
    # 그 경우 절반 쪽에서는 누적 합의 끝자리가 5 인 값이 나오는데, 5% 판정은 없으므로 두 배가 맞다.
    half_m = (v * notes * 100 + MAX_SCORE - 1) // MAX_SCORE
    hit = ((half_m * MAX_SCORE) // (notes * 100)) == v
    fives = int(np.sum(hit & (half_m % 10 == 5)))
    if fives >= max(3, 0.10 * v.size) and notes * 2 <= hi:
        notes *= 2
    final_rate = float(rate[notes - lo])
    chance = min(1.0, notes / 10000)
    # 우연으로 설명되는 수준이면 믿지 않는다
    if final_rate < min_rate or final_rate < chance + 0.4:
        return None
    return NotesFit(notes, final_rate, chance, int(v.size))


def refine_notes(values, notes: int, scales=(1, 2, 5, 10), limit: int = 4000) -> int:
    """fit_notes 가 고른 노트 수가 진짜의 1/2, 1/5, 1/10 일 때 바로잡는다. values 는 시간 순서의 판독값(-1 = 못 읽음).

    판정이 모두 10% 단위이면 누적 합이 늘 10 의 배수라서 진짜 노트 수의 1/2, 1/5, 1/10 도 산식 격자에는 똑같이 맞는다
    (CHICKEN 545 → 109, 798 → 399). 그런데 그런 수로는 90% 판정 하나의 증가폭이 18·45 처럼 나와 증가폭 검사에 대부분 걸린다
    (통과 비율 12%쯤). 그럴 때만 배수로 올린다.
    배수는 클수록 검사가 느슨해지므로(격자가 촘촘해지고 증가폭이 모두 10 의 배수가 된다) 조금 더 잘 맞는다는 이유로는 올리지 않는다:
    지금 수로 통과하는 값이 가장 잘 맞는 배수의 6할이 안 될 때만, 9할 이상 맞는 가장 작은 배수를 고른다. limit 를 넘는 수는 보지 않는다.
    """
    values = list(values)
    kept = {k: int(verify_sequence(values, notes * k).sum()) for k in scales if k == 1 or notes * k <= limit}
    best = max(kept.values())
    if kept[1] >= 0.6 * best:
        return notes
    return notes * min(k for k in kept if kept[k] >= 0.9 * best)


def lnds(values) -> list[int]:
    """가장 긴 '줄지 않는' 부분열의 위치."""
    tails: list[int] = []
    tail_index: list[int] = []
    prev = [-1] * len(values)
    for i, x in enumerate(values):
        k = bisect_right(tails, x)
        prev[i] = tail_index[k - 1] if k else -1
        if k == len(tails):
            tails.append(x)
            tail_index.append(i)
        else:
            tails[k] = x
            tail_index[k] = i
    out = []
    k = tail_index[-1] if tail_index else -1
    while k >= 0:
        out.append(k)
        k = prev[k]
    return out[::-1]


BREAK_HEAL = 3     # 연속 판독값의 증가폭이 점수 규칙을 벗어난 경우: 한쪽에서 값을 이 개수까지 버려 증가폭이 규칙에 맞게 되면 그 값들을 오독으로 보고 버린다


def verify_sequence(values, notes: int, tolerant: bool = False) -> np.ndarray:
    """시간 순서로 늘어선 판독값(-1 = 못 읽음) 중 규칙을 모두 통과한 것을 True 로 표시한다.

    1) 산식 격자 위  2) 시간 순서대로 줄지 않음  3) 이웃 값과의 증가폭이 판정 합으로 설명됨
    통과하지 못한 값은 버릴 뿐, 다른 값으로 바꾸지 않는다.

    tolerant(등록 판단의 검산 근거에만 쓴다. 2026-11-14 확정): 연속 판독값의 증가폭이 점수 규칙을 벗어났고, 두 값 가운데 어느 하나를 빼도 규칙에 맞지 않을 때,
      - 한쪽에서 값을 BREAK_HEAL 개까지 버려 증가폭이 규칙에 맞게 되면 그 값들을 오독으로 보고 버리고,
      - 그래도 맞지 않으면 그 두 값 사이를 "끊긴 자리"로 받아들여 양쪽을 모두 둔다. 받아들이는 수에 상한은 두지 않는다
        (맥스 판정으로 플레이하면 1% 판정이 자주 나온다: 실제 플레이 경험, 2026-11-14. 상한을 두면 그런 판에서 다시 무너진다).
    까닭: 0.2초마다 뽑은 값 사이에는 1% 판정이 세 번 이상 몰릴 수 있다(Boom!). 그런 증가폭은 그 앞뒤 값의 끝자리를 계속 어긋나게 해서,
    tolerant 가 아니면 그 한 곳 때문에 곡의 절반이 버려진다(읽힌 프레임 수가 적은 쪽의 값을 하나씩 버리며 끝자리가 다시 맞을 때까지 간다).
    노트 수를 고를 때(refine_notes)와 프레임마다의 채점에는 쓰지 않는다: 거기서는 그 "크게 버려짐"이 노트 수가 어긋났다는 신호다.
    """
    values = list(values)
    keep = np.zeros(len(values), dtype=bool)
    idx = [i for i, x in enumerate(values) if x >= 0 and on_lattice(x, notes)]
    if not idx:
        return keep
    chain = [idx[k] for k in lnds([values[i] for i in idx])]
    support: dict[int, int] = {}
    for i in chain:
        support[values[i]] = support.get(values[i], 0) + 1
    dropped: set[int] = set()
    accepted: set[tuple[int, int]] = set()
    while True:
        vals = sorted(x for x in support if x not in dropped)
        ms = [lattice_m(x, notes) for x in vals]

        def pair_ok(a: int, b: int) -> bool:
            return step_ok(ms[b] - ms[a])

        bad = next((k for k in range(1, len(vals)) if not pair_ok(k - 1, k) and (vals[k - 1], vals[k]) not in accepted), None)
        if bad is None:
            break
        a, b = vals[bad - 1], vals[bad]
        ok_without_b = bad + 1 >= len(vals) or pair_ok(bad - 1, bad + 1)
        ok_without_a = bad - 2 < 0 or pair_ok(bad - 2, bad)
        if ok_without_b and not ok_without_a:
            dropped.add(b)
        elif ok_without_a and not ok_without_b:
            dropped.add(a)
        elif tolerant and not ok_without_a and not ok_without_b:
            right = next((j for j in range(2, BREAK_HEAL + 1) if bad + j >= len(vals) or pair_ok(bad - 1, bad + j)), None)
            left = next((j for j in range(2, BREAK_HEAL + 1) if bad - 1 - j < 0 or pair_ok(bad - 1 - j, bad)), None)
            if right is None and left is None:
                accepted.add((a, b))
            else:
                after = vals[bad:bad + right] if right is not None else None
                before = vals[max(0, bad - left):bad] if left is not None else None
                if after is not None and before is not None:      # 더 적게 버리는 쪽, 같으면 그 값들이 읽힌 프레임 수가 적은 쪽, 그것도 같으면 뒤쪽
                    cost_after = (len(after), sum(support[x] for x in after))
                    cost_before = (len(before), sum(support[x] for x in before))
                    dropped.update(after if cost_after <= cost_before else before)
                else:
                    dropped.update(after if after is not None else before)
        elif support[a] < support[b]:
            dropped.add(a)
        elif support[b] < support[a]:
            dropped.add(b)
        else:
            dropped.update((a, b))
    for i in chain:
        if values[i] not in dropped:
            keep[i] = True
    return keep
