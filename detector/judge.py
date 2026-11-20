"""등록 요청의 판단(확정·보류·실패)을 SCORE 모듈 계약서 3절대로 흉내 낸다. 그림은 읽지 않고, 이미 읽어 둔 프레임별 판독(locate_video 의 npz)을 시간 순서대로 입력으로 전달한다.

모듈이 하는 일을 시간 순서대로 따라간다(각 시점에 그때까지 받은 프레임만으로 정한다).
  - 플레이 중: 고정 뒤의 화면을 낮은 빈도(PLAY_EVERY)로 읽어 둔다. 쓰는 곳은 노트 수 맞추기와 검산뿐이다(계약서 3-1).
  - 요청 뒤: 캡처 간격(CAPTURE)마다 온 화면만 최종 값의 근거로 쓴다(3-2). 기한은 요청부터 DEADLINE.
  - 채택 조건은 3-3 의 일곱 가지. 하나라도 어긋나면 값을 내지 않는다.

언제 답하는가(계약서의 결정 항목 D3 "곡이 끝나기 전에 등록 키를 누른 경우"와 얽힌 부분)는 두 방식을 모두 재 본다.
  - fast  : 요청 뒤 같은 값이 서로 다른 캡처 3장 이상에서, STABLE_FOR 초 이어져 읽히면 답한다. **모듈이 쓰는 방식이다**
            (2026-11-13 확정: 곡이 끝나기 전에 눌러도 되게 하는 기능은 넣지 않는다).
  - settle: (쓰지 않는다. 견줌용) 점수판이 화면에서 사라질 때까지(앱의 알림, 또는 모듈이 본 "더는 안 읽힘") 지켜보고, 사라지기 직전의 마지막 값을 답한다.
            곡이 끝나기 전에 눌러도 되게 하려는 것이다. 사라지기 전에는 그 값이 최종인지 알 수 없다(쉬는 구간에서도 0.3초쯤은 값이 멈춘다).

실행: .venv\\Scripts\\python.exe -m detector.judge <위치 탐색 실행 폴더(예: vid-d2-v17-r)> [사례 …]
출력: data\\full\\<폴더>\\judge.csv 와 요약
"""
import csv
import json
import sys
from dataclasses import dataclass, field

import numpy as np

from .build_dataset import VALIDATION
from .score_frames import READ_CONF
from .score_rules import fit_notes, lattice_m, refine_notes, verify_sequence
from . import inputs
from .selftrain import ROOT

PLAY_EVERY = 0.2          # 플레이 중 읽는 간격(초). 계약서 3-1: 1초에 2~5번
CAPTURE = 0.1             # 요청 뒤 캡처 간격(초). 앱의 설정값이며 보장값이 아니다
DEADLINE = 8.0            # 요청부터 기한(초)
NEED = 3                  # 같은 값으로 읽힌 서로 다른 캡처의 수 (3-3 의 1)
STABLE_FOR = 0.5          # 그 캡처들의 처음과 끝 사이가 이만큼(초)은 돼야 한다: 같은 값이 0.5초 이어져야 답한다.
                          # 녹화 270개 가운데 2개는 키가 최종 점수보다 0.23·0.40초 먼저 눌렸다(곡 끝에 맞춰 누르다 마지막 노트 직전에 눌림).
                          # 캡처 3장만 보면 캡처가 촘촘할 때(0.033초) 0.07초 만에 답해 최종 직전 값을 냈다.
                          # 0.5초면 최종보다 0.5초 일찍 눌러도 270개 모두 맞고(캡처 0.033~0.2초), 0.7초부터는 최종 점수가 그만큼 떠 있지 않은 2~3개를 못 낸다
SHORT_FOR = 0.3           # 사라지면 직전 값(2026-11-14 확정): 0.5초를 채우기 전에 점수가 사라졌을 때, 그 직전에 같은 값이 이만큼(초)은 이어졌어야 한다
NEED_SHORT = 3            # 그 사이의 서로 다른 캡처 수
GONE_FOR = 2.0            # 점수로 볼 값이 이만큼(초) 내리 안 보이면 사라진 것으로 본다. 1.0 이면 곡 중간에 눌렀을 때 연출에 가려진 것을 사라진 것으로 보는 일이
                          # 19,376번 중 20번 있었고 2.0 이면 없었다(docs\기록\앱연결.md). 그래서 2.0 으로 확정했다(2026-11-14). SHORT_FOR 0.3 도 그때 확정한 값이다
