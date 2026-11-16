"""제외 목록(연출 구간 + 가려진 프레임)을 따로 검산한다 (독립 검산, 2026-11-14).

"못 읽은 정상 프레임 N장"은 제외 목록을 믿고 센 숫자다. 목록이 넓게 잘못 잡혀 있으면 판독 실패가 제외 뒤에 숨는다.
그 목록은 fever.py 가 그림의 달라짐으로 만든 것이고, RIGHT 의 것(v17)은 그림으로 확인한 적이 없었다.

  stats   모듈이 읽은 결과와 목록을 포갠다. 제외된 프레임 가운데 모듈이 못 읽은 것이 어느 구간에 얼마나 있는지 센다.
          모듈이 다 읽은 구간은 제외가 틀렸어도 숨는 것이 없다(정상 프레임으로 되돌려도 읽은 프레임만 는다).
          그래서 그림으로 확인할 것은 "못 읽은 프레임이 든 구간"뿐이다.
          같은 기어의 세 위치는 연출의 횟수와 길이가 비슷해야 하므로 그것도 나란히 낸다(목록이 서로 다른 판독으로 만들어졌는데도 맞는지).
  sheets  못 읽은 프레임이 든 구간을 녹화에서 꺼내 넓게 자른 그림으로 만든다: 구간 앞뒤, 가장자리 세 장씩, 안쪽,
          그리고 못 읽은 프레임 가운데 처음 둘과 마지막 둘.
          사례마다 처음 구간, 못 읽은 것이 가장 많은 구간, 길이가 다른 구간을 고른다. 연출 밖에서 "가려짐"으로 빠진 프레임도 넣는다.
          사람이 볼 것: 연출이 맞는가, 구간의 가장자리에 숫자가 다 보이는데 제외되고 못 읽힌 프레임이 있는가.
          그림은 크므로(사례당 2~5MB) git 밖(data\\ 아래)에 만든다. 같은 실행이면 같은 프레임이 뽑힌다.
  edges   사례마다 한 줄로 줄인 그림: 가운데쯤의 연출 구간에서 못 읽은 첫 장·끝 장과 그 바로 앞·뒤 장.
          한 위치의 모든 사례를 두 장에 본다. 연출 구간 안에서 따로 떨어져 못 읽힌 덩어리도 함께 낸다(second-…).
  frames  고른 프레임 몇 장을 두 배로 키운 그림(사용자에게 보일 것). 노란 네모는 그 시각에 모듈이 읽던 영역이다.
  judge   사용자가 판정할 프레임을 번호를 붙여 사례마다 그림 한 장으로 모은다. 번호, 읽힌 값, 앞뒤로 읽힌 값, 본 것을 그림에 적는다.
          목록은 tsv(번호, 사례, 프레임, Claude 가 본 것, 묶음 설명). 초록 네모가 모듈이 읽던 영역이다. 사용자의 판정이 있으면 함께 적는다.

프레임 번호는 1부터 센다(판독 결과의 index 와 같다). 그림의 글자: IN = 제외된 프레임, OUT = 정상 프레임, U = 모듈이 못 읽음, R = 읽음.
목록은 RIGHT 가 v17, LEFT·CENTER 가 v12 의 것이다(final_report 와 같다).

실행: .venv\\Scripts\\python.exe -m detector.check_masks <모듈이 읽은 실행(vid-…)> stats <출력 폴더>
      .venv\\Scripts\\python.exe -m detector.check_masks <모듈이 읽은 실행(vid-…)> sheets <그림 폴더> <right | left | center | 사례 …>
      .venv\\Scripts\\python.exe -m detector.check_masks <모듈이 읽은 실행(vid-…)> edges <그림 폴더> <right | left | center …>
      .venv\\Scripts\\python.exe -m detector.check_masks <모듈이 읽은 실행(vid-…)> frames <그림 파일(.jpg 또는 .png)> <사례> <프레임 번호 …>
      .venv\\Scripts\\python.exe -m detector.check_masks <모듈이 읽은 실행(vid-…)> judge <출력 폴더> <후보 목록.tsv>
출력: <출력 폴더>\\제외목록-사례별.csv, 제외목록-구간.csv / <그림 폴더>\\<사례>.png, 그림목록.tsv, edges-<위치>-N.png, second-<위치>-N.png
      <출력 폴더>\\판정-<첫 번호>-<끝 번호>-<사례>.jpg
"""
import csv
import json
import subprocess
import sys
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFont

