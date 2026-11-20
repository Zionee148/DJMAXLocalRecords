"""프로그램의 아이콘(app\\AppIcon.ico)을 그린다. 자체 제작한 그림이다.

쓰는 법 (프로젝트 폴더에서):
    .venv\\Scripts\\python.exe tools\\make_icon.py [--out <ico 파일>] [--preview <png 파일>]

그림: 어두운 둥근 네모 위에 노랑에서 초록으로 이어지는 그러데이션의 굵은 "90%".
숫자와 기호를 타원과 다각형으로 직접 그린다. 글꼴 파일이나 다른 그림의 화소, 게임의 그림·로고·캐릭터를 쓰지 않는다.
  9: 둥근 고리에서 왼쪽 아래로 곧게 뻗는 다리.   0: 세로로 긴 타원, 가운데 구멍은 좁다.   %: 고리 둘과 굵은 빗금.

크게(1024) 그려서 줄인다. 32픽셀 이하는 바탕의 여백을 줄이고 글자를 더 크게 놓는다.
ICO 에는 16·24·32·48·64·128·256 을 넣는다. 256 은 PNG 로, 나머지는 32비트 BMP 로 넣는다(옛 도구들도 읽게).
--out 을 주지 않으면 app\\AppIcon.ico 를 바꾼다. 같은 입력이면 같은 파일이 나온다.
"""
from __future__ import annotations

import argparse
import io
import struct
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
N = 1024
SIZES = [16, 24, 32, 48, 64, 128, 256]
TILE = (14, 14, 16)
YELLOW, GREEN = (238, 224, 60), (118, 196, 72)
# 글자 셋을 나란히 놓았을 때의 너비를 1로 본 치수(가로는 그 너비, 세로도 같은 단위)
HEIGHT = 0.433                 # 0 의 높이


def blank() -> Image.Image:
    return Image.new("L", (N, N), 0)


def ellipse(cx: float, cy: float, rx: float, ry: float) -> Image.Image:
    m = blank()
    ImageDraw.Draw(m).ellipse([cx - rx, cy - ry, cx + rx, cy + ry], fill=255)
    return m


def polygon(points: list[tuple[float, float]]) -> Image.Image:
    m = blank()
    ImageDraw.Draw(m).polygon(points, fill=255)
    return m


def union(*masks: Image.Image) -> Image.Image:
    out = masks[0]
    for m in masks[1:]:
        out = ImageChops.lighter(out, m)
    return out


def nine(x: float, y: float, w: float, h: float) -> Image.Image:
    """9: 둥근 고리(구멍은 작고 둥글다) + 오른쪽 어깨에서 왼쪽 아래로 곧게 뻗어 바닥에서 가로로 잘리는 다리."""
    bowl = ellipse(x + w / 2, y + h * 0.315, w / 2, h * 0.315)
    # 다리의 오른쪽 변은 바닥의 오른쪽 끝에서 고리에 닿는 접선이다(고리에서 다리로 꺾이지 않고 매끄럽게 이어진다).
    # 고리(중심에서 본 반지름 0.5w, 0.315h)와 점 (0.53w, h)의 접점이 (0.950w, 0.452h)다
    leg = polygon([(x + w * 0.46, y + h * 0.60), (x + w * 0.950, y + h * 0.452), (x + w * 0.53, y + h), (x + w * 0.09, y + h)])
    hole = ellipse(x + w * 0.50, y + h * 0.33, w * 0.185, h * 0.127)
    return ImageChops.subtract(union(bowl, leg), hole)


def zero(x: float, y: float, w: float, h: float) -> Image.Image:
    """0: 세로로 긴 타원. 옆은 두껍고 구멍은 좁고 길다."""
    return ImageChops.subtract(ellipse(x + w / 2, y + h / 2, w / 2, h / 2), ellipse(x + w * 0.51, y + h * 0.49, w * 0.187, h * 0.31))


def percent(x: float, y: float, w: float, h: float) -> Image.Image:
    """%: 왼쪽 위와 오른쪽 아래의 고리, 오른쪽 위에서 왼쪽 아래로 가는 굵은 빗금(끝은 가로로 잘린다)."""
    rx, ry, t = w * 0.23, h * 0.25, w * 0.150          # 고리의 반지름과 두께
    rings = []
    for cx, cy in ((x + rx, y + ry), (x + w - rx, y + h - ry)):
        rings.append(ImageChops.subtract(ellipse(cx, cy, rx, ry), ellipse(cx, cy, rx - t, ry - t)))
    slash = polygon([(x + w * 0.685, y), (x + w * 0.945, y), (x + w * 0.315, y + h), (x + w * 0.055, y + h)])
    return union(*rings, slash)


