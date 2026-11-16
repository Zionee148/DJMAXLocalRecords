"""숫자 줄 탐지 모델 학습 (LEFT·CENTER 자료만).

기어를 세 묶음으로 나눈다. "묶음 k 빼고 학습"한 모델은 그 묶음의 기어를 한 번도 보지 못했으므로,
그 기어들에서의 결과는 처음 보는 기어 디자인에서의 결과다. (녹화 자체는 같은 날 같은 곡이라 완전히 다른 자료는 아니다.)

실행: .venv\\Scripts\\python.exe -m detector.locate_train <이름> <0|1|2|all> [걸음 수=4000]
출력: data\\locate_runs\\<이름>\\fold<k>.pt 또는 all.pt
"""
import json
import sys
import time

import cv2
import numpy as np
import torch
import torch.nn.functional as F

from .locate_model import SCALE, STRIDE, RowFinder
from .selftrain import DEVICE, ROOT

DATA = ROOT / "data" / "locate"
RUNS = ROOT / "data" / "locate_runs"
PATCH_H, PATCH_W = 256, 512
FOLDS = 3
# d1(크기 0.75~1.35배, 색 뒤집기 없음)은 빼 둔 기어 가운데 글자가 크거나 바탕이 밝은 기어(TECHNIKA 2, So Happy)를 못 찾았다.
SCALE_RANGE = (0.55, 2.0)
INVERT = 0.25


def fold_of(gear: str, gears: list[str]) -> int:
    return gears.index(gear) % FOLDS


def load(which: str):
    """which 가 숫자면 그 묶음을 뺀 나머지, all 이면 전부."""
    index = json.loads((DATA / "index.json").read_text(encoding="utf-8"))
    gears = sorted({i["gear"] for i in index})
    keep = [i for i in index if which == "all" or fold_of(i["gear"], gears) != int(which)]
    images = [np.load(DATA / f"{i['case']}.npy") for i in keep]
    return keep, images


def sample(keep, images, rng):
    """그림 하나에서 조각 하나. 절반 넘게는 숫자 줄이 들어가게, 나머지는 아무 데나."""
    k = int(rng.integers(len(keep)))
    image = images[k][int(rng.integers(len(images[k])))]
    box = np.array(keep[k]["box"], np.float32)
    scale = float(np.exp(rng.uniform(np.log(SCALE_RANGE[0]), np.log(SCALE_RANGE[1]))))   # 글자 크기가 달라져도 찾도록
    if scale != 1.0:
        image = cv2.resize(image, None, fx=scale, fy=scale, interpolation=cv2.INTER_AREA if scale < 1 else cv2.INTER_LINEAR)
        box = box * scale
    h, w = image.shape[:2]
    if h < PATCH_H or w < PATCH_W:                              # 줄였더니 조각보다 작아지면 검정으로 채운다
        canvas = np.zeros((max(h, PATCH_H), max(w, PATCH_W), 3), np.uint8); canvas[:h, :w] = image; image = canvas; h, w = image.shape[:2]
    if rng.random() < 0.6:
        lo_x, hi_x = max(0, int(box[2]) - PATCH_W + 1), min(w - PATCH_W, int(box[0]))
        lo_y, hi_y = max(0, int(box[3]) - PATCH_H + 1), min(h - PATCH_H, int(box[1]))
        x = int(rng.integers(lo_x, hi_x + 1)) if hi_x >= lo_x else int(rng.integers(0, w - PATCH_W + 1))
        y = int(rng.integers(lo_y, hi_y + 1)) if hi_y >= lo_y else int(rng.integers(0, h - PATCH_H + 1))
    else:
        x = int(rng.integers(0, w - PATCH_W + 1)); y = int(rng.integers(0, h - PATCH_H + 1))
    patch = image[y:y + PATCH_H, x:x + PATCH_W].astype(np.float32)
    box = box - np.array([x, y, x, y], np.float32)
    # 색과 밝기를 흔든다: 영상과 실제 화면의 차이, 기어 색 차이에 덜 기대도록
    if rng.random() < 0.5:
        patch = patch[:, :, rng.permutation(3)]
    if rng.random() < INVERT:                                    # 밝은 바탕에 어두운 숫자(TECHNIKA 2 등)도 같은 숫자 줄이다
        patch = 255.0 - patch
    patch = (patch - 127.5) * rng.uniform(0.7, 1.3) + 127.5 + rng.uniform(-25, 25)
    if rng.random() < 0.3:
        patch = cv2.GaussianBlur(patch, (3, 3), rng.uniform(0.3, 0.9))
    if rng.random() < 0.3:
        patch = patch + rng.normal(0, rng.uniform(1, 4), patch.shape).astype(np.float32)
    return np.clip(patch, 0, 255).astype(np.uint8), box