from .fever import episodes, normal_frames
from .full_frames import sources
from .score_frames import READ_CONF
from .score_rules import verify_sequence
from . import inputs
from .selftrain import ROOT, notes_of

FPS = 60.0
ODD = 0.15                     # 구간의 길이가 그 사례의 가운데 값에서 이 비율(+0.05초) 넘게 벗어나면 "길이가 다른 구간"
CROP_W, CROP_H = 440, 150      # 그림 한 칸에 넣는 화면 넓이(점수 영역을 가운데에). 연출이 보이도록 영역보다 넓게 자른다
PER_ROW = 4
MAX_STRIPS = 3                 # 그림 한 장에 넣는 구간 수(넘으면 다음 장으로)
GAP = 3                        # 못 읽은 프레임 사이가 이 장수 이하로 벌어졌으면 한 덩어리로 본다
TIMES_FROM = ("vid-d2-v17-r", "v17")      # 프레임의 실제 시각을 영상에서 받아 적어 둔 실행(시제품이 읽은 것)
JUDGED = ROOT / "truth" / "unread-judgement.tsv"      # 사용자가 프레임을 직접 보고 내린 판정(사례, 프레임, 판정, 까닭, 판정일)
FONT = Path(r"C:\Windows\Fonts\malgun.ttf")              # 그림에 한글을 적을 글꼴(없으면 기본 글꼴로 적어 한글이 깨진다)
FONT_BOLD = Path(r"C:\Windows\Fonts\malgunbd.ttf")


def position_of(case_id: str) -> str:
    return "CENTER" if "-center-" in case_id else ("RIGHT" if "-right-" in case_id else "LEFT")


def load_case(run: str, case_id: str):
    """사례 하나의 판독과 목록을 포갠다. 목록이 없으면 None."""
    position = position_of(case_id)
    masks = ROOT / "data" / "full" / ("v17" if position == "RIGHT" else "v12") / "fever" / f"{case_id}.npz"
    path = ROOT / "data" / "full" / run / f"{case_id}.npz"
    if not masks.exists() or not path.exists():
        return None
    z, m = np.load(path), np.load(masks)
    n = min(len(z["values"]), len(m["covered"]))
    start, stop = int(m["window"][0]), int(m["window"][1])
    inside = np.zeros(n, bool); inside[start:stop] = True
    raw = np.where((z["conf"] >= READ_CONF) & ~z["blank"] & (z["values"] > 0), z["values"], -1)
    keep = np.array(verify_sequence([int(v) for v in raw], notes_of(case_id)), bool)[:n]
    locked = z["locked"][:n]
    fever = m["fever"][:n] & inside
    normal = normal_frames(case_id, inside, m["fever"][:n], m["covered"][:n])
    covered = inside & ~normal & ~fever                       # 연출 밖에서 "가려짐"으로 빠진 프레임
    return {"case": case_id, "position": position, "n": n, "window": (start, stop), "inside": inside, "keep": keep, "locked": locked,
            "fever": fever, "covered": covered, "normal": normal, "unread": locked & ~keep, "change": m["change"][:n],
            "episodes": episodes(inside & ~normal & fever), "covered_runs": episodes(covered)}


def cases_of(run: str) -> list[str]:
    return [p.stem for p in inputs(ROOT / "data" / "full" / run, "*.npz", "사례별 판독 결과")]


def names() -> dict:
    path = ROOT / "data" / "frames05b" / "cases.json"
    return {c["id"]: c["name"] for c in json.loads(path.read_text(encoding="utf-8"))} if path.exists() else {}


def odd_length(c: dict, a: int, b: int) -> bool:
    lengths = [(y - x) / FPS for x, y in c["episodes"]]
    middle = float(np.median(lengths))
    return abs((b - a) / FPS - middle) > ODD * middle + 0.05


