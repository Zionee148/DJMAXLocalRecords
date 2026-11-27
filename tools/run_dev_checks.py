"""개발용 검사 다섯 항목을 새 시험 폴더에서 차례로 돌린다.

쓰는 법 (프로젝트 폴더에서):
    dotnet build app\\DJMAXLadderRecords.csproj -c Debug
    .venv\\Scripts\\python.exe tools\\run_dev_checks.py <결과 이름> [--build <Debug 빌드 폴더>] [검사 이름 …]

하는 일
  1. Debug 빌드 결과(app\\bin\\Debug\\net9.0-windows)를 새 시험 폴더 data\\devchecks\\<결과 이름>\\app 에 복사한다.
     그 이름의 폴더가 이미 있으면 멈춘다(지난 실행의 파일에 기대지 않게 늘 새 폴더에서 돌린다).
     빌드 폴더에 남아 있던 기록·설정·지난 검사 결과는 복사하지 않는다.
  2. 그 폴더에서 합성 자료를 만든다(--dev-fixtures: 합성 기록 3,200개, V아카이브 형식의 합성 표본 넷).
     개인 기록·설정은 읽지 않는다. 곡 목록만 읽어 정해진 규칙으로 만든다.
  3. 자동 검사 다섯을 차례로 돌린다(검사 이름을 주면 그것만). 검사마다 제한 시간이 있다.
       records-modal        기록 창 검사               --records-modal-check              제한 90초 (평소 10초쯤)
       hotkey-flow          단축키 흐름 검사           --hotkey-flow-check                제한 60초 (평소 3초쯤. 검사 안의 제한은 30초)
       overlay-states       오버레이 상태 검사         --overlay-check smoke              제한 60초 (평소 5초쯤)
       store-optimization   저장 최적화 검사           --store-optimization-check         제한 60초 (평소 2초쯤)
       release-feature      가져오기 등 기능 검사      --release-feature-check <표본>     제한 60초 (평소 2초쯤)
     제한 시간은 평소 걸리는 시간의 몇 배로 넉넉히 잡았다. 넘으면 응답이 없는 것으로 보고 그 검사를 끝낸다.
  4. 결과를 넷으로 가른다.
       통과       종료 코드 0 이고 결과 파일이 통과라고 적었다
       실패       검사가 실패했다(종료 코드가 0 이 아니거나, 결과 파일이 없거나, 결과 파일이 통과가 아니다. 어느 경우인지는 요약의 '까닭' 열에 적는다)
       실행 불가  자료나 환경이 없어 검사를 돌리지 못했다(검사가 CheckUnavailableException 을 냈다)
       시간 초과  제한 시간 안에 끝나지 않았다
     통과가 아닌 것이 하나라도 있으면 종료 코드 1 로 끝난다. 실행 불가와 시간 초과는 통과가 아니다.
  5. data\\devchecks\\<결과 이름>\\summary.tsv 와 summary.json 을 쓴다. 검사마다 남긴 결과 파일은 app 폴더에 그대로 있다.
     summary.json 에는 돌린 때와 그때 저장소의 커밋(커밋하지 않은 변경이 있었는지 포함), 빌드 폴더와 그 실행 파일이 만들어진 때를 적는다.
     참고값이다: 커밋은 빌드를 만든 커밋이 아니라 돌린 때 저장소의 커밋이고, 변경의 수는 git 이 추적하는 파일만 센다(새로 만든 파일은 세지 않는다).
     빌드한 뒤 곧바로 돌렸을 때만 그 폴더의 코드를 가리킨다고 볼 수 있다.
     시험 폴더는 그때의 빌드를 복사한 것이다. 코드를 고친 뒤에는 다시 빌드해 새 결과 이름으로 돌린다(앞의 폴더가 새 코드로 바뀌지 않는다).

사람이 화면을 보는 도구는 여기서 돌리지 않는다(끝나지 않는 도구다). 시험 폴더에서 직접 켠다.
    data\\devchecks\\<결과 이름>\\app\\DJMAXLocalRecords.exe --overlay-check     오버레이를 상태별로 보기
    data\\devchecks\\<결과 이름>\\app\\DJMAXLocalRecords.exe --hotkey-probe      실제 키를 눌러 단축키 흐름 보기

검사들은 창을 띄운다(주 화면, 기록 목록 창, 확인 창). 포트 18766·18769·18770 과 전역 단축키 Ctrl+Alt+Shift+F1~F5 를 잠깐 쓴다.
개인용 앱(포트 18765, Ctrl+F8~F12)과는 겹치지 않는다. 실제 게임, 실제 V아카이브 접속, 실제 키 입력은 쓰지 않는다.
"""
from __future__ import annotations

