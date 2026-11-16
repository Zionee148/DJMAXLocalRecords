"""직접 읽은 씨앗 라벨(truth\\seed-labels\\*.csv)을 점수 규칙으로 검사한다.

읽은 사람이 틀릴 수 있으므로, 산식 격자·줄지 않음·증가폭을 통과한 값만 학습에 쓴다.
실행: .venv\\Scripts\\python.exe -m detector.check_seeds
"""
import csv
from collections import defaultdict

from .score_rules import lattice_m, verify_sequence
from .selftrain import ROOT, notes_of

SEEDS = ROOT / "truth" / "seed-labels"


def read_seeds() -> dict[str, list[tuple[str, int]]]:
    """사례별 (파일 이름, 값) 목록. 파일 이름 순서 = 시간 순서."""
    by_case = defaultdict(list)
    files = sorted(SEEDS.glob("*.csv"))
    if not files:
        # 라벨이 없는데 "검사할 것이 없음"으로 조용히 끝나면 통과한 것처럼 보인다
        raise SystemExit(f"씨앗 라벨이 없다: {SEEDS}\\*.csv\n직접 읽은 라벨은 저장소에 들어 있지 않다. 없으면 검사도 학습도 하지 않는다.")
    for path in files:
        with open(path, encoding="utf-8-sig") as f:
            for r in csv.DictReader(f):
                by_case[r["id"]].append((r["file"], int(r["value"])))
    return {k: sorted(v) for k, v in by_case.items()}


def verified_seeds() -> dict[tuple[str, str], int]:
    """규칙을 통과한 씨앗만: (사례, 파일 이름) → 값. 0 은 규칙으로 검사할 수 없어 그대로 받는다."""
    out = {}
    for case_id, rows in read_seeds().items():
        values = [v for _, v in rows]
        keep = verify_sequence([v if v > 0 else -1 for v in values], notes_of(case_id))
        for (name, v), ok in zip(rows, keep):
            if ok or v == 0:
                out[(case_id, name)] = v
    return out


def main():
    good = verified_seeds()
    for case_id, rows in read_seeds().items():
        notes = notes_of(case_id)
        bad = [f"{name}={v}{'(격자 밖)' if lattice_m(v, notes) is None else '(이어지지 않음)'}" for name, v in rows if (case_id, name) not in good]
        print(f"{case_id}: 읽은 값 {len(rows)}개, 통과 {len(rows) - len(bad)}개" + (", 탈락 " + ", ".join(bad) if bad else ""))


if __name__ == "__main__":
    main()