def stats(run: str, out: Path):
    out.mkdir(parents=True, exist_ok=True)
    name = names()
    rows, spans = [], []
    gears: dict[str, dict] = {}
    for case_id in cases_of(run):
        c = load_case(run, case_id)
        if c is None:
            print(f"목록이 없어 건너뜀: {case_id}")
            continue
        un, start = c["unread"], c["window"][0]
        lengths = [(b - a) / FPS for a, b in c["episodes"]]
        with_unread = [(a, b) for a, b in c["episodes"] if un[a:b].any()]
        excluded = c["inside"] & ~c["normal"]
        row = {"사례": case_id, "기어": name.get(case_id, ""), "위치": c["position"], "플레이 구간": int(c["inside"].sum()),
               "연출 구간 수": len(c["episodes"]), "연출 프레임": int((c["fever"] & excluded).sum()),
               "연출 길이 가운데(초)": f"{np.median(lengths):.2f}" if lengths else "", "가장 긴 연출(초)": f"{max(lengths):.2f}" if lengths else "",
               "연출 밖 가려짐 프레임": int(c["covered"].sum()),
               "고정 뒤 정상 프레임": int((c["normal"] & c["locked"]).sum()), "못 읽은 정상 프레임": int((c["normal"] & un).sum()),
               "고정 뒤 제외 프레임": int((excluded & c["locked"]).sum()), "그중 못 읽음": int((excluded & un).sum()),
               "못 읽은 프레임이 든 연출 구간": len(with_unread), "연출 밖 가려짐 중 못 읽음": int((c["covered"] & un).sum()),
               "구간 처음 3장 중 못 읽음": sum(int(un[a:a + 3].sum()) for a, b in c["episodes"]),
               "구간 마지막 3장 중 못 읽음": sum(int(un[b - 3:b].sum()) for a, b in c["episodes"])}
        rows.append(row)
        gears.setdefault(case_id.rsplit("-", 2)[0], {})[c["position"]] = row
        for kind, runs in (("연출", c["episodes"]), ("가려짐", c["covered_runs"])):
            for k, (a, b) in enumerate(runs):
                spans.append({"사례": case_id, "기어": name.get(case_id, ""), "갈래": kind, "번호": k, "첫 프레임": a + 1, "끝 프레임": b,
                              "구간 시작 뒤(초)": f"{(a - start) / FPS:.1f}", "길이(초)": f"{(b - a) / FPS:.2f}",
                              "달라짐 최고": f"{float(c['change'][a:b].max()):.0f}", "고정 뒤 프레임": int(c["locked"][a:b].sum()),
                              "못 읽음": int(un[a:b].sum()), "길이가 다름": "예" if kind == "연출" and odd_length(c, a, b) else ""})
    for path, table in ((out / "제외목록-사례별.csv", rows), (out / "제외목록-구간.csv", spans)):
        with open(path, "w", newline="", encoding="utf-8-sig") as f:
            w = csv.DictWriter(f, fieldnames=list(table[0])); w.writeheader(); w.writerows(table)

    print(f"모듈이 읽은 실행 {run}, 사례 {len(rows)}개")
    for position in ("LEFT", "CENTER", "RIGHT"):
        part = [r for r in rows if r["위치"] == position]
        total = lambda key: sum(r[key] for r in part)
        print(f"{position:6} 사례 {len(part)} | 고정 뒤 정상 프레임 {total('고정 뒤 정상 프레임'):,}, 못 읽음 {total('못 읽은 정상 프레임')} | "
              f"고정 뒤 제외 프레임 {total('고정 뒤 제외 프레임'):,}, 그중 못 읽음 {total('그중 못 읽음'):,} | "
              f"연출 구간 {total('연출 구간 수')}, 못 읽은 프레임이 든 구간 {total('못 읽은 프레임이 든 연출 구간')} | "
              f"연출 밖 가려짐 {total('연출 밖 가려짐 프레임')}, 그중 못 읽음 {total('연출 밖 가려짐 중 못 읽음')} | "
              f"제외 안에 못 읽은 프레임이 있는 사례 {sum(r['그중 못 읽음'] > 0 for r in part)}개, 연출이 하나도 없는 사례 {sum(r['연출 구간 수'] == 0 for r in part)}개")
    print("기어별(연출이 잡힌 기어만). 값은 LEFT/CENTER/RIGHT 순서: 연출 구간 수 | 길이 가운데(초) | 가장 긴 것(초) | 제외 안의 못 읽음")
    for gear in sorted(gears):
        g = gears[gear]
        if not any(r["연출 구간 수"] for r in g.values()):
            continue
        cell = lambda key: "/".join(str(g[p][key]) if p in g and g[p][key] != "" else "-" for p in ("LEFT", "CENTER", "RIGHT"))
        print(f"  {gear} {next(iter(g.values()))['기어'][:18]:18} | {cell('연출 구간 수'):9} | {cell('연출 길이 가운데(초)'):15} | {cell('가장 긴 연출(초)'):15} | {cell('그중 못 읽음')}")


def area_at(steps: list, t: float):
    current = steps[0][1]
    for at, area in steps:
        if at <= t + 1e-9:
            current = area
    return current


