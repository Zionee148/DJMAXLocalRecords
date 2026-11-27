"""컬렉션 가져오기가 화면을 읽을 때 쓰는 자료(app\\runtime\\data\\collection\\collection.json)를 만든다.

컬렉션 판독용 자료에는 이 도구가 게임과 같은 글꼴로 미리 그린 글자 그림을 넣는다. 판독할 때는 원본 글꼴 파일이 필요하지 않다.
녹화에서 얻는 것(점수의 숫자 표본, BEST 의 숫자, 카테고리 그림)도 여기서 함께 묶는다.

들어가는 것:
  title      제목의 글자 그림. 곡 목록의 이름(게임의 표기가 다른 곡은 그 표기)에 나오는 글자만.
             영문·기호 Lato Light 45px, 한글 Noto Sans KR·일본어 Noto Sans JP 굵기 300 45px.
  artist     목록의 아티스트 줄의 글자 그림. 곡 목록의 아티스트에 나오는 글자 모두(지금 읽는 것은 이름이 같은 곡들의 아티스트뿐이다).
             Lato Black 18px, 한글·일본어는 Noto Sans 굵기 900.
  digits     HIGH SCORE 칸의 글자 표본(숫자 열 개와 '-', 100만 점의 통째 표본). 받아 둔 실제 화면에서 모은 것과 녹화에서 모은 것.
  best       표 왼쪽 BEST 의 숫자 표본. 받아 둔 실제 화면과 녹화에서.
  categories 팩마다의 카테고리 줄 그림. 녹화를 읽은 결과의 곡과 그 프레임에서 모은다.
  shown      게임이 곡 목록의 이름과 다르게 그리는 곡(이름 -> 게임의 글).

쓰는 법(이 PC 에서만 돈다. 글꼴이 git 밖에 있다):

  1. 곡 목록을 바꿨을 때, 게임의 표기를 더했을 때 (보통의 쓰임)
       .venv\\Scripts\\python.exe -I tools\\build_collection_data.py <판정 JSON>
     title·artist·shown 만 다시 만들고, 화면에서 모은 표본(digits·best·categories)은 **지금 자료 파일의 것을 그대로 둔다.**
     글꼴만 있으면 된다. 녹화나 받아 둔 화면이 필요 없다.

  2. 화면에서 모은 표본까지 다시 만들 때 (게임의 표 모양이 바뀌었을 때)
       .venv\\Scripts\\python.exe -I tools\\build_collection_data.py <판정 JSON> --samples <glyphs.npz> <scan.json 이 있는 결과 폴더> <그 녹화> <받아 둔 폴더> <읽은 JSON>
     다섯을 모두 줘야 한다. 받아 둔 실제 화면 없이는 표본을 다시 만들지 않는다: 녹화만으로 만든 표본은 실제 화면에서 100만 점 같은 칸을 확실하게 읽지 못한다.
       glyphs.npz   녹화에서 모은 글자 묶음(묶음마다의 그림과 너비. 묶음의 이름은 판정 JSON 에 있다)
       결과 폴더     녹화를 읽은 결과(scan.json, cells.npz)
       받아 둔 폴더  앱의 --collection-capture 가 남긴 폴더
       읽은 JSON    앱의 --collection-read-capture 가 그 폴더를 읽어 낸 JSON(16칸이 견줄 값과 같은 화면의 목록)
     받아 둔 화면의 표 조각에서, 숫자가 서로 떨어져 있어 빈 줄로 나뉘는 칸의 조각을 읽은 값의 자리 숫자로 이름 붙여 모은다.
     글자는 화면에서 조금씩 다른 자리에 그려져 숫자마다 모양이 몇 가지다: 전체의 5% 이상인 모양을 모두 싣는다. 100만 점은 칸 전체를 한 표본으로 싣는다.

  --out <파일>  제품의 자료 파일 대신 다른 곳에 적는다(시험용). 1번에서 그대로 둘 표본은 언제나 제품의 자료 파일에서 읽는다.

글꼴의 자리: Lato 는 data\\fonts-src\\dl\\Lato (Google Fonts 저장소의 ofl/lato. OFL), Noto Sans 는 C:\\Windows\\Fonts 의 NotoSansKR-VF.ttf·NotoSansJP-VF.ttf.
곡 목록(app\\runtime\\data\\catalog.json)을 바꾸면 1번으로 다시 돌린다: 새 글자가 있으면 글자 그림이 늘어난다.
"""
import base64
import collections
import json
import sys
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
LATO = ROOT / "data" / "fonts-src" / "dl" / "Lato"
NOTO_KR = "C:/Windows/Fonts/NotoSansKR-VF.ttf"
NOTO_JP = "C:/Windows/Fonts/NotoSansJP-VF.ttf"
BIG = 2000

