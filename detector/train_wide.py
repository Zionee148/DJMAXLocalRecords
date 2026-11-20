"""판독기 보강: 자르는 위치가 달라져도 읽도록, 넓게 잘린 조각을 섞어 이전 모델에서 이어 학습한다.

  - 라벨은 새로 만들지 않는다. 0.5초 자료에서 이전 모델이 규칙을 통과해 읽은 값과 초기 학습 라벨(train_full 과 같은 방식)을 그대로 쓰고,
    같은 프레임을 여러 넓이로 다시 잘라 같은 라벨을 붙인다. 넓게 자른 조각에 옆의 글자나 장식이 들어와도 라벨은 점수 숫자뿐이다.
  - 전체 프레임에서 뽑은 자료(build_full_train)도 함께 쓴다. v7 이 나빠졌던 일을 되풀이하지 않도록 v6 를 만들 때 쓴 것(v5 판독에서 뽑은 것)을 쓴다.
  - RIGHT 녹화는 학습에 쓰지 않는다.

  - 다섯째 인자를 주면 "효과를 겹친 그림"도 만든다 (composites). 화면 효과가 점수판을 반쯤 덮은 프레임을 읽게 하려는 것이다.

  - 여덟째 인자(위치 탐색으로 읽은 실행, 예: vid-d2-v15)를 주면 "모듈이 실제로 자르는 자리"의 조각도 만든다 (located).
    위치 탐색이 고정한 영역은 기준 영역(검사 설정에 적힌 점수 영역)과 다르다(더 좁게 잡히는 기어가 많다. 그러면 숫자가 더 크게 들어간다). v15 를 찾은 자리로 읽었더니
    기준 영역으로는 읽는 정상 프레임 108장을 못 읽었다: So Happy 는 넓적한 숫자를 두 번 읽고(35087 → 335087), AURORA·MAID·GROOVE COASTER 는
    붙어 있는 1 을 하나 빠뜨렸다(411152 → 41152). 가려진 프레임이 아니었다.

실행: .venv\\Scripts\\python.exe -m detector.train_wide <새 이름> <이전 실행> [걸음 수=8000] [전체 프레임 자료 폴더=full_train-v5] [사례당 겹친 그림 수=0] [초기 학습 라벨 반복 수=0] [학습률=0.001] [위치 탐색 실행=없음]
출력: data\\runs\\<새 이름>\\model.pt, round1.json, predictions.npz
"""
import ctypes
import json
import sys
import time

import numpy as np
import torch

from .build_dataset import MARGIN_X, MARGIN_Y, letterbox
from .model import HEIGHT, WIDTH
from .selftrain import (DATA_NAME, EMPTY, ROOT, TRAIN_POS, borrowed_predictions, evaluate, load, load_model, make_labels, predict,
                        save_predictions, seed_labels, summary, train)

WIDE = ROOT / "data" / "wide"
OVERLAYS = ROOT / "data" / "overlays"
VARIANTS = 2
EXTRA_MAX = 52          # 양옆으로 더 넓게 자르는 폭의 상한(화소)
POLISH_STEPS = 0        # 다듬기 단계는 끈다. 800걸음으로 해 본 v9 는 라벨과 다르게 읽는 프레임이 27장 → 82장으로 오히려 늘었다


def variants(d, half: dict, rng) -> tuple[list, list]:
    """라벨이 붙은 0.5초 프레임마다 넓이를 달리해 다시 자른 조각들과 그 라벨."""
    images, labels = [], []
    first = {}
    for k in range(len(d.cases)):
        first[k] = int(np.where(d.case_of == k)[0][0])
    by_case: dict[int, list[int]] = {}
    for i in half:
        by_case.setdefault(int(d.case_of[i]), []).append(i)
    for k, items in by_case.items():
        path = WIDE / f"{d.cases[k]['id']}.npz"
        if not path.exists():
            continue
        z = np.load(path)
        wide, (x0, y0, x1, y1) = z["images"], (int(v) for v in z["inner"])
        h, w = wide.shape[1:3]
        for i in items:
            picture = wide[i - first[k]]
            for _ in range(VARIANTS):
                left = 0 if rng.random() < 0.3 else int(rng.integers(0, EXTRA_MAX + 1))
                right = 0 if rng.random() < 0.3 else int(rng.integers(0, EXTRA_MAX + 1))
                up, down = int(rng.integers(-3, 11)), int(rng.integers(-3, 11))
                xa, xb = max(0, x0 - MARGIN_X - left), min(w, x1 + MARGIN_X + right)
                ya, yb = max(0, y0 - MARGIN_Y - up), min(h, y1 + MARGIN_Y + down)
                images.append(letterbox(picture[ya:yb, xa:xb])[0]); labels.append(half[i])
    return images, labels