def grab(source: str, start: float, indices: list[int]) -> dict:
    """녹화에서 프레임 몇 장(구간 시작부터 센 위치, 0부터)을 뽑는다. final_report.grab 과 같은 방식이다."""
    expr = "+".join(f"eq(n\\,{i})" for i in indices)
    raw = subprocess.run(["ffmpeg", "-nostdin", "-hide_banner", "-loglevel", "error", "-threads", "2", "-ss", str(start), "-t", str(indices[-1] / FPS + 1),
                          "-i", source, "-an", "-vf", f"select={expr}", "-fps_mode", "passthrough", "-f", "rawvideo", "-pix_fmt", "bgr24", "-"],
                         capture_output=True, stdin=subprocess.DEVNULL).stdout
    frames = np.frombuffer(raw, np.uint8).reshape(-1, 1080, 1920, 3)
    return {i: frames[k] for k, i in enumerate(indices[:len(frames)])}


def true_times(case_id: str):
    """프레임의 실제 시각(구간 시작 기준). 영상에서 직접 받아 적은 실행의 것을 쓴다.

    .NET 결과를 옮긴 실행은 프레임 수가 시제품과 다르면 시각을 "번호 ÷ 60"으로 채워 두는데, 구간이 프레임 경계에서 시작하지 않으면
    그 값은 실제보다 한 프레임 가까이 이르다. 그 시각으로 건너뛰면 한 장 앞의 프레임이 나온다(1-01 RIGHT 로 확인).
    """
    for run in TIMES_FROM:
        path = ROOT / "data" / "full" / run / f"{case_id}.npz"
        if path.exists():
            return np.load(path)["pts"]
    return None


def grab_at(source: str, at: float):
    """녹화의 그 시각에 놓인 프레임 한 장. 앞에서부터 모두 풀지 않고 그 자리로 건너뛴다(ffmpeg 가 앞의 키 프레임부터 풀어 정확한 프레임을 낸다).

    grab(차례로 모두 풀기)과 같은 프레임이 나오는지 일곱 사례에서 화소까지 견줘 확인했다(실제 시각 - 0.004초로 건너뛸 때).
    """
    raw = subprocess.run(["ffmpeg", "-nostdin", "-hide_banner", "-loglevel", "error", "-threads", "2", "-ss", f"{at:.4f}", "-i", source, "-an",
                          "-frames:v", "1", "-f", "rawvideo", "-pix_fmt", "bgr24", "-"], capture_output=True, stdin=subprocess.DEVNULL).stdout
    return np.frombuffer(raw, np.uint8).reshape(1080, 1920, 3) if len(raw) == 1080 * 1920 * 3 else None


def unread_runs(c: dict, a: int, b: int) -> list:
    """구간 안의 못 읽은 프레임을 이어진 덩어리로 나눈다(사이가 GAP 장 이하면 한 덩어리). 가장 큰 덩어리가 맨 앞에 온다."""
    missed = a + np.flatnonzero(c["unread"][a:b])
    if not len(missed):
        return []
    runs = np.split(missed, np.flatnonzero(np.diff(missed) > GAP) + 1)
    return sorted(runs, key=len, reverse=True)


