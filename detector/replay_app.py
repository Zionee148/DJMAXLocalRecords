"""앱 경로 시험: 시험용 주 화면에 녹화를 실제 시간으로 넣는다.

앱의 Debug 빌드에 들어 있는 --score-replay-check(app\\ScoreReplayChecks.cs)를 사례마다 한 번씩 돌린다.
주 화면을 그대로 띄우고 게임 창 캡처와 Steam 곡 정보만 바꿔 끼운 것이다. 실제 게임 창 캡처, 실제 Steam, 전역 단축키는 거치지 않는다.

한 사례가 녹화 길이(100초쯤)만큼 걸리므로 여러 개를 함께 돌린다. 함께 도는 것끼리 기록·설정·진단 기록이 섞이지 않게
빌드 결과 폴더를 작업자 수만큼 복사해(data\\replay\\_work\\w번호) 폴더마다 하나씩 돌리고 포트도 달리 준다. 복사한 폴더는 시작할 때마다 새로 만든다
(개인 기록이 없는 시험용 폴더다. 실제 개인 기록 폴더는 건드리지 않는다).

실행: .venv\\Scripts\\python.exe -m detector.replay_app <결과 이름> [--workers N] [--script cancel|confirm] [--list <사례 목록 파일>] [사례 이름의 일부 …]
      .venv\\Scripts\\python.exe -m detector.replay_app <결과 이름> --flows <사례 이름> [--workers N] [각본 …]
      (--flows: 등록 무결성의 분기 시험. app\\ScoreFlowChecks.cs 의 각본을 각각 한 번씩 돌린다. 각본을 주지 않으면 전부)
      (사례를 주지 않으면 270개 전부. 이미 결과가 있는 사례는 건너뛴다)
      (--cases <사례 표>: 기어 표본 270개의 표 대신 같은 모양의 다른 표를 쓴다. 다른 녹화를 넣어 볼 때 쓴다. 저장소 폴더 기준의 경로)
출력: data\\replay\\<결과 이름>\\<사례>.json, summary.tsv
이번 실행이 만든 결과만 읽는다(분기 시험은 돌릴 때마다 그 각본의 지난 결과를 먼저 지운다). 시간 초과, 비정상 종료,
결과 파일이 없는 것은 사유를 적어 실패(사례는 error)로 남기고, 그런 것이나 통과하지 못한 각본이 있으면 종료 코드 1로 끝난다.
먼저: dotnet build app\\DJMAXLadderRecords.csproj -c Debug
"""
import csv
import json
import queue
import shutil
import subprocess
import sys
import time
from concurrent.futures import ThreadPoolExecutor

from . import inputs
from .selftrain import ROOT

BUILD = ROOT / "app" / "bin" / "Debug" / "net9.0-windows"
CASES = ROOT / "data" / "parity_locate" / "cases.tsv"
PORT = 18800


def launch(command: list, folder, limit: float, target):
    """시험 프로그램을 한 번 실행하고 (결과 파일의 내용, 실행 실패 사유)를 반환한다. 프로그램이 정상 종료되고 결과 파일을 읽으면 두 번째 값은 빈 문자열이다.
    그 밖의 경우에는 실행 실패 사유(시간 초과, 비정상 종료, 결과 파일이 없음, 결과 파일을 읽지 못함)를 담는다. 결과 파일에 기록된 시험 통과 여부는 호출한 쪽에서 확인한다.

    부르기 전에 target 이 없어야 한다(이번 실행이 만든 결과만 읽는다). 시간 초과와 비정상 종료는 결과 파일이 남았더라도
    사유를 반환한다. 그때의 내용은 무엇을 하다 멈췄는지 보는 데에만 쓰고 통과로 치지 않는다."""
    why = []
    try:
        code = subprocess.run(command, cwd=folder, timeout=limit, stdin=subprocess.DEVNULL).returncode
        if code != 0:
            why.append(f"시험 프로그램이 비정상 종료함(종료 코드 {code})")
    except subprocess.TimeoutExpired:
        why.append(f"시험이 {limit:.0f}초 안에 끝나지 않음(시간 초과)")
    d = {}
    if not target.exists():
        why.append("결과 파일이 만들어지지 않음")
    else:
        try:
            d = json.loads(target.read_text(encoding="utf-8"))
            if not isinstance(d, dict):
                raise ValueError
        except ValueError:
            d = {}
            why.append("결과 파일을 읽지 못함")
    return d, ", ".join(why)


def event(d: dict, kind: str, last: bool = False):
    found = [e for e in d.get("diagnostics", []) if e.get("kind") == kind]
    return (found[-1] if last else found[0]) if found else None


