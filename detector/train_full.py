"""판독기 보강 학습: 0.5초 자료의 라벨에 전체 프레임에서 뽑은 자료(build_full_train)를 더해, 이전 모델에서 이어서 학습한다.

RIGHT 녹화는 여기서도 학습에 쓰지 않는다 (전체 프레임 자료도 LEFT·CENTER 에서만 뽑았다).

실행: .venv\\Scripts\\python.exe -m detector.train_full <새 실행 이름> <이전 실행 이름> [걸음 수=12000]
출력: data\\runs\\<새 이름>\\model.pt, round1.json, predictions.npz
"""
import ctypes
import json
import sys
import time

import numpy as np
import torch

from .model import HEIGHT, WIDTH
from .selftrain import (DATA_NAME, EMPTY, ROOT, TRAIN_POS, borrowed_predictions, evaluate, load, load_model, make_labels, predict,
                        save_predictions, seed_labels, summary, train)


def main():
    name, previous = sys.argv[1], sys.argv[2]
    steps = int(sys.argv[3]) if len(sys.argv) > 3 else 12000
    try:
        ctypes.windll.kernel32.SetPriorityClass(ctypes.windll.kernel32.GetCurrentProcess(), 0x4000)
    except Exception:
        pass
    torch.set_num_threads(6); torch.manual_seed(0); np.random.seed(0)
    out = ROOT / "data" / "runs" / name; out.mkdir(parents=True, exist_ok=True)
    (out / "config.json").write_text(json.dumps({"data": DATA_NAME, "height": HEIGHT}), encoding="utf-8")

    d = load()
    seed = seed_labels(d)
    values, conf, texts = borrowed_predictions(d, previous)
    half = make_labels(d, values, conf, TRAIN_POS, texts); half.update(seed)
    half_index = sorted(half)

    files = sorted((ROOT / "data" / "full_train").glob("*.npz"))
    counts = [len(np.load(f)["labels"]) for f in files]
    images = np.empty((len(half_index) + sum(counts), HEIGHT, WIDTH, 3), np.uint8)
    images[:len(half_index)] = d.images[half_index]
    labels = {k: half[i] for k, i in enumerate(half_index)}
    at = len(half_index)
    for f in files:
        z = np.load(f)
        n = len(z["labels"])
        images[at:at + n] = z["images"]
        labels.update({at + j: int(v) for j, v in enumerate(z["labels"])})
        at += n
    full = sum(counts)
    print(f"0.5초 자료 라벨 {len(half_index)}장 + 전체 프레임 자료 {full}장 (사례 {len(files)}개), "
          f"그중 숫자 없음 {sum(1 for v in labels.values() if v == EMPTY)}장", flush=True)

    model, _ = load_model(previous)                 # 이전 모델에서 이어서
    started = time.time()
    loss = train(model, images, labels, steps, 1e-3)
    torch.save(model.state_dict(), out / "model.pt")
    print(f"학습 끝: 손실 {loss:.3f}, {time.time() - started:.0f}초", flush=True)

    values, conf, texts = predict(model, d.images)  # 0.5초 자료로 예비 채점
    save_predictions(d, values, conf, texts, out)
    rows_train = evaluate(d, values, conf, TRAIN_POS)
    rows_right = evaluate(d, values, conf, {"RIGHT"})
    print("   LEFT·CENTER  " + summary(rows_train), flush=True)
    print("   RIGHT(평가)  " + summary(rows_right), flush=True)
    (out / "round1.json").write_text(json.dumps({"train": rows_train, "right": rows_right}, ensure_ascii=False, indent=1), encoding="utf-8")


if __name__ == "__main__":
    main()