def edges(run: str, out: Path, wanted: list[str]):
    """사례마다 한 줄씩: 가운데쯤의 연출 구간에서 못 읽은 첫 장과 끝 장, 그 바로 앞·뒤 장. 모든 사례를 한눈에 본다.

    두 번째 그림(second-…)은 연출 구간 안에서 가장 큰 덩어리 말고 따로 떨어져 못 읽힌 덩어리(사례마다 처음 둘)의 앞 장, 첫 장, 가운데 장, 뒤 장이다.
    """
    out.mkdir(parents=True, exist_ok=True)
    table = sources()
    for position in wanted:
        first, second = [], []
        for case_id in cases_of(run):
            if position_of(case_id).lower() != position:
                continue
            c = load_case(run, case_id)
            spans = [] if c is None else [e for e in c["episodes"] if c["unread"][e[0]:e[1]].any()]
            if not spans:
                continue
            times = true_times(case_id)
            steps = json.loads((ROOT / "data" / "full" / run / f"{case_id}.steps.json").read_text(encoding="utf-8"))["steps"]

            def tile(i):
                frame = grab_at(table[case_id]["source"], table[case_id]["start"] + float(times[i]) - 0.004)
                x0, y0, x1, y1 = area_at(steps, i / FPS)
                left = int(np.clip((x0 + x1) / 2 - CROP_W / 2, 0, 1920 - CROP_W)); top = int(np.clip((y0 + y1) / 2 - CROP_H / 2, 0, 1080 - CROP_H))
                picture = np.zeros((CROP_H + 20, CROP_W, 3), np.uint8)
                if frame is not None:
                    picture[20:] = frame[top:top + CROP_H, left:left + CROP_W]
                excluded = not c["normal"][i]
                cv2.putText(picture, f"{case_id[:-7]} #{i + 1} {'IN' if excluded else 'OUT'} {'U' if c['unread'][i] else 'R'}", (4, 15),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.5, (80, 80, 255) if excluded else (80, 255, 80), 1, cv2.LINE_AA)
                return picture

            a, b = spans[len(spans) // 2]
            missed = a + np.flatnonzero(c["unread"][a:b])
            first += [tile(i) for i in (int(missed[0]) - 1, int(missed[0]), int(missed[-1]), int(missed[-1]) + 1)]
            apart = [r for e in spans for r in unread_runs(c, *e)[1:] if len(r) >= 2][:2]
            for r in apart:
                second += [tile(i) for i in (int(r[0]) - 1, int(r[0]), int(r[len(r) // 2]), int(r[-1]) + 1)]
            print(f"{case_id}: 가운데 구간 #{a + 1}~{b}, 따로 떨어진 덩어리 {len(apart)}개", flush=True)
        for name, tiles in ((f"edges-{position}", first), (f"second-{position}", second)):
            rows = [np.hstack(tiles[r:r + PER_ROW]) for r in range(0, len(tiles), PER_ROW)]
            for page in range(0, len(rows), 11):
                cv2.imencode(".png", np.vstack(rows[page:page + 11]))[1].tofile(str(out / f"{name}-{page // 11 + 1}.png"))


def picks(c: dict) -> list[tuple[str, int, int, list[int]]]:
    """그림으로 만들 구간과 그 안에서 뽑을 프레임 위치. (설명, 첫 위치, 끝 위치, 프레임 위치들)"""
    un, n = c["unread"], c["n"]
    with_unread = [(a, b) for a, b in c["episodes"] if un[a:b].any()]
    chosen = []
    if with_unread:
        chosen.append(with_unread[0])
        most = max(with_unread, key=lambda e: int(un[e[0]:e[1]].sum()))
        if most not in chosen:
            chosen.append(most)
        for e in sorted((e for e in with_unread if odd_length(c, *e)), key=lambda e: -int(un[e[0]:e[1]].sum())):
            if e not in chosen and len(chosen) < 5:
                chosen.append(e)
    out = []
    for a, b in sorted(chosen):
        inner = [a + round((b - 1 - a) * f) for f in (0.25, 0.5, 0.75)]
        # 못 읽은 프레임 가운데 처음 둘과 마지막 둘: 숫자가 다 보이는데 못 읽힌 것이 있다면 가려지기 시작할 때와 걷힐 때다
        missed = a + np.flatnonzero(un[a:b])
        edges = [int(i) for i in (*missed[:2], *missed[-2:])]
        frames = sorted({i for i in [a - 3, a - 1, a, a + 1, a + 2, *inner, *edges, b - 3, b - 2, b - 1, b, b + 2] if 0 <= i < n})
        out.append((f"fever ep{c['episodes'].index((a, b)):02d} frames {a + 1}-{b} ({(b - a) / FPS:.2f}s) unread {int(un[a:b].sum())}", a, b, frames))
    for a, b in [r for r in c["covered_runs"] if un[r[0]:r[1]].any()][:3]:
        inner = list(range(a, b)) if b - a <= 8 else [a + round((b - 1 - a) * f) for f in np.linspace(0, 1, 8)]
        frames = sorted({i for i in [a - 2, a - 1, *inner, b, b + 1] if 0 <= i < n})
        out.append((f"covered frames {a + 1}-{b} unread {int(un[a:b].sum())}", a, b, frames))
    return out


def sheets(run: str, out: Path, wanted: list[str]):
    folder = out
    folder.mkdir(parents=True, exist_ok=True)
    table = sources()
    listing = []
    for case_id in cases_of(run):
        if not (case_id in wanted or position_of(case_id).lower() in wanted):
            continue
        c = load_case(run, case_id)
        if c is None:
            continue
        strips = picks(c)
        if not strips:
            continue
        steps = json.loads((ROOT / "data" / "full" / run / f"{case_id}.steps.json").read_text(encoding="utf-8"))["steps"]
        need = sorted({i for _, _, _, frames in strips for i in frames})
        got = grab(table[case_id]["source"], table[case_id]["start"], need)
        blocks = []
        for label, a, b, frames in strips:
            tiles = []
            for i in frames:
                if i not in got:
                    continue
                x0, y0, x1, y1 = area_at(steps, i / FPS)
                left = int(np.clip((x0 + x1) / 2 - CROP_W / 2, 0, 1920 - CROP_W)); top = int(np.clip((y0 + y1) / 2 - CROP_H / 2, 0, 1080 - CROP_H))
                tile = np.zeros((CROP_H + 20, CROP_W, 3), np.uint8)
                tile[20:] = got[i][top:top + CROP_H, left:left + CROP_W]
                excluded = not c["normal"][i]
                text = f"#{i + 1} {'IN' if excluded else 'OUT'} {'U' if c['unread'][i] else ('R' if c['keep'][i] else '-')}"
                cv2.putText(tile, text, (4, 15), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (80, 80, 255) if excluded else (80, 255, 80), 1, cv2.LINE_AA)
                tiles.append(tile)
                listing.append([case_id, label, i + 1, "IN" if excluded else "OUT", "U" if c["unread"][i] else "R"])
            while len(tiles) % PER_ROW:
                tiles.append(np.zeros_like(tiles[0]))
            head = np.zeros((24, CROP_W * PER_ROW, 3), np.uint8)
            cv2.putText(head, f"{case_id[:-7]}  {label}", (4, 17), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (255, 255, 255), 1, cv2.LINE_AA)
            blocks.append(np.vstack([head] + [np.hstack(tiles[r:r + PER_ROW]) for r in range(0, len(tiles), PER_ROW)]))
        for page in range(0, len(blocks), MAX_STRIPS):
            suffix = "" if page == 0 else f"-{page // MAX_STRIPS + 1}"
            cv2.imencode(".png", np.vstack(blocks[page:page + MAX_STRIPS]))[1].tofile(str(folder / f"{case_id[:-7]}{suffix}.png"))
        print(f"{case_id}: 구간 {len(strips)}개, 프레임 {len(need)}장", flush=True)
    path = out / "그림목록.tsv"
    old = [r for r in csv.reader(open(path, encoding="utf-8"), delimiter="\t")][1:] if path.exists() else []
    done = {r[0] for r in listing}
    with open(path, "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f, delimiter="\t"); w.writerow(["사례", "구간", "프레임", "목록", "모듈"])
        w.writerows([r for r in old if r[0] not in done] + listing)


def frames(run: str, out: Path, case_id: str, numbers: list[int]):
    """고른 프레임 몇 장을 두 배로 키워 한 장에 모은다(사용자가 직접 보고 판정할 그림). 노란 네모는 그 시각에 모듈이 읽던 영역이다."""
    z = np.load(ROOT / "data" / "full" / run / f"{case_id}.npz")
    c = load_case(run, case_id)                               # 목록이 없는 사례(3-05 CENTER)는 None
    raw = np.where((z["conf"] >= READ_CONF) & ~z["blank"] & (z["values"] > 0), z["values"], -1)
    keep = np.array(verify_sequence([int(v) for v in raw], notes_of(case_id)), bool)
    steps = json.loads((ROOT / "data" / "full" / run / f"{case_id}.steps.json").read_text(encoding="utf-8"))["steps"]
    table = sources()
    got = grab(table[case_id]["source"], table[case_id]["start"], sorted(n - 1 for n in numbers))
    tiles = []
    for n in numbers:
        i = n - 1
        if i not in got:
            continue
        x0, y0, x1, y1 = area_at(steps, i / FPS)
        left = int(np.clip((x0 + x1) / 2 - CROP_W / 2, 0, 1920 - CROP_W)); top = int(np.clip((y0 + y1) / 2 - CROP_H / 2, 0, 1080 - CROP_H))
        big = cv2.resize(got[i][top:top + CROP_H, left:left + CROP_W], None, fx=2, fy=2, interpolation=cv2.INTER_CUBIC)
        if z["locked"][i]:
            cv2.rectangle(big, (int((x0 - left) * 2), int((y0 - top) * 2)), (int((x1 - left) * 2), int((y1 - top) * 2)), (0, 255, 255), 1)
        tile = np.zeros((big.shape[0] + 26, big.shape[1], 3), np.uint8)
        tile[26:] = big
        state = "not locked" if not z["locked"][i] else ("pass" if keep[i] else "unread")
        where = "  (no list)" if c is None else ("  excluded" if c["inside"][i] and not c["normal"][i] else "  normal")
        text = f"{case_id[:-7]} #{n}  module read '{z['texts'][i]}' conf {float(z['conf'][i]):.2f}  {state}{where}"
        cv2.putText(tile, text, (6, 19), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255, 255, 255), 1, cv2.LINE_AA)
        tiles.append(tile)
    if len(tiles) % 2:
        tiles.append(np.zeros_like(tiles[0]))
    out.parent.mkdir(parents=True, exist_ok=True)
    sheet = np.vstack([np.hstack(tiles[r:r + 2]) for r in range(0, len(tiles), 2)])
    # 문서 폴더(git)에 둘 그림은 .jpg 로 받는다(같은 그림이 .png 의 1/7 쯤이다)
    encoded = cv2.imencode(".jpg", sheet, [cv2.IMWRITE_JPEG_QUALITY, 90])[1] if out.suffix.lower() == ".jpg" else cv2.imencode(".png", sheet)[1]
    encoded.tofile(str(out))
    print(f"{out}: {len(numbers)}장")


def written(lines: list, width: int):
    """글 몇 줄을 너비에 맞춰 접어 검은 바탕의 그림(BGR)으로 만든다. lines: (글, 글자 크기, 색(RGB), 굵게)"""
    drawn, height = [], 8
    probe = ImageDraw.Draw(Image.new("RGB", (8, 8)))
    for text, size, color, bold in lines:
        font = ImageFont.truetype(str(FONT_BOLD if bold else FONT), size) if FONT.exists() else ImageFont.load_default()
        line = ""
        for word in text.split(" "):                            # 낱말 사이에서 접는다(프레임 번호 같은 것이 줄 끝에서 쪼개지지 않게)
            longer = word if not line else line + " " + word
            if line and probe.textlength(longer, font=font) > width - 24:
                drawn.append((line, font, color, height)); height += size + 8; line = word
            else:
                line = longer
        drawn.append((line, font, color, height)); height += size + 8
    picture = Image.new("RGB", (width, height + 4), (17, 17, 17))
    draw = ImageDraw.Draw(picture)
    for text, font, color, y in drawn:
        draw.text((12, y), text, font=font, fill=color)
    return np.asarray(picture)[:, :, ::-1]


def judge(run: str, out: Path, listing: Path):
    """사용자가 판정할 프레임을 번호를 붙여 사례마다 그림 한 장으로 모은다. 번호·읽힌 값·앞뒤로 읽힌 값·본 것을 그림에 적는다.

    목록(tsv): 번호, 사례, 프레임, Claude 가 본 것, 묶음 설명(사례의 첫 줄에만). 판정이 truth\\unread-judgement.tsv 에 있으면 함께 적는다.
    쪽(HTML)으로 만들지 않는 까닭: 앱의 미리 보기는 쪽 옆의 그림 파일을 읽지 못하고, 그림을 쪽 안에 넣으면 커서(300KB 를 넘으면) 열지 못한다.
    """
    with open(listing, encoding="utf-8-sig", newline="") as f:
        wanted = [r for r in csv.DictReader(f, delimiter="\t") if r["번호"].strip()]
    said = {}
    if JUDGED.exists():
        with open(JUDGED, encoding="utf-8-sig", newline="") as f:
            said = {(r["사례"], int(r["프레임"])): f"{r['판정']} ({r['까닭']}, {r['판정일']})" for r in csv.DictReader(f, delimiter="\t")}
    out.mkdir(parents=True, exist_ok=True)
    table, name = sources(), names()
    width = CROP_W * 2
    white, grey, yellow = (255, 255, 255), (190, 190, 190), (255, 221, 0)
    groups: dict[str, list] = {}
    for row in wanted:
        groups.setdefault(row["사례"], []).append(row)
    for short, rows in groups.items():
        case_id = short if short.endswith("-actual") else short + "-actual"
        z = np.load(ROOT / "data" / "full" / run / f"{case_id}.npz")
        c = load_case(run, case_id)
        raw = np.where((z["conf"] >= READ_CONF) & ~z["blank"] & (z["values"] > 0), z["values"], -1)
        keep = np.array(verify_sequence([int(v) for v in raw], notes_of(case_id)), bool)
        passed = np.flatnonzero(keep)
        times = true_times(case_id)
        steps = json.loads((ROOT / "data" / "full" / run / f"{case_id}.steps.json").read_text(encoding="utf-8"))["steps"]
        numbers = [r["번호"].strip() for r in rows]
        head = [(f"{case_id[:-7]} {name.get(case_id, '')} · {position_of(case_id)}   ({numbers[0]}~{numbers[-1]}번, {len(rows)}장)", 24, yellow, True)]
        if rows[0].get("묶음 설명"):
            head.append((rows[0]["묶음 설명"], 18, white, False))
        head.append(("초록 네모는 모듈이 읽던 영역. '앞뒤로 읽힌 값'은 가장 가까운 앞뒤의 규칙 통과 프레임에서 읽힌 값(참값을 짐작하는 데 쓴다).", 16, grey, False))
        blocks = [written(head, width)]
        for row in rows:
            number, i = row["번호"].strip(), int(row["프레임"]) - 1
            frame = grab_at(table[case_id]["source"], table[case_id]["start"] + float(times[i]) - 0.004)
            x0, y0, x1, y1 = area_at(steps, i / FPS)
            left = int(np.clip((x0 + x1) / 2 - CROP_W / 2, 0, 1920 - CROP_W)); top = int(np.clip((y0 + y1) / 2 - CROP_H / 2, 0, 1080 - CROP_H))
            big = cv2.resize(frame[top:top + CROP_H, left:left + CROP_W], None, fx=2, fy=2, interpolation=cv2.INTER_CUBIC)
            cv2.rectangle(big, (int((x0 - left) * 2), int((y0 - top) * 2)), (int((x1 - left) * 2), int((y1 - top) * 2)), (0, 255, 0), 1)
            if keep[i]:
                kind = "규칙 통과(읽음)"
            elif z["blank"][i]:
                kind = "숫자가 없는 화면으로 봄"
            elif raw[i] > 0:
                kind = "또렷이 읽었지만 점수 규칙을 통과하지 못함"
            elif z["conf"][i] >= READ_CONF:
                kind = "또렷이 읽었지만 점수로 볼 수 없는 글자(0 이거나 자릿수가 넘침)"
            else:
                kind = "확신 있게 읽지 못함"
            before, after = passed[passed < i], passed[passed > i]
            near = " → ".join(f"{int(raw[k])} (#{int(k) + 1})" for k in ([before[-1]] if len(before) else []) + ([after[0]] if len(after) else []))
            where = "제외 목록이 없는 사례" if c is None else ("연출 구간으로 제외" if c["fever"][i] and not c["normal"][i] else ("가려짐으로 제외" if not c["normal"][i] else "정상 프레임"))
            verdict = said.get((case_id[:-7], i + 1), "아직 없음")
            blocks.append(written([(f"{number}번   프레임 {i + 1} ({float(times[i]):.2f}초)", 24, yellow, True),
                                   (f"읽힌 값 '{z['texts'][i]}' (확신도 {float(z['conf'][i]):.2f}) · {kind} · 앞뒤로 읽힌 값 {near}", 18, white, False),
                                   (f"제외 목록: {where} · 본 것: {row['Claude 가 본 것']}", 18, white, False),
                                   (f"사용자 판정: {verdict}", 18, grey if verdict == "아직 없음" else yellow, False)], width))
            blocks.append(big)
            print(f"{number}번 {case_id[:-7]} #{i + 1}: {kind}", flush=True)
        target = out / f"판정-{int(numbers[0]):02d}-{int(numbers[-1]):02d}-{case_id[:-7]}.jpg"
        cv2.imencode(".jpg", np.vstack(blocks), [cv2.IMWRITE_JPEG_QUALITY, 92])[1].tofile(str(target))
        print(f"  → {target}", flush=True)


def main():
    run, mode, out = sys.argv[1], sys.argv[2], ROOT / sys.argv[3]
    if mode == "stats":
        stats(run, out)
    elif mode == "sheets":
        sheets(run, out, [w.lower() if w in ("left", "center", "right", "LEFT", "CENTER", "RIGHT") else w for w in sys.argv[4:]])
    elif mode == "edges":
        edges(run, out, [w.lower() for w in sys.argv[4:]])
    elif mode == "frames":
        frames(run, out, sys.argv[4], [int(n) for n in sys.argv[5:]])
    elif mode == "judge":
        judge(run, out, ROOT / sys.argv[4])
    else:
        raise SystemExit(__doc__)


if __name__ == "__main__":
    main()
