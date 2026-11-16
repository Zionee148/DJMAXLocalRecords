"""위치 탐색이 찾은 자리로 읽은 전체 프레임 결과를, Codex 영역으로 읽은 결과와 사례별로 견준다 (LEFT·CENTER).

독립 정답이 없으므로 여기서도 일치율은 말할 수 없다. 보는 것은 세 가지다.
  - 규칙 통과율: 찾은 자리로 읽은 값이 점수 규칙을 통과한 프레임의 비율. "고정 뒤"와 "고정 전까지 포함"을 따로 낸다.
    플레이 구간은 Codex 영역으로 읽은 쪽의 것을 쓴다(규칙을 통과한 첫 점수 ~ 등록 키 직전). 고정 전 프레임은 읽지 않은 것으로 센다.
  - 같은 프레임을 두 자리에서 읽은 값이 같은가: Codex 영역 쪽이 규칙 통과로 읽은 프레임을 찾은 자리 쪽이 다르게 읽은 수.
  - 최종 점수 (full_summary 와 같은 판단).

실행: .venv\\Scripts\\python.exe -m detector.locate_compare <Codex 영역으로 읽은 실행> <찾은 자리로 읽은 실행>
출력: data\\full\\<찾은 자리 실행>\\compare.csv
"""
import csv
import json
import sys

import numpy as np

from .full_summary import play_window, summarize
from .score_frames import READ_CONF
from .score_rules import verify_sequence
from . import inputs
from .selftrain import DATA_NAME, ROOT, notes_of


def reads(z, case_id):
    raw = np.where((z["conf"] >= READ_CONF) & ~z["blank"] & (z["values"] > 0), z["values"], -1)
    return raw, np.array(verify_sequence([int(v) for v in raw], notes_of(case_id)), bool)


def main():
    ref_run, loc_run = sys.argv[1], sys.argv[2]
    meta = {c["id"]: c for c in json.loads((ROOT / "data" / DATA_NAME / "cases.json").read_text(encoding="utf-8"))}
    ref_dir, loc_dir = ROOT / "data" / "full" / ref_run, ROOT / "data" / "full" / loc_run
    rows = []
    for path in inputs(ref_dir, "*.npz", "사례별 판독 결과"):
        case_id = path.stem
        if "-right-" in case_id:
            continue
        a = np.load(path)
        ra, ka = reads(a, case_id)
        row = {"사례": case_id, "기어": meta[case_id]["name"], "고정": "아니오", "플레이 구간": "", "고정 전 프레임": "", "Codex 영역 통과율": "",
               "고정 뒤 통과율": "", "고정 전 포함 통과율": "", "Codex 쪽 통과 프레임": "", "그중 다르게 읽음": "", "그중 못 읽음(고정 뒤)": "", "최종 판정": "고정 못 함"}
        if ka.sum() >= 8:
            start, stop = play_window(a, ra, ka)
            row["플레이 구간"] = stop - start
            row["Codex 영역 통과율"] = f"{ka[start:stop].mean():.4f}"
        if (loc_dir / path.name).exists() and ka.sum() >= 8:
            b = np.load(loc_dir / path.name)
            n = min(len(ra), len(b["values"]))
            rb, kb = reads(b, case_id)
            locked = b["locked"][:n]
            window = np.zeros(n, bool); window[start:min(stop, n)] = True
            after = window & locked
            row.update({"고정": "예", "고정 전 프레임": int((window & ~locked).sum()),
                        "고정 뒤 통과율": f"{kb[:n][after].mean():.4f}" if after.any() else "",
                        "고정 전 포함 통과율": f"{kb[:n][after].sum() / max(1, window.sum()):.4f}",
                        "Codex 쪽 통과 프레임": int((ka[:n] & after).sum()),
                        "그중 다르게 읽음": int((ka[:n] & after & (rb[:n] > 0) & (rb[:n] != ra[:n])).sum()),
                        "그중 못 읽음(고정 뒤)": int((ka[:n] & after & (rb[:n] < 0)).sum()),
                        "최종 판정": summarize(case_id, b, meta[case_id]["expected"])["최종 판정"]})
        rows.append(row)
    with open(loc_dir / "compare.csv", "w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0])); w.writeheader(); w.writerows(rows)
    found = [r for r in rows if r["고정"] == "예"]
    print(f"사례 {len(rows)}개 (3-05 CENTER 는 견줄 판독이 없어 빠짐), 고정 {len(found)}개")
    print("최종 점수:", {k: sum(r["최종 판정"] == k for r in rows) for k in ("맞음", "틀림", "보류", "고정 못 함")})

    def bins(key, pool):
        v = np.array([float(r[key]) for r in pool if r[key] != ""])
        return f"99% 이상 {int((v >= .99).sum())} / 95~99% {int(((v >= .95) & (v < .99)).sum())} / 90~95% {int(((v >= .90) & (v < .95)).sum())} / 90% 미만 {int((v < .90).sum())}"

    print("규칙 통과율 (일치율이 아니다)")
    print("  Codex 영역으로 읽음   :", bins("Codex 영역 통과율", rows))
    print("  찾은 자리, 고정 뒤     :", bins("고정 뒤 통과율", found))
    print("  찾은 자리, 고정 전 포함:", bins("고정 전 포함 통과율", found))
    before = np.array([r["고정 전 프레임"] for r in found])
    print(f"고정 전 플레이 프레임: 중앙값 {int(np.median(before))}장, 가장 많은 사례 {int(before.max())}장")
    total = sum(r["Codex 쪽 통과 프레임"] for r in found)
    print(f"Codex 영역 쪽이 규칙 통과로 읽은 고정 뒤 프레임 {total:,}장 가운데, 찾은 자리 쪽이 다르게 읽음 {sum(r['그중 다르게 읽음'] for r in found):,}장, "
          f"못 읽음 {sum(r['그중 못 읽음(고정 뒤)'] for r in found):,}장")
    worse = [r for r in found if r["Codex 영역 통과율"] and float(r["Codex 영역 통과율"]) >= .99 and float(r["고정 뒤 통과율"] or 0) < .99]
    print("Codex 영역으로는 99% 이상인데 찾은 자리(고정 뒤)로는 못 미치는 사례:", len(worse))
    for r in sorted(worse, key=lambda r: float(r["고정 뒤 통과율"] or 0))[:25]:
        print(f"  {r['사례']:<22} {r['기어'][:18]:<18} {r['Codex 영역 통과율']} → {r['고정 뒤 통과율']}  다르게 {r['그중 다르게 읽음']}, 못 읽음 {r['그중 못 읽음(고정 뒤)']}")
    better = [r for r in found if r["Codex 영역 통과율"] and float(r["Codex 영역 통과율"]) < .99 and float(r["고정 뒤 통과율"] or 0) >= .99]
    print("거꾸로(Codex 영역으로는 못 미치는데 찾은 자리로는 99% 이상):", [f"{r['사례']} {r['기어']}" for r in better])


if __name__ == "__main__":
    main()
