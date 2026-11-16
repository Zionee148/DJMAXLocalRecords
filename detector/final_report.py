"""앱 연결의 마지막 영상 시험 결과를 정리한다 (기획서 v3 완료 기준 1).

만드는 것 (<출력 폴더> 아래)
  사례별표.csv        270개 사례마다: 기어, 위치, 특징, 자리 고정까지 걸린 시간(시작 기준과 함께), 프레임 지표, 등록 후보와 걸린 시간, 근거 수, 드문 캡처에서의 결과
  사례별표.md         그 가운데 실제 게임 시험 때 견줘 볼 것만 추린 표
  못읽은프레임.html   못 읽은 정상 프레임을 한 장씩: 넓게 자른 그림(모듈이 읽은 영역 표시)과 읽힌 값. 사용자가 직접 보고 판단한다
  못읽은프레임\\*.png
  요약.txt            합계와 정답 라벨 대조

세 가지 시험의 결과를 읽는다. 서로 다른 조건이므로 표에서 열을 나눈다.
  (가) 녹화 전체 프레임 검사: 모든 프레임(초당 60장)을 모듈에 직접 넣은 것.   data\\net\\<전체 프레임 폴더>, data\\full\\<전체 프레임 실행>
  (나) 앱 경로: 시험용 주 화면에 실제 시간으로 넣은 것(앱은 0.1초마다 캡처).   data\\replay\\<앱 경로 이름>
  (다) 연결 코드를 가짜 시계로, 캡처 0.15초·0.2초.                              data\\net\\link-all-i150.tsv, link-all-i200.tsv (있으면)

프레임 지표의 뜻 (기획서 v3)
  못 읽은 정상 프레임 = 고정 뒤 정상 프레임 가운데, 판독 확신도와 점수 규칙을 거쳐 쓸 값이 남지 않은 프레임.
  그중 "확신도는 높지만 규칙을 통과하지 못한 수"는 그 부분집합이다. 정답과 견준 오독 수가 아니다.
  정답과 견준 수는 정답 라벨이 있는 사례에만 낸다. 규칙을 통과한 틀린 값은 이 집계로 잡을 수 없다.

실행: .venv\\Scripts\\python.exe -m detector.final_report <전체 프레임 폴더(data\\net 아래)> <전체 프레임 실행(vid-…)> <앱 경로 이름> <출력 폴더>
"""
import csv
import html
import json
import subprocess
import sys

import cv2
import numpy as np

from .fever import normal_frames
from .locate_video import first_score
from .score_frames import READ_CONF, own_truth, tally
from .score_rules import verify_sequence
from .selftrain import ROOT, notes_of

CASES = ROOT / "data" / "parity_locate" / "cases.tsv"
JUDGED = ROOT / "truth" / "unread-judgement.tsv"      # 사용자가 못 읽은 정상 프레임을 직접 보고 내린 판정(사례, 프레임, 제외/미통과)


def judgements() -> dict:
    """(사례, 프레임 번호) → (판정, 까닭, 판정일). 파일이 없으면 빈 것."""
    if not JUDGED.exists():
        return {}
    with open(JUDGED, encoding="utf-8-sig", newline="") as f:
        return {(r["사례"], int(r["프레임"])): (r["판정"], r["까닭"], r["판정일"]) for r in csv.DictReader(f, delimiter="	")}


def area_at(steps, t):
    cur = None
    for s in steps:
        if s[0] <= t + 1e-9:
            cur = s[1:]
    return cur


