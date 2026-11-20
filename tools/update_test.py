"""자동 업데이트를 시험용 빌드와 가짜 릴리즈 서버로 시험한다.

쓰는 법 (프로젝트 폴더에서):
    .venv\\Scripts\\python.exe tools\\update_test.py build <이름> [--worktree]
    .venv\\Scripts\\python.exe tools\\update_test.py run <이름> [시나리오 …] [--keep]
    .venv\\Scripts\\python.exe tools\\update_test.py manual <이름>

build   시험용 빌드 셋(9.0.0, 9.0.1, 9.0.2)과 배포 파일(ZIP)을 data\\updatetest\\<이름>\\builds 에 만든다.
        커밋된 소스(HEAD)로 만든다. --worktree 를 주면 커밋하지 않은 작업 폴더의 소스로 만든다(개발 중에 쓴다).
        배포하는 빌드와 같은 방법(Release, .NET 실행 환경 포함)으로 만들되 시험용 표시(-p:UpdateTestSource=true)를 더한다.
        그 표시가 있는 빌드만 실행 파일 옆의 update-test.json 이 가리키는 서버에서 새 버전을 찾는다.
run     시나리오마다 새 폴더(data\\updatetest\\<이름>\\runs\\<시나리오>\\app)에 9.0.0 과 합성 기록·설정을 놓고,
        이 PC 안에서만 도는 가짜 릴리즈 서버를 켜고, 프로그램을 실제로 켜서 창의 단추를 눌러 가며 확인한다.
        결과는 runs\\summary.tsv 와 summary.json, 시나리오마다 runs\\<시나리오>\\result.json 과 update.log 사본.
        통과한 시나리오의 app 폴더는 지운다(하나가 200MB 를 넘는다). --keep 을 주면 남긴다. 실패한 것은 늘 남긴다.
manual  사람이 직접 볼 시험 폴더(data\\updatetest\\<이름>\\manual\\app)를 만들고 가짜 릴리즈 서버를 켜 둔다(Ctrl+C 로 끈다).
        순서와 기대 결과는 docs\\기록\\자동업데이트.md 의 "직접 확인" 절.

지키는 것
  - 개인 기록 폴더를 읽지도 쓰지도 않는다. 기록·설정은 곡 목록만 읽어 합성한다.
  - 공식 저장소에 아무것도 올리지 않고, 인터넷에 묻지 않는다. 서버는 127.0.0.1 에서만 듣는다.
  - 시험용 프로그램은 포트 18781, 단축키 Ctrl+Alt+Shift+F6~F10, 따로 붙인 이름의 중복 실행 방지를 써서
    켜져 있는 개인용 프로그램(포트 18765, Ctrl+F8~F12)과 겹치지 않는다.
  - 프로세스를 이름으로 끝내지 않는다. 이 도구가 켠 프로그램은 창을 닫아 끝낸다.

시험하는 동안 프로그램 창이 여러 번 떴다 사라진다. 그 창을 건드리지 않는다.
"""
from __future__ import annotations

import argparse
import ctypes
import ctypes.wintypes as wt
import hashlib
import http.server
import json
import shutil
import subprocess
import sys
import tarfile
import threading
import time
import traceback
import zipfile
from datetime import datetime
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import make_bundle as bundle  # noqa: E402

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "data" / "updatetest"
PRODUCT, EXE, MANIFEST = "DJMAXLocalRecords", "DJMAXLocalRecords.exe", "update-manifest.json"
OLD, NEW, NEXT = "9.0.0", "9.0.1", "9.0.2"
SERVER_PORT, OVERLAY_PORT, INSTANCE = 18780, 18781, "updatetest"
MAIN_TITLE = "디맥 개인 기록 저장 오버레이"
USER_FILES = ["records.json", "records.json.bak", "hotkeys.json", "preferences.json", "appearance.json"]


class Fail(Exception):
    """시나리오가 기대와 다른 것을 봤다."""


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


def picture(folder: Path, skip: tuple[str, ...] = ("update", "diagnostics")) -> dict[str, str]:
    """폴더의 파일마다 해시. 맨 위의 skip 폴더는 뺀다."""
    found = {}
    for path in sorted(folder.rglob("*")):
        relative = path.relative_to(folder).as_posix()
        if path.is_file() and relative.split("/")[0] not in skip:
            found[relative] = sha256(path)
    return found


# ───────────────────────────── 빌드

def build(name: str, worktree: bool) -> int:
    target = OUT / name / "builds"
    if target.exists():
        print(f"멈춤: 이미 있다. 새 이름을 쓴다: {target}", file=sys.stderr)
        return 1
    work = OUT / name / "work"
    source = work / "src"
    source.mkdir(parents=True)
    if worktree:
        for part in bundle.SOURCES:
            shutil.copytree(ROOT / part, source / part, ignore=shutil.ignore_patterns("bin", "obj"))
        origin = "작업 폴더(커밋하지 않은 변경 포함)"
    else:
        archive = work / "src.tar"
        bundle.git("archive", "--format=tar", "-o", str(archive), "HEAD", "--", *bundle.SOURCES)
        with tarfile.open(archive) as tar:
            tar.extractall(source, filter="data")
        origin = "커밋 " + bundle.git("rev-parse", "HEAD").strip()
    print("소스:", origin)
    info = {"source": origin, "made_at": f"{datetime.now():%Y-%m-%d %H:%M:%S}", "versions": {}, "packages": {}}
    for version in (OLD, NEW, NEXT):
        print(f"빌드 중: {version} (Release, .NET 실행 환경 포함, 시험용 표시)")
        notes: list[str] = []
        publish = bundle.publish_source(source, work / ("publish-" + version), version, ("-p:UpdateTestSource=true",), notes)
        app = target / version / PRODUCT
        bundle.make_app_folder(publish, source, app)
        bundle.write_manifest(app, version)
        info["versions"][version] = {"files": len(bundle.files_under(app)), "bytes": sum(p.stat().st_size for p in bundle.files_under(app)),
                                     "exe_sha256": sha256(app / EXE), "notes": notes}
    packages = target / "packages"
    packages.mkdir()
    # 배포 파일: 정상 둘과, 일부러 다르게 만든 것 둘
    made = {
        "good": pack(target / NEW / PRODUCT, packages / "good.zip", NEW),
        "next": pack(target / NEXT / PRODUCT, packages / "next.zip", NEXT),
        # 새 버전에만 있는 파일이 하나 더 든 것(그 자리에 사용자의 파일을 두어 겹치게 할 때 쓴다)
        "extra": pack(target / NEW / PRODUCT, packages / "extra.zip", NEW, add={"data/new-in-901.txt": b"only in 9.0.1\n"}),
        # 곡 목록이 깨져서 켜지지 않는 새 버전(설치는 되고, 보통으로 켜면 실행 오류가 난다)
        "badstart": pack(target / NEW / PRODUCT, packages / "badstart.zip", NEW, replace={"data/catalog.json": b"{broken"}),
    }
    info["packages"] = {key: {"file": value.name, "bytes": value.stat().st_size, "sha256": sha256(value)} for key, value in made.items()}
    (target / "build-info.json").write_text(json.dumps(info, ensure_ascii=False, indent=1), encoding="utf-8")
    shutil.rmtree(work, ignore_errors=True)
    print("만든 곳:", target)
    return 0


def pack(app: Path, target: Path, version: str, replace: dict[str, bytes] | None = None, add: dict[str, bytes] | None = None) -> Path:
    """앱 폴더를 배포 파일로 묶는다. replace·add 를 주면 그 파일만 바꾸거나 더하고 목록도 거기에 맞춘다."""
    replace, add = replace or {}, add or {}
    files = []
    with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        entries = [(p.relative_to(app).as_posix(), p) for p in bundle.files_under(app) if p.name != MANIFEST or p.parent != app]
        for relative, path in entries:
            content = replace.get(relative)
            if content is None:
                archive.write(path, f"{PRODUCT}/{relative}")
                files.append({"Path": relative, "Size": path.stat().st_size, "Sha256": sha256(path)})
            else:
                archive.writestr(f"{PRODUCT}/{relative}", content)
                files.append({"Path": relative, "Size": len(content), "Sha256": hashlib.sha256(content).hexdigest()})
        for relative, content in add.items():
            archive.writestr(f"{PRODUCT}/{relative}", content)
            files.append({"Path": relative, "Size": len(content), "Sha256": hashlib.sha256(content).hexdigest()})
        manifest = {"Product": PRODUCT, "Version": version, "Target": "win-x64", "Files": files}
        archive.writestr(f"{PRODUCT}/{MANIFEST}", json.dumps(manifest, ensure_ascii=False, indent=1))
    return target