def text(left: float, width: float) -> tuple[Image.Image, float, float]:
    """'90%' 를 left 에서 width 너비로, 세로는 가운데에 놓는다. (글자 모양의 마스크, 글자의 위 y, 글자의 아래 y)를 돌려준다."""
    h = width * HEIGHT
    top = (N - h) / 2
    mask = union(nine(left, top, width * 0.305, h * 0.975),
                 zero(left + width * 0.305, top, width * 0.328, h),
                 percent(left + width * 0.652, top + h * 0.074, width * 0.348, h * 0.84))
    return mask, top, top + h


def paint(left: float, width: float, inset: int, radius: int) -> Image.Image:
    mask, top, bottom = text(left, width)
    grad = Image.new("RGB", (N, N))
    d = ImageDraw.Draw(grad)
    for y in range(N):
        t = min(1.0, max(0.0, (y - top) / (bottom - top)))
        d.line([(0, y), (N, y)], fill=tuple(round(a + (b - a) * t) for a, b in zip(YELLOW, GREEN)))
    plate = Image.new("L", (N, N), 0)
    ImageDraw.Draw(plate).rounded_rectangle([inset, inset, N - inset, N - inset], radius=radius, fill=255)
    out = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    out.paste(Image.new("RGBA", (N, N), TILE + (255,)), (0, 0), plate)
    out.paste(grad, (0, 0), mask)
    return out


def large() -> Image.Image:
    """48픽셀 이상에 쓰는 그림."""
    return paint(78, 868, 48, 210)


def small() -> Image.Image:
    """32픽셀 이하에 쓰는 그림: 바탕의 여백을 줄이고 글자를 네모의 너비에 거의 차게 놓는다."""
    return paint(26, 972, 0, 190)


def frames() -> dict[int, Image.Image]:
    big, little = large(), small()
    return {size: (little if size <= 32 else big).resize((size, size), Image.LANCZOS) for size in SIZES}


def bmp_entry(image: Image.Image) -> bytes:
    """ICO 안의 32비트 BMP: 헤더(높이는 두 배로 적는다) + 아래 행부터 위로 BGRA + 1비트 마스크(모두 0. 투명도는 알파 채널로 나타낸다)."""
    w, h = image.size
    pixels = image.transpose(Image.FLIP_TOP_BOTTOM).tobytes("raw", "BGRA")
    mask = bytes(((w + 31) // 32) * 4 * h)
    return struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0, len(pixels) + len(mask), 0, 0, 0, 0) + pixels + mask


def png_entry(image: Image.Image) -> bytes:
    data = io.BytesIO()
    image.save(data, format="PNG", optimize=True)
    return data.getvalue()


def write_ico(images: dict[int, Image.Image], path: Path) -> None:
    entries = [(size, png_entry(image) if size >= 256 else bmp_entry(image)) for size, image in sorted(images.items())]
    header = struct.pack("<HHH", 0, 1, len(entries))
    offset = len(header) + 16 * len(entries)
    table, body = b"", b""
    for size, data in entries:
        table += struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
        body += data
    path.write_bytes(header + table + body)


def preview(images: dict[int, Image.Image], path: Path) -> None:
    """크기마다 밝은 바탕과 어두운 바탕에 놓은 그림(눈으로 보려는 것)."""
    pad = 24
    width = sum(size + pad for size in SIZES) + pad
    sheet = Image.new("RGB", (width, (256 + pad * 2) * 2), (245, 245, 247))
    ImageDraw.Draw(sheet).rectangle([0, 256 + pad * 2, width, sheet.height], fill=(32, 32, 36))
    for row in range(2):
        x = pad
        for size in reversed(SIZES):
            sheet.paste(images[size], (x, row * (256 + pad * 2) + pad + (256 - size) // 2), images[size])
            x += size + pad
    path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(path)


def main() -> int:
    parser = argparse.ArgumentParser(description="프로그램의 아이콘을 그린다.")
    parser.add_argument("--out", type=Path, default=ROOT / "app" / "AppIcon.ico", help="쓸 ico 파일 (기본: %(default)s)")
    parser.add_argument("--preview", type=Path, help="크기마다 놓아 본 그림을 쓸 png 파일")
    args = parser.parse_args()
    images = frames()
    write_ico(images, args.out)
    print(f"아이콘: {args.out}  ({args.out.stat().st_size:,}바이트, 크기 {SIZES})")
    if args.preview:
        preview(images, args.preview)
        print(f"미리 보기: {args.preview}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
