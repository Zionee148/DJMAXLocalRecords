"""전체 프레임 채점: 프레임별 판독을 독립 정답과 대조한다 (통과 기준: 사례별 숫자 완전 일치율 99%).

정답은 이 판독 모델과 무관하게 만든 것만 쓴다.
  - truth\\cases\\<사례>\\output\\frames*.csv : Windows OCR + 점수 규칙 + 이웃 대조로 만든 프레임별 정답
  - 검사 설정의 reviewed-source-labels\\<사례>.json : 확대 이미지를 직접 판독한 검토 라벨 (프레임 일부. 결과에는 "Codex 검토 라벨"로 적힌다)
정답이 없는 프레임은 "정답 불명"으로 따로 세고, 맞았다고 치지 않는다.

프레임별 출력은 두 가지로 잰다.
  - 그대로 : 그 프레임 하나만 보고 읽은 값 (확신도 0.5 미만이면 미판독). 여러 프레임을 합쳐 틀린 값을 가리지 않는다.
  - 규칙 검사 : 점수 규칙(산식·줄지 않음·증가폭)을 통과하지 못한 값은 내지 않는다 (오독 → 미판독). 값을 바꾸거나 채우지는 않는다.

실행: .venv\\Scripts\\python.exe -m detector.score_frames <실행 이름>
출력: data\\full\\<실행 이름>\\score.csv, mismatches.csv
"""
import csv
import json
import sys

import numpy as np

from .build_dataset import VALIDATION
from .score_rules import on_lattice, verify_sequence
from . import inputs
from .selftrain import ROOT, notes_of

PASS = 0.99
READ_CONF = 0.5


def own_truth(case_id: str):
    """Windows OCR 과 점수 규칙으로 만든 정답 라벨(truth 폴더): 프레임 번호 → 값. 없으면 None."""
    short = case_id.removesuffix("-actual")
    for path in (ROOT / "truth" / "cases" / case_id / "output" / "frames-v2.csv",
                 ROOT / "truth" / f"pilot-{short}" / "output" / "frames-v2.csv",
                 ROOT / "truth" / "cases" / case_id / "output" / "frames.csv"):
        if path.exists():
            with open(path, encoding="utf-8-sig") as f:
                return {int(r["index"]): int(r["score"]) for r in csv.DictReader(f) if int(r["score"]) >= 0}, path.parent.parent.name + "\\" + path.name
    return None, ""


def codex_truth(case_id: str, pts: np.ndarray):
    """확대 이미지를 직접 판독한 검토 라벨. 라벨에 적힌 프레임 시각이 이 판독의 프레임 시각(pts)과 맞을 때만 쓴다."""
    path = VALIDATION / "reviewed-source-labels" / f"{case_id}.json"
    if not path.exists():
        return None, ""
    frames = json.loads(path.read_text(encoding="utf-8"))["frames"]
    truth = {}
    for key, item in frames.items():
        i = int(key)
        if i < 1 or i > len(pts) or abs(float(pts[i - 1]) - item["pts"]) > 0.002:
            return None, "Codex 검토 라벨(프레임 번호가 맞지 않아 쓰지 않음)"
        truth[i] = int(item["score"])
    return truth, "Codex 검토 라벨"


def outputs(z, notes: int):
    """프레임별 출력 두 가지. -1 = 미판독."""
    values, conf, blank = z["values"], z["conf"], z["blank"]
    raw = np.where((conf >= READ_CONF) & ~blank, values, -1)
    keep = verify_sequence([int(v) if v > 0 else -1 for v in raw], notes)
    checked = np.where(keep, raw, -1)
    first = int(np.argmax(keep)) if keep.any() else len(raw)
    zeros = (raw == 0) & (np.arange(len(raw)) < first)          # 첫 점수 이전의 0 은 규칙으로 볼 수 없어 그대로 둔다
    checked[zeros] = 0
    return raw, checked


def tally(output, truth: dict):
    match = wrong = unread = 0
    bad = []
    for i, want in truth.items():
        got = int(output[i - 1])
        if got == want:
            match += 1
        elif got < 0:
            unread += 1; bad.append((i, want, got))
        else:
            wrong += 1; bad.append((i, want, got))
    return match, wrong, unread, bad


def main():
    run = sys.argv[1]
    folder = ROOT / "data" / "full" / run
    rows, misses = [], []
    for path in inputs(folder, "*.npz", "사례별 판독 결과"):
        case_id = path.stem
        z = np.load(path)
        raw, checked = outputs(z, notes_of(case_id))
        for truth, source in (own_truth(case_id), codex_truth(case_id, z["pts"])):
            if truth is None:
                if source:
                    print(f"{case_id}: {source}")
                continue
            truth = {i: v for i, v in truth.items() if i <= len(raw)}
            # 산식으로 나올 수 없는 정답은 정답 쪽이 틀렸다는 신호다 (검토 라벨의 CYTUS CENTER 에서 16개 중 6개가 그랬다)
            suspect = sum(1 for v in truth.values() if v > 0 and not on_lattice(v, notes_of(case_id)))
            row = {"사례": case_id, "정답 출처": source, "전체 프레임": len(raw), "정답 확인": len(truth), "정답 불명": len(raw) - len(truth),
                   "산식에 안 맞는 정답": suspect}
            for label, output in (("그대로", raw), ("규칙 검사", checked)):
                match, wrong, unread, bad = tally(output, truth)
                rate = match / len(truth) if truth else 0.0
                row.update({f"{label} 일치": match, f"{label} 오독": wrong, f"{label} 미판독": unread,
                            f"{label} 일치율": f"{rate:.4f}", f"{label} 99%": "충족" if rate >= PASS else "미달"})
                misses += [(case_id, source, label, i, f"{z['pts'][i - 1]:.3f}", want, got, f"{z['conf'][i - 1]:.2f}", str(z["texts"][i - 1])) for i, want, got in bad]
            rows.append(row)
            print(f"{case_id} [{source}] 전체 {row['전체 프레임']} / 정답 확인 {row['정답 확인']} / 불명 {row['정답 불명']}"
                  + (f" / 산식에 안 맞는 정답 {suspect}" if suspect else ""))
            for label in ("그대로", "규칙 검사"):
                print(f"   {label:<6} 일치 {row[label + ' 일치']} / 오독 {row[label + ' 오독']} / 미판독 {row[label + ' 미판독']}"
                      f"  → 일치율 {float(row[label + ' 일치율']):.2%}  ({row[label + ' 99%']})")
    if not rows:
        raise SystemExit(f"정답과 견줄 수 있는 사례가 하나도 없다({folder.name}). 채점하지 않았다. 정답 라벨(truth)과 검사 설정(testdata)이 있어야 한다.")
    if rows:
        with open(folder / "score.csv", "w", newline="", encoding="utf-8-sig") as f:
            w = csv.DictWriter(f, fieldnames=list(rows[0])); w.writeheader(); w.writerows(rows)
        with open(folder / "mismatches.csv", "w", newline="", encoding="utf-8-sig") as f:
            w = csv.writer(f); w.writerow(["사례", "정답 출처", "방식", "프레임", "시각", "정답", "판독", "확신도", "읽힌 글자"]); w.writerows(misses)
    print(f"정답이 있는 사례 {len({r['사례'] for r in rows})}개 / 판독한 사례 {len(list(folder.glob('*.npz')))}개")


if __name__ == "__main__":
    main()