MIN_PLAY_VALUES = 8       # 플레이 중 규칙을 통과한 서로 다른 값 (3-3 의 5)
MIN_AGREE = 0.6           # 또렷이 읽힌 값 가운데 규칙을 통과한 비율 (3-3 의 7)
MAX_NOTES_PER_SECOND = 50 # 3-3 의 4: 마지막 플레이 값에서 그 사이 시간에 오를 수 있는 한도 (넉넉하게 잡은 값)
GONE = 4.0                # settle: 점수가 이만큼(초) 내리 안 보이면 점수판이 사라진 것으로 본다. 피버 연출(길어야 2~4초)보다 길어야 한다.
                          # 1.0 으로 뒀을 때는 일찍 누른 뒤 연출에 가려진 것을 "사라짐"으로 보고 곡 중간 값을 낸 일이 있었다(270개 중 3초 일찍 2건, 6초 일찍 8건)
LEFT_LAG = 0.3            # 앱이 화면이 바뀐 것을 알리기까지 걸린다고 친 시간(초). 흉내일 뿐 잰 값이 아니다


@dataclass
class Result:
    state: str = "Failed"               # Accepted / Failed
    reason: str = ""
    score: int | None = None
    candidate: int | None = None
    elapsed: float = 0.0                # 요청부터 답까지(초)
    evidence: dict = field(default_factory=dict)
    basis: str = ""                     # 값을 낸 근거: Stable(같은 값이 STABLE_FOR 이어짐) / Gone(그 전에 점수가 사라져 직전 값을 냄)


def play_evidence(at, values, clear, locked, pressed_at: float):
    """요청 전까지 낮은 빈도로 읽어 둔 값에서: 노트 수, 규칙을 통과한 서로 다른 값의 수, 통과 비율, 마지막 통과 값과 시각."""
    picks, last = [], -1e9
    for i in np.flatnonzero(locked & (at < pressed_at)):
        if at[i] - last >= PLAY_EVERY - 1e-6:
            picks.append(i); last = at[i]
    picks = np.array(picks, int)
    read = [int(values[i]) if clear[i] and values[i] > 0 else -1 for i in picks]
    fit = fit_notes([v for v in read if v > 0])
    out = {"PlayReads": int(sum(v > 0 for v in read)), "NoteCount": 0, "PlayValuesVerified": 0, "Agree": 0.0, "LastPlayValue": None, "LastPlayAt": None}
    if fit is None:
        return out
    notes = refine_notes(read, fit.notes)
    keep = verify_sequence(read, notes, tolerant=True)     # 증가폭이 점수 규칙을 벗어난 곳이 드물게 하나 있어도 나머지 값을 버리지 않는다 (score_rules 의 설명)
    good = np.flatnonzero(keep)
    out.update({"NoteCount": notes, "PlayValuesVerified": len({read[i] for i in good}), "Agree": float(keep.sum() / max(1, out["PlayReads"]))})
    if len(good):
        out.update({"LastPlayValue": read[good[-1]], "LastPlayAt": float(at[picks[good[-1]]])})
    return out


def play_reason(play: dict) -> str:
    """요청 하나에 걸리는 조건(3-3 의 5·7, 3-4). 통과하면 빈 문자열."""
    if play["NoteCount"] == 0 or play["PlayValuesVerified"] < MIN_PLAY_VALUES:
        return "NotEnoughPlayEvidence"
    if play["Agree"] < MIN_AGREE:
        return "RuleConflict"
    return ""


def value_ok(value: int, when: float, play: dict) -> bool:
    """3-3 의 3·4·6: 0 이 아니고, 산식으로 나올 수 있고, 마지막 플레이 값보다 작지 않으며, 그 사이 시간에 오를 수 있는 만큼만 올랐다."""
    if value <= 0 or play["NoteCount"] == 0 or play["LastPlayValue"] is None:
        return False
    m = lattice_m(value, play["NoteCount"])
    if m is None or value < play["LastPlayValue"]:
        return False
    rise = m - lattice_m(play["LastPlayValue"], play["NoteCount"])
    return rise <= 100 * (MAX_NOTES_PER_SECOND * max(0.0, when - play["LastPlayAt"]) + 2)


