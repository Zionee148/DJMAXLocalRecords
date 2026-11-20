""".NET 모듈이 녹화를 처음부터 끝까지 읽은 결과(dotnet\\ModuleCheck 의 run)를 시제품(Python 도구 locate_video)의 결과와 같은 형식으로 바꾼다.

바꾼 뒤에는 시제품에 쓰던 채점 도구를 그대로 쓴다:
  python -m detector.locate_video d2 <판독 이름>-net --summary
  python -m detector.locate_video d2 <판독 이름>-net --normal v12 <기준 영역으로 읽은 실행>
  python -m detector.judge vid-d2-<판독 이름>-net

프레임의 시각(pts)은 같은 사례를 시제품으로 읽은 결과에서 가져온다(프레임 수가 같을 때. 다르면 번호 ÷ 60).

실행: .venv\\Scripts\\python.exe -m detector.import_net <.NET 결과 폴더> <만들 실행 이름(예: vid-d2-v17-net)> <시제품 실행(예: vid-d2-v17-r)>
"""
import json
import sys
from pathlib import Path

import numpy as np

from .build_dataset import VALIDATION
from .full_summary import summarize
from .locate_video import first_score, save_steps
from . import inputs
from .selftrain import BLANK_STD, ROOT


def main():
    source, name, base = Path(sys.argv[1]), sys.argv[2], sys.argv[3]
    out = ROOT / "data" / "full" / name
    out.mkdir(parents=True, exist_ok=True)
    expected = {c["id"]: c["registration"]["expected"] for c in json.loads((VALIDATION / "candidate-v4" / "all270.json").read_text(encoding="utf-8"))}
    done = same_pts = 0
    for path in inputs(source, "*.info.json", "가져올 판독 결과"):
        info = json.loads(path.read_text(encoding="utf-8"))
        case_id = info["id"]
        position = "CENTER" if "-center-" in case_id else ("RIGHT" if "-right-" in case_id else "LEFT")
        record = {"사례": case_id, "위치": position, "고정": "예" if info["locked"] else "아니오", "고정 시각": "", "첫 점수 뒤(초)": "", "고정 전 프레임": "",
                  "플레이 구간": "", "규칙 통과": "", "규칙 통과율": "", "최종 점수": "", "기대 점수": expected.get(case_id, -1), "최종 판정": "보류",
                  "움직이는 점수판": "예" if info["moving"] else "", "자세": "" if info["pose"] is None else " ".join(str(v) for v in info["pose"]), "영역(마지막)": ""}
        if info["locked"]:
            raw = (source / f"{case_id}.bin").read_bytes()
            n = int(np.frombuffer(raw, "<i4", 1)[0]); at = 4
            values = np.frombuffer(raw, "<i4", n, at); at += 4 * n
            conf = np.frombuffer(raw, "<f4", n, at); at += 4 * n
            contrast = np.frombuffer(raw, "<f4", n, at); at += 4 * n
            locked = np.frombuffer(raw, np.uint8, n, at).astype(bool); at += n
            second = np.frombuffer(raw, np.uint8, n, at).astype(bool); at += n
            texts = np.array([raw[at + 8 * i:at + 8 * i + 8].decode("ascii").strip() for i in range(n)])
            old = np.load(ROOT / "data" / "full" / base / f"{case_id}.npz")
            if len(old["pts"]) == n:
                pts = old["pts"]; same_pts += 1
            else:
                pts = (np.arange(n) / 60.0).astype(np.float32)
            steps = [(s[0], tuple(s[1:])) for s in info["steps"]]
            np.savez_compressed(out / f"{case_id}.npz", index=np.arange(1, n + 1), pts=pts, values=values, conf=conf, texts=texts, blank=contrast < BLANK_STD,
                                press=old["press"], locked=locked, lock_at=np.float32(info["lockAt"]),
                                area_first=np.array(steps[0][1], np.float32), area_last=np.array(steps[-1][1], np.float32), second=second)
            save_steps(out, case_id, info["lockAt"], info["pose"], [(-1.0, steps[0][1])] + steps[1:], info["moving"])
            row = summarize(case_id, np.load(out / f"{case_id}.npz"), expected.get(case_id, -1))
            first = first_score(case_id)
            record.update({"고정 시각": f"{info['lockAt']:.1f}", "첫 점수 뒤(초)": "" if first is None else f"{info['lockAt'] - first:.1f}",
                           "고정 전 프레임": int((~locked).sum()), "플레이 구간": row["플레이 구간"], "규칙 통과": row["규칙 통과"], "규칙 통과율": row["규칙 통과율"],
                           "최종 점수": row["최종 점수"], "최종 판정": row["최종 판정"], "영역(마지막)": " ".join(str(int(round(v))) for v in steps[-1][1])})
        (out / f"{case_id}.json").write_text(json.dumps(record, ensure_ascii=False), encoding="utf-8")
        done += 1
    print(f"사례 {done}개를 {out} 에 만듦 (프레임 수가 시제품과 같아 시각을 그대로 쓴 사례 {same_pts}개)")


if __name__ == "__main__":
    main()
