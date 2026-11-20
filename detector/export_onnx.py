"""판독 모델을 ONNX 로 내보내고, .NET 에 넣어도 같은 값이 나오는지 확인할 자료를 만든다.

학습은 Python 으로 하지만 앱은 .NET 이다. 옮기다 값이 달라지면 지금까지의 검증이 소용없어지므로 먼저 확인한다.
  1) 내보낸 모델(ONNX Runtime, CPU)이 원래 모델과 같은 숫자열을 내는가
  2) 그림 크기 맞추기를 다른 방식으로 해도 숫자열이 같은가 (.NET 에서 똑같이 구현하지 못했을 때의 위험)
  3) .NET 대조용 자료: 원본 크기로 잘라 낸 그림과, Python 이 그 그림에서 읽은 숫자열

대조용 그림은 LEFT·CENTER 사례에서만 뽑는다. RIGHT 는 학습에 쓰지 않는 평가용이라, LEFT·CENTER 가 기준을 통과한 뒤에 본다.

실행: .venv\\Scripts\\python.exe -m detector.export_onnx <실행 이름>
출력: data\\runs\\<이름>\\reader.onnx, data\\parity\\crops.bin, letterboxed.bin, expected.tsv
"""
import json
import struct
import sys
import time
from pathlib import Path

import cv2
import numpy as np
import onnxruntime as ort
import torch

from .build_dataset import HEIGHT, VALIDATION, WIDTH, cut, here, letterbox, read_image
from .model import BLANK, decode
from .selftrain import ROOT, load_model, to_tensor

PARITY = ROOT / "data" / "parity"


def greedy(logits: np.ndarray) -> list[str]:
    texts = []
    for row in logits.argmax(-1):
        out, last = [], BLANK
        for c in row:
            if c != last and c != BLANK:
                out.append(str(int(c)))
            last = c
        texts.append("".join(out))
    return texts


def letterbox_linear(part):
    """크기 맞추기를 일부러 다르게: 늘릴 때도 줄일 때도 선형 보간만 쓴다."""
    scale = min(HEIGHT / part.shape[0], WIDTH / part.shape[1])
    w = max(1, round(part.shape[1] * scale)); h = max(1, round(part.shape[0] * scale))
    small = cv2.resize(part, (w, h), interpolation=cv2.INTER_LINEAR)
    canvas = np.zeros((HEIGHT, WIDTH, 3), np.uint8)
    y = (HEIGHT - h) // 2; x = (WIDTH - w) // 2
    canvas[y:y + h, x:x + w] = small
    return canvas[:, :, ::-1].copy()


@torch.no_grad()
def main():
    run = sys.argv[1]
    model, _ = load_model(run)
    model = model.cpu().eval()
    path = ROOT / "data" / "runs" / run / "reader.onnx"
    torch.onnx.export(model, torch.zeros(1, 3, HEIGHT, WIDTH), str(path), input_names=["image"], output_names=["logits"],
                      dynamic_axes={"image": {0: "batch"}, "logits": {0: "batch"}}, opset_version=17, dynamo=False)
    print(f"내보냄: {path.name} ({path.stat().st_size / 1e6:.1f} MB)", flush=True)

    # 대조용 그림: LEFT·CENTER 사례를 고르게, 사례마다 플레이 프레임을 5장에 1장
    cases = [c for c in json.loads((VALIDATION / "candidate-v4" / "all270.json").read_text(encoding="utf-8")) if c["position"] != "RIGHT"]
    parts = []
    for c in cases[::4]:
        area = json.loads((VALIDATION / "candidate-v4" / "all270-results" / f"{c['id']}.json").read_text(encoding="utf-8")).get("area")
        if area is None:
            continue
        gear = c["id"].rsplit("-", 2)[0]
        for p in sorted(here(c["frames"]).glob("f*.png"))[::5]:
            parts.append(cut(read_image(p), area, gear))
    usual = np.stack([letterbox(p)[0] for p in parts])
    linear = np.stack([letterbox_linear(p) for p in parts])
    print(f"대조용 그림 {len(parts)}장 (사례 {len(cases[::4])}개)", flush=True)

    def run_torch(images):
        return np.concatenate([model(to_tensor(images[s:s + 256])).numpy() for s in range(0, len(images), 256)])

    session = ort.InferenceSession(str(path), providers=["CPUExecutionProvider"])

    def run_onnx(images):
        return np.concatenate([session.run(None, {"image": to_tensor(images[s:s + 256]).numpy()})[0] for s in range(0, len(images), 256)])

    a, b = run_torch(usual), run_onnx(usual)
    ta, tb = greedy(a), greedy(b)
    print(f"1) 원래 모델과 내보낸 모델: 숫자열이 다른 그림 {sum(x != y for x, y in zip(ta, tb))} / {len(ta)}, 출력값 최대 차이 {np.abs(a - b).max():.5f}")
    tl = greedy(run_onnx(linear))
    read = [i for i, t in enumerate(tb) if t]
    print(f"2) 크기 맞추기를 선형 보간으로 바꿨을 때: 숫자열이 달라진 그림 {sum(tb[i] != tl[i] for i in range(len(tb)))} / {len(tb)}"
          f" (숫자가 읽힌 {len(read)}장 중에서는 {sum(tb[i] != tl[i] for i in read)}장)")

    one = to_tensor(usual[:1]).numpy()
    session.run(None, {"image": one})
    started = time.perf_counter()
    for i in range(200):
        session.run(None, {"image": to_tensor(usual[i:i + 1]).numpy()})
    print(f"3) Python 의 ONNX Runtime(CPU)으로 한 장 읽는 시간: {(time.perf_counter() - started) / 200 * 1000:.1f} ms (다른 작업이 돌고 있으면 더 길게 나온다)")

    PARITY.mkdir(parents=True, exist_ok=True)
    with open(PARITY / "crops.bin", "wb") as f:                     # 원본 크기 그림(BGR): 장수, 그리고 장마다 높이·너비·화소
        f.write(struct.pack("<i", len(parts)))
        for p in parts:
            f.write(struct.pack("<ii", p.shape[0], p.shape[1])); f.write(np.ascontiguousarray(p).tobytes())
    usual.tofile(PARITY / "letterboxed.bin")                         # 크기를 맞춘 그림(RGB, 64x256)
    conf = np.exp(a.astype(np.float64) - np.log(np.exp(a.astype(np.float64)).sum(-1, keepdims=True))).max(-1).prod(-1)
    _, sure, _ = decode(torch.from_numpy(a))                         # 모듈이 쓰는 확신도: 모델이 그 숫자열에 준 확률
    contrast = [letterbox(p)[1] for p in parts]
    # 줄마다: 숫자열, (옛) 칸별 확률의 곱, 확신도, 자른 그림의 밝기 표준편차(빈 화면 판단에 쓴다)
    (PARITY / "expected.tsv").write_text("\n".join(f"{t}\t{c:.4f}\t{s:.6f}\t{k:.4f}" for t, c, s, k in zip(ta, conf, sure, contrast)) + "\n", encoding="utf-8")
    print(f".NET 대조용 자료: {PARITY}")


if __name__ == "__main__":
    main()