TITLE = {"latin": "Lato-Light.ttf", "size": 45, "weight": 300, "height": 50, "base": 38}
ARTIST = {"latin": "Lato-Black.ttf", "size": 18, "weight": 900, "height": 26, "base": 19}
BEST_NEAR = 16
CATEGORY = (605, 266, 1025, 286)       # 화면에서 카테고리 줄의 자리: x0, y0, x1, y1
CATEGORY_NEAR = 30


def pack(mask):
    return base64.b64encode(np.packbits(np.asarray(mask, dtype=np.uint8)).tobytes()).decode("ascii")


class Glyphs:
    """한 글자씩 그린다. 글자마다 (그림, 왼쪽으로 비켜난 정도, 다음 글자까지의 거리)."""

    def __init__(self, spec):
        self.spec = spec
        self.fonts = {}
        for key, path in (("latin", LATO / spec["latin"]), ("kr", NOTO_KR), ("jp", NOTO_JP)):
            small, big = ImageFont.truetype(str(path), spec["size"]), ImageFont.truetype(str(path), BIG)
            if key != "latin":
                small.set_variation_by_axes([spec["weight"]])
                big.set_variation_by_axes([spec["weight"]])
            self.fonts[key] = (small, big)
        self.missing = {key: self.render(key, "\uffff")[0].tobytes() for key in self.fonts}

    def render(self, key, ch):
        small, big = self.fonts[key]
        size, height, base = self.spec["size"], self.spec["height"], self.spec["base"]
        image = Image.new("L", (size * 3, height + 40), 0)
        ImageDraw.Draw(image).text((size, base + 20), ch, font=small, fill=255, anchor="ls")
        mask = (np.array(image) > 128)[20:20 + height]
        cols = np.nonzero(mask.any(axis=0))[0]
        advance = big.getlength(ch) / BIG * size
        if len(cols) == 0:
            return np.zeros((height, 0), bool), 0, advance
        return mask[:, cols.min():cols.max() + 1], int(cols.min()) - size, advance

    def get(self, ch):
        code = ord(ch)
        if 0xAC00 <= code <= 0xD7A3 or 0x1100 <= code <= 0x11FF or 0x3130 <= code <= 0x318F:
            order = ("kr", "jp", "latin")
        elif 0x3040 <= code <= 0x30FF or 0x4E00 <= code <= 0x9FFF:
            order = ("jp", "kr", "latin")
        else:
            order = ("latin", "kr", "jp")
        for key in order:
            mask, left, advance = self.render(key, ch)
            if ch.isspace() or mask.tobytes() != self.missing[key]:
                return mask, left, advance, key
        mask, left, advance = self.render(order[0], ch)
        return mask, left, advance, "없음"


def glyph_set(spec, texts):
    glyphs = Glyphs(spec)
    out, absent = {}, []
    for ch in sorted({ch for text in texts for ch in text}):
        mask, left, advance, key = glyphs.get(ch)
        if key == "없음":
            absent.append(ch)
        out[ch] = [int(mask.shape[1]), int(left), round(float(advance), 4), pack(mask)]
    return {"height": spec["height"], "base": spec["base"], "glyphs": out}, absent


def split(mask):
    cols = mask.any(axis=0)
    out, x = [], 0
    while x < mask.shape[1]:
        if not cols[x]:
            x += 1
            continue
        end = x
        while end < mask.shape[1] and cols[end]:
            end += 1
        out.append(mask[:, x:end])
        x = end
    return out


CELL_X = [930 - 605 + 6, 1054 - 605 + 6, 1178 - 605 + 6, 1302 - 605 + 6]     # 표 조각(x 605, y 455 에서 자른 것) 안에서 칸의 자리
CELL_Y = [477 - 455, 501 - 455, 525 - 455, 549 - 455]
CELL_W, CELL_H, CELL_WHITE = 112, 21, 170
BEST_BOX = (613 - 605, 497 - 455, 735 - 605, 523 - 455)


