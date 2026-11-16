"""저장된 모델로 전체 사례를 다시 채점하고 기어별 표를 만든다.

실행: .venv\\Scripts\\python.exe -m detector.report <실행 이름>
출력: data\\runs\\<이름>\\report.json, report-gears.csv, predictions.npz
"""
import csv
import json
import sys
from collections import Counter

import torch

from .selftrain import ROOT, evaluate, load, load_model, predict, save_predictions, summary


def main():
    run = sys.argv[1]
    out = ROOT / "data" / "runs" / run
    torch.set_num_threads(6)
    model, cfg = load_model(run)
    d = load(cfg["data"])
    values, conf, texts = predict(model, d.images)
    save_predictions(d, values, conf, texts, out)
    rows = evaluate(d, values, conf, {"LEFT", "CENTER", "RIGHT"})
    (out / "report.json").write_text(json.dumps(rows, ensure_ascii=False, indent=1), encoding="utf-8")
    for label, pos in (("LEFT·CENTER", {"LEFT", "CENTER"}), ("RIGHT(평가)", {"RIGHT"})):
        print(label, summary([r for r in rows if r["position"] in pos]), flush=True)
    for r in rows:
        if r["result"] == "wrong":
            print(f"  틀림 {r['id']} {r['name']}: 낸 값 {r['final']}, 정답 {r['expected']}")
    print("보류 이유:", dict(Counter(r["why"] for r in rows if r["result"] == "abstain")))
    gears: dict[str, dict] = {}
    for r in rows:
        g = gears.setdefault(r["gear"], {"gear": r["gear"], "name": r["name"]})
        g[r["position"]] = r["result"]; g[r["position"] + "_coverage"] = round(r["coverage"], 2); g[r["position"] + "_why"] = r["why"]
    with open(out / "report-gears.csv", "w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, fieldnames=["gear", "name", "LEFT", "CENTER", "RIGHT", "LEFT_coverage", "CENTER_coverage", "RIGHT_coverage",
                                          "LEFT_why", "CENTER_why", "RIGHT_why"])
        w.writeheader(); w.writerows(gears.values())
    full = Counter(sum(g.get(p) == "correct" for p in ("LEFT", "CENTER", "RIGHT")) for g in gears.values())
    print("기어 90종 중 세 위치 모두 최종 점수 맞음:", full[3], "/ 두 위치:", full[2], "/ 한 위치:", full[1], "/ 없음:", full[0])
    for g in gears.values():
        if sum(g.get(p) == "correct" for p in ("LEFT", "CENTER", "RIGHT")) < 3:
            print(f"  {g['gear']} {g['name']:<22} " + "  ".join(f"{p} {g.get(p, '-')}({g.get(p + '_coverage', '-')})" for p in ("LEFT", "CENTER", "RIGHT")))


if __name__ == "__main__":
    main()
