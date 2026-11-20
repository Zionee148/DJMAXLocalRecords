"""전체 프레임 판독 요약 (정답 없이 볼 수 있는 것만).

독립 정답이 없는 사례는 일치율을 말할 수 없다. 여기서 내는 숫자는 판독이 점수 규칙과 얼마나 맞아떨어지는지이며,
"99% 통과"의 근거가 아니다. 어느 사례가 멀쩡해 보이고 어느 사례가 문제인지 가려내는 용도다.

플레이 구간 = 규칙을 통과한 첫 점수가 나온 프레임부터 등록 키를 누르기 직전 프레임까지.
  - 규칙 통과   : 그 프레임의 판독이 산식·줄지 않음·증가폭 검사를 통과
  - 규칙에 걸림 : 확신 있게 읽었는데 규칙에 어긋남 (오독일 가능성이 높다)
  - 미판독     : 확신 있게 읽지 못함
  - 앞뒤로 정해짐 : 같은 값이 규칙을 통과한 두 프레임 사이라서 그 값일 수밖에 없는 프레임. 이 프레임들에서
                  판독이 그 값과 같은 비율을 "추정 일치율"로 낸다 (정답이 있는 사례로 이 추정이 얼마나 맞는지 따로 확인한다).

실행: .venv\\Scripts\\python.exe -m detector.full_summary <실행 이름> [left-center]
출력: data\\full\\<실행 이름>\\summary.csv
"""
import csv
import json
import sys

import numpy as np

from .score_frames import READ_CONF
from .score_rules import verify_sequence
from . import inputs
from .selftrain import DATA_NAME, MIN_DISTINCT, ROOT, notes_of


def play_window(z, raw, keep) -> tuple[int, int]:
    """플레이 구간의 처음과 끝(프레임 위치). 규칙을 통과한 첫 점수부터 마지막 통과 프레임까지, 등록 키 이전.

    시작 전의 "0" 을 100 같은 값으로 잘못 읽은 프레임 하나가 규칙을 통과하면 구간이 0 이 떠 있는 동안까지 늘어난다.
    그래서 두 번째 값이 나오기 전에 "0" 으로 읽힌 프레임이 있으면 그 뒤부터를 구간으로 본다.
    """
    good = np.where(keep)[0]
    start = int(good[0])
    later = good[raw[good] != raw[start]]
    if len(later):
        zeros = np.where((z["values"][start:later[0]] == 0) & (z["conf"][start:later[0]] >= READ_CONF))[0]
        if len(zeros):
            start = start + int(zeros[-1]) + 1
    end = int(np.searchsorted(z["pts"], float(z["press"])))
    return start, max(start, min(end, int(good[-1]) + 1))


def summarize(case_id: str, z, expected: int) -> dict:
    values, conf, blank, pts = z["values"], z["conf"], z["blank"], z["pts"]
    press = float(z["press"])
    raw = np.where((conf >= READ_CONF) & ~blank & (values > 0), values, -1)
    keep = verify_sequence([int(v) for v in raw], notes_of(case_id))
    good = np.where(keep)[0]
    row = {"사례": case_id, "전체 프레임": len(raw), "플레이 구간": 0, "규칙 통과": 0, "규칙에 걸림": 0, "미판독": 0, "규칙 통과율": "",
           "앞뒤로 정해짐": 0, "그중 일치": 0, "그중 다르게 읽음": 0, "그중 미판독": 0, "추정 일치율": "", "값이 바뀌는 사이": 0, "끝 꼬리": 0,
           "최종 점수": "", "기대 점수": expected, "최종 판정": "보류"}
    distinct = sorted({int(raw[i]) for i in good})
    if len(distinct) < MIN_DISTINCT:
        return row
    start, stop = play_window(z, raw, keep)
    row["끝 꼬리"] = max(0, int(np.searchsorted(pts, press)) - stop)   # 마지막 통과 프레임부터 등록 키까지: 정답 없이는 어떤 화면인지 모른다
    window = slice(start, stop)
    n = window.stop - window.start
    in_keep, in_raw = keep[window], raw[window]
    row.update({"플레이 구간": n, "규칙 통과": int(in_keep.sum()), "규칙에 걸림": int(((in_raw > 0) & ~in_keep).sum()),
                "미판독": int((in_raw < 0).sum()), "규칙 통과율": f"{in_keep.mean():.4f}" if n else ""})
    implied = np.full(len(raw), -1)
    first, last = {}, {}
    for i in good:
        v = int(raw[i]); first.setdefault(v, int(i)); last[v] = int(i)
    for v in first:
        implied[first[v]:last[v] + 1] = v
    known = implied[window] > 0
    same = known & (in_raw == implied[window])
    row.update({"앞뒤로 정해짐": int(known.sum()), "그중 일치": int(same.sum()),
                "그중 다르게 읽음": int((known & (in_raw > 0) & ~same).sum()), "그중 미판독": int((known & (in_raw < 0)).sum()),
                "추정 일치율": f"{same.sum() / known.sum():.4f}" if known.any() else "", "값이 바뀌는 사이": int((~known).sum())})
    final = distinct[-1]
    after = sum(1 for i in good if raw[i] == final and pts[i] >= press - 1.0)
    if after >= 6:
        row["최종 점수"] = final
        row["최종 판정"] = "맞음" if final == expected else "틀림"
    return row