def capture_samples(folder, read):
    """받아 둔 화면의 표 조각에서 점수 칸과 BEST 의 숫자 표본을 모은다. 숫자마다 가장 흔한 모양들(전체의 5% 이상)을 낸다."""
    cells = collections.defaultdict(collections.Counter)      # 글자 -> {(너비, 비트): 개수}
    best = collections.defaultdict(collections.Counter)
    used = 0
    for entry in read:
        table = cv2.imread(str(Path(folder) / f"{entry['n']}-table.png"))
        if table is None:
            continue
        low = table.min(axis=2)
        used += 1
        for i, text in enumerate(entry["score"].values()):
            if text == "":
                continue
            x, y = CELL_X[i % 4], CELL_Y[i // 4]
            cell = low[y:y + CELL_H, x:x + CELL_W] > CELL_WHITE
            if text == "1000000":                 # 100만 점은 0 이 서로 붙어 낱글자로 나뉘지 않는다: 칸의 글자 전체를 한 표본으로 둔다
                cols = np.nonzero(cell.any(axis=0))[0]
                whole = cell[:, cols.min():cols.max() + 1]
                cells[text][(whole.shape[1], whole.tobytes())] += 1
            parts = split(cell)
            if len(parts) != len(text):
                continue
            for part, ch in zip(parts, text):
                cells[ch][(part.shape[1], part.tobytes())] += 1
        if entry.get("best"):
            x0, y0, x1, y1 = BEST_BOX
            parts = split(low[y0:y1, x0:x1] > CELL_WHITE)
            if len(parts) == len(entry["best"]):
                for part, ch in zip(parts, entry["best"]):
                    best[ch][(part.shape[1], part.tobytes())] += 1

    def pick(found, height):
        out, notes = [], []
        for ch in ["-"] + list("0123456789") + ["1000000"]:
            if ch not in found:
                continue
            total = sum(found[ch].values())
            least = 3 if len(ch) > 1 else max(3, total * 0.05)        # 통째 표본(100만 점)은 모양이 여러 가지라 세 번 넘게 나온 것을 모두 싣는다
            shapes = [(k, c) for k, c in found[ch].most_common() if c >= least]
            notes.append(f"{ch}: {total}개, 모양 {len(found[ch])}가지, 고른 것 {[c for _, c in shapes]}")
            for (width, bits), _ in shapes:
                out.append([ch, int(width), pack(np.frombuffer(bits, dtype=bool).reshape(height, width))])
        return out, notes

    cell_out, cell_notes = pick(cells, CELL_H)
    best_out, best_notes = pick(best, BEST_BOX[3] - BEST_BOX[1])
    return cell_out, best_out, [f"받아 둔 화면 {used}장"] + ["칸 " + n for n in cell_notes] + ["BEST " + n for n in best_notes]


def best_digits(folder, scan):
    """BEST 의 숫자 조각을 모양이 같은 것끼리 묶고, 그 곡의 16칸 최댓값의 자리 숫자로 이름을 붙인다."""
    z = np.load(folder / "cells.npz")
    shape = tuple(int(v) for v in z["best_shape"])
    groups = []           # [그림, {숫자: 표}]
    for entry in scan:
        values = [int(v) for v in entry.get("score", {}).values() if v.isdigit()]
        if not values:
            continue
        image = np.unpackbits(z["best"][entry["frame"]])[: shape[0] * shape[1]].reshape(shape).astype(bool)
        parts, text = split(image), str(max(values))
        if len(parts) != len(text):
            continue
        for part, digit in zip(parts, text):
            for group in groups:
                if group[0].shape == part.shape and np.count_nonzero(group[0] != part) < BEST_NEAR:
                    group[1][digit] += 1
                    break
            else:
                groups.append([part, collections.Counter({digit: 1})])
    mixed = [dict(g[1]) for g in groups if len(g[1]) > 1]
    if mixed:
        raise SystemExit(f"BEST 의 글자 묶음에 두 숫자가 섞였다: {mixed}")
    out = [[g[1].most_common(1)[0][0], int(g[0].shape[1]), pack(g[0])] for g in sorted(groups, key=lambda g: -sum(g[1].values()))]
    have = {o[0] for o in out}
    if have != set("0123456789"):
        raise SystemExit(f"BEST 의 숫자 표본에 빠진 숫자가 있다: {sorted(set('0123456789') - have)}")
    return {"height": shape[0], "width": shape[1], "glyphs": out}, len(groups)


def category_mask(frame):
    """카테고리 줄에서 글자로 볼 화소. 글자는 색이 여러 가지라, 굵은 바탕을 빼고 바탕보다 밝은 것을 고른다(앱의 CollectionScreen.Category 와 같아야 한다)."""
    x0, y0, x1, y1 = CATEGORY
    part = frame[y0 - 8:y1 + 8, x0:x1].max(axis=2)
    back = cv2.morphologyEx(part, cv2.MORPH_OPEN, np.ones((7, 7), np.uint8))
    return ((part.astype(np.int16) - back) > 24)[8:-8]


def categories(scan, video, songs):
    """팩마다 카테고리 줄의 그림 하나(그 팩의 곡들에서 화소마다 다수결). 색 글자와 움직이는 배경 때문에 곡마다 조금씩 다르게 잡힌다."""
    cap = cv2.VideoCapture(video)
    by_pack = collections.defaultdict(list)       # 팩 -> [그림…]
    pack_of = {s["Id"]: s["Pack"] for s in songs}
    seen = []
    for entry in scan:
        if "id" not in entry:
            continue
        cap.set(cv2.CAP_PROP_POS_FRAMES, entry["frame"])
        ok, frame = cap.read()
        if not ok:
            continue
        mask = category_mask(frame)
        by_pack[pack_of[entry["id"]]].append(mask)
        seen.append((pack_of[entry["id"]], mask))
    cap.release()
    refs = {name: np.mean(masks, axis=0) > 0.5 for name, masks in sorted(by_pack.items())}
    # 같은 글을 쓰는 팩(예: 여러 협업 팩의 MUSIC GAME)은 그림이 같다: 서로 가까운 팩을 한 묶음으로 본다
    same = {a: {b for b in refs if np.count_nonzero(refs[a] != refs[b]) < CATEGORY_NEAR} for a in refs}
    right = 0
    wrong = collections.Counter()
    for name, mask in seen:
        nearest = min(refs, key=lambda k: np.count_nonzero(refs[k] != mask))
        if nearest in same[name]:
            right += 1
        else:
            wrong[(name, nearest)] += 1
    labels = sorted({tuple(sorted(v)) for v in same.values()}, key=lambda v: (-len(v), v))
    notes = [f"곡 {len(seen)} 가운데 가장 가까운 그림이 제 팩(이나 같은 글을 쓰는 팩)인 곡 {right}", f"다른 팩으로 간 것 {dict(wrong)}",
             "같은 글을 쓰는 팩의 묶음: " + ", ".join("=".join(v) for v in labels if len(v) > 1)]
    return {"height": CATEGORY[3] - CATEGORY[1], "width": CATEGORY[2] - CATEGORY[0], "packs": {k: pack(v) for k, v in refs.items()}}, notes


def main():
    product = ROOT / "app" / "runtime" / "data" / "collection" / "collection.json"
    argv = sys.argv[1:]
    out_path = product
    if "--out" in argv:
        at = argv.index("--out")
        out_path = Path(argv[at + 1])
        del argv[at:at + 2]
    samples = None
    if "--samples" in argv:
        at = argv.index("--samples")
        samples = argv[at + 1:at + 6]
        if len(samples) != 5 or any(a.startswith("--") for a in samples):
            raise SystemExit("--samples 에는 다섯을 준다: <glyphs.npz> <scan.json 이 있는 결과 폴더> <그 녹화> <받아 둔 폴더> <읽은 JSON>")
        del argv[at:at + 6]
    if len(argv) != 1:
        raise SystemExit("쓰는 법은 파일 맨 위에 있다: build_collection_data.py <판정 JSON> [--samples …] [--out <파일>]")
    judged = json.loads(Path(argv[0]).read_text(encoding="utf-8"))
    songs = json.loads((ROOT / "app" / "runtime" / "data" / "catalog.json").read_text(encoding="utf-8-sig"))["Songs"]

    shown = judged["게임의 표기(곡 목록의 이름과 다른 곡)"]
    names = [shown.get(s["Name"].strip(), s["Name"].strip()) for s in songs]
    title, absent = glyph_set(TITLE, names)
    by_name = collections.defaultdict(list)
    for s in songs:
        by_name[s["Name"].strip()].append(s)
    same = {name: group for name, group in by_name.items() if len(group) > 1}
    artist, absent2 = glyph_set(ARTIST, [s["Artist"] for s in songs])

    if samples is None:
        if not product.exists():
            raise SystemExit(f"그대로 둘 표본을 읽을 자료 파일이 없다: {product}. --samples 로 표본까지 만든다")
        kept = json.loads(product.read_text(encoding="utf-8"))
        digit_part, best, cats = kept["digits"], kept["best"], kept["categories"]
        notes = ["화면에서 모은 표본(digits·best·categories)은 지금 자료 파일의 것을 그대로 두었다"]
    else:
        sample = np.load(samples[0])
        folder, video = Path(samples[1]), samples[2]
        scan = json.loads((folder / "scan.json").read_text(encoding="utf-8"))
        labels = judged["글자 묶음의 이름(glyphs.py 의 묶음 번호)"]
        digits = [[label, int(sample["widths"][int(i)]), pack(sample["boards"][int(i)][:, :int(sample["widths"][int(i)])])]
                  for i, label in labels.items() if label == "-" or label.isdigit()]
        best, _ = best_digits(folder, scan)
        cats, notes = categories(scan, video, songs)
        real_cells, real_best, real_notes = capture_samples(samples[3], json.loads(Path(samples[4]).read_text(encoding="utf-8")))
        # 입력에서 표본을 다 얻었는지 본다. 모자라면 아무것도 쓰지 않는다(폴더나 녹화의 경로가 틀려도 다섯 인자는 채워진다)
        lacking = sorted(set(["-"] + list("0123456789")) - {g[0] for g in real_cells})
        if lacking:
            raise SystemExit(f"받아 둔 화면에서 점수 칸의 표본을 얻지 못한 글자가 있다: {lacking}. 받아 둔 폴더({samples[3]})와 읽은 JSON({samples[4]})을 확인한다. 자료 파일은 쓰지 않았다")
        lacking = sorted(set("0123456789") - {g[0] for g in real_best})
        if lacking:
            raise SystemExit(f"받아 둔 화면에서 BEST 의 표본을 얻지 못한 숫자가 있다: {lacking}. 받아 둔 폴더({samples[3]})와 읽은 JSON({samples[4]})을 확인한다. 자료 파일은 쓰지 않았다")
        lacking = sorted({s["Pack"] for s in songs if any(e.get("id") == s["Id"] for e in scan)} - set(cats["packs"]))
        if lacking or not cats["packs"]:
            raise SystemExit(f"녹화에서 카테고리 그림을 얻지 못한 팩이 있다: {lacking or '전부'}. 녹화({samples[2]})와 결과 폴더({samples[1]})를 확인한다. 자료 파일은 쓰지 않았다")
        digit_part = {"height": int(sample["boards"].shape[1]), "glyphs": real_cells + digits}   # 실제 화면의 표본을 먼저 댄다
        best["glyphs"] = real_best + best["glyphs"]
        notes = real_notes + notes

    data = {"version": 1, "title": title, "artist": artist, "digits": digit_part, "best": best, "categories": cats, "shown": shown}
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps(data, ensure_ascii=False, separators=(",", ":")), encoding="utf-8", newline="\n")
    print(f"적은 곳 {out_path} ({out_path.stat().st_size:,}바이트)")
    print(f"제목의 글자 {len(title['glyphs'])}, 글꼴에 없는 글자 {absent}")
    print(f"아티스트의 글자 {len(artist['glyphs'])} (이름이 같은 곡 {len(same)}쌍), 글꼴에 없는 글자 {absent2}")
    print(f"점수의 글자 표본 {len(digit_part['glyphs'])}, BEST 의 숫자 표본 {len(best['glyphs'])}, 카테고리 그림 {len(cats['packs'])}팩")
    for note in notes:
        print("  ", note)


if __name__ == "__main__":
    main()
