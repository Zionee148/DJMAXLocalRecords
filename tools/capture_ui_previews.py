"""지금 Windows 배율에서 프로그램의 창들을 PNG 이미지로 저장한다(지원 범위의 배율 시험에서 글이 잘리거나 겹치는지 미리 보려는 것).

쓰는 법 (프로젝트 폴더에서, Debug 빌드 뒤):
    .venv\\Scripts\\python.exe tools\\capture_ui_previews.py <이름> [--build <Debug 빌드 폴더>]
    보기: .venv\\Scripts\\python.exe tools\\capture_ui_previews.py scale-125

하는 일
  1. Debug 빌드를 새 폴더 data\\scaletest\\<이름>\\app 에 복사한다(기록·설정·지난 결과는 복사하지 않는다. 같은 이름이 있으면 멈춘다).
  2. 그 안에서 개발용 검사 --ui-check, --dialogs-check 를 돌린다. 이 검사들이 주 화면과 세부 창을 띄워 PNG 로 저장한다(합성 자료만 쓴다).
  3. 저장된 그림(*.png)을 data\\scaletest\\<이름>\\ 으로 복사하고, 그때의 Windows 배율(이 프로세스가 본 시스템 DPI)과 그림 크기를 summary.json 에 적는다.

그림은 화면을 캡처한 것이 아니라 프로그램이 창을 비트맵에 직접 그린 것이다. 화면에 실제로 보이는 모양과 대체로 같지만 같다고 보장하지는 않는다.
흐려 보이는 현상(Windows 가 창을 비트맵째 확대한 경우)은 이 그림에 나오지 않는다. 그것은 사람이 화면에서 직접 확인한다.
실제 게임, 개인 기록은 쓰지 않는다. 검사들은 창을 잠깐 띄우고 포트 18766·18768 을 잠깐 쓴다.
"""
from __future__ import annotations

import ctypes
import json
import shutil
import struct
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
BUILD = ROOT / "app" / "bin" / "Debug" / "net9.0-windows"
OUT = ROOT / "data" / "scaletest"
EXE = "DJMAXLocalRecords.exe"
SKIP = {"records.json", "records.json.bak", "records.json.tmp", "hotkeys.json", "preferences.json", "records-view.json", "appearance.json", "UserData",
        "test-error.txt", "test-data", "diagnostics", "diagnostics-check", "dev-fixtures", "probe-preferences.json"}
SKIP_ENDS = ("-validation.json", "-validation.txt", ".png")
CHECKS = [["--ui-check"], ["--dialogs-check"]]


def png_size(path: Path):
    with path.open("rb") as f:
        head = f.read(24)
    return struct.unpack(">II", head[16:24]) if head[:8] == b"\x89PNG\r\n\x1a\n" else (0, 0)


def main(argv: list) -> int:
    args = list(argv)
    build = BUILD
    if "--build" in args:
        k = args.index("--build")
        build = Path(args[k + 1]).resolve()
        del args[k:k + 2]
    if len(args) != 1 or args[0].startswith("-"):
        print(__doc__)
        return 2
    folder = OUT / args[0]
    if folder.exists():
        print(f"멈춤: 이미 있는 이름이다: {folder}")
        return 2
    if not (build / EXE).exists():
        print(f"멈춤: Debug 빌드가 없다: {build}")
        return 2
    app = folder / "app"
    shutil.copytree(build, app, ignore=lambda d, names: [n for n in names if n in SKIP or n.endswith(SKIP_ENDS)])
    # 이 프로세스가 본 시스템 DPI. 새로 뜨는 프로그램도 같은 값을 본다(프로그램은 배율을 시스템 단위로만 안다)
    ctypes.windll.user32.SetProcessDPIAware()
    dpi = ctypes.windll.user32.GetDpiForSystem()
    rows = []
    for check in CHECKS:
        error = app / "test-error.txt"
        error.unlink(missing_ok=True)
        started = time.time()
        try:
            code = subprocess.run([str(app / EXE), *check], cwd=app, timeout=240, stdin=subprocess.DEVNULL,
                                  stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL).returncode
        except subprocess.TimeoutExpired:
            code = None
        why = error.read_text(encoding="utf-8", errors="replace").splitlines()[0] if error.exists() and error.stat().st_size else ""
        rows.append({"check": " ".join(check), "exit": code, "seconds": round(time.time() - started, 1), "why": why})
        print(f"{' '.join(check)}: 종료 코드 {code}{' · ' + why if why else ''}")
    pictures = []
    for png in sorted(app.glob("*.png")):
        shutil.copy2(png, folder / png.name)
        w, h = png_size(png)
        pictures.append({"name": png.name, "width": w, "height": h})
    summary = {"ran_at": time.strftime("%Y-%m-%d %H:%M:%S"), "system_dpi": dpi, "scale_percent": round(dpi * 100 / 96), "build": str(build), "checks": rows, "pictures": pictures}
    (folder / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=1), encoding="utf-8")
    print(f"배율 {summary['scale_percent']}% (시스템 DPI {dpi}), 그림 {len(pictures)}장 → {folder}")
    for p in pictures:
        print(f"  {p['name']}: {p['width']}×{p['height']}")
    return 0 if all(r["exit"] == 0 for r in rows) else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