def main():
    run = sys.argv[1]
    folder = ROOT / "data" / "full" / run
    expected = {c["id"]: c["expected"] for c in json.loads((ROOT / "data" / DATA_NAME / "cases.json").read_text(encoding="utf-8"))}
    names = {c["id"]: c["name"] for c in json.loads((ROOT / "data" / DATA_NAME / "cases.json").read_text(encoding="utf-8"))}
    only = sys.argv[2] if len(sys.argv) > 2 else "all"           # left-center 를 주면 RIGHT 사례는 요약에 넣지 않는다
    paths = [p for p in inputs(folder, "*.npz", "사례별 판독 결과") if only != "left-center" or "-right-" not in p.stem]
    rows = [summarize(p.stem, np.load(p), expected[p.stem]) for p in paths]
    # 채점 기준(2026-11-11): 숫자가 완전히 가려진 프레임은 따로 세고 통과율의 분모(플레이 구간의 프레임 수)에서 뺀다. 분류는 classify_frames 가 남긴 것을 쓴다.
    for r in rows:
        r["완전히 가림(제외)"] = 0; r["제외 반영 통과율"] = r["규칙 통과율"]
        path = folder / "classes" / f"{r['사례']}.npz"
        if path.exists() and r["플레이 구간"]:
            c = np.load(path)
            hidden = int((c["kind"] == list(c["names"]).index("완전히 가림") + 1).sum())
            r["완전히 가림(제외)"] = hidden
            r["제외 반영 통과율"] = f"{r['규칙 통과'] / max(1, r['플레이 구간'] - hidden):.4f}"
    with open(folder / "summary.csv", "w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0])); w.writeheader(); w.writerows(rows)
    print(f"사례 {len(rows)}개, 프레임 {sum(r['전체 프레임'] for r in rows):,}장")
    verdict = {k: sum(r["최종 판정"] == k for r in rows) for k in ("맞음", "틀림", "보류")}
    print("최종 점수:", verdict)
    for label in ("규칙 통과율", "제외 반영 통과율", "추정 일치율"):
        rates = np.array([float(r[label]) for r in rows if r[label]])
        print(f"{label}: 99% 이상 {int((rates >= 0.99).sum())} / 95~99% {int(((rates >= 0.95) & (rates < 0.99)).sum())}"
              f" / 90~95% {int(((rates >= 0.90) & (rates < 0.95)).sum())} / 90% 미만 {int((rates < 0.90).sum())} / 낼 수 없음 {len(rows) - len(rates)}")
    tails = np.array([r["끝 꼬리"] for r in rows])
    print(f"끝 꼬리(마지막 통과 프레임부터 등록 키까지): 중앙값 {int(np.median(tails))}장, 60장 넘는 사례 {int((tails > 60).sum())}개")
    for label in ("규칙 통과율", "제외 반영 통과율"):
        gears: dict[str, list] = {}
        for r in rows:
            if r[label]:
                gears.setdefault(r["사례"].rsplit("-", 2)[0], []).append(float(r[label]))
        print(f"세 위치 모두 {label} 99% 이상인 기어:", sum(len(v) == 3 and min(v) >= 0.99 for v in gears.values()), "/ 90")
    worst = sorted((r for r in rows), key=lambda r: float(r["제외 반영 통과율"] or 0))[:62]
    print("제외 반영 통과율이 낮은 사례:")
    for r in worst:
        print(f"  {r['사례']:<20} {names[r['사례']][:18]:<18} 구간 {r['플레이 구간']:>5}  제외 {r['완전히 가림(제외)']:>4}  통과율 {r['규칙 통과율'] or '-':>6} → {r['제외 반영 통과율'] or '-':>6}"
              f"  걸림 {r['규칙에 걸림']:>4}  미판독 {r['미판독']:>4}  최종 {r['최종 판정']}")


if __name__ == "__main__":
    main()