# ───────────────────────────── 가짜 릴리즈 서버

class Releases:
    """GitHub 의 릴리즈 목록과 첨부 파일 내려받기를 흉내 낸다. 127.0.0.1 에서만 듣는다."""

    def __init__(self, port: int):
        self.port = port
        self.base = f"http://127.0.0.1:{port}"
        self.items: list[dict] = []
        self.files: dict[str, tuple[Path, str]] = {}   # 이름 → (파일, 보내는 방식)
        self.log: list[tuple[float, str]] = []
        self.lock = threading.Lock()
        outer = self

        class Handler(http.server.BaseHTTPRequestHandler):
            def log_message(self, *args):
                pass

            def do_GET(self):
                with outer.lock:
                    outer.log.append((time.time(), self.path))
                try:
                    if self.path.startswith("/releases"):
                        body = json.dumps(outer.items, ensure_ascii=False).encode("utf-8")
                        self.send_response(200)
                        self.send_header("Content-Type", "application/json; charset=utf-8")
                        self.send_header("Content-Length", str(len(body)))
                        self.end_headers()
                        self.wfile.write(body)
                    elif self.path.startswith("/download/") and self.path[len("/download/"):] in outer.files:
                        path, mode = outer.files[self.path[len("/download/"):]]
                        data = path.read_bytes()
                        if mode == "corrupt":   # 크기는 같고 내용이 한 군데 다르다
                            data = data[:len(data) // 2] + bytes([data[len(data) // 2] ^ 0xFF]) + data[len(data) // 2 + 1:]
                        self.send_response(200)
                        self.send_header("Content-Type", "application/zip")
                        self.send_header("Content-Length", str(len(data)))
                        self.end_headers()
                        if mode == "truncate":   # 반쯤 보내고 끊는다
                            self.wfile.write(data[:len(data) // 2])
                            self.wfile.flush()
                            self.connection.close()
                        elif mode == "slow":     # 40초쯤 걸리게 조금씩 보낸다(도중에 취소를 누를 수 있게)
                            step = max(1, len(data) // 400)
                            for start in range(0, len(data), step):
                                self.wfile.write(data[start:start + step])
                                self.wfile.flush()
                                time.sleep(0.1)
                        else:
                            self.wfile.write(data)
                    else:
                        self.send_error(404)
                except (ConnectionError, OSError):
                    pass

        self.server = http.server.ThreadingHTTPServer(("127.0.0.1", port), Handler)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()

    def stop(self):
        self.server.shutdown()
        self.server.server_close()

    def release(self, version: str, package: Path | None, mode: str = "ok", tag: str | None = None, draft: bool = False, prerelease: bool = False,
                asset: str | None = None, notes: str | None = None) -> dict:
        """릴리즈 하나를 만든다(목록에 넣지는 않는다). package 가 None 이면 첨부 파일이 없는 릴리즈다."""
        tag = tag or "v" + version
        item = {"tag_name": tag, "name": f"{PRODUCT} {tag}", "draft": draft, "prerelease": prerelease, "html_url": f"{self.base}/tag/{tag}",
                "body": notes or f"# {tag} 패치노트\n\n- 시험용 릴리즈입니다.\n- 자동 업데이트 시험 ({version})", "assets": []}
        if package is not None:
            name = asset or f"{PRODUCT}-v{version}-win-x64.zip"
            self.files[name] = (package, mode)
            item["assets"].append({"name": name, "size": package.stat().st_size, "digest": "sha256:" + sha256(package),
                                   "browser_download_url": f"{self.base}/download/{name}"})
        return item

    def count(self, prefix: str, since: float = 0.0) -> int:
        with self.lock:
            return sum(1 for at, path in self.log if path.startswith(prefix) and at >= since)


# ───────────────────────────── 창 다루기 (이 도구가 켠 프로그램의 창만)

user32, kernel32 = ctypes.windll.user32, ctypes.windll.kernel32
EnumProc = ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)
user32.SendMessageTimeoutW.argtypes = [wt.HWND, wt.UINT, wt.WPARAM, wt.LPARAM, wt.UINT, wt.UINT, ctypes.POINTER(ctypes.c_size_t)]
user32.SendMessageTimeoutW.restype = ctypes.c_size_t
user32.PostMessageW.argtypes = [wt.HWND, wt.UINT, wt.WPARAM, wt.LPARAM]
kernel32.OpenProcess.restype = wt.HANDLE
kernel32.OpenProcess.argtypes = [wt.DWORD, wt.BOOL, wt.DWORD]
kernel32.QueryFullProcessImageNameW.argtypes = [wt.HANDLE, wt.DWORD, wt.LPWSTR, ctypes.POINTER(wt.DWORD)]
kernel32.CloseHandle.argtypes = [wt.HANDLE]
kernel32.WaitForSingleObject.argtypes = [wt.HANDLE, wt.DWORD]
user32.EnumWindows.argtypes = [EnumProc, wt.LPARAM]
user32.EnumChildWindows.argtypes = [wt.HWND, EnumProc, wt.LPARAM]
user32.GetClassNameW.argtypes = [wt.HWND, wt.LPWSTR, ctypes.c_int]
user32.GetWindowThreadProcessId.argtypes = [wt.HWND, ctypes.POINTER(wt.DWORD)]
for name in ("IsWindowVisible", "IsWindow", "IsWindowEnabled"):
    getattr(user32, name).argtypes = [wt.HWND]
WM_SETTEXT, WM_GETTEXT, WM_GETTEXTLENGTH, WM_CLOSE, BM_CLICK = 0x000C, 0x000D, 0x000E, 0x0010, 0x00F5


def process_path(pid: int) -> str | None:
    handle = kernel32.OpenProcess(0x1000, False, pid)   # PROCESS_QUERY_LIMITED_INFORMATION
    if not handle:
        return None
    try:
        size = wt.DWORD(1024)
        buffer = ctypes.create_unicode_buffer(size.value)
        return buffer.value if kernel32.QueryFullProcessImageNameW(handle, 0, buffer, ctypes.byref(size)) else None
    finally:
        kernel32.CloseHandle(handle)


def window_text(hwnd: int) -> str:
    length = ctypes.c_size_t()
    if not user32.SendMessageTimeoutW(hwnd, WM_GETTEXTLENGTH, 0, 0, 2, 2000, ctypes.byref(length)):
        return ""
    buffer = ctypes.create_unicode_buffer(length.value + 2)
    copied = ctypes.c_size_t()
    user32.SendMessageTimeoutW(hwnd, WM_GETTEXT, length.value + 1, ctypes.cast(buffer, ctypes.c_void_p).value, 2, 2000, ctypes.byref(copied))
    return buffer.value


def class_name(hwnd: int) -> str:
    buffer = ctypes.create_unicode_buffer(256)
    user32.GetClassNameW(hwnd, buffer, 256)
    return buffer.value


class Window:
    def __init__(self, hwnd: int, pid: int, exe: str):
        self.hwnd, self.pid, self.exe = hwnd, pid, exe

    @property
    def title(self) -> str:
        return window_text(self.hwnd)

    def children(self) -> list[tuple[int, str, str]]:
        found = []

        def each(hwnd, _):
            if user32.IsWindowVisible(hwnd):
                found.append((hwnd, class_name(hwnd), window_text(hwnd)))
            return True
        user32.EnumChildWindows(self.hwnd, EnumProc(each), 0)
        return found

    def texts(self) -> str:
        return "\n".join(text for _, _, text in self.children() if text)

    def button(self, label: str) -> int | None:
        for hwnd, kind, text in self.children():
            if "BUTTON" in kind.upper() and text == label:
                return hwnd
        return None

    def enabled(self, label: str) -> bool:
        hwnd = self.button(label)
        return bool(hwnd) and bool(user32.IsWindowEnabled(hwnd))

    def click(self, label: str) -> None:
        hwnd = self.button(label)
        if not hwnd:
            raise Fail(f"단추를 찾지 못했다: [{label}] (창 \"{self.title}\"의 글: {self.texts()[:300]!r})")
        if not user32.IsWindowEnabled(hwnd):
            raise Fail(f"단추가 눌리지 않는 상태다: [{label}]")
        user32.PostMessageW(hwnd, BM_CLICK, 0, 0)

    def set_text(self, text: str) -> None:
        for hwnd, kind, _ in self.children():
            if "EDIT" in kind.upper():
                done = ctypes.c_size_t()
                buffer = ctypes.create_unicode_buffer(text)   # 부르는 동안 살아 있어야 한다
                user32.SendMessageTimeoutW(hwnd, WM_SETTEXT, 0, ctypes.addressof(buffer), 2, 2000, ctypes.byref(done))
                return
        raise Fail("글 칸을 찾지 못했다")

    def close(self) -> None:
        user32.PostMessageW(self.hwnd, WM_CLOSE, 0, 0)

    def alive(self) -> bool:
        return bool(user32.IsWindow(self.hwnd))


# Windows 가 프로그램의 프로세스 안에 만드는 입력 표시 창. 프로그램이 띄운 창이 아니므로 세지 않는다.
# 이름을 아는 이 종류만 뺀다. 제목 없는 창을 통째로 빼면 프로그램의 실제 오류 창을 놓칠 수 있다.
SYSTEM_WINDOW_CLASSES = {"UAC_InputIndicatorOverlayWnd", "UAC Input Indicator"}
SKIPPED_WINDOWS: list[dict[str, str]] = []   # 그렇게 뺀 창(결과에 남긴다)


def windows_under(folder: Path) -> list[Window]:
    """실행 파일이 folder 아래에 있는 프로세스의 보이는 최상위 창(Windows 의 입력 표시 창은 뺀다)."""
    root = str(folder.resolve()).lower().rstrip("\\") + "\\"
    found: list[Window] = []
    paths: dict[int, str | None] = {}

    def each(hwnd, _):
        if not user32.IsWindowVisible(hwnd):
            return True
        pid = wt.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
        if pid.value not in paths:
            paths[pid.value] = process_path(pid.value)
        exe = paths[pid.value]
        if exe and exe.lower().startswith(root):
            kind = class_name(hwnd)
            if kind in SYSTEM_WINDOW_CLASSES:
                skipped = {"class": kind, "title": window_text(hwnd), "exe": exe}
                if skipped not in SKIPPED_WINDOWS:
                    SKIPPED_WINDOWS.append(skipped)
                return True
            found.append(Window(hwnd, pid.value, exe))
        return True
    user32.EnumWindows(EnumProc(each), 0)
    return found


def pid_alive(pid: int) -> bool:
    handle = kernel32.OpenProcess(0x1000 | 0x00100000, False, pid)   # + SYNCHRONIZE
    if not handle:
        return False
    try:
        return kernel32.WaitForSingleObject(handle, 0) == 0x102   # WAIT_TIMEOUT: 아직 돌고 있다
    finally:
        kernel32.CloseHandle(handle)


# ───────────────────────────── 시나리오의 도구

class Run:
    def __init__(self, base: Path, name: str, builds: Path, server: Releases):
        self.folder = base / name
        # 경로에 한글과 빈칸을 넣는다(실제로 쓰는 폴더가 그렇다)
        self.app = self.folder / "한글 폴더 (시험)" / "app"
        self.builds, self.server, self.name = builds, server, name
        self.checks: list[dict] = []
        self.seen: list[str] = []          # 지나가며 본 창의 제목과 글(증거로 남긴다)
        self.started: list[int] = []
        self.packages = builds / "packages"

    # 준비
    def lay(self) -> None:
        """9.0.0 과 합성한 기록·설정, 사용자가 둔 것처럼 보이는 파일을 놓는다."""
        shutil.copytree(self.builds / OLD / PRODUCT, self.app)
        catalog = json.loads((self.app / "data" / "catalog.json").read_text(encoding="utf-8"))
        records, undo = {}, []
        for index, song in enumerate(catalog["Songs"][:40]):
            chart = song["Charts"][index % len(song["Charts"])]
            key = f"{song['Id']}|{chart['Button']}|{chart['Pattern']}|{'하드' if index % 3 == 0 else '노말'}"
            score = {"Accuracy": round(90 + index * 0.21, 2), "At": f"2026-10-{1 + index % 28:02d}T12:00:00", "PlayCount": 1 + index % 5,
                     "LastPlayedAt": f"2026-11-{1 + index % 15:02d}T21:30:00", "RawScore": 900000 + index * 2100}
            records[key] = score
            if index < 3:
                undo.append({"Key": key, "Before": None, "After": score, "Related": None})
        data = {"Format": 1, "ActiveId": "p1", "Profiles": [
            {"Id": "p1", "Name": "시험 프로필", "Records": records, "Undo": undo},
            {"Id": "p2", "Name": "둘째 프로필", "Records": dict(list(records.items())[:5]), "Undo": []}]}
        (self.app / "records.json").write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
        (self.app / "records.json.bak").write_text(json.dumps({**data, "ActiveId": "p2"}, ensure_ascii=False, indent=2), encoding="utf-8")
        # Ctrl+Alt+Shift+F6~F10 (개인용 프로그램의 Ctrl+F8~F12, 개발용 검사의 F1~F5 와 겹치지 않는다)
        names = ["등록", "새로 감지하기", "저장 확인", "저장 취소", "감지 시작"]
        (self.app / "hotkeys.json").write_text(json.dumps([{"Name": n, "Key": 117 + i, "Modifiers": 7} for i, n in enumerate(names)], ensure_ascii=False), encoding="utf-8")
        (self.app / "preferences.json").write_text(json.dumps({"Fullscreen": False, "DiagnosticImages": False, "Dark": True}), encoding="utf-8")
        (self.app / "appearance.json").write_text(json.dumps({"Font": "맑은 고딕"}, ensure_ascii=False), encoding="utf-8")
        (self.app / "diagnostics").mkdir()
        (self.app / "diagnostics" / "old-session.log").write_text("사용자가 보관 중인 진단 기록\n", encoding="utf-8")
        (self.app / "내 백업").mkdir()
        (self.app / "내 백업" / "records.json").write_text("사용자가 직접 만든 백업\n", encoding="utf-8")
        (self.app / "메모.txt").write_text("사용자가 둔 파일\n", encoding="utf-8")
        (self.app / "update" / "backup" / "내가 둔 폴더").mkdir(parents=True)
        (self.app / "update" / "backup" / "내가 둔 폴더" / "note.txt").write_text("자동 업데이트의 것이 아니다\n", encoding="utf-8")
        (self.app / "update-test.json").write_text(json.dumps({"Source": self.server.base, "OverlayPort": OVERLAY_PORT, "Instance": INSTANCE}), encoding="utf-8")

    # 확인을 적는다
    def check(self, ok: bool, what: str, detail: str = "") -> None:
        self.checks.append({"ok": bool(ok), "what": what, "detail": detail})
        print(f"    {'통과' if ok else '실패'}  {what}" + (f"  ({detail})" if detail and not ok else ""))
        if not ok:
            raise Fail(what + (": " + detail if detail else ""))

    # 프로그램 켜고 끄기
    def start(self, exe: Path | None = None) -> int:
        exe = exe or self.app / EXE
        process = subprocess.Popen([str(exe)], cwd=str(exe.parent))
        self.started.append(process.pid)
        return process.pid

    def wait(self, test, seconds: float, what: str, every: float = 0.25):
        deadline = time.time() + seconds
        while True:
            value = test()
            if value:
                return value
            if time.time() > deadline:
                titles = [(w.title, Path(w.exe).parent.name) for w in windows_under(self.folder)]
                raise Fail(f"{seconds:.0f}초 안에 되지 않았다: {what} (지금 보이는 창: {titles})")
            time.sleep(every)

    def windows(self) -> list[Window]:
        found = windows_under(self.folder)
        for window in found:
            line = f"{Path(window.exe).parent.name} | {window.title} | {window.texts()[:200]!r}"
            if line not in self.seen:
                self.seen.append(line)
        return found

    def window(self, title: str, where: str | None = None, contains: str | None = None) -> Window | None:
        """제목이 title 인 창. where 는 실행 파일이 놓인 폴더의 이름(app, DJMAXLocalRecords=풀어 놓은 새 버전, program=백업)."""
        for window in self.windows():
            if window.title == title and (where is None or Path(window.exe).parent.name == where) and (contains is None or contains in window.texts()):
                return window
        return None

    def main(self) -> Window | None:
        return self.window(MAIN_TITLE, "app")

    def note(self) -> str:
        """주 화면의 업데이트 줄에 적힌 글."""
        main = self.main()
        if main is None:
            return ""
        for _, _, text in main.children():
            if text.startswith("v9.") or text.startswith("개발용"):
                return text
        return ""

    def wait_main(self, contains: str, seconds: float = 90) -> Window:
        self.wait(lambda: contains in self.note(), seconds, f"주 화면의 업데이트 줄에 \"{contains}\"")
        return self.main()

    def dialog(self, title: str, seconds: float = 30, where: str | None = None, contains: str | None = None) -> Window:
        return self.wait(lambda: self.window(title, where, contains), seconds, f"\"{title}\" 창" + (f" ({contains})" if contains else ""))

    def answer(self, title: str, button: str, contains: str | None = None, seconds: float = 30, where: str | None = None) -> str:
        """창이 뜨기를 기다려 글을 읽고 단추를 누른다. 읽은 글을 돌려준다."""
        window = self.dialog(title, seconds, where, contains)
        text = window.texts()
        if contains is not None and contains not in text:
            raise Fail(f"\"{title}\" 창의 글에 \"{contains}\"가 없다: {text[:400]!r}")
        window.click(button)
        self.wait(lambda: not window.alive() or not user32.IsWindowVisible(window.hwnd), 15, f"\"{title}\" 창이 닫힘")
        return text

    def close_app(self, seconds: float = 30) -> None:
        main = self.main()
        if main is None:
            return
        pid = main.pid
        main.close()
        self.wait(lambda: not pid_alive(pid), seconds, "프로그램이 끝남")

    def quiet(self, seconds: float = 30) -> None:
        """이 시나리오 폴더에서 켜진 프로세스가 모두 끝나기를 기다린다."""
        self.wait(lambda: not any(pid_alive(pid) for pid in self.started) and not windows_under(self.folder), seconds, "프로세스가 모두 끝남")

    # 자주 쓰는 확인
    def user_data(self) -> dict[str, str]:
        files = {name: sha256(self.app / name) for name in USER_FILES if (self.app / name).exists()}
        for extra in ["diagnostics/old-session.log", "내 백업/records.json", "메모.txt", "update/backup/내가 둔 폴더/note.txt", "update-test.json"]:
            files[extra] = sha256(self.app / extra)
        return files

    def program(self) -> dict[str, str]:
        """프로그램 파일(목록에 있는 것)과 그 밖의 파일을 모두 본다. update, diagnostics 는 뺀다."""
        return picture(self.app)

    def version(self) -> str:
        """프로그램 폴더의 목록에 적힌 버전. 교체하는 동안에는 목록이 잠깐 없을 수 있다(그때는 빈 글)."""
        try:
            return json.loads((self.app / MANIFEST).read_text(encoding="utf-8"))["Version"]
        except (OSError, ValueError, KeyError):
            return ""

    def backups(self) -> list[Path]:
        """자동 업데이트가 만든 백업 폴더(표시 파일이 든 것)."""
        root = self.app / "update" / "backup"
        return sorted(p for p in root.iterdir() if p.is_dir() and (p / "backup-info.json").exists()) if root.is_dir() else []

    def no_leftovers(self, seconds: float = 60) -> None:
        update = self.app / "update"
        self.wait(lambda: not (update / "staging").exists() and not (update / "download").exists() and not list(update.glob("failed-*")) and not list(update.glob("replaced-*")),
                  seconds, "내려받은 것과 풀어 놓은 것이 치워짐")
        self.check(not (update / "state.json").exists(), "상태 파일이 남지 않음")

    def offer(self, expect: str = "v" + NEW) -> Window:
        """[업데이트 확인]을 눌러 새 버전 안내 창을 띄운다."""
        self.main().click("업데이트 확인")
        return self.dialog("업데이트", 30, "app", expect)

    def update(self, to: str, seconds: float = 240) -> None:
        """새 버전 안내 창에서 [업데이트]를 눌러 끝까지 간다(새 버전의 주 화면이 뜰 때까지)."""
        offer = self.offer("v" + to)
        text = offer.texts()
        self.check("v" + to in text and "패치노트" in text or "시험용 릴리즈" in text, "안내 창에 새 버전과 패치노트가 보임", text[:200])
        offer.click("업데이트")
        self.wait(lambda: self.windows() and self.version() == to and "업데이트 완료" in self.note(), seconds, f"v{to} 의 주 화면이 뜨고 업데이트 줄에 완료가 적힘", 0.15)

    def evidence(self) -> None:
        self.folder.mkdir(parents=True, exist_ok=True)
        log = self.app / "update" / "update.log"
        if log.exists():
            shutil.copy2(log, self.folder / "update.log")
        (self.folder / "result.json").write_text(json.dumps({"scenario": self.name, "checks": self.checks, "windows_seen": self.seen,
                                                              "system_windows_skipped": [w for w in SKIPPED_WINDOWS if w["exe"].lower().startswith(str(self.folder.resolve()).lower())],
                                                              "server_requests": [path for _, path in self.server.log]}, ensure_ascii=False, indent=1), encoding="utf-8")


# ───────────────────────────── 시나리오

def scenario_update(run: Run) -> None:
    """정상 업데이트 → 이어서 한 번 더 → 이전 버전 복구 → 더 새 형식의 기록."""
    server = run.server
    good, later = server.release(NEW, run.packages / "good.zip"), server.release(NEXT, run.packages / "next.zip")
    server.items = [good]
    run.lay()
    before_user, old_exe = run.user_data(), sha256(run.app / EXE)
    began = time.time()
    run.start()
    run.wait_main("새 버전 v" + NEW + " 있음")
    run.check(len(run.windows()) == 1, "켤 때의 확인은 창을 띄우지 않고 주 화면의 줄에만 알림", str([w.title for w in run.windows()]))
    run.check(server.count("/releases", began) == 1 and server.count("/download", began) == 0, "켤 때 한 번만 묻고, 승인 전에는 내려받지 않음")
    offer = run.offer()
    run.check(offer.button("업데이트") and offer.button("나중에") and "v" + OLD in offer.texts(), "현재 버전·새 버전과 업데이트 / 나중에 가 보임", offer.texts()[:200])
    offer.click("나중에")
    time.sleep(3)
    run.check(server.count("/download", began) == 0 and run.main() is not None and len(run.windows()) == 1 and "새 버전" in run.note(), "나중에: 내려받지 않고 그대로 쓰며, 다시 묻는 창이 뜨지 않음")
    run.update(NEW)
    run.check(run.version() == NEW and sha256(run.app / EXE) != old_exe, "프로그램이 9.0.1 로 바뀜")
    run.check(run.user_data() == before_user, "기록·프로필·되돌리기 이력·설정·진단 기록·사용자가 둔 파일이 그대로임")
    backups = run.backups()
    run.check(len(backups) == 1 and backups[0].name.startswith("v" + OLD + "-"), "직전 버전의 백업이 하나 생김", str([b.name for b in backups]))
    info = json.loads((backups[0] / "backup-info.json").read_text(encoding="utf-8"))
    run.check(info["Program"] is True and sha256(backups[0] / "program" / EXE) == old_exe, "백업에 옛 프로그램이 있음")
    run.check(sorted(p.name for p in backups[0].iterdir()) == ["backup-info.json", "data", "program"], "백업 폴더에 프로그램·기록 사본·표시 파일만 있음(안내 글 파일을 두지 않음)", str(sorted(p.name for p in backups[0].iterdir())))
    run.check(all(sha256(backups[0] / "data" / name) == before_user[name] for name in USER_FILES), "백업에 업데이트 직전의 기록·설정 사본이 있음")
    run.no_leftovers()
    run.check(server.count("/download", began) == 1, "배포 파일을 한 번만 내려받음")
    time.sleep(2)
    run.check("업데이트 완료" in run.note() and len(run.windows()) == 1, "새 버전이 켤 때 다시 확인했지만 완료 안내를 지우지 않고 창도 띄우지 않음", run.note())

    # 켜 둔 동안 새 버전이 나와도 스스로 묻지 않는다
    asked = server.count("/releases")
    server.items = [later, good]
    time.sleep(12)
    run.check(server.count("/releases") == asked and "v" + NEXT not in run.note(), "켜 둔 동안 새 릴리즈가 나와도 스스로 다시 묻지 않음")
    run.update(NEXT)
    run.check(run.version() == NEXT and run.user_data() == before_user, "이어서 9.0.2 로 업데이트, 개인 자료 그대로")
    backups = run.backups()
    run.check(len(backups) == 1 and backups[0].name.startswith("v" + NEW + "-"), "성공한 뒤에는 직전 버전(9.0.1)의 백업 하나만 남음", str([b.name for b in backups]))
    run.check((run.app / "update" / "backup" / "내가 둔 폴더" / "note.txt").exists() and (run.app / "내 백업" / "records.json").exists(), "사용자가 만든 백업과 폴더는 정리되지 않음")
    run.no_leftovers()

    # 업데이트한 뒤에 기록이 쌓인 상태에서 이전 버전으로 되돌린다
    run.close_app()
    run.quiet()
    records = json.loads((run.app / "records.json").read_text(encoding="utf-8"))
    records["Profiles"][0]["Records"]["varchive:999999|4|SC|하드"] = {"Accuracy": 99.99, "At": "2026-11-16T12:00:00", "PlayCount": 1, "LastPlayedAt": None, "RawScore": 999900}
    (run.app / "records.json").write_text(json.dumps(records, ensure_ascii=False, indent=2), encoding="utf-8")
    after_update = run.user_data()
    run.start()
    main = run.wait_main("v" + NEXT)
    run.wait(lambda: run.main() and run.main().button("이전 버전 복구"), 20, "[이전 버전 복구] 단추가 보임")
    run.main().click("이전 버전 복구")
    text = run.answer("이전 버전 복구", "예", "v" + NEW, where="app")
    run.check("그대로 둡니다" in text, "복구를 묻는 창이 기록을 그대로 둔다고 알림", text[:300])
    run.wait(lambda: run.windows() and run.version() == NEW and "되돌림" in run.note(), 180, "9.0.1 의 주 화면이 뜨고 되돌림이 적힘", 0.15)
    run.check(run.user_data() == after_update, "되돌린 뒤에도 업데이트 후에 쌓인 기록이 그대로임(과거 사본으로 바뀌지 않음)")
    run.check(len(run.backups()) == 1 and run.main().button("이전 버전 복구") is None, "백업은 남고, 같은 버전이라 복구 단추는 숨음")
    run.no_leftovers()
    run.close_app()
    run.quiet()

    # 이전 버전이 읽지 못하는 더 새 형식의 기록: 건드리지 않고 알린다
    records["Format"] = 2
    (run.app / "records.json").write_text(json.dumps(records, ensure_ascii=False, indent=2), encoding="utf-8")
    newer = sha256(run.app / "records.json")
    run.start()
    text = run.answer("실행 오류", "확인", "새 버전에서 저장된 형식", 60, "app")
    run.check("건드리지 않았습니다" in text and "update\\backup" in text and "https://github.com/Zionee148/DJMAXLocalRecords" in text, "더 새 형식의 기록은 읽지 않고 선택지와 GitHub 안내를 알림", text[:400])
    run.quiet()
    run.check(sha256(run.app / "records.json") == newer and not (run.app / "records.json.tmp").exists(), "그 기록 파일은 바뀌지 않음")


def scenario_checks(run: Run) -> None:
    """대상이 아닌 릴리즈, 조회 실패, 자동으로 받을 수 없는 릴리즈, 시작할 수 없는 상태."""
    server = run.server
    good = server.release(NEW, run.packages / "good.zip")
    server.items = [server.release(OLD, run.packages / "good.zip"), server.release("8.9.9", run.packages / "good.zip"),
                    server.release("9.5.0", run.packages / "good.zip", prerelease=True), server.release("9.6.0", run.packages / "good.zip", draft=True),
                    server.release("9.7.0", run.packages / "good.zip", tag="v9.7.0-beta"), server.release("9.8.0", run.packages / "good.zip", tag="9.8.0")]
    run.lay()
    before_user, before_program = run.user_data(), run.program()
    run.start()
    run.wait_main("최신 버전")
    run.check(len(run.windows()) == 1, "같은 버전·낮은 버전·시험판·초안·다른 이름표는 안내하지 않음", run.note())
    run.main().click("업데이트 확인")
    run.answer("업데이트", "확인", "최신 버전입니다", where="app")

    # 자동으로 받을 수 없는 릴리즈(첨부 파일의 이름이 다르다)
    server.items = [server.release(NEW, run.packages / "good.zip", asset="DJMAXLocalRecords.zip")]
    run.main().click("업데이트 확인")
    text = run.answer("업데이트", "아니요", "자동으로 업데이트할 수 없습니다", where="app")
    run.check("직접 받아야 함" in run.note() and server.count("/download") == 0, "받을 파일이 갖춰지지 않은 릴리즈는 내려받지 않고 알림", run.note())

    # 시작할 수 없는 상태: 감지 중, 다른 창이 열려 있음
    server.items = [good]
    run.main().click("감지 시작")
    time.sleep(1)
    run.offer().click("업데이트")
    run.answer("업데이트", "확인", "감지 중입니다", where="app")
    run.main().click("중지 / 숨기기")
    time.sleep(1)
    run.main().click("등록한 기록 보기")
    records = run.wait(lambda: next((w for w in run.windows() if w.title.startswith("등록한 기록 보기")), None), 30, "기록 목록 창")
    run.offer().click("업데이트")
    run.answer("업데이트", "확인", "다른 창이 열려 있습니다", where="app")
    records.close()
    run.wait(lambda: not any(w.title.startswith("등록한 기록 보기") for w in run.windows()), 15, "기록 목록 창이 닫힘")
    run.check(server.count("/download") == 0 and not (run.app / "update" / "staging").exists(), "막힌 동안 내려받지 않음")
    run.close_app()
    run.quiet()

    # 인터넷이 없을 때: 최신이라고 적지 않고, 프로그램은 그대로 쓸 수 있다
    server.stop()
    run.start()
    run.wait_main("새 버전 확인 실패")
    run.check(len(run.windows()) == 1 and "최신" not in run.note(), "조회에 실패하면 최신 버전이라고 적지 않고 창도 띄우지 않음", run.note())
    run.main().click("업데이트 확인")
    run.answer("업데이트", "확인", "확인하지 못했습니다", where="app")
    saved = sha256(run.app / "records.json")
    run.main().click("새로")
    ask = run.dialog("새 프로필", 15, "app")
    ask.set_text("조회 실패 뒤에 만든 프로필")
    ask.click("확인")
    run.wait(lambda: sha256(run.app / "records.json") != saved, 15, "프로필을 만들어 기록 파일이 저장됨")
    names = [profile["Name"] for profile in json.loads((run.app / "records.json").read_text(encoding="utf-8"))["Profiles"]]
    run.check("조회 실패 뒤에 만든 프로필" in names, "조회에 실패해도 기존 기능(프로필 만들기와 저장)을 쓸 수 있음", str(names))
    run.close_app()
    run.quiet()
    program = run.program()
    for name in ("records.json", "records.json.bak", "records-view.json"):
        before_program.pop(name, None)
        program.pop(name, None)
    run.check(program == before_program and run.version() == OLD, "프로그램 파일은 그대로임")


def scenario_cancel(run: Run) -> None:
    """내려받는 도중에 취소한다. 그 뒤에 다시 업데이트한다."""
    server = run.server
    server.items = [server.release(NEW, run.packages / "good.zip", mode="slow")]
    run.lay()
    before_user, before_program = run.user_data(), run.program()
    run.start()
    run.wait_main("새 버전 v" + NEW)
    run.offer().click("업데이트")
    progress = run.dialog("업데이트", 30, "app", "MB")
    run.check(progress.button("취소") is not None and "다운로드" in progress.texts(), "진행 창에 단계와 취소 단추가 보임", progress.texts()[:200])
    time.sleep(2)
    progress.click("취소")
    text = run.answer("업데이트", "확인", "업데이트를 취소했습니다", 60, "app")
    run.check("바뀌지 않았습니다" in text, "취소하면 바뀐 것이 없다고 알림")
    run.check(run.program() == before_program and run.user_data() == before_user and run.version() == OLD, "프로그램과 개인 자료가 그대로임")
    run.check(not run.backups(), "쓰지 않은 백업 폴더가 남지 않음")
    run.no_leftovers()
    # 조작 제한이 풀렸는가: 저장이 된다
    saved = sha256(run.app / "records.json")
    run.main().click("새로")
    ask = run.dialog("새 프로필", 15, "app")
    ask.set_text("취소한 뒤에 만든 프로필")
    ask.click("확인")
    run.wait(lambda: sha256(run.app / "records.json") != saved, 15, "프로필을 만들어 기록 파일이 저장됨")
    names = [profile["Name"] for profile in json.loads((run.app / "records.json").read_text(encoding="utf-8"))["Profiles"]]
    run.check("취소한 뒤에 만든 프로필" in names, "취소한 뒤 조작 제한이 풀림(프로필을 만들고 저장함)", str(names))
    # 다시 하면 된다
    server.items = [server.release(NEW, run.packages / "good.zip")]
    after_cancel = run.user_data()
    run.update(NEW)
    run.check(run.version() == NEW and run.user_data() == after_cancel, "취소한 뒤 다시 업데이트하면 정상으로 끝남")
    run.no_leftovers()


def scenario_download(run: Run) -> None:
    """받는 도중 끊김, 손상된 파일, 다른 버전이 든 파일."""
    server = run.server
    run.lay()
    before_user, before_program = run.user_data(), run.program()
    run.start()
    # 릴리즈는 차례가 왔을 때 만든다(같은 이름의 첨부 파일을 보내는 방식이 차례마다 다르다)
    cases = [(lambda: server.release(NEW, run.packages / "good.zip", mode="truncate"), "끊겼", "받는 도중 연결이 끊김"),
             (lambda: server.release(NEW, run.packages / "good.zip", mode="corrupt"), "SHA-256", "손상된 파일(크기는 같고 내용이 다름)"),
             (lambda: server.release("9.0.5", run.packages / "good.zip"), "버전", "고른 버전과 다른 버전이 든 파일")]
    server.items = [server.release(NEW, run.packages / "good.zip")]
    run.wait_main("새 버전")
    for make, word, what in cases:
        release = make()
        server.items = [release]
        run.offer(release["tag_name"]).click("업데이트")
        text = run.answer("업데이트 실패", "확인", word, 120, "app")
        run.check("계속 사용합니다" in text and "바뀌지 않았습니다" in text, f"{what}: 원인과 다음 행동을 알림", text[:300])
        run.check(run.program() == before_program and run.user_data() == before_user and not run.backups(), f"{what}: 프로그램·개인 자료가 그대로이고 백업 폴더도 남지 않음")
        run.no_leftovers()
        run.check(run.main() is not None and run.main().enabled("업데이트 확인"), f"{what}: 프로그램을 계속 쓸 수 있음")


def scenario_backup(run: Run) -> None:
    """백업을 만들 수 없으면 교체를 시작하지 않는다."""
    server = run.server
    server.items = [server.release(NEW, run.packages / "good.zip")]
    run.lay()
    # 백업 폴더가 있어야 할 자리에 파일을 둔다(사용자가 둔 폴더는 옮겨 둔다)
    shutil.rmtree(run.app / "update" / "backup")
    (run.app / "update" / "backup").write_text("백업 폴더의 자리를 막는 파일\n", encoding="utf-8")
    blocker = sha256(run.app / "update" / "backup")
    user = {name: sha256(run.app / name) for name in USER_FILES}
    before_program = run.program()
    run.start()
    run.wait_main("새 버전")
    run.offer().click("업데이트")
    text = run.answer("업데이트 실패", "확인", "백업하지 못해", 180, "app")
    run.check("프로그램을 바꾸지 않았습니다" in text or "바뀌지 않았습니다" in text, "백업 실패: 교체하지 않았다고 알림", text[:300])
    run.check(run.program() == before_program and {name: sha256(run.app / name) for name in USER_FILES} == user and run.version() == OLD, "백업 실패: 프로그램·개인 자료가 그대로임")
    run.check(sha256(run.app / "update" / "backup") == blocker, "막고 있던 파일은 그대로임")
    run.wait(lambda: not (run.app / "update" / "staging").exists() and not (run.app / "update" / "download").exists(), 60, "풀어 놓은 것이 치워짐")
    run.check(run.main().enabled("업데이트 확인"), "프로그램을 계속 쓸 수 있음")


def swap_failure(run: Run, package: str, prepare, release_word: str, what: str) -> None:
    server = run.server
    server.items = [server.release(NEW, run.packages / package)]
    run.lay()
    held = prepare()
    before_user, before_program = run.user_data(), run.program()
    old_pid = run.start()
    run.wait_main("새 버전")
    run.offer().click("업데이트")
    notice = run.dialog("업데이트 실패", 240, "DJMAXLocalRecords", "기존 버전으로 되돌렸습니다")
    text = notice.texts()
    run.check(release_word in text and "업데이트하기 전 그대로" in text, f"{what}: 기존 버전으로 되돌렸다고 실패 원인과 함께 알림", text[:400])
    run.check(not pid_alive(old_pid), "그때 기존 프로그램은 이미 끝나 있었음(끝난 뒤에도 안내가 보임)")
    run.check(run.program() == before_program and run.user_data() == before_user and run.version() == OLD, f"{what}: 프로그램·개인 자료가 바꾸기 전과 똑같음")
    if held is not None:
        held.close()
    notice.click("확인")
    run.wait_main("지난 업데이트 실패", 120)
    run.check(run.version() == OLD and not run.backups(), "기존 버전이 다시 켜지고, 쓰지 않은 백업 폴더는 남지 않음")
    run.no_leftovers()
    run.check(run.program() == before_program and run.user_data() == before_user, "다시 켜진 뒤에도 그대로임")


def scenario_swap_locked(run: Run) -> None:
    """교체 도중의 실패 ①: 다른 프로그램이 프로그램 파일을 잡고 있다."""
    swap_failure(run, "good.zip", lambda: open(run.app / "overlay.html", "rb"), "실패 원인", "다른 프로그램이 잡은 파일")


def scenario_conflict(run: Run) -> None:
    """새 버전의 파일이 올 자리에 사용자가 둔 파일·폴더가 있다 → 바꾸기 전에 멈춘다(덮어쓰지도, 백업으로 옮기지도 않는다).

    옛 파일을 다 옮긴 뒤 새 파일을 들여오다 막히는 경우(교체 도중의 실패)는 이제 여기까지 오지 않는다.
    그 경우의 되돌리기는 자체 검사(--self-test)의 실패 끼워 넣기가 본다.
    """
    server = run.server
    server.items = [server.release(NEW, run.packages / "extra.zip")]
    run.lay()
    mine = run.app / "data" / "new-in-901.txt"   # 새 버전에 새로 생기는 파일과 같은 이름. 지금 설치의 목록에는 없다

    def refused(what: str) -> None:
        before_user, before_program = run.user_data(), run.program()
        run.offer().click("업데이트")
        text = run.answer("업데이트 실패", "확인", "이름이 같은", 240, "app")
        run.check("data\\new-in-901.txt" in text and "덮어쓰지 않고" in text and "바뀌지 않았습니다" in text, f"{what}: 그 이름을 대며 덮어쓰지 않고 멈췄다고 알림", text[:500])
        run.check(run.program() == before_program and run.user_data() == before_user and run.version() == OLD, f"{what}: 프로그램·개인 자료·사용자가 둔 것이 그대로임")
        run.check(not run.backups(), f"{what}: 백업 폴더가 남지 않음(아무것도 옮기지 않았다)")
        run.no_leftovers()
        run.check(run.main().enabled("업데이트 확인"), f"{what}: 프로그램을 계속 쓸 수 있음")

    mine.write_text("사용자가 둔 파일\n", encoding="utf-8")
    run.start()
    run.wait_main("새 버전")
    refused("사용자의 파일")
    run.check(mine.read_text(encoding="utf-8") == "사용자가 둔 파일\n", "사용자의 파일 내용이 그대로임")
    mine.unlink()
    mine.mkdir()
    (mine / "keep.txt").write_text("새 파일이 올 자리에 사용자가 둔 폴더\n", encoding="utf-8")
    refused("사용자의 폴더")
    run.check((mine / "keep.txt").exists(), "사용자의 폴더가 그대로임")
    # 사용자가 그것을 치우면 업데이트된다
    shutil.rmtree(mine)
    before_user = run.user_data()
    run.update(NEW)
    run.check(mine.read_bytes() == b"only in 9.0.1\n" and run.version() == NEW and run.user_data() == before_user, "치운 뒤에는 업데이트되고 새 버전의 파일이 그 자리에 들어옴")
    run.no_leftovers()


def scenario_cleanup(run: Run) -> None:
    """지난 시도의 임시 파일을 다른 프로그램이 잡고 있어 시작할 때 하는 지난 업데이트 파일의 정리가 끝나지 않는 사이에 업데이트한다.

    이 정리는 2초마다(40초까지) 같은 자리(update\\download, update\\staging)를 다시 지운다. 멈추지 않으면 새로 받은 것과 푼 것을 지워 업데이트가 실패한다.
    """
    server = run.server
    server.items = [server.release(NEW, run.packages / "good.zip")]
    run.lay()
    stale = run.app / "update" / "staging" / "held-by-another-program.bin"
    stale.parent.mkdir(parents=True)
    stale.write_bytes(b"left by an earlier attempt\n")
    before_user = run.user_data()
    with open(stale, "rb"):   # 지울 수 없게 잡고 있는다
        run.start()
        run.wait_main("새 버전")
        run.update(NEW)
        run.check(stale.exists(), "잡혀 있던 지난 파일이 끝까지 남아 있었음(지난 파일의 정리가 끝나지 못하는 조건이었다)")
        run.check(run.version() == NEW and run.user_data() == before_user, "그 사이에 한 업데이트가 끝까지 됨, 개인 자료 그대로")
    run.no_leftovers()


def bad_start(run: Run):
    server = run.server
    server.items = [server.release(NEW, run.packages / "badstart.zip")]
    run.lay()
    before_user, before_program = run.user_data(), run.program()
    run.start()
    run.wait_main("새 버전")
    run.offer().click("업데이트")
    # 새 버전은 설치되지만 켜면 실행 오류가 난다
    run.answer("실행 오류", "확인", None, 240, "app")
    return before_user, before_program


def scenario_badstart_rollback(run: Run) -> None:
    """새 버전이 켜지지 않는다 → 설치를 맡은 쪽이 묻고 이전 버전으로 되돌린다."""
    before_user, before_program = bad_start(run)
    ask = run.dialog("업데이트", 60, "DJMAXLocalRecords", "정상으로 시작되지 않았습니다")
    text = ask.texts()
    run.check("이전 버전(v" + OLD + ")으로 되돌릴까요?" in text and "그대로 둡니다" in text, "새 버전이 켜지지 않으면 성공으로 치지 않고 되돌릴지 물음", text[:400])
    run.check(run.version() == NEW and len(run.backups()) == 1, "그때까지 복구 자료(직전 버전의 백업)가 남아 있음")
    ask.click("예")
    run.answer("업데이트", "확인", "이전 버전으로 되돌렸습니다", 120, "DJMAXLocalRecords")
    run.wait_main("지난 업데이트 실패", 120)
    run.check(run.program() == before_program and run.user_data() == before_user and run.version() == OLD, "프로그램·개인 자료가 업데이트 전과 똑같음")
    run.no_leftovers()


def scenario_badstart_manual(run: Run) -> None:
    """새 버전이 켜지지 않는데 그대로 둔다 → 프로그램이 켜지지 않는 상태에서 백업의 실행 파일로 되돌린다."""
    before_user, before_program = bad_start(run)
    ask = run.dialog("업데이트", 60, "DJMAXLocalRecords", "정상으로 시작되지 않았습니다")
    text = ask.texts()
    run.check("program\\" + EXE in text and "https://github.com/Zionee148/DJMAXLocalRecords" in text, "그대로 둘 때 되돌리려면 실행할 파일과 GitHub 안내를 알려 줌", text[:500])
    ask.click("아니요")
    run.quiet(60)
    backups = run.backups()
    run.check(len(backups) == 1 and run.version() == NEW, "프로그램이 켜지지 않는 채로 백업이 남아 있음")
    run.check(str(backups[0] / "program" / EXE) in text, "창이 알려 준 파일이 실제로 그 백업의 실행 파일임", text[:500])
    # 사용자가 백업 폴더의 실행 파일을 직접 켠다
    run.start(backups[0] / "program" / EXE)
    text = run.answer("이전 버전 복구", "예", "이 버전으로 되돌릴까요?", 60, "program")
    run.check("v" + OLD in text and "그대로 둡니다" in text, "백업의 실행 파일이 되돌릴지 묻고 기록은 그대로 둔다고 알림", text[:300])
    run.wait_main("이전 버전으로 되돌림", 180)
    run.check(run.program() == before_program and run.user_data() == before_user and run.version() == OLD, "프로그램·개인 자료가 업데이트 전과 똑같음")
    run.no_leftovers()


def interrupt(run: Run, package: str) -> Path:
    """교체가 도중에 멈춘 폴더를 만든다. 백업 폴더를 돌려준다.

    옛 프로그램 파일과 목록은 백업으로 옮겨졌고, 새 파일은 모두 들어왔지만 새 목록은 아직 들어오지 않았다(목록을 맨 나중에 넣는다).
    설치를 맡은 쪽이 돌던 자리(풀어 놓은 새 버전)는 실제처럼 남아 있고, 상태 파일은 "교체 중"이다.
    그 순간에 전원이 꺼진 것을 실제로 만든 것이 아니라, 그때의 폴더 모습을 파일로 만든 것이다.
    """
    backup = run.app / "update" / "backup" / f"v{OLD}-20261116-200000"
    program = backup / "program"
    manifest = json.loads((run.app / MANIFEST).read_text(encoding="utf-8"))
    for name in [item["Path"] for item in manifest["Files"]] + [MANIFEST]:
        (program / name).parent.mkdir(parents=True, exist_ok=True)
        shutil.move(run.app / name, program / name)
    (backup / "data").mkdir()
    for name in USER_FILES:
        shutil.copy2(run.app / name, backup / "data" / name)
    made = f"{datetime.now():%Y-%m-%d %H:%M:%S}"
    (backup / "backup-info.json").write_text(json.dumps({"Product": PRODUCT, "Version": OLD, "ReplacedBy": NEW, "Made": made, "Program": False}), encoding="utf-8")
    staging = run.app / "update" / "staging"
    with zipfile.ZipFile(run.packages / package) as archive:
        archive.extractall(staging)
    for path in sorted((staging / PRODUCT).rglob("*")):
        relative = path.relative_to(staging / PRODUCT)
        if path.is_file() and relative.as_posix() != MANIFEST:
            (run.app / relative).parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(path, run.app / relative)
    (run.app / "update" / "state.json").write_text(json.dumps({"Phase": "swapping", "From": OLD, "To": NEW, "Backup": backup.name, "At": made}), encoding="utf-8")
    return backup


def scenario_interrupted_restore(run: Run) -> None:
    """교체가 새 파일을 다 들여온 뒤 새 목록을 넣기 전에 멈췄다 → 백업의 실행 파일로 되돌린다 → 같은 업데이트를 다시 한다.

    새 버전에만 있는 파일이 되돌린 뒤에 남으면, 다음 업데이트가 그것을 사용자의 파일로 보고 멈춘다.
    """
    server = run.server
    server.items = [server.release(NEW, run.packages / "extra.zip")]
    run.lay()
    before_user, before_program = run.user_data(), run.program()
    backup = interrupt(run, "extra.zip")
    stray = run.app / "data" / "new-in-901.txt"
    run.check(stray.exists() and run.version() == "" and not (run.app / EXE).samefile(backup / "program" / EXE), "만든 상태: 새 버전에만 있는 파일이 들어와 있고 프로그램 폴더에 목록이 없음")
    run.start(backup / "program" / EXE)
    text = run.answer("이전 버전 복구", "예", "이 버전으로 되돌릴까요?", 60, "program")
    run.check("v" + OLD in text and "그대로 둡니다" in text, "백업의 실행 파일이 되돌릴지 물음", text[:300])
    run.wait_main("이전 버전으로 되돌림", 180)
    run.check(not stray.exists(), "새 버전에만 있던 파일이 되돌린 뒤에 남지 않음")
    run.check(run.program() == before_program and run.user_data() == before_user and run.version() == OLD, "프로그램·개인 자료가 업데이트 전과 똑같음")
    run.no_leftovers()
    run.update(NEW)
    run.check(stray.read_bytes() == b"only in 9.0.1\n" and run.version() == NEW and run.user_data() == before_user, "같은 업데이트를 다시 하면 끝까지 됨, 개인 자료 그대로")
    run.no_leftovers()


def scenario_interrupted_copyback(run: Run) -> None:
    """같은 멈춤 → 사용자가 안내대로 백업의 program 폴더를 프로그램 폴더에 복사해 덮어쓴다 → 같은 업데이트를 다시 한다.

    손으로 복사하면 새 버전에만 있는 파일이 남는다. 다음 업데이트가 준비할 때 그것을 가려내 치워 둔다.
    """
    server = run.server
    server.items = [server.release(NEW, run.packages / "extra.zip")]
    run.lay()
    before_user = run.user_data()
    backup = interrupt(run, "extra.zip")
    shutil.copytree(backup / "program", run.app, dirs_exist_ok=True)
    stray = run.app / "data" / "new-in-901.txt"
    run.check(stray.exists() and run.version() == OLD, "손으로 되돌린 뒤: 옛 버전이고, 새 버전에만 있던 파일이 남아 있음")
    run.start()
    text = run.answer("업데이트", "확인", "도중에 멈췄습니다", 60, "app")
    run.check("기록과 설정은 건드리지 않았습니다" in text and str(backup / "program") in text, "켜면 지난 업데이트가 도중에 멈췄다고 백업 폴더와 함께 알림", text[:400])
    run.wait_main("새 버전")
    run.check(run.user_data() == before_user, "개인 자료 그대로")
    run.update(NEW)
    run.check(stray.read_bytes() == b"only in 9.0.1\n" and run.version() == NEW and run.user_data() == before_user, "남아 있던 파일 때문에 멈추지 않고 업데이트가 끝까지 됨, 개인 자료 그대로")
    run.no_leftovers()
    log = (run.app / "update" / "update.log").read_text(encoding="utf-8")
    run.check("지난 교체가 남긴 새 버전의 파일 1개를 치워 둠" in log, "그 파일 하나만 치워 두었다고 기록에 남음")


SCENARIOS = {
    "update": scenario_update,
    "checks": scenario_checks,
    "cancel": scenario_cancel,
    "download": scenario_download,
    "backup": scenario_backup,
    "swap-locked": scenario_swap_locked,
    "conflict": scenario_conflict,
    "cleanup": scenario_cleanup,
    "badstart-rollback": scenario_badstart_rollback,
    "badstart-manual": scenario_badstart_manual,
    "interrupted-restore": scenario_interrupted_restore,
    "interrupted-copyback": scenario_interrupted_copyback,
}


def run(name: str, wanted: list[str], keep: bool) -> int:
    builds = OUT / name / "builds"
    if not (builds / "build-info.json").exists():
        print(f"멈춤: 빌드가 없다. 먼저 build 를 한다: {builds}", file=sys.stderr)
        return 1
    stamp = f"{datetime.now():%Y%m%d-%H%M%S}"
    base = OUT / name / "runs" / stamp
    results = []
    for index, key in enumerate(wanted or list(SCENARIOS)):
        if key not in SCENARIOS:
            print(f"멈춤: 그런 시나리오가 없다: {key} (있는 것: {', '.join(SCENARIOS)})", file=sys.stderr)
            return 1
        print(f"[{key}] {SCENARIOS[key].__doc__.strip()}")
        server = Releases(SERVER_PORT)
        current = Run(base, key, builds, server)
        began, error = time.time(), None
        try:
            SCENARIOS[key](current)
            if current.main() is not None:
                current.close_app()
            current.quiet(60)
        except Fail as failure:
            error = str(failure)
        except Exception:   # 도구의 잘못도 실패로 적는다
            error = traceback.format_exc()
        finally:
            try:
                server.stop()
            except Exception:
                pass
            # 남은 창을 닫는다(이 시나리오 폴더에서 켜진 것만. 프로세스를 이름으로 끝내지 않는다)
            for _ in range(20):
                left = windows_under(current.folder)
                if not left and not any(pid_alive(pid) for pid in current.started):
                    break
                for window in left:
                    for label in ("확인", "아니요", "취소", "나중에"):
                        button = window.button(label)
                        if button:
                            user32.PostMessageW(button, BM_CLICK, 0, 0)
                            break
                    else:
                        window.close()
                time.sleep(1)
            current.evidence()
        passed = error is None
        still = [pid for pid in current.started if pid_alive(pid)] + [w.pid for w in windows_under(current.folder)]
        if still:
            print(f"    ! 끝나지 않은 프로세스가 있다: {sorted(set(still))}")
        if error:
            print(f"    실패: {error}")
        results.append({"scenario": key, "result": "통과" if passed else "실패", "checks": len(current.checks), "seconds": round(time.time() - began, 1),
                        "reason": error or "", "left_running": sorted(set(still))})
        if passed and not keep and not still:
            shutil.rmtree(current.app, ignore_errors=True)
    lines = ["scenario\tresult\tchecks\tseconds\treason"] + [f"{r['scenario']}\t{r['result']}\t{r['checks']}\t{r['seconds']}\t{r['reason'].splitlines()[-1] if r['reason'] else ''}" for r in results]
    (base / "summary.tsv").write_text("\n".join(lines) + "\n", encoding="utf-8")
    info = json.loads((builds / "build-info.json").read_text(encoding="utf-8"))
    (base / "summary.json").write_text(json.dumps({"ran_at": stamp, "build": info, "results": results}, ensure_ascii=False, indent=1), encoding="utf-8")
    print()
    for line in lines[1:]:
        print(line)
    print("결과:", base)
    return 0 if all(r["result"] == "통과" for r in results) else 1


def manual(name: str) -> int:
    builds = OUT / name / "builds"
    if not (builds / "build-info.json").exists():
        print(f"멈춤: 빌드가 없다. 먼저 build 를 한다: {builds}", file=sys.stderr)
        return 1
    base = OUT / name / "manual"
    if base.exists():
        print(f"멈춤: 이미 있다. 지우거나 새 이름으로 build 한다: {base}", file=sys.stderr)
        return 1
    server = Releases(SERVER_PORT)
    current = Run(OUT / name, "manual", builds, server)
    current.lay()
    server.items = [server.release(NEW, current.packages / "good.zip", mode="slow", notes="# v9.0.1 패치노트\n\n- 자동 업데이트를 직접 확인하기 위한 시험용 릴리즈입니다.\n- 업데이트한 뒤 주 화면의 업데이트 줄에 v9.0.1 이 보이면 됩니다.")]
    print("시험 폴더:", current.app)
    print("실행 파일:", current.app / EXE)
    print(f"가짜 릴리즈 서버: {server.base} (v{NEW} 하나가 올라가 있다. 진행 창을 보고 취소도 해 볼 수 있게 40초쯤에 걸쳐 천천히 보낸다). 끝내려면 Ctrl+C")
    try:
        while True:
            time.sleep(1)
    except KeyboardInterrupt:
        server.stop()
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description="자동 업데이트를 시험용 빌드와 가짜 릴리즈 서버로 시험한다.")
    parser.add_argument("action", choices=["build", "run", "manual"])
    parser.add_argument("name", help="결과 이름(data\\updatetest\\<이름>)")
    parser.add_argument("scenarios", nargs="*", help="run 에서 돌릴 시나리오(주지 않으면 전부)")
    parser.add_argument("--worktree", action="store_true", help="build: 커밋하지 않은 작업 폴더의 소스로 만든다")
    parser.add_argument("--keep", action="store_true", help="run: 통과한 시나리오의 app 폴더도 남긴다")
    args = parser.parse_args()
    for stream in (sys.stdout, sys.stderr):
        stream.reconfigure(errors="replace", line_buffering=True)
    try:
        if args.action == "build":
            return build(args.name, args.worktree)
        if args.action == "run":
            return run(args.name, args.scenarios, args.keep)
        return manual(args.name)
    except bundle.Stop as stop:
        print(f"멈춤: {stop}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