import json
import shutil
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
BUILD = ROOT / "app" / "bin" / "Debug" / "net9.0-windows"
OUT = ROOT / "data" / "devchecks"
EXE = "DJMAXLocalRecords.exe"

# 빌드 폴더에서 가져오지 않는 것: 기록·설정, 지난 검사가 남긴 것
SKIP_NAMES = {"records.json", "records.json.bak", "records.json.tmp", "hotkeys.json", "preferences.json", "records-view.json", "UserData",
              "appearance.json", "test-error.txt", "test-data", "diagnostics", "diagnostics-check", "dev-fixtures", "probe-preferences.json",
              "update", "update-test.json", "update-official-check.txt"}
SKIP_ENDS = ("-validation.json", "-validation.txt", "-preview.png", "-preview-dark.png")

# 이름, 설명, 인자, 제한 시간(초), 결과 파일, 결과 파일에서 통과를 읽는 방법
CHECKS = [
    ("records-modal", "기록 창 검사", ["--records-modal-check"], 90, "records-modal-validation.json", "passed"),
    ("hotkey-flow", "단축키 흐름 검사", ["--hotkey-flow-check"], 60, "hotkey-flow-validation.json", "passed"),
    ("overlay-states", "오버레이 상태 검사", ["--overlay-check", "smoke"], 60, "overlay-tool-validation.json", "passed"),
    ("store-optimization", "저장 최적화 검사", ["--store-optimization-check"], 60, "store-optimization-validation.json", "rollback_verified+snapshot_isolated"),
    ("release-feature", "가져오기 등 기능 검사", ["--release-feature-check", r"dev-fixtures\varchive"], 60, "release-feature-validation.json", "notes"),
]


class Stop(Exception):
    pass


def source_state(build: Path) -> dict:
    """언제, 어느 커밋의 저장소에서, 언제 만들어진 빌드로 돌렸는가. 빌드가 그 커밋의 코드로 만들어졌는지까지는 알 수 없다(빌드한 때를 함께 적는다)."""
    def git(*args):
        try:
            return subprocess.run(["git", *args], cwd=ROOT, capture_output=True, text=True, encoding="utf-8", timeout=20).stdout.strip()
        except (OSError, subprocess.SubprocessError):
            return ""
    stamp = lambda t: time.strftime("%Y-%m-%d %H:%M:%S", time.localtime(t))
    changed = [line for line in git("status", "--porcelain", "--untracked-files=no").splitlines() if line.strip()]
    return {"ran_at": stamp(time.time()), "commit": git("rev-parse", "HEAD"), "commit_title": git("log", "-1", "--format=%s"),
            "uncommitted_changes": len(changed), "build": str(build), "build_made_at": stamp((build / "DJMAXLocalRecords.dll").stat().st_mtime)}


def copy_build(build: Path, app: Path) -> int:
    def skip(folder, names):
        return [n for n in names if n in SKIP_NAMES or n.endswith(SKIP_ENDS)]
    shutil.copytree(build, app, ignore=skip)
    return sum(1 for p in app.rglob("*") if p.is_file())


def run(app: Path, args: list, limit: float):
    """검사 하나를 실행해 (결과 낱말, 종료 코드, 걸린 초, 사유)를 돌려준다. 사유는 test-error.txt 의 첫 줄에서 가져온다."""
    error = app / "test-error.txt"
    error.unlink(missing_ok=True)
    started = time.time()
    try:
        code = subprocess.run([str(app / EXE), *args], cwd=app, timeout=limit, stdin=subprocess.DEVNULL,
                              stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL).returncode
    except subprocess.TimeoutExpired:
        return "시간 초과", None, time.time() - started, f"{limit:.0f}초 안에 끝나지 않아 멈춤"
    took = time.time() - started
    first = ""
    if error.exists():
        first = error.read_text(encoding="utf-8", errors="replace").splitlines()[0] if error.stat().st_size else ""
    if "CheckUnavailableException" in first:
        return "실행 불가", code, took, first.split(":", 1)[-1].strip()
    if code != 0 or first:
        return "실패", code, took, first.split(":", 1)[-1].strip() if first else f"종료 코드 {code}"
    return "통과", code, took, ""