LOCATED_VARIANTS = 2
LOCATED_SHAKE = 3       # 찾은 자리의 네 변을 이만큼(화소)까지 흔든다. 영역은 탐지 모델의 검출 영역에 따라 조금씩 달라진다


def located(d, half: dict, rng, run: str, shake: int = LOCATED_SHAKE, copies: int = LOCATED_VARIANTS) -> tuple[list, list, list]:
    """라벨이 붙은 0.5초 프레임을, 위치 탐색이 그 시각에 쓰던 영역으로 다시 자른 조각들과 그 라벨 (LEFT·CENTER 만. RIGHT 의 영역 내역은 읽지 않는다).

    고정 전의 프레임은 만들지 않는다(그때는 영역이 없다). 기울여 읽는 사례(CYTUS)와, 넓은 조각 밖으로 나가는 영역도 건너뛴다.
    영역은 넓어지기만 하므로, 그 시각의 영역은 그때의 숫자를 다 담고 있다 — 라벨은 그대로 쓸 수 있다.
    """
    from .full_frames import area_of
    from .locate_video import load_steps
    images, labels, where = [], [], []
    first = {k: int(np.where(d.case_of == k)[0][0]) for k in range(len(d.cases))}
    by_case: dict[int, list[int]] = {}
    for i in half:
        by_case.setdefault(int(d.case_of[i]), []).append(i)
    for k, items in by_case.items():
        case_id = d.cases[k]["id"]
        path = WIDE / f"{case_id}.npz"
        found = None if "-right-" in case_id else load_steps(ROOT / "data" / "full" / run, case_id)
        if not path.exists() or found is None or found[1] is not None:
            continue
        lock_at, _, steps = found
        z = np.load(path)
        wide, inner = z["images"], [int(v) for v in z["inner"]]
        codex = area_of(case_id)
        dx, dy = codex[0] - inner[0], codex[1] - inner[1]          # 넓은 조각의 왼쪽 위 구석이 화면에서 놓인 자리
        h, w = wide.shape[1:3]
        for i in items:
            at = float(d.seconds[i])
            if at < lock_at or half[i] == EMPTY:
                continue
            area = next(a for t, a in reversed(steps) if t <= at)
            for _ in range(copies):
                move = rng.integers(-shake, shake + 1, 4) if shake else np.zeros(4, int)
                xa, ya = int(round(area[0])) - MARGIN_X - dx + int(move[0]), int(round(area[1])) - MARGIN_Y - dy + int(move[1])
                xb, yb = int(round(area[2])) + MARGIN_X - dx + int(move[2]), int(round(area[3])) + MARGIN_Y - dy + int(move[3])
                if xa < 0 or ya < 0 or xb > w or yb > h or xb - xa < 16 or yb - ya < 16:
                    continue
                images.append(letterbox(wide[i - first[k]][ya:yb, xa:xb])[0]); labels.append(half[i]); where.append(i)
    return images, labels, where


