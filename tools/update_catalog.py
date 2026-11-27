"""V-ARCHIVE 의 곡 목록(songs.json)을 앱의 곡 목록(app/runtime/data/catalog.json) 꼴로 바꾸고, 지금 곡 목록과 견준다.

게임 업데이트 때 쓴다. 곡 번호는 V-ARCHIVE 의 번호(title)이고, 기록의 곡 번호(varchive:<번호>)도
그 번호다. 그래서 없어지거나 이름이 바뀐 번호가 있으면 기존 기록의 연결이 끊긴다. 이 도구는 그런 차이가 있으면 곡 목록을 바꾸지 않는다.

쓰는 법 (프로젝트 폴더에서):
    .venv\\Scripts\\python.exe tools\\update_catalog.py compare <songs.json> [--report <보고서.md>]
    .venv\\Scripts\\python.exe tools\\update_catalog.py apply <songs.json> --retrieved-at <받은 시각>
    (둘 다 --catalog <파일> 을 주면 app/runtime/data/catalog.json 대신 그 파일을 읽고 쓴다. 예행연습이나 시험에 쓴다)

songs.json 은 https://v-archive.net/db/v2/songs.json 을 curl 로 받아 둔 파일이다(이 PC 의 Python 은 HTTPS 인증서를 찾지 못한다).
받은 시각은 curl 이 함께 받은 응답 머리의 Date 다. 두 가지 꼴을 받는다:
    응답 머리 그대로            "Sat, 21 Nov 2026 12:07:26 GMT"
    시간대가 붙은 ISO 8601      2026-11-21T12:07:26Z
어느 쪽이든 앱이 읽는 UTC 표기(2026-11-21T12:07:26Z)로 바꿔 적는다. 그 밖의 꼴과 시간대가 없는 시각은 받지 않는다
(앱은 이 값을 날짜로 읽는다. 읽지 못하는 글이 들어가면 앱이 켜지지 않는다).

끝 코드 (Python 이 스스로 멈출 때의 코드 1 을 오류로 쓴다. 그래서 어떻게 멈춰도 "바꿀 수 있음"으로 읽히지 않는다)
    compare  0 = 차이 없음. 할 일이 없다
             3 = 곡 목록을 바꿀 수 있는 차이가 있다(새 곡, 늘어난 패턴). **3 일 때만 apply 를 돌린다**
             2 = 먼저 사람이 봐야 하는 차이가 있다(없어진 번호, 곡명·아티스트·팩이 바뀐 곡, 없어진 패턴, 패턴이 하나도 없는 새 곡). 멈추고 확인한다
             1 = 오류(파일을 읽지 못함, 모르는 버튼·패턴 이름이나 칸, 잘못된 인자, 같은 파일을 두 자리에 줌). 원인을 본다
    apply    0 = 정상으로 끝났다. 곡 목록을 새로 썼는지, 차이가 없어 바꾸지 않았는지는 마지막 출력("곡 목록을 새로 썼다" / "차이가 없어 …")으로 안다
             2 = 먼저 사람이 봐야 하는 차이가 있어 바꾸지 않았다
             1 = 오류. 곡 목록은 바뀌지 않았다
출력과 보고서는 UTF-8 이다(결과를 파일로 돌리거나 다른 프로그램으로 받을 때도 같다).
compare 는 파일을 바꾸지 않는다(--report 를 주면 그 보고서만 쓴다).
apply 는 새 곡 목록을 같은 폴더의 임시 파일(<곡 목록 이름>.new)에 쓰고, 같은 자료로 다시 만든 것과 바이트가 같은지 본 뒤에
곡 목록과 바꿔 놓는다. 도중에 멈추거나 거절하면 전의 곡 목록이 그대로 남는다.
입력(songs.json), 곡 목록, 보고서는 서로 다른 파일이어야 한다. 같은 파일을 주면 아무것도 쓰지 않고 오류로 끝난다.
알려진 한계: 같은 파일인지는 경로로 견준다(대소문자는 가리지 않는다). 하드 링크처럼 이름이 다른데 같은 파일인 경우는 알아보지 못한다.
보고서의 이름을 임시 파일의 이름(<곡 목록 이름>.new)으로 주거나, 같은 곡 목록에 apply 를 동시에 둘 돌리는 경우도 막지 않는다.
한 번에 하나씩 돌리고, 보고서는 곡 목록과 다른 이름으로 둔다.

apply 뒤에는 늘어난 곡과 패턴을 앱이 실제로 쓰는지 개발용 빌드로 본다(Steam 곡 정보로 찾기, V아카이브 가져오기, 기존 기록 유지, 동명곡의 소리 기준):
    dotnet build app\\DJMAXLadderRecords.csproj -c Debug
    (Debug 빌드 폴더를 새 시험 폴더에 복사한 뒤 그 폴더에서) DJMAXLocalRecords.exe --catalog-update-check <바꾸기 전의 catalog.json 사본>
    결과는 그 폴더의 catalog-update-validation.json. 검사의 내용은 app\\CatalogUpdateChecks.cs 맨 위.

바꾸는 규칙(지금 catalog.json 이 이 규칙으로 만들어졌는지는 compare 가 "규칙 확인"으로 본다):
    title → SourceId, Id = "varchive:<title>", name → Name, composer → Artist, dlcCode → Pack,
    patterns 의 "4B"·"5B"·"6B"·"8B" → Button 4·5·6·8, "NM"·"HD"·"MX"·"SC" → NORMAL·HARD·MAXIMUM·SC.
    Charts 는 버튼 오름차순, 같은 버튼 안에서는 패턴 이름의 글자 순서(HARD, MAXIMUM, NORMAL, SC).
    Songs 는 SourceId 오름차순. 파일은 UTF-8(BOM 없음), 들여쓰기 2칸, 줄 끝 CRLF, 마지막 줄 끝 없음.
"""
from __future__ import annotations