def result_says_pass(app: Path, name: str, how: str) -> str:
    """결과 파일이 통과이면 빈 문자열을, 아니면 사유를 반환한다. 사유는 파일 없음, 읽기 실패, 통과가 아닌 값을 구분해 적는다."""
    path = app / name
    if not path.exists():
        return f"결과 파일({name})이 없음"
    try:
        data = json.loads(path.read_text(encoding="utf-8-sig"))
    except (ValueError, OSError) as e:
        return f"결과 파일({name})을 읽지 못함: {e}"
    if how == "notes":
        return "" if data.get("notes") else "결과 파일에 확인한 항목이 없음"
    for key in how.split("+"):
        if data.get(key) is not True:
            return f"결과 파일의 {key} 가 {data.get(key)!r}"
    return ""


def main(argv: list) -> int:
    args = list(argv)
    build = BUILD
    if "--build" in args:
        k = args.index("--build")
        build = Path(args[k + 1]).resolve()
        del args[k:k + 2]
    if not args or args[0].startswith("-"):
        print(__doc__)
        return 2
    name, picked = args[0], args[1:]
    known = [c[0] for c in CHECKS]
    for p in picked:
        if p not in known:
            raise Stop(f"모르는 검사 이름: {p} (아는 것: {', '.join(known)})")
    if not (build / EXE).exists():
        raise Stop(f"Debug 빌드가 없다: {build}\n먼저: dotnet build app\\DJMAXLadderRecords.csproj -c Debug")
    folder = OUT / name
    if folder.exists():
        raise Stop(f"이미 있는 결과 이름이다: {folder}\n지난 실행의 파일에 기대지 않게 새 이름으로 돌린다.")
    app = folder / "app"
    files = copy_build(build, app)
    print(f"시험 폴더: {app} (빌드에서 파일 {files}개 복사)")
    source = source_state(build)
    print(f"저장소 커밋 {source['commit'][:7]}{' + 커밋하지 않은 변경 ' + str(source['uncommitted_changes']) + '개' if source['uncommitted_changes'] else ''}, 빌드한 때 {source['build_made_at']}")

    rows = []
    kind, code, took, why = run(app, ["--dev-fixtures"], 60)
    made = {}
    if kind == "통과":
        try:
            made = json.loads((app / "dev-fixtures" / "made.json").read_text(encoding="utf-8-sig"))
        except (ValueError, OSError) as e:
            kind, why = "실패", f"합성 자료의 기록(dev-fixtures\\made.json)을 읽지 못함: {e}"
    print(f"합성 자료: {kind} ({took:.1f}초) {why or made}")
    rows.append({"name": "dev-fixtures", "title": "합성 자료 만들기", "result": kind, "seconds": round(took, 1), "why": why, "detail": made})

    for check, title, cargs, limit, result_file, how in CHECKS:
        if picked and check not in picked:
            continue
        (app / result_file).unlink(missing_ok=True)
        kind, code, took, why = run(app, cargs, limit)
        if kind == "통과":
            trouble = result_says_pass(app, result_file, how)
            if trouble:
                kind, why = "실패", trouble
        print(f"{title}({check}): {kind} ({took:.1f}초){' · ' + why if why else ''}")
        rows.append({"name": check, "title": title, "result": kind, "seconds": round(took, 1), "why": why, "command": " ".join([EXE, *cargs]),
                     "limit_seconds": limit, "result_file": result_file})

    lines = ["검사\t이름\t결과\t걸린 초\t까닭"] + [f"{r['title']}\t{r['name']}\t{r['result']}\t{r['seconds']}\t{r['why']}" for r in rows]
    (folder / "summary.tsv").write_text("\n".join(lines) + "\n", encoding="utf-8-sig")
    (folder / "summary.json").write_text(json.dumps({**source, "app": str(app), "rows": rows}, ensure_ascii=False, indent=1), encoding="utf-8")
    bad = [r for r in rows if r["result"] != "통과"]
    print(f"합계: {len(rows) - len(bad)}개 통과, 통과가 아닌 것 {len(bad)}개 → {folder / 'summary.tsv'}")
    return 1 if bad else 0


if __name__ == "__main__":
    try:
        sys.exit(main(sys.argv[1:]))
    except Stop as e:
        print(f"멈춤: {e}")
        sys.exit(2)