def composites(files: list, rng, per_case: int) -> tuple[list, list]:
    """깨끗한 프레임 위에 "효과만 찍힌 그림"(build_overlays)을 겹쳐, 효과가 나타나는 도중의 그림을 흉내 낸다. 라벨은 깨끗한 프레임의 것.

    효과가 있는 사례는 주로 제 사례의 효과 그림을, 없는 사례는 다른 사례의 것을 조금만 겹친다(처음 보는 효과에도 덜 흔들리도록).
    겹치는 방법: 전체를 반투명하게 / 한쪽에서 번져 오게 / 밝게 더하기(번쩍임).
    """
    bank = {p.stem: np.load(p) for p in sorted(OVERLAYS.glob("*.npy"))}
    if not bank or per_case <= 0:
        return [], []
    keys = list(bank)
    images, labels = [], []
    ramp = np.linspace(0, 1, WIDTH, dtype=np.float32)[None, :, None]
    for f in files:
        z = np.load(f)
        pictures, values = z["images"], z["labels"]             # 한 번만 푼다 (프레임마다 z["images"] 를 부르면 그때마다 통째로 다시 푼다)
        clean = np.where(values > 0)[0]
        if len(clean) == 0:
            continue
        own = f.stem in bank
        for i in rng.choice(clean, per_case if own else per_case // 4, replace=True):
            source = bank[f.stem] if (own and rng.random() < 0.75) else bank[keys[int(rng.integers(len(keys)))]]
            cover = source[int(rng.integers(len(source)))].astype(np.float32)
            base = pictures[i].astype(np.float32)
            strength = float(rng.uniform(0.25, 0.8))
            kind = rng.random()
            if kind < 0.6:
                mixed = base * (1 - strength) + cover * strength
            elif kind < 0.85:
                edge = float(rng.uniform(0.2, 0.9))
                mask = np.clip((ramp - edge) * 6 + 0.5, 0, 1) if rng.random() < 0.5 else np.clip((edge - ramp) * 6 + 0.5, 0, 1)
                mixed = base * (1 - mask * min(0.92, strength + 0.15)) + cover * (mask * min(0.92, strength + 0.15))
            else:
                mixed = base + cover * strength
            images.append(np.clip(mixed, 0, 255).astype(np.uint8)); labels.append(int(values[i]))
    return images, labels


def main():
    name, previous = sys.argv[1], sys.argv[2]
    steps = int(sys.argv[3]) if len(sys.argv) > 3 else 8000
    full_folder = sys.argv[4] if len(sys.argv) > 4 else "full_train-v5"
    per_case = int(sys.argv[5]) if len(sys.argv) > 5 else 0          # 사례마다 만들 "효과를 겹친 그림"의 수 (0 = 만들지 않음)
    seed_copies = int(sys.argv[6]) if len(sys.argv) > 6 else 0       # 전체 프레임에서 직접 판독한 초기 학습 라벨(build_seed_full)을 몇 번씩 넣을지 (0 = 안 넣음)
    rate = float(sys.argv[7]) if len(sys.argv) > 7 else 1e-3          # 학습률의 꼭짓점. 이미 좋은 모델을 조금만 고칠 때는 낮춘다
    located_run = sys.argv[8] if len(sys.argv) > 8 else ""            # 위치 탐색으로 읽은 실행. 주면 그 영역으로 자른 조각도 넣는다
    try:
        ctypes.windll.kernel32.SetPriorityClass(ctypes.windll.kernel32.GetCurrentProcess(), 0x4000)
    except Exception:
        pass
    torch.set_num_threads(6); torch.manual_seed(0); np.random.seed(0)
    rng = np.random.default_rng(0)
    out = ROOT / "data" / "runs" / name; out.mkdir(parents=True, exist_ok=True)
    (out / "config.json").write_text(json.dumps({"data": DATA_NAME, "height": HEIGHT}), encoding="utf-8")

    d = load()
    seed = seed_labels(d)
    values, conf, texts = borrowed_predictions(d, previous)
    half = make_labels(d, values, conf, TRAIN_POS, texts); half.update(seed)
    half_index = sorted(half)
    wide_images, wide_labels = variants(d, half, rng)
    if located_run:
        found_images, found_labels, _ = located(d, half, rng, located_run)
        wide_images += found_images; wide_labels += found_labels
        print(f"찾은 자리로 자른 조각 {len(found_images)}장 ({located_run})", flush=True)

    files = sorted((ROOT / "data" / full_folder).glob("*.npz"))
    counts = [len(np.load(f)["labels"]) for f in files]
    mixed_images, mixed_labels = composites(files, rng, per_case)
    wide_images += mixed_images; wide_labels += mixed_labels
    print(f"효과를 겹친 그림 {len(mixed_images)}장", flush=True)
    if seed_copies and (ROOT / "data" / "seed_full.npz").exists():
        # 직접 읽은 라벨은 수가 적어(백여 장) 수십만 장 사이에 묻힌다. 여러 번 넣어 준다. 규칙 검사를 통과한 것만 들어 있다.
        z = np.load(ROOT / "data" / "seed_full.npz")
        for _ in range(seed_copies):
            wide_images += list(z["images"]); wide_labels += [int(v) for v in z["labels"]]
        print(f"직접 판독한 초기 학습 라벨 {len(z['labels'])}장 x {seed_copies}번", flush=True)
    if seed_copies and located_run and (ROOT / "data" / "seed_located.npz").exists():
        # 같은 종류의 초기 학습 라벨이지만 찾은 자리로 자른 것 (build_seed_located). 기준 영역으로는 읽는데 찾은 자리로는 못 읽던 정상 프레임이다.
        z = np.load(ROOT / "data" / "seed_located.npz")
        for _ in range(seed_copies):
            wide_images += list(z["images"]); wide_labels += [int(v) for v in z["labels"]]
        print(f"찾은 자리로 자른 초기 학습 라벨 {len(z['labels'])}장 x {seed_copies}번", flush=True)
    total = len(half_index) + len(wide_images) + sum(counts)
    images = np.empty((total, HEIGHT, WIDTH, 3), np.uint8)
    images[:len(half_index)] = d.images[half_index]
    labels = {k: half[i] for k, i in enumerate(half_index)}
    at = len(half_index)
    images[at:at + len(wide_images)] = np.stack(wide_images)
    labels.update({at + j: int(v) for j, v in enumerate(wide_labels)})
    at += len(wide_images)
    for f in files:
        z = np.load(f)
        n = len(z["labels"])
        images[at:at + n] = z["images"]
        labels.update({at + j: int(v) for j, v in enumerate(z["labels"])})
        at += n
    print(f"0.5초 자료 라벨 {len(half_index)}장 + 넓이를 달리한 조각 {len(wide_images)}장 + 전체 프레임 자료 {sum(counts)}장({full_folder}), "
          f"그중 숫자 없음 {sum(1 for v in labels.values() if v == EMPTY)}장", flush=True)

    model, _ = load_model(previous)
    started = time.time()
    loss = train(model, images, labels, steps, rate)
    print(f"학습 끝: 손실 {loss:.3f}, {time.time() - started:.0f}초", flush=True)

    # 다듬기: 학습 라벨이 있는데도 다르게 읽는 0.5초 프레임을 여러 번 더 보여 준다.
    # v7·v8 에서 GROOVE COASTER 의 한 값(58771)을 587711 로 읽기 시작했는데, 그 프레임은 라벨이 맞게 붙어 있었다.
    # 28만 장 가운데 한 장이라 묻힌 것이다. 여기서 쓰는 것은 학습 라벨뿐이고 채점용 검토 라벨(reviewed-source-labels)은 쓰지 않는다.
    def wrong_ones():
        v, _, _ = predict(model, images[:len(half_index)])
        return [k for k in range(len(half_index)) if labels[k] >= 0 and int(v[k]) != labels[k]]

    wrong = wrong_ones()
    print(f"다듬기 전: 라벨과 다르게 읽는 0.5초 프레임 {len(wrong)}장", flush=True)
    if wrong and POLISH_STEPS:
        pick = np.concatenate([rng.choice(len(images), 60000, replace=False), np.repeat(np.array(wrong), 40)])
        train(model, images[pick], {j: labels[int(k)] for j, k in enumerate(pick)}, POLISH_STEPS, 2e-4)
        print(f"다듬기 후: {len(wrong_ones())}장", flush=True)
    torch.save(model.state_dict(), out / "model.pt")

    values, conf, texts = predict(model, d.images)
    save_predictions(d, values, conf, texts, out)
    rows_train = evaluate(d, values, conf, TRAIN_POS)
    print("   LEFT·CENTER  " + summary(rows_train), flush=True)
    # RIGHT 는 LEFT·CENTER 가 기준을 통과한 뒤에 본다. 여기서는 채점하지도 출력하지도 않는다.
    (out / "round1.json").write_text(json.dumps({"train": rows_train}, ensure_ascii=False, indent=1), encoding="utf-8")


if __name__ == "__main__":
    main()