def judge(at, values, clear, locked, pressed_at: float, mode: str, left_at: float | None = None, capture: float = CAPTURE,
          stable_for: float | None = None, short_for: float | None = None, gone_for: float | None = None) -> Result:
    """요청 하나를 판단한다. at: 프레임 시각(초), values: 판독값, clear: 또렷이 읽혔는가(값 0 포함), locked: 위치가 고정된 뒤인가.

    요청 뒤의 화면에서 "점수로 볼 수 있는 값"(value_ok)이 서로 다른 캡처 NEED 장에서 내리 읽히면 그 값이 후보가 된다.
    점수로 볼 수 없는 판독(0, 산식에 없는 값, 줄어든 값)은 후보가 되지 못하고, 같은 값이 연속으로 읽힌 캡처 수를 처음부터 다시 세게 한다.
    RIGHT 에서는 결과 화면의 같은 자리에 다른 숫자가 떠서 또렷이 읽힌다. 그것이 후보를 밀어내지 못하게 하려는 것이다.

    사라지면 직전 값(바로 답하기에서만): 같은 값이 STABLE_FOR 를 채우기 전에 점수가 사라지면, 그 값이 SHORT_FOR 이상(캡처 NEED_SHORT 장 이상)
    이어졌고 그 뒤로 점수로 볼 수 있는 값이 다시 나오지 않은 채 GONE_FOR 가 지났을 때 그 값을 낸다. 다른 점수로 바뀌면 이 예외 조건을 적용하지 않는다.
    short_for 에 0 을 주면 이 규칙을 끈다.
    """
    stable_for = STABLE_FOR if stable_for is None else stable_for
    short_for = SHORT_FOR if short_for is None else short_for
    gone_for = GONE_FOR if gone_for is None else gone_for
    play = play_evidence(at, values, clear, locked, pressed_at)
    res = Result(evidence=dict(play))
    blocked = play_reason(play)
    frames, t = [], pressed_at                                  # 요청 뒤 캡처: pressed_at 이후, capture 간격
    while t <= pressed_at + DEADLINE + 1e-6:
        i = int(np.searchsorted(at, t - 1e-6))
        if i >= len(at):
            break
        if not frames or frames[-1] != i:
            frames.append(i)
        t += capture
    run_value, run, stable, stable_at = None, [], None, None    # 지금 이어지는 같은 값, 그 캡처들, 가장 최근에 NEED 장을 채운 값
    changed = False                                             # 요청 뒤 후보가 바뀐 적이 있는가 (곡이 아직 진행 중이었다)
    no_score_since = None                                       # 점수로 볼 값이 안 보이기 시작한 시각
    last = None                                                 # 가장 최근에 이어졌던 점수로 볼 수 있는 같은 값: (값, 처음 시각, 마지막 시각, 캡처 수)
    seen = unread = zeros = others = 0
    seen_clear = False
    basis = "Stable"

    def finish(when: float, why_not: str = "") -> Result:
        res.elapsed = when - pressed_at
        res.evidence.update({"FramesAfterRequest": seen, "FramesUnread": unread})
        res.candidate = stable
        if not frames or not locked[frames[0]]:
            res.reason = "LocationUncertain"
        elif stable is None:
            res.reason = (blocked or why_not or ("NoFrames" if seen == 0 else "Occluded" if not seen_clear else "ZeroScore" if zeros and not others
                                      else "NotEnoughFrames" if seen < NEED else "AmbiguousDigits"))
        elif blocked or why_not:
            res.reason = blocked or why_not
        else:
            res.state, res.score, res.basis = "Accepted", stable, basis
        return res

    for i in frames:
        now = float(at[i])
        if left_at is not None and now >= left_at:              # 앱이 "화면이 바뀌었다"고 알렸다: 그 뒤의 화면은 쓰지 않는다
            return finish(now)
        seen += 1
        v = int(values[i]) if locked[i] and clear[i] else -1
        if v < 0:
            unread += 1
        else:
            seen_clear = True
            zeros += v == 0
        if v < 0 or not value_ok(v, now, play):
            others += v > 0
            if v >= 0:
                run_value, run = None, []                       # 다른 것으로 읽힌 화면이 끼었다 (3-3 의 1)
            no_score_since = now if no_score_since is None else no_score_since
            if mode == "settle" and stable is not None and now - no_score_since >= GONE:
                return finish(now)                              # 점수판이 사라졌다
            if (mode == "fast" and short_for > 0 and last is not None and last[3] >= NEED_SHORT and last[2] - last[1] >= short_for - 1e-6
                    and now - no_score_since >= gone_for - 1e-6):
                stable, stable_at, basis = last[0], now, "Gone"     # 0.5초를 채우기 전에 점수가 사라졌다: 직전 값을 낸다
                return finish(now)
            continue
        no_score_since = None
        if v == run_value:
            run.append(i)
        else:
            run_value, run = v, [i]
        last = (run_value, float(at[run[0]]), now, len(run))
        if len(run) >= NEED and now - float(at[run[0]]) >= stable_for - 1e-6 and stable != run_value:
            changed = changed or stable is not None
            stable, stable_at = run_value, now
        if mode == "fast" and stable is not None:
            return finish(stable_at)
    end = pressed_at + DEADLINE                                 # 기한
    if mode == "settle" and changed:
        return finish(end, "NotStable")                         # 요청 뒤 점수가 바뀌었는데 기한까지 점수판이 사라진 것을 못 봤다
    return finish(end, "" if stable is not None else "Timeout" if seen_clear and not (zeros and not others) else "")


