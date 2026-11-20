"""개인용·배포용 묶음을 만든다.

쓰는 법 (프로젝트 폴더에서):
    .venv\\Scripts\\python.exe tools\\make_bundle.py 개인용
    .venv\\Scripts\\python.exe tools\\make_bundle.py 배포용
    .venv\\Scripts\\python.exe tools\\make_bundle.py 둘다
    옵션: --version 1.2.3   묶음의 버전. 실행 파일에 들어가고, 자동 업데이트가 이 번호로 새 버전인지 견준다.
                            주지 않으면 0.0.0(개발용 빌드)이고 그 묶음은 새 버전을 확인하지 않는다.
          --out <저장할 곳>          주지 않으면 저장소 맨 위의 dist 폴더
          --personal <개인 기록 폴더>  개인용을 만들 때만 필요하다. 배포용은 개인 기록 폴더 없이 만든다
    두 폴더를 늘 같은 곳으로 쓰려면 환경 변수(DJMAX_BUNDLE_OUT, DJMAX_PERSONAL_RECORDS)나
    저장소 맨 위의 local-paths.json(git 에 넣지 않는 파일. {"bundle_out": "…", "personal_records": "…"})에 적어 둔다.
    쓰는 순서: 명령의 옵션 → 환경 변수 → local-paths.json → (저장할 곳만) dist

하는 일
  1. 커밋된 소스(HEAD)의 app\\ 과 dotnet\\ScoreModule\\ 을 임시 폴더에 풀어 거기서 Release 로 빌드한다.
     작업 폴더의 bin\\·obj\\ 와 커밋하지 않은 변경은 쓰지 않는다(묶음이 어느 커밋인지 분명하게 하려는 것이다).
     .NET 실행 환경을 포함해 빌드한다(받는 사람이 .NET 을 따로 설치하지 않아도 된다. 폴더가 177MB 쯤 된다).
  2. 빌드 결과에서 실행에 필요 없는 것(*.pdb, *.lib)을 빼고 앱 폴더를 만든다.
     저장소 맨 위의 LICENSE 와 licenses\\(함께 들어가는 외부 라이브러리의 라이선스·고지문 원문)를 같은 이름으로 넣는다.
     프로그램 파일 목록(update-manifest.json)을 쓴다. 자동 업데이트는 이 목록에 있는 파일만 바꾼다.
     개인 자료는 목록을 쓴 뒤에 넣으므로 목록에 들어가지 않는다.
  3. 개인용: 개인 기록 폴더에서 기록과 설정을 읽어 넣는다. 그 폴더는 읽기만 한다.
     appearance.json 은 넣지 않고(새 기본 디자인을 쓴다) 앱 폴더 밖에 따로 남긴다.
     배포용: 개인 자료를 넣지 않는다. 앱이 빈 기록과 기본 설정으로 시작한다.
  4. <저장할 곳>\\<개인용|배포용>\\DJMAXLocalRecords 에 둔다. 이미 있으면 지우지 않고
     DJMAXLocalRecords-backup-날짜-시각 으로 이름을 바꿔 남긴다.
  5. 같은 자리에 묶음정보.txt(커밋, 버전, 개인 자료의 출처, 파일 목록과 해시)를 쓴다.
  6. 배포용이고 버전을 줬으면 같은 자리에 릴리즈에 올릴 ZIP(DJMAXLocalRecords-v1.2.3-win-x64.zip)도 만든다.
     자동 업데이트는 릴리즈에 "첨부"된 이 이름의 파일을 찾는다. 이름을 바꾸거나 본문의 링크로만 걸면 찾지 못한다.
     ZIP 의 SHA-256 은 묶음정보.txt 에 적는다(GitHub 이 릴리즈에 보여 주는 값과 같아야 한다).

개인 기록 폴더는 읽기만 한다. 이번에 만드는 종류의 폴더(<저장할 곳>\\<개인용|배포용>)가 개인 기록 폴더와 같거나
한쪽이 다른 쪽 안에 있으면 빌드하기 전에 멈춘다(.. 과 상대 경로를 풀고 대소문자를 가리지 않고 견준다).
그 폴더 바로 아래에 두는 것(4의 DJMAXLocalRecords, 5의 묶음정보.txt, 개인용의 이전-appearance.json)과
같은 이름이 있을 때 앞의 것을 남기는 자리(…-backup-날짜-시각)가 모두 그 안이라, 폴더째로 견준다.

앱을 실행해 보지는 않는다(실행하면 앱이 묶음 안에 diagnostics 폴더를 만든다).
확인하려면 묶음을 다른 곳에 복사해서 실행한다.

규칙의 근거: docs\\plans\\20261111-상세기획서-공동작업인계.txt 16절·23절,
docs\\결정사항.md 의 "앱 쪽 합의" 9번과 저장 위치 결정(2026-11-13).
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tarfile
import tempfile
import zipfile
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PRODUCT = "DJMAXLocalRecords"
PROJECT = "app/DJMAXLadderRecords.csproj"
# 빌드에 필요한 소스. 앱 프로젝트가 참조하는 프로젝트가 늘면 여기에 더한다
SOURCES = ["app", "dotnet/ScoreModule"]
# 묶음에 함께 넣는 라이선스·고지문. 받은 사람이 묶음 안에서 조건을 확인할 수 있게, 저장소 맨 위의 것을 같은 이름으로 앱 폴더에 둔다.
# 자체 코드의 라이선스 파일(LICENSE)과, 묶음에 포함된 외부 라이브러리의 라이선스·고지문 원문 폴더(licenses)다
NOTICES = ["LICENSE", "licenses"]

# 개인 기록 폴더, 묶음 출력 폴더 같은 로컬 경로 설정 파일. git 에 넣지 않는다(.gitignore)
LOCAL = ROOT / "local-paths.json"


def local_path(key: str, env: str) -> Path | None:
    """로컬 경로 설정에서 폴더 경로를 읽는다: 환경 변수, 없으면 local-paths.json 의 값. 어디에도 없으면 None.

    같은 것이 detector\\build_dataset.py 에도 있다(그쪽은 tools 를 불러오지 않는다)."""
    value = os.environ.get(env)
    if not value and LOCAL.is_file():
        value = json.loads(LOCAL.read_text(encoding="utf-8-sig")).get(key)
    return Path(value) if value else None


DEFAULT_OUT = local_path("bundle_out", "DJMAX_BUNDLE_OUT") or ROOT / "dist"
DEFAULT_PERSONAL = local_path("personal_records", "DJMAX_PERSONAL_RECORDS")

# 개인용에 옮겨 넣는 것(기획서 23-2). records.json 은 반드시 있어야 한다
CARRY = ["records.json", "records.json.bak", "hotkeys.json", "preferences.json", "records-view.json"]
# 옮겨 넣지 않고 앱 폴더 밖에 남기는 것. 개인용에도 새 기본 디자인을 쓰기로 했다(기획서 16절·23-4)
DESIGN = "appearance.json"
# 앱이 실행 중에 실행 파일 옆에 만드는 것. 빌드 결과에 있으면 안 되고 배포용에는 끝까지 없어야 한다
USER_NAMES = {*CARRY, DESIGN, "records.json.tmp", "diagnostics", "update", "update-test.json"}
# 프로그램 파일 목록. 자동 업데이트가 읽는다(app\UpdatePackage.cs 의 UpdateManifest 와 형식이 같아야 한다)
MANIFEST = "update-manifest.json"
TARGET = "win-x64"
# 빌드 결과에서 빼는 것: 디버그 기호와 C++ 링크용 파일
DROP = {".pdb", ".lib"}
# 이것이 없으면 앱이 뜨지 않는다. 실행 자료(app\runtime\)는 따로 하나하나 견준다
MUST_HAVE = [
    "DJMAXLocalRecords.exe", "DJMAXLocalRecords.dll", "DJMAXLocalRecords.runtimeconfig.json",
    "DJMAXLocalRecords.deps.json", "DjmaxScore.dll", "onnxruntime.dll", "overlay.html",
    # .NET 실행 환경이 함께 들어 있어야 한다
    "coreclr.dll", "hostfxr.dll", "System.Windows.Forms.dll",
]

KINDS = {"개인용": ["개인용"], "배포용": ["배포용"], "둘다": ["개인용", "배포용"],
         "personal": ["개인용"], "public": ["배포용"], "both": ["개인용", "배포용"]}


class Stop(Exception):
    """더 나아가면 안 되는 상황. 메시지를 보이고 끝낸다."""


def find_tool(name: str, fallback: str) -> str:
    found = shutil.which(name)
    if found:
        return found
    if Path(fallback).is_file():
        return fallback
    raise Stop(f"{name} 을 찾지 못했다(PATH 와 {fallback}).")


def run(cmd: list[str], env: dict[str, str] | None = None) -> str:
    done = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace", env=env)
    if done.returncode != 0:
        raise Stop(f"명령이 실패했다: {' '.join(cmd)}\n{done.stdout}\n{done.stderr}")
    return done.stdout


def git(*args: str) -> str:
    exe = find_tool("git", r"C:\Program Files\Git\cmd\git.exe")
    return run([exe, "-C", str(ROOT), "-c", "core.quotepath=false", *args])


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


def files_under(folder: Path) -> list[Path]:
    return sorted(p for p in folder.rglob("*") if p.is_file())


def modified(path: Path) -> str:
    return datetime.fromtimestamp(path.stat().st_mtime).strftime("%Y-%m-%d %H:%M:%S")


def inside(child: Path, parent: Path) -> bool:
    child, parent = child.resolve(), parent.resolve()
    return child == parent or parent in child.parents


def overlap(a: Path, b: Path) -> bool:
    """두 경로가 같거나 한쪽이 다른 쪽 안에 있는가. inside 가 .. 과 상대 경로를 풀고, Windows 의 경로 비교는 대소문자를 가리지 않는다."""
    return inside(a, b) or inside(b, a)


def backup_of(target: Path, stamp: str) -> Path:
    """같은 이름이 이미 있을 때 앞의 것을 옮겨 남기는 자리."""
    return target.with_name(f"{target.stem}-backup-{stamp}{target.suffix}")


def check_targets(out: Path, personal: Path | None, kinds: list[str]) -> None:
    """아무것도 쓰기 전에, 이번에 만드는 종류의 폴더가 개인 기록 폴더와 겹치는지 본다.

    개인 기록 폴더를 알려 주지 않았으면 개인용은 만들지 않는다. 배포용은 겹침 확인 없이 만든다(보호할 폴더를 알 수 없다).

    place 는 그 폴더 바로 아래에 앱 폴더, 묶음정보.txt, (개인용은) 이전-appearance.json 을 두고, 같은 이름이 있으면
    그 자리에서 이름을 바꿔 남긴다. 저장할 곳의 맨 위나 앱 폴더만 견주면, 그 가운데 하나를 개인 기록 폴더로 가리켰을 때
    그 폴더의 이름이 바뀐다. 두는 것을 하나씩 견주는 대신 폴더째로 견준다(나중에 두는 것이 늘어도 빠지지 않는다)."""
    if personal is None:
        if "개인용" in kinds:
            raise Stop("개인 기록 폴더를 알려 주지 않았다. 개인용 묶음은 그 폴더의 기록과 설정을 읽어 만든다.\n"
                       "--personal <폴더>, 환경 변수 DJMAX_PERSONAL_RECORDS, 저장소 맨 위 local-paths.json 의 personal_records 가운데 하나로 알려 준다.")
        return
    if inside(out, personal):
        raise Stop(f"저장할 곳이 개인 기록 폴더 안이다. 그 폴더에는 쓰지 않는다: {out}")
    for kind in kinds:
        dest = out / kind
        if overlap(dest, personal):
            raise Stop(f"{kind} 묶음을 둘 폴더와 개인 기록 폴더가 같거나 한쪽이 다른 쪽 안에 있다. 개인 기록 폴더는 읽기만 한다.\n"
                       f"  묶음을 둘 폴더: {dest}\n  개인 기록 폴더: {personal}\n"
                       "--out 이나 --personal 을 바꿔 다시 한다.")


def count_profiles(records: Path) -> int:
    """기록 파일이 온전한지 본다(읽히고 프로필이 있어야 한다). 프로필 수를 돌려준다."""
    try:
        data = json.loads(records.read_text(encoding="utf-8-sig"))
    except (ValueError, OSError) as error:
        raise Stop(f"records.json 을 읽지 못했다({error}). 묶음을 만들지 않는다: {records}")
    profiles = data.get("Profiles") if isinstance(data, dict) else None
    if not profiles:
        raise Stop(f"records.json 에 프로필이 없다. 묶음을 만들지 않는다: {records}")
    return len(profiles)


def check_personal(personal: Path) -> None:
    """빌드에 시간을 쓰기 전에 개인 기록 폴더부터 본다."""
    if not (personal / "records.json").is_file():
        raise Stop(f"개인 기록을 찾지 못했다: {personal / 'records.json'}\n"
                   "다른 사본을 대신 쓰지 않는다. 폴더가 바뀌었으면 --personal 로 알려 준다.")
    if (personal / "records.json.tmp").exists():
        raise Stop(f"저장하다 만 흔적(records.json.tmp)이 있다: {personal}\n"
                   "개인용 앱을 끄고, 기록이 온전한지 확인한 뒤 다시 한다.")
    count_profiles(personal / "records.json")


def asset_name(version: str) -> str:
    """릴리즈에 올리는 ZIP 의 이름. 자동 업데이트가 찾는 이름과 같아야 한다(app\\UpdateCheck.cs 의 AssetName)."""
    return f"{PRODUCT}-v{version}-{TARGET}.zip"


def build(work: Path, version: str | None = None, properties: tuple[str, ...] = ()) -> tuple[Path, Path, list[str]]:
    """커밋된 소스를 임시 폴더에 풀어 빌드한다. (빌드 결과 폴더, 풀어 놓은 소스 폴더, 묶음정보에 적을 줄)

    properties 는 빌드에 더 넘길 것이다(자동 업데이트의 시험용 빌드를 만드는 tools\\update_test.py 가 쓴다)."""
    commit = git("rev-parse", "HEAD").strip()
    subject = git("log", "-1", "--format=%s").strip()
    pending = [line for line in git("status", "--porcelain", "--", *SOURCES).splitlines() if line.strip()]
    notes = [f"소스 커밋: {commit}", f"커밋 제목: {subject}",
             f"버전: {version}" if version else "버전: 0.0.0 (개발용 빌드. 새 버전을 확인하지 않는다)"]
    print(f"소스: 커밋 {commit[:7]} ({subject})")
    if pending:
        print("  ! 커밋하지 않은 변경이 있다. 묶음에는 들어가지 않는다:")
        for line in pending:
            print("      " + line)
        notes.append("커밋하지 않은 변경(묶음에 들어가지 않았다):")
        notes += ["  " + line for line in pending]
    else:
        notes.append("커밋하지 않은 변경: 없음")

    archive, source = work / "src.tar", work / "src"
    git("archive", "--format=tar", "-o", str(archive), "HEAD", "--", *SOURCES, *NOTICES)
    with tarfile.open(archive) as tar:
        tar.extractall(source, filter="data")

    print("빌드 중 (Release, .NET 실행 환경 포함)…")
    publish = work / "publish"
    # 메시지를 영어로 받는다(한글 메시지는 받아 적을 때 깨진다). 빌드 서버를 남기지 않아야 임시 폴더를 지울 수 있다
    return publish_source(source, publish, version, properties, notes), source, notes


def publish_source(source: Path, publish: Path, version: str | None, properties: tuple[str, ...], notes: list[str]) -> Path:
    """풀어 놓은 소스를 .NET 실행 환경을 포함해 Release 로 빌드한다."""
    dotnet = find_tool("dotnet", r"C:\Program Files\dotnet\dotnet.exe")
    env = {**os.environ, "DOTNET_CLI_UI_LANGUAGE": "en", "DOTNET_NOLOGO": "1"}
    extra = [f"-p:Version={version}"] if version else []
    output = run([dotnet, "publish", str(source / PROJECT), "-c", "Release", "-r", TARGET, "--self-contained", "true",
                  "-o", str(publish), "-v", "minimal", "--disable-build-servers", *extra, *properties], env)
    warnings = sorted({line.strip() for line in output.splitlines() if ": warning " in line})
    for line in warnings:
        print("  ! " + line)
    notes.append(f"빌드 경고: {len(warnings)}개")
    return publish


def make_app_folder(publish: Path, source: Path, app: Path) -> None:
    """빌드 결과를 앱 폴더로 옮겨 담고, 빠진 것과 들어가면 안 되는 것을 확인한다."""
    for path in files_under(publish):
        if path.suffix.lower() in DROP:
            continue
        target = app / path.relative_to(publish)
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(path, target)

    missing = [name for name in MUST_HAVE if not (app / name).is_file()]
    # 실행 자료는 app\runtime\ 의 구조 그대로 앱 폴더에 있어야 한다(앱 프로젝트의 복사 규칙)
    runtime = source / "app" / "runtime"
    for path in files_under(runtime):
        copied = app / path.relative_to(runtime)
        if not copied.is_file() or sha256(copied) != sha256(path):
            missing.append(str(path.relative_to(runtime)))
    if missing:
        raise Stop("빌드 결과에 없거나 원본과 다른 파일이 있다: " + ", ".join(missing))
    refuse_user_files(app)


def add_notices(source: Path, app: Path) -> int:
    """라이선스·고지문을 앱 폴더에 넣는다(프로그램 파일 목록을 쓰기 전에 부른다). 넣은 파일 수를 돌려준다.

    빌드 결과에 같은 이름이 이미 있으면 덮어쓰지 않고 멈춘다."""
    count = 0
    for name in NOTICES:
        origin = source / name
        if not origin.exists():
            raise Stop(f"라이선스·고지문이 커밋된 소스에 없다: {name}")
        if (app / name).exists():
            raise Stop(f"빌드 결과에 같은 이름이 이미 있다. 덮어쓰지 않는다: {name}")
        for path in [origin] if origin.is_file() else files_under(origin):
            target = app / path.relative_to(source)
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(path, target)
            if sha256(target) != sha256(path):
                raise Stop(f"라이선스·고지문이 원본과 다르게 복사되었다: {path.relative_to(source)}")
            count += 1
    if count < len(NOTICES):
        raise Stop("라이선스·고지문 폴더가 비어 있다.")
    return count


def refuse_user_files(app: Path) -> None:
    present = sorted(p.name for p in app.iterdir() if p.name.lower() in {n.lower() for n in USER_NAMES})
    if present:
        raise Stop("개인 자료나 실행 흔적이 들어 있으면 안 되는 자리에 있다: " + ", ".join(present))


def write_manifest(app: Path, version: str | None) -> None:
    """프로그램 파일 목록을 쓴다. 이때 앱 폴더에 있는 모든 파일이 프로그램 파일이다(개인 자료는 이 뒤에 넣는다)."""
    refuse_user_files(app)
    files = [{"Path": p.relative_to(app).as_posix(), "Size": p.stat().st_size, "Sha256": sha256(p)}
             for p in files_under(app) if p.name != MANIFEST or p.parent != app]
    manifest = {"Product": PRODUCT, "Version": version or "0.0.0", "Target": TARGET, "Files": files}
    (app / MANIFEST).write_text(json.dumps(manifest, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")


def make_zip(app: Path, target: Path) -> str:
    """앱 폴더를 릴리즈에 올릴 ZIP 으로 묶는다(맨 위 폴더는 DJMAXLocalRecords). 다시 열어 목록과 견준 뒤 SHA-256 을 돌려준다."""
    names = []
    with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for path in files_under(app):
            name = f"{PRODUCT}/{path.relative_to(app).as_posix()}"
            archive.write(path, name)
            names.append(name)
    listed = {f"{PRODUCT}/{item['Path']}" for item in json.loads((app / MANIFEST).read_text(encoding="utf-8"))["Files"]}
    with zipfile.ZipFile(target) as archive:
        packed = set(archive.namelist())
        broken = archive.testzip()
    if broken or packed != listed | {f"{PRODUCT}/{MANIFEST}"} or len(names) != len(packed):
        raise Stop(f"ZIP 의 내용이 프로그램 파일 목록과 다르다: {target}")
    return sha256(target)


def carry_personal(personal: Path, app: Path, staged: Path) -> list[str]:
    """개인 기록 폴더에서 기록과 설정을 읽어 넣는다. 묶음정보에 적을 줄을 돌려준다."""
    built = {p.relative_to(app).as_posix().lower() for p in files_under(app)}
    notes = [f"개인 자료를 읽은 곳: {personal}", "옮겨 넣은 개인 자료:"]
    carried = set()
    for name in CARRY:
        origin = personal / name
        if not origin.is_file():
            notes.append(f"  (없음) {name}")
            print(f"  개인 폴더에 없음: {name}")
            continue
        before = sha256(origin)
        shutil.copy2(origin, app / name)
        # 앱이 저장하는 순간과 겹쳤는지 본다: 복사 앞뒤의 원본과 사본이 모두 같아야 한다
        if not (sha256(app / name) == before == sha256(origin)):
            raise Stop(f"복사하는 사이에 {name} 이 바뀌었다. 개인용 앱을 끄고 다시 한다.")
        carried.add(name.lower())
        notes.append(f"  {name}  {origin.stat().st_size:,}바이트  고친 날 {modified(origin)}  sha256 {before}")

    # 빌드하는 사이에 기록이 바뀌었을 수 있으니 옮겨 넣은 사본으로 한 번 더 본다
    profiles = count_profiles(app / "records.json")
    print(f"  기록: records.json {(app / 'records.json').stat().st_size:,}바이트, "
          f"고친 날 {modified(personal / 'records.json')}, 프로필 {profiles}개")

    if (personal / DESIGN).is_file():
        kept = staged / ("이전-" + DESIGN)
        shutil.copy2(personal / DESIGN, kept)
        notes.append(f"넣지 않고 따로 남긴 것: {DESIGN} → {kept.name} (새 기본 디자인을 쓴다)")

    # 개인 폴더에 있는데 묶음으로 오지 않은 것을 숨기지 않는다(옛 버전의 파일, 진단 기록 등)
    left: dict[str, int] = {}
    for path in files_under(personal):
        relative = path.relative_to(personal).as_posix()
        if relative.lower() in built or relative.lower() in carried or relative.lower() == DESIGN.lower():
            continue
        top = relative.split("/")[0] + ("/" if "/" in relative else "")
        left[top] = left.get(top, 0) + 1
    if left:
        notes.append("개인 폴더에 있지만 가져오지 않은 것:")
        print("  개인 폴더에 있지만 가져오지 않은 것:")
        for top, count in sorted(left.items()):
            line = f"{top} (파일 {count}개)" if top.endswith("/") else top
            notes.append("  " + line)
            print("    " + line)
    return notes


def write_info(staged: Path, kind: str, made: datetime, notes: list[str]) -> None:
    app = staged / PRODUCT
    files = files_under(app)
    total = sum(p.stat().st_size for p in files)
    lines = [f"{PRODUCT} {kind} 묶음", f"만든 때: {made:%Y-%m-%d %H:%M:%S}", *notes, "",
             f"파일 {len(files)}개, 합계 {total:,}바이트", "sha256  크기  경로"]
    lines += [f"{sha256(p)}  {p.stat().st_size:>10,}  {p.relative_to(app).as_posix()}" for p in files]
    (staged / "묶음정보.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"  {kind}: 파일 {len(files)}개, {total / 1048576:.1f}MB")


def place(staged: Path, dest: Path, stamp: str) -> None:
    """다 만든 것을 저장할 곳으로 옮긴다. 같은 이름이 있으면 지우지 않고 이름을 바꿔 남긴다."""
    dest.mkdir(parents=True, exist_ok=True)
    items = sorted(staged.iterdir())
    try:
        for item in items:
            target = dest / item.name
            if target.exists():
                kept = backup_of(target, stamp)
                target.rename(kept)
                print(f"  앞의 것을 남김: {kept}")
        for item in items:
            shutil.move(str(item), str(dest / item.name))
    except OSError as error:
        raise Stop(f"저장할 곳으로 옮기지 못했다({error}).\n"
                   f"그 폴더의 앱이 켜져 있으면 끄고 다시 한다. 지운 것은 없다: {dest}")
    print(f"  만든 곳: {dest / PRODUCT}")


def main() -> int:
    parser = argparse.ArgumentParser(description="개인용·배포용 묶음을 만든다.")
    parser.add_argument("kind", choices=sorted(KINDS), help="만들 묶음")
    parser.add_argument("--version", help="묶음의 버전(예: 1.2.3). 주지 않으면 0.0.0 = 개발용 빌드")
    parser.add_argument("--out", type=Path, default=DEFAULT_OUT, help="저장할 곳 (기본: %(default)s)")
    parser.add_argument("--personal", type=Path, default=DEFAULT_PERSONAL,
                        help="개인 기록 폴더. 개인용을 만들 때만 필요하다 (기본: %(default)s)")
    args = parser.parse_args()
    # 다른 프로그램이 출력을 받아 기록할 때도 줄 순서가 섞이지 않고, 인코딩할 수 없는 글자 때문에 멈추지 않게 한다
    for stream in (sys.stdout, sys.stderr):
        stream.reconfigure(errors="replace", line_buffering=True)

    kinds = KINDS[args.kind]
    work = Path(tempfile.mkdtemp(prefix="djmax-bundle-"))
    try:
        if args.version is not None and (not re.fullmatch(r"[0-9]{1,5}\.[0-9]{1,5}\.[0-9]{1,5}", args.version) or args.version == "0.0.0"):
            raise Stop(f"버전은 1.2.3 꼴로 준다(v 를 붙이지 않는다. 0.0.0 은 개발용 빌드의 번호라 쓸 수 없다): {args.version}")
        check_targets(args.out, args.personal, kinds)
        if "개인용" in kinds:
            check_personal(args.personal)

        publish, source, build_notes = build(work, args.version)
        made = datetime.now()
        for kind in kinds:
            staged = work / "out" / kind
            app = staged / PRODUCT
            make_app_folder(publish, source, app)
            notes = list(build_notes)
            # 여기까지가 프로그램 파일이다. 목록을 쓴 뒤에 개인 자료를 넣는다.
            # 텍스트 설명서(사용안내.txt)는 묶음에 넣지 않는다. 사용법은 GitHub 페이지에서 안내한다
            notes.append("사용안내.txt: 넣지 않음(사용법은 GitHub 페이지에서 안내한다)")
            notes.append(f"라이선스·고지문: {', '.join(NOTICES)} (파일 {add_notices(source, app)}개)")
            write_manifest(app, args.version)
            if kind == "개인용":
                notes += carry_personal(args.personal, app, staged)
            else:
                refuse_user_files(app)
                notes.append("개인 자료: 넣지 않음(빈 기록과 기본 설정으로 시작한다)")
                if args.version:
                    packed = staged / asset_name(args.version)
                    digest = make_zip(app, packed)
                    notes.append(f"릴리즈에 올릴 ZIP: {packed.name}  {packed.stat().st_size:,}바이트  sha256 {digest}")
                    print(f"  릴리즈에 올릴 ZIP: {packed.name} ({packed.stat().st_size / 1048576:.1f}MB)")
            write_info(staged, kind, made, notes)

        # 모두 다 만들어진 뒤에만 저장할 곳을 건드린다
        for kind in kinds:
            dest = args.out / kind
            old = dest / PRODUCT / "records.json"
            if kind == "개인용" and old.is_file() and old.stat().st_mtime > (args.personal / "records.json").stat().st_mtime:
                print("  ! 앞서 만든 개인용 묶음의 기록이 개인 기록 폴더의 것보다 새것이다. "
                      "묶음 폴더에서 앱을 쓴 적이 있다면 그 기록은 아래 backup 폴더에만 남는다.")
            place(work / "out" / kind, dest, f"{made:%Y%m%d-%H%M%S}")
        return 0
    except Stop as stop:
        print(f"멈춤: {stop}", file=sys.stderr)
        return 1
    finally:
        shutil.rmtree(work, ignore_errors=True)
        if work.exists():
            print(f"임시 폴더를 다 지우지 못했다: {work}", file=sys.stderr)


if __name__ == "__main__":
    sys.exit(main())