import argparse
import json
import os
import sys
from datetime import datetime, timezone
from email.utils import parsedate_to_datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CATALOG = ROOT / "app" / "runtime" / "data" / "catalog.json"
SOURCE = "https://v-archive.net/db/v2/songs.json"
BUTTONS = {"4B": 4, "5B": 5, "6B": 6, "8B": 8}
PATTERNS = {"NM": "NORMAL", "HD": "HARD", "MX": "MAXIMUM", "SC": "SC"}
SAME, ERROR, REVIEW, CHANGE = 0, 1, 2, 3            # 끝 코드(파일 맨 위의 설명)
VERDICT = {SAME: "차이 없음", CHANGE: "곡 목록을 바꿀 수 있음", REVIEW: "먼저 사람이 봐야 함"}


class Unknown(Exception):
    """읽을 수 없거나 받을 수 없는 입력(모르는 버튼·패턴 이름이나 칸, 잘못된 시각, 같은 파일을 두 자리에 줌 등)."""


def normalize_time(text: str) -> str:
    """받은 시각을 앱이 읽는 UTC 표기(초까지. 예: 2026-11-21T12:07:26Z)로 바꾼다.

    응답 머리의 Date("Sat, 21 Nov 2026 12:07:26 GMT")와 시간대가 붙은 ISO 8601 을 받는다. 그 밖의 꼴과 시간대가 없는 시각은 Unknown."""
    text = text.strip()
    when = None
    try:
        when = datetime.fromisoformat(text)
    except ValueError:
        try:
            when = parsedate_to_datetime(text)
        except (TypeError, ValueError):
            when = None
    if when is None or when.tzinfo is None:
        raise Unknown(f"받은 시각을 읽을 수 없다: '{text}'. 응답 머리의 Date(\"Sat, 21 Nov 2026 12:07:26 GMT\")나 "
                      "시간대가 붙은 ISO 8601(2026-11-21T12:07:26Z)로 적는다")
    return when.astimezone(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def convert_song(item) -> dict:
    """V-ARCHIVE 의 곡 하나를 catalog.json 의 곡 하나로 바꾼다. 칸이 없거나 값의 종류가 다르면 Unknown."""
    if not isinstance(item, dict):
        raise Unknown(f"곡이 객체가 아니다: {json.dumps(item, ensure_ascii=False)[:200]}")
    for key in ("title", "name", "composer", "dlcCode", "patterns"):
        if key not in item:
            raise Unknown(f"곡에 '{key}' 칸이 없다: {json.dumps(item, ensure_ascii=False)[:200]}")
    number = item["title"]
    if isinstance(number, bool) or not isinstance(number, int) or number < 0:
        raise Unknown(f"곡 번호(title)가 0 이상의 정수가 아니다: {json.dumps(number, ensure_ascii=False)[:80]}")
    for key in ("name", "composer", "dlcCode"):
        if not isinstance(item[key], str) or not item[key].strip():
            raise Unknown(f"번호 {number} 의 '{key}' 가 빈 값이거나 글자가 아니다")
    if not isinstance(item["patterns"], dict):
        raise Unknown(f"번호 {number} 의 patterns 가 객체가 아니다")
    charts = []
    for button_name, patterns in item["patterns"].items():
        if button_name not in BUTTONS:
            raise Unknown(f"번호 {number} 의 모르는 버튼 이름 '{button_name}'")
        if not isinstance(patterns, dict):
            raise Unknown(f"번호 {number} 의 {button_name} 패턴이 객체가 아니다")
        for pattern_name in patterns:
            if pattern_name not in PATTERNS:
                raise Unknown(f"번호 {number} 의 모르는 패턴 이름 '{pattern_name}'")
            charts.append({"Button": BUTTONS[button_name], "Pattern": PATTERNS[pattern_name]})
    charts.sort(key=lambda c: (c["Button"], c["Pattern"]))
    return {"Id": f"varchive:{number}", "SourceId": number, "Name": item["name"],
            "Artist": item["composer"], "Pack": item["dlcCode"], "Charts": charts}


def convert(raw: list, retrieved_at: str) -> dict:
    songs = sorted((convert_song(item) for item in raw), key=lambda s: s["SourceId"])
    ids = [s["SourceId"] for s in songs]
    if len(ids) != len(set(ids)):
        raise Unknown("같은 번호(title)가 두 번 이상 나온다")
    return {"Source": SOURCE, "RetrievedAt": retrieved_at, "Songs": songs}


def dump(catalog: dict) -> bytes:
    """catalog.json 의 바이트(들여쓰기 2칸, CRLF, 마지막 줄 끝 없음)."""
    return json.dumps(catalog, ensure_ascii=False, indent=2).replace("\n", "\r\n").encode("utf-8")


def charts_of(song: dict) -> set:
    return {(c["Button"], c["Pattern"]) for c in song["Charts"]}


def label(song: dict) -> str:
    return f"{song['SourceId']} {song['Name']} — {song['Artist']} [{song['Pack']}]"


def chart_text(charts) -> str:
    return ", ".join(f"{b}B {p}" for b, p in sorted(charts))


def compare(old: dict, new: dict) -> tuple[int, list[str], dict]:
    """(끝 코드, 보고서 줄, 집계)."""
    before = {s["SourceId"]: s for s in old["Songs"]}
    after = {s["SourceId"]: s for s in new["Songs"]}
    added = [after[i] for i in sorted(after.keys() - before.keys())]
    removed = [before[i] for i in sorted(before.keys() - after.keys())]
    empty = [s for s in added if not s["Charts"]]          # 패턴이 하나도 없는 새 곡: 앱이 쓸 수 없고 검사할 것도 없다
    renamed, more, fewer = [], [], []
    for i in sorted(before.keys() & after.keys()):
        a, b = before[i], after[i]
        changes = [f"{k}: '{a[k]}' → '{b[k]}'" for k in ("Name", "Artist", "Pack") if a[k] != b[k]]
        if changes:
            renamed.append((b, changes))
        if charts_of(b) - charts_of(a):
            more.append((b, charts_of(b) - charts_of(a)))
        if charts_of(a) - charts_of(b):
            fewer.append((b, charts_of(a) - charts_of(b)))
    counts = {"지금 곡 수": len(before), "받은 곡 수": len(after), "새 곡": len(added), "없어진 번호": len(removed),
              "곡명·아티스트·팩이 바뀐 곡": len(renamed), "패턴이 늘어난 곡": len(more), "패턴이 없어진 곡": len(fewer),
              "패턴이 없는 새 곡": len(empty)}
    lines = ["## 집계", ""] + [f"- {k}: {v}" for k, v in counts.items()] + [""]
    if added:
        lines += ["## 새 곡 (곡 목록에 더한다)", ""]
        lines += [f"- {label(s)}: {chart_text(charts_of(s)) or '(패턴 없음)'}" for s in added] + [""]
    if more:
        lines += ["## 패턴이 늘어난 곡 (기존 기록은 그대로다)", ""]
        lines += [f"- {label(s)}: {chart_text(c)}" for s, c in more] + [""]
    if removed or renamed or fewer or empty:
        lines += ["## 먼저 사람이 봐야 하는 차이 (곡 목록을 바꾸지 않는다)", ""]
        lines += [f"- 없어진 번호: {label(s)}" for s in removed]
        lines += [f"- 바뀐 곡: {label(s)}: " + "; ".join(c) for s, c in renamed]
        lines += [f"- 없어진 패턴: {label(s)}: {chart_text(c)}" for s, c in fewer]
        lines += [f"- 패턴이 없는 새 곡: {label(s)}" for s in empty]
        lines += [""]
    names = {}
    for s in new["Songs"]:
        names.setdefault(s["Name"].strip().lower(), []).append(s)
    new_ids = {s["SourceId"] for s in added} | {s["SourceId"] for s, _ in renamed}
    same = [group for group in names.values() if len(group) > 1 and any(s["SourceId"] in new_ids for s in group)]
    if same:
        lines += ["## 이름이 같은 곡이 새로 생김 (소리 기준이 필요하다. tools\\build_audio_references.py 로 만든다)", ""]
        lines += ["- " + " / ".join(label(s) for s in group) for group in same] + [""]
    code = REVIEW if removed or renamed or fewer or empty else CHANGE if added or more else SAME
    return code, lines, counts


def load_raw(path: Path) -> list:
    raw = json.loads(path.read_text(encoding="utf-8-sig"))
    if not isinstance(raw, list) or not raw:
        raise Unknown("songs.json 이 비었거나 목록이 아니다")
    return raw


def same_file(a: Path, b: Path) -> bool:
    return os.path.normcase(str(a.resolve())) == os.path.normcase(str(b.resolve()))


def write_catalog(catalog: Path, new: dict, songs: Path, stamp: str) -> int:
    """새 곡 목록을 임시 파일에 쓰고 확인한 뒤 곡 목록과 바꿔 놓는다. 확인에 실패하면 Unknown(전의 곡 목록은 그대로다). 쓴 바이트 수를 돌려준다."""
    temp = catalog.with_name(catalog.name + ".new")
    if temp.exists():
        raise Unknown(f"임시 파일이 이미 있다: {temp}. 지난 실행이 남긴 것이면 지우고 다시 돌린다")
    data = dump(new)
    try:
        temp.write_bytes(data)
        again = dump(convert(load_raw(songs), stamp))
        written = temp.read_bytes()
        if written != again or len(json.loads(written.decode("utf-8"))["Songs"]) != len(new["Songs"]):
            raise Unknown("임시 파일이 같은 자료로 다시 만든 것과 다르다")
        os.replace(temp, catalog)
    finally:
        if temp.exists():
            temp.unlink()
    return len(data)


def run(args) -> int:
    catalog = args.catalog.resolve()
    places = [("입력(songs.json)", args.songs), ("곡 목록", catalog)] + ([("보고서", args.report)] if args.report else [])
    for i, (name_a, a) in enumerate(places):
        for name_b, b in places[i + 1:]:
            if same_file(a, b):
                raise Unknown(f"{name_a}와(과) {name_b}이(가) 같은 파일이다: {a}")
    stamp = normalize_time(args.retrieved_at) if args.retrieved_at else None

    old = json.loads(catalog.read_text(encoding="utf-8-sig"))
    new = convert(load_raw(args.songs), stamp or old["RetrievedAt"])
    code, lines, counts = compare(old, new)

    # 지금 catalog.json 이 위의 규칙으로 만들어졌는지: 같은 번호의 곡을 지금 받은 자료로 바꾼 것과 견준다(바뀐 곡은 뺀다)
    rebuilt = {s["SourceId"]: s for s in new["Songs"]}
    same_rule = sum(1 for s in old["Songs"] if rebuilt.get(s["SourceId"]) == s)
    head = [f"# 곡 목록 비교: {args.songs}", "",
            f"- 지금 곡 목록: {catalog.relative_to(ROOT) if catalog.is_relative_to(ROOT) else catalog} (받은 시각 {old['RetrievedAt']})",
            f"- 규칙 확인: 지금 곡 {len(old['Songs'])}개 가운데 {same_rule}개가 받은 자료를 이 도구의 규칙으로 바꾼 것과 칸까지 같다(아래 차이로 달라진 곡은 같지 않게 센다)",
            f"- 판정: {VERDICT[code]} (compare 의 끝 코드 {code})", ""]
    text = "\n".join(head + lines)
    print(text)
    if args.report:
        args.report.write_text(text, encoding="utf-8")
    if args.command == "compare":
        return code

    if code == REVIEW:
        print("먼저 사람이 봐야 하는 차이가 있어 곡 목록을 바꾸지 않았다.")
        return REVIEW
    if code == SAME:
        print("차이가 없어 곡 목록을 바꾸지 않았다.")
        return SAME
    if stamp is None:
        raise Unknown("--retrieved-at 이 없다(받은 시각을 적는다). 곡 목록을 바꾸지 않았다")
    size = write_catalog(catalog, new, args.songs, stamp)
    print(f"곡 목록을 새로 썼다: 곡 {len(new['Songs'])}개, {size:,}바이트, 받은 시각 {stamp}")
    return SAME


class Parser(argparse.ArgumentParser):
    def error(self, message):                       # argparse 는 보통 끝 코드 2 로 끝낸다. 2 는 "먼저 사람이 봐야 함"이므로 오류 코드로 바꾼다
        self.print_usage(sys.stderr)
        print(f"오류: 잘못된 인자: {message}", file=sys.stderr)
        raise SystemExit(ERROR)


def main() -> int:
    # 출력은 언제나 UTF-8 로 낸다. 결과를 파일로 돌리거나 다른 프로그램이 받을 때 Windows 의 기본 인코딩(CP949)으로는
    # 곡 이름과 보고서의 일부 글자를 적지 못해 도중에 멈춘다. 받는 쪽도 UTF-8 로 읽는다.
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8", errors="replace")
    parser = Parser(description=__doc__.splitlines()[0])
    parser.add_argument("command", choices=["compare", "apply"])
    parser.add_argument("songs", type=Path)
    parser.add_argument("--report", type=Path)
    parser.add_argument("--retrieved-at")
    parser.add_argument("--catalog", type=Path, default=CATALOG)
    args = parser.parse_args()
    try:
        return run(args)
    except Unknown as error:
        print(f"오류: {error}")
    except Exception as error:                      # 파일을 읽고 쓰지 못함, JSON 이 아님 등. 어떤 경우에도 오류 코드로 끝낸다
        print(f"오류({type(error).__name__}): {error}")
    return ERROR


if __name__ == "__main__":
    sys.exit(main())
