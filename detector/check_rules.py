"""점수 규칙 모듈 시험: OCR 조사 결과만으로 사례마다 노트 수를 맞출 수 있는지 본다.

실제 게임에서는 채보별 노트 수를 미리 알 수 없으므로, 한 판에서 읽힌 값만으로 맞춰지는지가 중요하다.
실행: .venv\\Scripts\\python.exe -m detector.check_rules
"""
import csv
from collections import Counter, defaultdict
from pathlib import Path

from .score_rules import fit_notes, on_lattice, verify_sequence

ROOT = Path(__file__).resolve().parents[1]
EXPECTED = {"5-04": 545, "5-05": 1087}   # 다른 곡 (OCR 값으로 맞춘 추정)


def consensus(cells):
    votes = Counter(int(c) for c in cells if int(c) >= 0)
    if not votes:
        return -1
    value, count = votes.most_common(1)[0]
    return value if count >= 2 else -1


def main():
    rows = defaultdict(list)
    with open(ROOT / "truth" / "survey" / "ocr-survey.csv", encoding="utf-8-sig") as f:
        for r in csv.DictReader(f):
            if r["file"] != "(no-area)":
                rows[r["id"]].append(r)
    fitted = wrong = none = 0
    examples = []
    for case, items in sorted(rows.items()):
        gear = case.rsplit("-", 2)[0]
        truth = EXPECTED.get(gear, 798)
        best = None
        for family in "us":
            values = [consensus([r[f"{family}{k}"] for k in (1, 2, 3)]) for r in items]
            good = sum(v >= 0 and on_lattice(v, truth) for v in values)
            if best is None or good > best[0]:
                best = (good, values)
        values = best[1]
        fit = fit_notes(values, min_values=8)
        if fit is None:
            none += 1
        elif fit.notes == truth:
            fitted += 1
        else:
            wrong += 1
            examples.append(f"  {case}: {fit.notes} 로 맞춤 (실제 {truth}), 값 {fit.values}개, 적중 {fit.rate:.0%}")
    total = len(rows)
    print(f"사례 {total}개, 표본 20프레임의 OCR 값만으로 노트 수 맞추기")
    print(f"  맞게 맞춤 {fitted} / 틀리게 맞춤 {wrong} / 근거 부족으로 맞추지 않음 {none}")
    for line in examples[:10]:
        print(line)

    # 검사 함수가 틀린 값을 걸러내는지: 실제 예 (640877 -> 640977 은 OCR 오독)
    seq = [639624, 639624, 640877, 640977, -1, 643383, 643383]
    print("예시 판독열:", seq)
    print("규칙 통과   :", verify_sequence(seq, 798).tolist())


if __name__ == "__main__":
    main()