def load(folder, case_id: str):
    z = np.load(folder / f"{case_id}.npz")
    at = z["pts"].astype(np.float64)
    n = len(at)
    values, conf, blank, locked = z["values"][:n], z["conf"][:n], z["blank"][:n], z["locked"][:n].astype(bool)
    clear = (conf >= READ_CONF) & ~blank & (values >= 0)
    return at, values, clear, locked, float(z["press"])


def main():
    folder = ROOT / "data" / "full" / sys.argv[1]
    expected = {c["id"]: c["registration"]["expected"] for c in json.loads((VALIDATION / "candidate-v4" / "all270.json").read_text(encoding="utf-8"))}
    wanted = sys.argv[2:] or [p.stem for p in inputs(folder, "*.npz", "사례별 판독 결과")]
    rows = []
    for case_id in wanted:
        at, values, clear, locked, press = load(folder, case_id)
        want = expected[case_id]
        hits = np.flatnonzero(clear & (values == want))
        final_first = float(at[hits[0]]) if len(hits) else None           # 최종 점수가 처음 읽힌 시각 (흉내 낼 "일찍 누름"의 기준)
        # 점수판이 사라진 뒤 앱이 알리는 시각(흉내): 최종 점수가 마지막으로 읽힌 프레임 + LEFT_LAG. 앱의 알림 시각만 정답을 빌려 정한다
        left_at = float(at[hits[-1]]) + 1 / 60 + LEFT_LAG if len(hits) else None
        row = {"사례": case_id, "기대 점수": want, "키 누른 시각": f"{press:.2f}", "최종 점수가 처음 읽힌 시각": "" if final_first is None else f"{final_first:.2f}"}

        def put(name: str, r: Result):
            verdict = ("맞음" if r.score == want else "틀림") if r.state == "Accepted" else f"안 냄({r.reason})"
            row.update({f"{name} 판정": verdict, f"{name} 값": "" if r.score is None else r.score, f"{name} 걸린 시간": f"{r.elapsed:.2f}"})
            return r

        r = put("바로 답하기", judge(at, values, clear, locked, press, "fast"))
        row.update({"노트 수": r.evidence["NoteCount"], "플레이 중 통과한 값": r.evidence["PlayValuesVerified"], "통과 비율": f"{r.evidence['Agree']:.3f}"})
        put("사라질 때까지(앱 알림)", judge(at, values, clear, locked, press, "settle", left_at))
        put("사라질 때까지(알림 없음)", judge(at, values, clear, locked, press, "settle", None))
        if final_first is not None:
            for early in (1.0, 3.0, 6.0):                                  # 최종 점수가 뜨기 전에 누른 경우 (곡이 끝나기 직전에 누름)
                p = final_first - early
                put(f"{early:.0f}초 일찍·바로", judge(at, values, clear, locked, p, "fast"))
                put(f"{early:.0f}초 일찍·사라질 때까지", judge(at, values, clear, locked, p, "settle", left_at))
                put(f"{early:.0f}초 일찍·알림 없음", judge(at, values, clear, locked, p, "settle", None))
        rows.append(row)
    fields = list(dict.fromkeys(k for r in rows for k in r))
    with open(folder / "judge.csv", "w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, fieldnames=fields); w.writeheader(); w.writerows(rows)
    print(f"사례 {len(rows)}개 ({sys.argv[1]}), 요청 뒤 캡처 간격 {CAPTURE}초, 플레이 중 읽기 {PLAY_EVERY}초마다")
    for position in ("left", "center", "right"):
        part = [r for r in rows if f"-{position}-" in r["사례"]]
        if not part:
            continue
        print(f"[{position.upper()}] {len(part)}개")
        for name in [k[:-3] for k in fields if k.endswith(" 판정")]:
            got = [r.get(f"{name} 판정", "") for r in part]
            right, wrong = sum(g == "맞음" for g in got), sum(g == "틀림" for g in got)
            reasons = {}
            for g in got:
                if g.startswith("안 냄"):
                    reasons[g[4:-1]] = reasons.get(g[4:-1], 0) + 1
            times = sorted(float(r[f"{name} 걸린 시간"]) for r in part if r.get(f"{name} 판정") == "맞음")
            took = f", 걸린 시간 가운데 {times[len(times) // 2]:.2f}초·가장 긴 {times[-1]:.2f}초" if times else ""
            print(f"   {name}: 맞음 {right} / 틀림 {wrong} / 안 냄 {len(part) - right - wrong} {reasons if reasons else ''}{took}")


if __name__ == "__main__":
    main()