def targets(box, device):
    """조각 하나의 정답: 칸마다 안(1)·밖(0)·셈하지 않음(-1), 그리고 네 가장자리까지의 거리."""
    gh, gw = PATCH_H // STRIDE, PATCH_W // STRIDE
    cy = (torch.arange(gh, device=device).float() + 0.5) * STRIDE
    cx = (torch.arange(gw, device=device).float() + 0.5) * STRIDE
    yy, xx = torch.meshgrid(cy, cx, indexing="ij")
    x0, y0, x1, y1 = (float(v) for v in box)
    inside = (xx > x0) & (xx < x1) & (yy > y0) & (yy < y1)
    mx, my = (x1 - x0) * 0.15, (y1 - y0) * 0.2                  # 가운데 쪽만 "안"으로 가르치고 가장자리 띠는 셈하지 않는다
    core = (xx > x0 + mx) & (xx < x1 - mx) & (yy > y0 + my) & (yy < y1 - my)
    cut = (x0 < 0) or (y0 < 0) or (x1 > PATCH_W) or (y1 > PATCH_H)   # 조각 가장자리에 잘린 줄은 통째로 셈하지 않는다
    label = torch.zeros(gh, gw, device=device)
    label[inside] = -1
    if not cut:
        label[core] = 1
    dist = torch.stack([xx - x0, yy - y0, x1 - xx, y1 - yy]) / SCALE
    return label, dist


def main():
    name, which = sys.argv[1], sys.argv[2]
    steps = int(sys.argv[3]) if len(sys.argv) > 3 else 4000
    torch.manual_seed(0); rng = np.random.default_rng(0)
    keep, images = load(which)
    print(f"학습 사례 {len(keep)}개, 그림 {sum(len(i) for i in images)}장, 기어 {len({k['gear'] for k in keep})}종", flush=True)
    model = RowFinder().to(DEVICE)
    optimizer = torch.optim.AdamW(model.parameters(), 2e-3, weight_decay=1e-4)
    schedule = torch.optim.lr_scheduler.OneCycleLR(optimizer, 2e-3, total_steps=steps)
    started = time.time()
    for step in range(1, steps + 1):
        batch = [sample(keep, images, rng) for _ in range(24)]
        x = torch.from_numpy(np.stack([b[0] for b in batch])).to(DEVICE).permute(0, 3, 1, 2).float() / 255
        made = [targets(b[1], DEVICE) for b in batch]
        label = torch.stack([m[0] for m in made]); dist = torch.stack([m[1] for m in made])
        logit, predicted = model(x)
        counted = label >= 0
        positive = label == 1
        weight = torch.where(positive, 4.0, 1.0)
        loss_map = (F.binary_cross_entropy_with_logits(logit, label.clamp(min=0), reduction="none") * weight)[counted].mean()
        loss_box = F.l1_loss(predicted.permute(0, 2, 3, 1)[positive], dist.permute(0, 2, 3, 1)[positive]) if positive.any() else logit.sum() * 0
        loss = loss_map + loss_box
        optimizer.zero_grad(); loss.backward(); optimizer.step(); schedule.step()
        if step % 500 == 0 or step == steps:
            print(f"  {step}/{steps} 지도 {loss_map.item():.4f} 네모 {loss_box.item():.4f} ({time.time() - started:.0f}초)", flush=True)
    out = RUNS / name; out.mkdir(parents=True, exist_ok=True)
    torch.save(model.state_dict(), out / (f"fold{which}.pt" if which != "all" else "all.pt"))


if __name__ == "__main__":
    main()
