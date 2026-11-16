"""숫자 줄 탐지 모델: 화면 아래쪽 띠를 보고 "점수 숫자 줄이 여기 있다"는 지도와 그 줄의 네모를 낸다.

  - 입력: RGB 그림 [묶음, 3, 높이, 너비] (0~1). 크기는 자유다. 좌표를 입력으로 받지 않는다.
  - 출력: 4화소마다 한 칸. 칸마다 [숫자 줄 안인가(로짓), 왼쪽·위·오른쪽·아래 가장자리까지의 거리(화소 ÷ SCALE)].
  - 한 칸이 보는 범위는 둘레 100화소쯤이다. 기어 전체 모습이 아니라 숫자 줄과 그 언저리만 본다.
"""
import cv2
import numpy as np
import torch
from torch import nn

STRIDE = 4
SCALE = 64.0      # 거리 값의 단위


def block(cin, cout, stride=1, dilation=1):
    return nn.Sequential(nn.Conv2d(cin, cout, 3, stride, dilation, dilation, bias=False), nn.BatchNorm2d(cout), nn.ReLU(inplace=True))


class RowFinder(nn.Module):
    def __init__(self, width: int = 48):
        super().__init__()
        self.body = nn.Sequential(block(3, 24, 2), block(24, width, 2), block(width, width), block(width, width, dilation=2),
                                  block(width, width, dilation=4), block(width, width))
        self.head = nn.Conv2d(width, 5, 1)

    def forward(self, image):
        out = self.head(self.body(image))
        return out[:, 0], torch.relu(out[:, 1:])          # 로짓 [B,h,w], 거리 [B,4,h,w]


@torch.no_grad()
def find_rows(model: RowFinder, images: np.ndarray, device, threshold: float = 0.5) -> list[list[tuple]]:
    """그림 묶음(RGB, uint8, [N,H,W,3]) → 그림마다 (x0, y0, x1, y1, 점수) 목록. 점수가 높은 순서."""
    x = torch.from_numpy(images).to(device).permute(0, 3, 1, 2).float() / 255
    logit, dist = model(x)
    prob = torch.sigmoid(logit).cpu().numpy(); dist = dist.cpu().numpy() * SCALE
    results = []
    for p, d in zip(prob, dist):
        mask = (p >= threshold).astype(np.uint8)
        count, labels, stats, _ = cv2.connectedComponentsWithStats(mask, 8)
        boxes = []
        for i in range(1, count):
            ys, xs = np.where(labels == i)
            if len(ys) < 2:
                continue
            cx, cy = xs * STRIDE + STRIDE / 2, ys * STRIDE + STRIDE / 2
            left, top, right, bottom = (d[k, ys, xs] for k in range(4))
            box = (float(np.median(cx - left)), float(np.median(cy - top)), float(np.median(cx + right)), float(np.median(cy + bottom)))
            boxes.append((*box, float(p[ys, xs].mean())))
        boxes.sort(key=lambda b: -b[4])
        results.append(boxes)
    return results
