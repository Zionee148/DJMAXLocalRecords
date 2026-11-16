"""등록 판단(judge.py)을 .NET 으로 옮긴 것이 같은 답을 내는지 확인할 자료를 만든다.

사례마다 프레임별 판독(시각, 값, 또렷이 읽혔는가, 고정 뒤인가)을 그대로 내보내고, 여러 상황(실제 키 시각, 키를 일찍·늦게 누름,
앱을 늦게 켬, 캡처 간격을 바꿈)마다 Python 이 낸 답을 적는다. .NET 쪽(dotnet\\ModuleCheck)이 같은 판독을 같은 상황으로 흘려 넣어 견준다.

실행: .venv\\Scripts\\python.exe -m detector.export_judge <위치 탐색 실행 폴더(예: vid-d2-v17-r)>
출력: data\\parity_judge\\reads.bin, expected.tsv
"""
import struct
import sys

import numpy as np

from .judge import judge, load
from . import inputs
from .selftrain import ROOT

PARITY = ROOT / "data" / "parity_judge"
# (이름, 키 시각을 옮기는 양(초), 이 시각(키 기준, 초)보다 앞은 고정 전으로 침(없으면 None), 캡처 간격)
SCENARIOS = [("실제 키", 0.0, None, 0.1), ("촘촘한 캡처", 0.0, None, 1 / 30), ("성긴 캡처", 0.0, None, 0.2), ("띄엄띄엄 캡처", 0.0, None, 0.5),
             ("1초 일찍", -1.0, None, 0.1), ("4초 일찍", -4.0, None, 0.1), ("20초 일찍", -20.0, None, 0.1),
             ("1초 늦게", 1.0, None, 0.1), ("2초 늦게", 2.0, None, 0.1), ("3초 늦게", 3.0, None, 0.1),
             ("키 8초 전에 켬", 0.0, -8.0, 0.1), ("키 4초 전에 켬", 0.0, -4.0, 0.1), ("키 2초 전에 켬", 0.0, -2.0, 0.1), ("키 뒤에 켬", 0.0, 0.3, 0.1)]


def main():
    folder = ROOT / "data" / "full" / sys.argv[1]
    cases = [p.stem for p in inputs(folder, "*.npz", "사례별 판독 결과")]
    PARITY.mkdir(parents=True, exist_ok=True)
    lines = []
    with open(PARITY / "reads.bin", "wb") as f:
        f.write(struct.pack("<i", len(cases)))
        for k, case_id in enumerate(cases):
            at, values, clear, locked, press = load(folder, case_id)
            name = case_id.encode("utf-8")
            f.write(struct.pack("<i", len(name))); f.write(name)
            f.write(struct.pack("<id", len(at), press))
            f.write(at.astype("<f8").tobytes()); f.write(values.astype("<i4").tobytes())
            f.write(clear.astype(np.uint8).tobytes()); f.write(locked.astype(np.uint8).tobytes())
            for label, shift, lock_from, capture in SCENARIOS:
                pressed = press + shift
                lock = locked if lock_from is None else locked & (at >= press + lock_from)
                r = judge(at, values, clear, lock, pressed, "fast", capture=capture)
                e = r.evidence
                lines.append("\t".join(str(x) for x in (
                    case_id, label, repr(shift), "" if lock_from is None else repr(lock_from), repr(capture), r.state, r.reason,
                    "" if r.score is None else r.score, "" if r.candidate is None else r.candidate, f"{r.elapsed:.6f}",
                    e["NoteCount"], e["PlayValuesVerified"], f"{e['Agree']:.6f}", "" if e["LastPlayValue"] is None else e["LastPlayValue"],
                    e.get("FramesAfterRequest", 0), e.get("FramesUnread", 0), r.basis)))
            if (k + 1) % 30 == 0:
                print(f"{k + 1} / {len(cases)}", flush=True)
    (PARITY / "expected.tsv").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"사례 {len(cases)}개 x 상황 {len(SCENARIOS)}가지 = {len(lines)}줄 → {PARITY}")


if __name__ == "__main__":
    main()