def grab(source: str, start: float, indices: list) -> dict:
    """녹화에서 프레임 몇 장(구간 시작부터 센 번호, 0부터)을 뽑는다."""
    expr = "+".join(f"eq(n\\,{i})" for i in indices)
    raw = subprocess.run(["ffmpeg", "-nostdin", "-hide_banner", "-loglevel", "error", "-ss", str(start), "-t", str(indices[-1] / 60 + 1), "-i", source,
                          "-an", "-vf", f"select={expr}", "-fps_mode", "passthrough", "-f", "rawvideo", "-pix_fmt", "bgr24", "-"],
                         capture_output=True, stdin=subprocess.DEVNULL).stdout
    frames = np.frombuffer(raw, np.uint8).reshape(-1, 1080, 1920, 3)
    return {i: frames[k] for k, i in enumerate(indices[:len(frames)])}


def main():
    net, run_name, replay_name, out_name = sys.argv[1:5]
    net_dir, run_dir, replay_dir = ROOT / "data" / "net" / net, ROOT / "data" / "full" / run_name, ROOT / "data" / "replay" / replay_name
    proto_dir = ROOT / "data" / "full" / "vid-d2-v17-r"
    out = ROOT / out_name
    (out / "못읽은프레임").mkdir(parents=True, exist_ok=True)
    table = [r for r in csv.reader(open(CASES, encoding="utf-8"), delimiter="\t")]
    info = {c["id"]: c for c in json.loads((ROOT / "data" / "frames05b" / "cases.json").read_text(encoding="utf-8"))}
    link = {}
    for key in ("i150", "i200"):
        path = ROOT / "data" / "net" / f"link-all-{key}.tsv"
        if path.exists():
            link[key] = {r["case"]: r for r in csv.DictReader(open(path, encoding="utf-8"), delimiter="\t")}

    rows, unread_list, truth_lines = [], [], []
    total = {"LEFT·CENTER": [0, 0, 0, 0], "RIGHT": [0, 0, 0, 0]}          # 사례, 고정 뒤 정상 프레임, 못 읽음, 그중 확신도 높음
    for r in table:
        case_id, position, source, start, press, expected = r[0], r[1], r[2], float(r[3]), float(r[5]) - float(r[3]), int(r[6])
        meta = info.get(case_id, {})
        row = {"기어 번호": meta.get("gear", case_id.rsplit("-", 2)[0]), "기어 이름": meta.get("name", ""), "위치": position}

        # (가) 녹화 전체 프레임 검사
        d = json.loads((net_dir / f"{case_id}.info.json").read_text(encoding="utf-8")) if (net_dir / f"{case_id}.info.json").exists() else None
        z = np.load(run_dir / f"{case_id}.npz") if (run_dir / f"{case_id}.npz").exists() else None
        features = []
        if d:
            if d["moving"]: features.append("움직이는 점수판")
            if d["pose"]: features.append("기울어진 숫자")
        row["특징"] = "·".join(features)
        first = first_score(case_id)
        row["[전체 프레임] 자리 고정(구간 시작부터, 초)"] = f"{d['lockAt']:.1f}" if d and d["locked"] else ("못 함" if d else "")
        row["[전체 프레임] 첫 점수가 읽힌 때(구간 시작부터, 초. 참고)"] = "" if first is None else f"{first:.1f}"
        row["[전체 프레임] 자리 고정(첫 점수 뒤, 초. 참고)"] = "" if first is None or not d or not d["locked"] else f"{d['lockAt'] - first:.1f}"
        row["플레이 시작부터 20초 기준"] = "미확인(녹화에 플레이 시작 시각이 없다)"
        normal_count = unread_count = confident_count = ""
        if z is not None:
            masks = "v17" if position == "RIGHT" else "v12"
            mask_path = ROOT / "data" / "full" / masks / "fever" / f"{case_id}.npz"
            n = len(z["values"])
            raw = np.where((z["conf"] >= READ_CONF) & ~z["blank"] & (z["values"] > 0), z["values"], -1)
            keep = np.array(verify_sequence([int(v) for v in raw], notes_of(case_id)), bool)
            if mask_path.exists():
                m = np.load(mask_path)
                n = min(n, len(m["covered"]))
                inside = np.zeros(n, bool); inside[int(m["window"][0]):int(m["window"][1])] = True
                normal = normal_frames(case_id, inside, m["fever"][:n], m["covered"][:n]) & z["locked"][:n]
                bad = np.flatnonzero(normal & ~keep[:n])
                confident = raw[bad] > 0
                normal_count, unread_count, confident_count = int(normal.sum()), len(bad), int(confident.sum())
                group = "RIGHT" if position == "RIGHT" else "LEFT·CENTER"
                t = total[group]; t[0] += 1; t[1] += normal_count; t[2] += unread_count; t[3] += confident_count
                proto = np.load(proto_dir / f"{case_id}.npz") if (proto_dir / f"{case_id}.npz").exists() else None
                for k, i in enumerate(bad):
                    unread_list.append({"case": case_id, "name": row["기어 이름"], "position": position, "source": source, "start": start, "index": int(i),
                                        "text": str(z["texts"][i]), "conf": float(z["conf"][i]), "confident": bool(confident[k]),
                                        "before": str(z["texts"][i - 1]) if i else "", "after": str(z["texts"][min(i + 1, n - 1)]),
                                        "proto": "" if proto is None or i >= len(proto["texts"]) else f"{proto['texts'][i]} ({float(proto['conf'][i]):.2f})",
                                        "area": area_at(d["steps"], i / 60.0) if d else None})
            truth, where = own_truth(case_id)
            if truth:
                checked = np.where(keep, raw, -1)
                after = {i: v for i, v in truth.items() if i - 1 < len(z["locked"]) and z["locked"][i - 1]}
                match, wrong, unread, _ = tally(checked, after)
                truth_lines.append(f"{case_id} {row['기어 이름']}: 정답 라벨이 있는 프레임 {len(truth)}장({where}), 그중 고정 뒤 {len(after)}장 → 일치 {match}, 오독 {wrong}, 미판독 {unread}. "
                                   f"라벨이 없는 프레임은 정답 미확인이다")
        row["[전체 프레임] 고정 뒤 정상 프레임 수"] = normal_count
        row["[전체 프레임] 못 읽은 정상 프레임 수"] = unread_count
        row["[전체 프레임] 그중 확신도는 높지만 규칙을 통과하지 못한 수"] = confident_count

        # (나) 앱 경로
        a = json.loads((replay_dir / f"{case_id}.json").read_text(encoding="utf-8")) if (replay_dir / f"{case_id}.json").exists() else None
        events = (a or {}).get("diagnostics", [])
        locked = next((e for e in events if e.get("kind") == "locked"), None)
        answer = next((e for e in reversed(events) if e.get("kind") == "answer"), None)
        frames = next((e for e in reversed(events) if e.get("kind") == "frames"), None)
        verdict = {"right": "맞음", "wrong": "틀림", "none": "못 냄"}.get((a or {}).get("verdict", ""), "" if a is None else "오류")
        row["기대 점수"] = expected
        row["[앱 경로] 자리 고정(판을 연 때부터, 초)"] = "" if not locked else f"{locked['sincePlayMs'] / 1000:.1f}"
        row["[앱 경로] 등록 후보"] = "" if a is None else a.get("candidate", "")
        row["[앱 경로] 결과"] = verdict
        row["[앱 경로] 실패 사유"] = "" if a is None or verdict == "맞음" else (answer or {}).get("reason", "") or a.get("failure", "") or " / ".join(a.get("notes", []))
        row["[앱 경로] 답의 근거"] = (answer or {}).get("basis", "")
        row["[앱 경로] 키부터 후보까지(초, 앱에서)"] = "" if a is None else a.get("answerSeconds", "")
        row["[앱 경로] 모듈의 답까지(ms, 키부터)"] = (answer or {}).get("elapsedMs", "")
        row["[앱 경로] 요청 뒤 넘긴 화면"] = (answer or {}).get("framesOffered", "")
        row["[앱 경로] 가장 긴 화면 간격(ms)"] = (answer or {}).get("maxGapMs", "")
        row["[앱 경로] 플레이 중 읽어 둔 값"] = (answer or {}).get("playReads", "")
        row["[앱 경로] 그중 규칙을 통과한 서로 다른 값"] = (answer or {}).get("playVerified", "")
        row["[앱 경로] 플레이 중 넘긴 화면"] = (frames or {}).get("observed", "")
        row["[앱 경로] 바빠서 버린 화면"] = (frames or {}).get("dropped", "")
        row["[앱 경로] 초당 캡처"] = "" if not a or not a.get("replaySeconds") else f"{a['captures'] / a['replaySeconds']:.2f}"
        row["[앱 경로] 새 판으로 본 횟수"] = "" if a is None else sum(e.get("kind") == "restart" for e in events)
        row["[앱 경로] 곡·판정을 앱이 확정"] = "" if a is None else ("예" if a.get("songLockedByApp") else "아니오")

        # (다) 드문 캡처
        for key, label in (("i150", "0.15초"), ("i200", "0.2초")):
            item = link.get(key, {}).get(case_id)
            row[f"[캡처 {label}, 가짜 시계] 결과"] = "" if not item else {"right": "맞음", "wrong": "틀림", "none": "못 냄"}[item["verdict"]] + ("" if item["verdict"] == "right" else f"({item['reason']})")
            row[f"[캡처 {label}, 가짜 시계] 답까지(ms)"] = "" if not item else item["elapsedMs"]
            row[f"[캡처 {label}, 가짜 시계] 자리 고정(초)"] = "" if not item else item["lockAt"]
        rows.append(row)

    with open(out / "사례별표.csv", "w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0])); w.writeheader(); w.writerows(rows)

    short = ["기어 번호", "기어 이름", "위치", "특징", "[앱 경로] 자리 고정(판을 연 때부터, 초)", "[전체 프레임] 자리 고정(구간 시작부터, 초)", "[전체 프레임] 자리 고정(첫 점수 뒤, 초. 참고)",
             "[앱 경로] 결과", "[앱 경로] 키부터 후보까지(초, 앱에서)", "[앱 경로] 플레이 중 읽어 둔 값", "[전체 프레임] 고정 뒤 정상 프레임 수", "[전체 프레임] 못 읽은 정상 프레임 수"]
    head = ["기어", "이름", "위치", "특징", "고정(앱 경로)", "고정(전체 프레임)", "고정(첫 점수 뒤)", "등록 후보", "키부터 후보까지", "플레이 중 읽은 값", "정상 프레임", "못 읽음"]
    lines = ["| " + " | ".join(head) + " |", "|" + "---|" * len(head)]
    for row in rows:
        lines.append("| " + " | ".join(str(row[k]) for k in short) + " |")
    (out / "사례별표.md").write_text("\n".join(lines) + "\n", encoding="utf-8")

    # 못 읽은 정상 프레임의 그림
    by_case = {}
    for u in unread_list:
        by_case.setdefault(u["case"], []).append(u)
    cards = []
    judged = judgements()
    for case_id, items in by_case.items():
        got = grab(items[0]["source"], items[0]["start"], sorted({u["index"] for u in items}))
        for u in items:
            frame, a = got.get(u["index"]), u["area"]
            name = f"{case_id[:-7]}-{u['index'] + 1}.png"
            if frame is not None and a is not None:
                x0, y0, x1, y1 = [int(round(v)) for v in a]
                left, top = max(0, x0 - 170), max(0, y0 - 80)
                wide = frame[top:min(1080, y1 + 80), left:min(1920, x1 + 170)].copy()
                cv2.rectangle(wide, (x0 - left, y0 - top), (x1 - left, y1 - top), (0, 255, 0), 1)
                big = cv2.resize(wide, None, fx=2, fy=2, interpolation=cv2.INTER_NEAREST)
                cv2.imencode(".png", big)[1].tofile(str(out / "못읽은프레임" / name))
            kind = "확신도는 높지만 점수 규칙을 통과하지 못함" if u["confident"] else "숫자가 안 읽혔거나 확신도가 낮음"
            cards.append((u, name, kind))
    page = ["<!doctype html><html lang='ko'><head><meta charset='utf-8'><title>못 읽은 정상 프레임</title>",
            "<style>body{font-family:'맑은 고딕',sans-serif;margin:24px;background:#f4f4f4;color:#222}h1{font-size:20px}.card{background:#fff;border:1px solid #ccc;border-radius:6px;padding:12px;margin:0 0 16px}"
            ".card img{max-width:100%;image-rendering:pixelated;border:1px solid #999}.k{color:#666}.v{font-weight:bold}table{border-collapse:collapse;margin:6px 0}td{padding:2px 12px 2px 0;vertical-align:top}</style></head><body>",
            f"<h1>못 읽은 정상 프레임 {len(cards)}장 (녹화 전체 프레임 검사, {html.escape(run_name)})</h1>",
            "<p>고정 뒤 정상 프레임 가운데, 판독 확신도와 점수 규칙을 거쳐 쓸 값이 남지 않은 프레임을 모두 실었다. 그림은 그 프레임을 넓게 자른 것(2배 확대)이고 초록 네모가 모듈이 읽은 영역이다. "
            "'앞·뒤 프레임'은 바로 앞뒤 프레임에서 읽힌 값이다(참값을 짐작하는 데 쓴다). 정답과 견준 것이 아니다.</p>"]
    verdicts = {"제외": 0, "미통과": 0, "판정 없음": 0}
    for u, name, kind in cards:
        verdict = judged.get((u["case"][:-7], u["index"] + 1))
        verdicts[verdict[0] if verdict and verdict[0] in verdicts else "판정 없음"] += 1
        said = f"{verdict[0]} — {verdict[1]} ({verdict[2]})" if verdict else "아직 없음"
        page.append("<div class='card'>"
                    f"<div><span class='v'>{html.escape(u['case'][:-7])} {html.escape(u['name'])}</span> · {u['position']} · 프레임 {u['index'] + 1} ({u['index'] / 60:.2f}초)</div>"
                    f"<table><tr><td class='k'>읽힌 값</td><td class='v'>'{html.escape(u['text'])}' (확신도 {u['conf']:.2f})</td></tr>"
                    f"<tr><td class='k'>갈래</td><td>{kind}</td></tr>"
                    f"<tr><td class='k'>앞·뒤 프레임</td><td>'{html.escape(u['before'])}' · '{html.escape(u['after'])}'</td></tr>"
                    f"<tr><td class='k'>시제품(Python)의 판독</td><td>{html.escape(u['proto']) or '-'}</td></tr>"
                    f"<tr><td class='k'>사용자 판정</td><td class='v'>{html.escape(said)}</td></tr></table>"
                    f"<img src='못읽은프레임/{name}' alt='{name}'></div>")
    page.append("</body></html>")
    (out / "못읽은프레임.html").write_text("\n".join(page), encoding="utf-8")

    # 요약
    app = [r for r in rows if r["[앱 경로] 결과"] != ""]
    text = [f"사례 {len(rows)}개. 전체 프레임 검사 {run_name}({net}), 앱 경로 {replay_name}"]
    for group, t in total.items():
        text.append(f"[전체 프레임] {group}: 사례 {t[0]}개, 고정 뒤 정상 프레임 {t[1]:,}장, 못 읽은 정상 프레임 {t[2]}장(그중 확신도는 높지만 규칙을 통과하지 못한 것 {t[3]}장)")
    locked_all = [float(r["[전체 프레임] 자리 고정(구간 시작부터, 초)"]) for r in rows if r["[전체 프레임] 자리 고정(구간 시작부터, 초)"] not in ("", "못 함")]
    after_first = sorted(float(r["[전체 프레임] 자리 고정(첫 점수 뒤, 초. 참고)"]) for r in rows if r["[전체 프레임] 자리 고정(첫 점수 뒤, 초. 참고)"] != "")
    text.append(f"[전체 프레임] 자리 고정 {len(locked_all)}개. 첫 점수 뒤(참고): 가운데 {after_first[len(after_first) // 2]:.1f}초, 가장 늦은 {after_first[-1]:.1f}초" if after_first else "[전체 프레임] 자리 고정 자료 없음")
    if app:
        count = {v: sum(r["[앱 경로] 결과"] == v for r in app) for v in ("맞음", "틀림", "못 냄", "오류")}
        took = sorted(float(r["[앱 경로] 키부터 후보까지(초, 앱에서)"]) for r in app if r["[앱 경로] 결과"] == "맞음")
        locks = sorted(float(r["[앱 경로] 자리 고정(판을 연 때부터, 초)"]) for r in app if r["[앱 경로] 자리 고정(판을 연 때부터, 초)"] != "")
        text.append(f"[앱 경로] 사례 {len(app)}개: 등록 후보 맞음 {count['맞음']}, 틀림 {count['틀림']}, 못 냄 {count['못 냄']}, 오류 {count['오류']}"
                    + (f" | 키부터 후보까지 가운데 {took[len(took) // 2]:.2f}초, 가장 긴 {took[-1]:.2f}초" if took else "")
                    + (f" | 자리 고정(판을 연 때부터) 가운데 {locks[len(locks) // 2]:.1f}초, 가장 늦은 {locks[-1]:.1f}초" if locks else ""))
        text.append(f"[앱 경로] 답의 근거: Stable {sum(r['[앱 경로] 답의 근거'] == 'Stable' for r in app)}, Gone {sum(r['[앱 경로] 답의 근거'] == 'Gone' for r in app)}"
                    f" | 새 판으로 잘못 본 횟수 {sum(int(r['[앱 경로] 새 판으로 본 횟수'] or 0) for r in app)}"
                    f" | 곡·판정을 앱이 확정하지 못한 사례 {sum(r['[앱 경로] 곡·판정을 앱이 확정'] == '아니오' for r in app)}")
        for r in app:
            if r["[앱 경로] 결과"] != "맞음":
                text.append(f"   {r['기어 번호']} {r['위치']} {r['기어 이름']}: {r['[앱 경로] 결과']} {r['[앱 경로] 실패 사유']}")
    for key, label in (("i150", "0.15초"), ("i200", "0.2초")):
        if key in link:
            col = f"[캡처 {label}, 가짜 시계] 결과"
            text.append(f"[캡처 {label}, 가짜 시계] 맞음 {sum(r[col] == '맞음' for r in rows)}, 틀림 {sum(r[col].startswith('틀림') for r in rows)}, 못 냄 {sum(r[col].startswith('못 냄') for r in rows)}"
                        + "".join(f"\n   {r['기어 번호']} {r['위치']} {r['기어 이름']}: {r[col]}" for r in rows if r[col] and r[col] != "맞음"))
    text += ["[정답 라벨 대조] " + line for line in truth_lines] or ["[정답 라벨 대조] 라벨이 있는 사례 없음"]
    text.append(f"[사용자 판정] 못 읽은 정상 프레임 {len(cards)}장 가운데 제외 {verdicts['제외']}장, 미통과 {verdicts['미통과']}장, 판정 없음 {verdicts['판정 없음']}장 ({JUDGED.name})")
    text.append(f"못 읽은 정상 프레임의 그림 {len(cards)}장 → {out / '못읽은프레임.html'}")
    (out / "요약.txt").write_text("\n".join(text) + "\n", encoding="utf-8")
    print("\n".join(text))


if __name__ == "__main__":
    main()