def summarize(out):
    rows = []
    for path in inputs(out, "*.json", "사례별 결과"):
        d = json.loads(path.read_text(encoding="utf-8"))
        locked, answer, frames = event(d, "locked"), event(d, "answer", last=True), event(d, "frames", last=True)
        rows.append({
            "사례": d.get("case", path.stem), "위치": d.get("position", ""), "각본": d.get("script", ""), "결과": d.get("verdict", "error"),
            "기대 점수": d.get("expected", ""), "등록 후보": d.get("candidate", ""), "실패 문구": d.get("failure", ""),
            "키부터 후보까지(초, 앱에서)": d.get("answerSeconds", ""), "모듈의 답까지(ms)": answer.get("elapsedMs", "") if answer else "",
            "모듈 사유": answer.get("reason", "") if answer else "", "자리 고정(초, 판을 연 때부터)": round(locked["sincePlayMs"] / 1000, 1) if locked else "",
            "요청 뒤 화면": answer.get("framesOffered", "") if answer else "", "가장 긴 간격(ms)": answer.get("maxGapMs", "") if answer else "",
            "플레이 중 읽은 값": answer.get("playReads", "") if answer else "", "통과한 값": answer.get("playVerified", "") if answer else "",
            "넘긴 화면": frames.get("observed", "") if frames else "", "바빠서 버린 화면": frames.get("dropped", "") if frames else "",
            "새 판으로 본 횟수": sum(e.get("kind") == "restart" for e in d.get("diagnostics", [])),
            "초당 캡처": round(d["captures"] / d["replaySeconds"], 2) if d.get("replaySeconds") else "",
            "곡을 앱이 확정": d.get("songLockedByApp", ""), "저장 확인": d.get("saved", d.get("unchanged", "")), "끝": d.get("finish", ""),
            "메모": " / ".join(d.get("notes", [])),
        })
    if not rows:
        return rows
    with open(out / "summary.tsv", "w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0]), delimiter="\t"); w.writeheader(); w.writerows(rows)
    return rows


FLOWS = ["pending-next-confirm", "pending-next-cancel", "saved-then-restart", "pending-restart", "change-during-request",
         "cancel-during-request", "guards", "records-during-pending", "stale-dialogs"]


def flows(name: str, case: str, workers: int, picked: list):
    """분기 시험: 각본마다 깨끗한 폴더에서 한 번씩 돌린다."""
    out = ROOT / "data" / "replay" / name
    out.mkdir(parents=True, exist_ok=True)
    todo = [s for s in (picked or FLOWS)]
    work = ROOT / "data" / "replay" / "_work"
    started = time.time()

    def run(job):
        i, scenario = job
        folder, target = work / f"f{i}", out / f"{scenario}.json"
        if folder.exists():
            shutil.rmtree(folder)
        shutil.copytree(BUILD, folder)
        # 같은 결과 이름으로 다시 돌리면 지난 결과가 남아 있다. 먼저 지워서 이번에 만들어진 것만 읽는다
        target.unlink(missing_ok=True)
        d, trouble = launch([str(folder / "DJMAXLocalRecords.exe"), "--score-flow-check", str(CASES), case, str(target), str(PORT + 50 + i), scenario],
                            folder, 420, target)
        if trouble:
            # 끝까지 돌지 못한 것은 결과 파일에 무엇이 적혀 있든 실패다. 파일에도 그렇게 남긴다
            d = {**d, "passed": False, "failures": [trouble, *d.get("failures", [])]}
            target.write_text(json.dumps(d, ensure_ascii=False, indent=2), encoding="utf-8")
        good = sum(c["ok"] for c in d.get("checks", []))
        print(f"{scenario}: {'통과' if d.get('passed') else '실패 <<<<<'} (확인 {good}/{len(d.get('checks', []))})", flush=True)
        for c in d.get("checks", []):
            if not c["ok"]:
                print(f"     아님: {c['what']}  → {c.get('got')}", flush=True)
        for text in d.get("failures", []):
            if text.startswith("오류") or text.startswith("시험") or text.startswith("결과"):
                print(f"     {text}", flush=True)
        for text in d.get("notes", []):
            print(f"     메모: {text}", flush=True)
        return scenario, d, trouble

    with ThreadPoolExecutor(max_workers=workers) as pool:
        results = list(pool.map(run, enumerate(todo)))
    broken = [scenario for scenario, _, trouble in results if trouble]
    results = [(scenario, d) for scenario, d, _ in results]
    lines = ["각본\t확인\t결과\t내용\t본 값"]
    for scenario, d in results:
        for c in d.get("checks", []):
            lines.append(f"{scenario}\t{'맞음' if c['ok'] else '아님'}\t\t{c['what']}\t{c.get('got') or ''}")
        lines.append(f"{scenario}\t\t{'통과' if d.get('passed') else '실패'}\t{' / '.join(d.get('failures', []))}\t")
    (out / "flows.tsv").write_text("\n".join(lines) + "\n", encoding="utf-8-sig")
    passed = sum(bool(d.get("passed")) for _, d in results)
    total = sum(len(d.get("checks", [])) for _, d in results)
    good = sum(c["ok"] for _, d in results for c in d.get("checks", []))
    print(f"분기 시험 (앱 경로, 실제 시간, 사례 {case}): 각본 {len(results)}개 중 통과 {passed}개, 확인 {good}/{total} | {time.time() - started:.0f}초 → {out / 'flows.tsv'}")
    if broken:
        print(f"끝까지 돌지 못한 각본 {len(broken)}개(시간 초과·비정상 종료·결과 없음. 통과로 치지 않는다): {', '.join(broken)}")
    return passed == len(results)


def main():
    args = sys.argv[1:]
    if "--flows" in args:
        k = args.index("--flows")
        rest = args[1:k] + args[k + 2:]
        workers = int(rest[rest.index("--workers") + 1]) if "--workers" in rest else 4
        picked = [a for j, a in enumerate(rest) if a != "--workers" and (j == 0 or rest[j - 1] != "--workers")]
        if not (BUILD / "DJMAXLocalRecords.exe").exists():
            raise SystemExit(f"Debug 빌드가 없다: {BUILD}")
        if not flows(args[0], args[k + 1], workers, picked):
            raise SystemExit(1)
        return
    name, workers, script, keys, cases = args[0], 4, "cancel", [], CASES
    k = 1
    while k < len(args):
        if args[k] == "--workers": workers = int(args[k + 1]); k += 2
        elif args[k] == "--script": script = args[k + 1]; k += 2
        elif args[k] == "--cases": cases = (ROOT / args[k + 1]).resolve(); k += 2
        elif args[k] == "--list":
            keys += [line.split("#")[0].strip() for line in open(args[k + 1], encoding="utf-8") if line.split("#")[0].strip()]; k += 2
        else: keys.append(args[k]); k += 1
    out = ROOT / "data" / "replay" / name
    out.mkdir(parents=True, exist_ok=True)
    table = [r for r in csv.reader(open(cases, encoding="utf-8"), delimiter="\t")]
    todo = [r for r in table if (not keys or any(key in r[0] for key in keys)) and not (out / f"{r[0]}.json").exists()]
    if not (BUILD / "DJMAXLocalRecords.exe").exists():
        raise SystemExit(f"Debug 빌드가 없다: {BUILD}")
    work = ROOT / "data" / "replay" / "_work"
    free = queue.Queue()
    for i in range(min(workers, max(1, len(todo)))):
        folder = work / f"w{i}"
        if folder.exists():
            shutil.rmtree(folder)
        shutil.copytree(BUILD, folder)
        free.put(i)
    started = time.time()
    done = [0]

    def run(row):
        i = free.get()
        try:
            folder, target = work / f"w{i}", out / f"{row[0]}.json"
            length = float(row[4]) - float(row[3])
            # 결과가 이미 있는 사례는 위에서 걸렀으므로 여기서 읽히는 것은 이번 실행이 만든 것뿐이다
            d, trouble = launch([str(folder / "DJMAXLocalRecords.exe"), "--score-replay-check", str(cases), row[0], str(target), str(PORT + i), script],
                                folder, length + 120, target)
            if trouble:
                # 끝까지 돌지 못한 것은 결과 파일에 무엇이 적혀 있든 오류로 남긴다(요약과 다음 실행이 이 파일을 읽는다)
                was = f" (결과 파일에는 {d['verdict']})" if d.get("verdict") else ""
                d = {"case": row[0], "position": row[1], "expected": int(row[6]), "script": script, "finish": "no-result", **d,
                     "verdict": "error", "notes": [trouble + was, *d.get("notes", [])]}
                target.write_text(json.dumps(d, ensure_ascii=False), encoding="utf-8")
            done[0] += 1
            flag = "" if d.get("verdict") == "right" else "   <<<<<"
            print(f"[{done[0]}/{len(todo)}] {row[0]}: {d.get('verdict')} {d.get('candidate', '-')} (기대 {row[6]}) "
                  f"{d.get('answerSeconds', '-')}초 {d.get('failure', '')} {' / '.join(d.get('notes', []))}{flag}", flush=True)
        finally:
            free.put(i)

    with ThreadPoolExecutor(max_workers=workers) as pool:
        list(pool.map(run, todo))
    rows = summarize(out)
    count = {v: sum(r["결과"] == v for r in rows) for v in ("right", "wrong", "none", "error")}
    print(f"사례 {len(rows)}개 (앱 경로, 실제 시간, 각본 {script}, 함께 {workers}개): 맞음 {count['right']}, 틀림 {count['wrong']}, 못 냄 {count['none']}, 오류 {count['error']} | "
          f"새 판으로 잘못 본 횟수 {sum(r['새 판으로 본 횟수'] for r in rows)} | 이번에 돌린 것 {len(todo)}개, {time.time() - started:.0f}초 → {out / 'summary.tsv'}")
    if count["error"]:
        # 끝까지 돌지 못한 사례(시간 초과·비정상 종료·결과 없음)가 남아 있으면 성공으로 끝내지 않는다
        raise SystemExit(1)


if __name__ == "__main__":
    main()
