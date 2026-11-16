"""숫자 줄 탐지 모델을 ONNX 로 내보내고, .NET 에 넣어도 같은 네모가 나오는지 확인할 자료를 만든다.

  1) 내보낸 모델(ONNX Runtime, CPU)이 원래 모델과 같은 네모를 내는가
  2) CPU 로 띠 한 장(340x960)을 보는 데 걸리는 시간 (모듈은 그래픽 카드를 쓰지 않는다)
  3) .NET 대조용 자료: 띠 그림과, Python 이 그 그림에서 낸 네모

띠는 LEFT·CENTER 녹화에서 뽑는다 (RIGHT 는 쓰지 않는다).

실행: .venv\\Scripts\\python.exe -m detector.export_finder <탐지 이름(예: d2)>
출력: data\\locate\\runs\\<이름>\\finder.onnx (RUNS 아래), data\\parity_finder\\bands.bin, expected.tsv
"""
import struct
import sys
import time

import numpy as np
import onnxruntime as ort
import torch

from .full_frames import sources
from .locate_model import SCALE, STRIDE, RowFinder, find_rows
from .locate_run import TOP
from .locate_train import RUNS
from .locate_video import BANDS, read_bands
from .selftrain import ROOT

PARITY = ROOT / "data" / "parity_finder"
EVERY = 9            # 사례를 이만큼 건너 하나씩
PER_CASE = 12        # 사례마다 띠 몇 장 (구간에 고르게)


def boxes_from(prob: np.ndarray, dist: np.ndarray, threshold: float = 0.5) -> list[tuple]:
    """find_rows 와 같은 뒤처리(모델 출력 → 네모). ONNX 출력에도 똑같이 쓰려고 떼어 놓았다."""
    import cv2
    mask = (prob >= threshold).astype(np.uint8)
    count, labels, _, _ = cv2.connectedComponentsWithStats(mask, 8)
    boxes = []
    for i in range(1, count):
        ys, xs = np.where(labels == i)
        if len(ys) < 2:
            continue
        cx, cy = xs * STRIDE + STRIDE / 2, ys * STRIDE + STRIDE / 2
        left, top, right, bottom = (dist[k, ys, xs] for k in range(4))
        boxes.append((float(np.median(cx - left)), float(np.median(cy - top)), float(np.median(cx + right)), float(np.median(cy + bottom)), float(prob[ys, xs].mean())))
    boxes.sort(key=lambda b: -b[4])
    return boxes


@torch.no_grad()
def main():
    name = sys.argv[1]
    model = RowFinder()
    model.load_state_dict(torch.load(RUNS / name / "all.pt", map_location="cpu")); model.eval()
    path = RUNS / name / "finder.onnx"
    torch.onnx.export(model, torch.zeros(1, 3, 1080 - TOP, 960), str(path), input_names=["image"], output_names=["logit", "distance"],
                      dynamic_axes={"image": {0: "batch"}, "logit": {0: "batch"}, "distance": {0: "batch"}}, opset_version=17, dynamo=False)
    print(f"내보냄: {path} ({path.stat().st_size / 1e6:.2f} MB)", flush=True)

    table = sources()
    cases = [c for c in sorted(table) if "-right-" not in c][::EVERY]
    bands = []
    for c in cases:
        position = "CENTER" if "-center-" in c else "LEFT"
        every = read_bands(table[c], position, 2)
        for k in np.linspace(0, len(every) - 1, PER_CASE).astype(int):
            bands.append(np.ascontiguousarray(every[k][:, :, ::-1]))           # RGB
    bands = np.stack(bands)
    print(f"대조용 띠 {len(bands)}장 (사례 {len(cases)}개, {bands.shape[2]}x{bands.shape[1]})", flush=True)

    usual = [find_rows(model, bands[i:i + 1], "cpu")[0] for i in range(len(bands))]
    session = ort.InferenceSession(str(path), providers=["CPUExecutionProvider"])

    def run_onnx(s, image):
        logit, dist = s.run(None, {"image": (image[None].transpose(0, 3, 1, 2).astype(np.float32) / 255)})
        return boxes_from(1 / (1 + np.exp(-logit[0])), dist[0] * SCALE)

    exported = [run_onnx(session, b) for b in bands]
    differ = sum(len(a) != len(b) for a, b in zip(usual, exported))
    worst = max((abs(x - y) for a, b in zip(usual, exported) if len(a) == len(b) for p, q in zip(a, b) for x, y in zip(p[:4], q[:4])), default=0.0)
    print(f"1) 원래 모델과 내보낸 모델: 네모 수가 다른 띠 {differ} / {len(bands)}, 네모 좌표의 가장 큰 차이 {worst:.4f}화소 (네모 {sum(len(a) for a in usual)}개)")

    for threads in (0, 1):
        options = ort.SessionOptions()
        if threads:
            options.intra_op_num_threads = options.inter_op_num_threads = threads
        s = ort.InferenceSession(str(path), options, providers=["CPUExecutionProvider"])
        for i in range(5):
            run_onnx(s, bands[i])
        started = time.perf_counter()
        for i in range(60):
            run_onnx(s, bands[i % len(bands)])
        print(f"2) Python 의 ONNX Runtime(CPU)으로 띠 한 장: {(time.perf_counter() - started) / 60 * 1000:.1f} ms ({'스레드 제한 없음' if not threads else f'스레드 {threads}개'}, 뒤처리 포함)")

    PARITY.mkdir(parents=True, exist_ok=True)
    with open(PARITY / "bands.bin", "wb") as f:                              # 장수, 높이, 너비, 그리고 화소(RGB)
        f.write(struct.pack("<iii", len(bands), bands.shape[1], bands.shape[2])); f.write(bands.tobytes())
    lines = []
    for b in exported:                                                       # 띠마다 한 줄: 네모 수, 그리고 네모마다 x0 y0 x1 y1 점수
        lines.append("\t".join([str(len(b))] + [f"{v:.4f}" for box in b for v in box]))
    (PARITY / "expected.tsv").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f".NET 대조용 자료: {PARITY}")


if __name__ == "__main__":
    main()
