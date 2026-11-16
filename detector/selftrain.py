"""규칙으로 검증하며 학습을 반복한다.

1) 출발 라벨: Windows OCR 조사에서 2종 이상 일치하고 점수 규칙을 통과한 값 (읽히는 글꼴에서만 나온다)
2) 모델을 학습시켜 LEFT·CENTER 의 모든 프레임을 읽힌다
3) 사례별로 점수 규칙(산식 격자·줄지 않음·증가폭)을 통과한 판독만 다음 회차의 라벨로 받아들인다
4) 반복. OCR 이 못 읽는 글꼴도 규칙을 통과하는 판독이 생기면 라벨이 늘어난다

RIGHT 녹화는 학습에도 라벨 만들기에도 쓰지 않고, 처음 보는 플레이에 대한 평가로만 쓴다.
노트 수는 이 시제품에서는 알려진 값(798, CHICKEN 545, Boom! 1087)을 쓴다.

1차 학습(v2)에서 드러난 문제를 고친 점:
  - "0, 0, 0 …" 만 읽어도 규칙을 통과해 0 으로 굳었다 → 서로 다른 값이 여러 번 올라간 사례만 라벨로 받고,
    0 은 규칙 검사에서 빼고 첫 점수 직전의 몇 장만 따로 받는다
  - 빈 화면을 0 으로 읽었다 → 밝기 변화가 거의 없는 그림은 "숫자 없음"으로 가르친다
  - 끝부분을 맞게 읽고도 확신도가 낮아 버렸다 → 규칙이 검증하므로 확신도 문턱을 낮추고,
    같은 값이 확인된 두 프레임 사이는 그 값으로 라벨한다 (점수는 줄지 않으므로)
  - 만·십만 자리 오독은 산식으로 못 잡는다 → 최종 점수는 플레이 값과 이어지는지, 어긋나는 판독이 없는지 본다

실행: .venv\\Scripts\\python.exe -m detector.selftrain <이름> [회차] [회차당 걸음 수] [스레드] [출발 판독을 빌릴 실행 이름]
"""
import csv
import ctypes
import json
import sys
import time
from collections import Counter
from pathlib import Path
from types import SimpleNamespace

import numpy as np
import torch
import torch.nn.functional as F

from .model import HEIGHT, ScoreReader, decode, to_value
from .score_rules import on_lattice, verify_sequence

ROOT = Path(__file__).resolve().parents[1]
DEVICE = "cuda" if torch.cuda.is_available() else "cpu"
DATA_NAME = "frames05b"
NOTES = {"5-04": 545, "5-05": 1087}
EMPTY = -1            # 라벨: 숫자 없음
BLANK_STD = 6.0       # 자른 그림의 밝기 표준편차가 이보다 작으면 빈 화면
LABEL_CONF = 0.3      # 규칙 검사에 넣을 판독의 최소 확신도 (검증은 규칙이 한다)
MIN_DISTINCT = 8      # 서로 다른 값이 이만큼은 규칙대로 올라가야 그 사례를 믿는다
TRAIN_POS = {"LEFT", "CENTER"}


# LEFT 녹화의 5-01~5-03 은 다른 곡이다. 1차 모델 판독 100~120개가 모두 297 노트 산식에 맞았다 (사용자 확인 전 추정)
NOTES_CASE = {"5-01-left-actual": 297, "5-02-left-actual": 297, "5-03-left-actual": 297}


def notes_of(case_id: str) -> int:
    return NOTES_CASE.get(case_id) or NOTES.get(case_id.rsplit("-", 2)[0], 798)


def run_config(run: str) -> dict:
    path = ROOT / "data" / "runs" / run / "config.json"
    return json.loads(path.read_text(encoding="utf-8")) if path.exists() else {"data": "frames05", "height": 48}


def load_model(run: str):
    cfg = run_config(run)
    model = ScoreReader(height=cfg["height"])
    model.load_state_dict(torch.load(ROOT / "data" / "runs" / run / "model.pt", map_location="cpu"))
    return model.to(DEVICE), cfg


def load(data_name: str = DATA_NAME):
    data = ROOT / "data" / data_name
    cases = json.loads((data / "cases.json").read_text(encoding="utf-8"))
    images, case_of, kinds, seconds, contrast, names, kept = [], [], [], [], [], [], []
    for c in cases:
        path = data / f"{c['id']}.npz"
        if not path.exists():
            continue
        z = np.load(path)
        n = len(z["images"])
        images.append(z["images"]); case_of.append(np.full(n, len(kept))); kinds.append(z["kinds"]); seconds.append(z["seconds"])
        contrast.append(z["contrast"] if "contrast" in z.files else np.full(n, 255.0, np.float32))
        names.extend(z["names"].tolist()); kept.append(c)
    d = SimpleNamespace(cases=kept, images=np.concatenate(images), case_of=np.concatenate(case_of), kinds=np.concatenate(kinds),
                        seconds=np.concatenate(seconds), names=names)
    d.blank = np.concatenate(contrast) < BLANK_STD
    # 사례별 프레임을 시간 순서로 (플레이 프레임과 등록 프레임이 섞인다)
    d.order = []
    for k in range(len(kept)):
        idx = np.where(d.case_of == k)[0]
        d.order.append(idx[np.argsort(d.seconds[idx], kind="stable")])
    return d


def ocr_labels(d):
    """OCR 조사 결과에서 규칙을 통과한 값만 출발 라벨로 쓴다."""
    index = {(d.cases[c]["id"], n): i for i, (c, n) in enumerate(zip(d.case_of, d.names))}
    by_case: dict[str, list] = {}
    with open(ROOT / "truth" / "survey" / "ocr-survey.csv", encoding="utf-8-sig") as f:
        for r in csv.DictReader(f):
            if r["file"] != "(no-area)":
                by_case.setdefault(r["id"], []).append(r)
    labels = {}
    for case_id, rows in by_case.items():
        notes = notes_of(case_id)
        best = None
        for family in "us":
            values = []
            for r in rows:
                votes = Counter(int(r[f"{family}{k}"]) for k in (1, 2, 3) if int(r[f"{family}{k}"]) >= 0)
                top = votes.most_common(1)
                values.append(top[0][0] if top and top[0][1] >= 2 else -1)
            keep = verify_sequence(values, notes)
            if best is None or keep.sum() > best[0]:
                best = (keep.sum(), values, keep)
        for r, v, ok in zip(rows, best[1], best[2]):
            i = index.get((case_id, r["file"]))
            if ok and i is not None:
                labels[i] = v
    return labels


def verified_reads(d, k, values, conf):
    """사례 k 의 프레임을 시간 순서로 읽은 값(0·빈 화면·확신도 낮음은 -1)과 규칙 통과 여부."""
    idx = d.order[k]
    reads = [int(values[i]) if (values[i] > 0 and conf[i] >= LABEL_CONF and not d.blank[i]) else -1 for i in idx]
    return idx, reads, verify_sequence(reads, notes_of(d.cases[k]["id"]))


def case_labels(d, k, values, conf):
    """사례 k 에서 규칙을 통과한 판독으로 만든 라벨과, 규칙대로 오른 서로 다른 값의 수."""
    idx, reads, keep = verified_reads(d, k, values, conf)
    good = [j for j in range(len(idx)) if keep[j]]
    distinct = len({reads[j] for j in good})
    labels = {}
    if distinct < MIN_DISTINCT:
        return labels, distinct
    first, last = {}, {}
    for j in good:
        first.setdefault(reads[j], j); last[reads[j]] = j
    for v in first:                                   # 같은 값이 확인된 두 프레임 사이는 그 값
        for j in range(first[v], last[v] + 1):
            if not d.blank[idx[j]]:
                labels[int(idx[j])] = v
    j = good[0] - 1; taken = 0                        # 첫 점수 바로 앞의 0
    while j >= 0 and taken < 6 and values[idx[j]] == 0 and conf[idx[j]] >= 0.5 and not d.blank[idx[j]]:
        labels[int(idx[j])] = 0; j -= 1; taken += 1
    return labels, distinct


STRIP_DISTINCT = 20   # 앞 글자를 떼어 보는 것은 시도가 여러 번이라 더 엄격하게 받는다


def make_labels(d, values, conf, positions, texts=None):
    """모델 판독에서 다음 회차 라벨을 만든다.

    잘린 그림에 "SCORE" 같은 글자가 같이 들어가면 모델이 그 글자까지 숫자로 읽어 값이 통째로 틀린다.
    그런 사례는 읽힌 글자의 앞 1~5자를 떼어 낸 값이 규칙대로 오르는지 보고, 오르면 그 값을 라벨로 쓴다.
    (학습용 라벨을 만들 때만 쓴다. 채점과 최종 점수 판단에는 쓰지 않는다)
    """
    labels, stripped = {}, []
    for k, c in enumerate(d.cases):
        if c["position"] not in positions:
            continue
        idx = d.order[k]
        for i in idx:
            if d.blank[i]:
                labels[int(i)] = EMPTY
        found, distinct = case_labels(d, k, values, conf)
        if not found and texts is not None:
            best = (STRIP_DISTINCT - 1, None, 0)
            for cut in range(1, 6):
                alt = values.copy()
                alt[idx] = [to_value(texts[i][cut:]) for i in idx]
                got, n = case_labels(d, k, alt, conf)
                if n > best[0]:
                    best = (n, got, cut)
            if best[1]:
                found = best[1]; stripped.append(f"{c['id']}(앞 {best[2]}자, 값 {best[0]}개)")
        labels.update(found)
    if stripped:
        print("   앞 글자를 떼어 라벨을 얻은 사례:", ", ".join(stripped), flush=True)
    return labels


def decide_final(d, k, values, conf):
    """등록 시점의 최종 점수. 근거가 부족하거나 어긋나는 판독이 있으면 None (내지 않음).

    돌려주는 값: (최종 점수 또는 None, 플레이 프레임 중 규칙 통과 비율, 보류 이유)
    """
    idx, reads, keep = verified_reads(d, k, values, conf)
    notes = notes_of(d.cases[k]["id"])
    play = d.kinds[idx] == 0
    coverage = float(keep[play].mean()) if play.any() else 0.0
    good = [j for j in range(len(idx)) if keep[j]]
    distinct = sorted({reads[j] for j in good})
    if len(distinct) < MIN_DISTINCT:
        return None, coverage, "규칙대로 오른 값이 적음"
    final = distinct[-1]
    at = [j for j in good if reads[j] == final]
    if sum(1 for j in at if not play[j]) < 3:
        return None, coverage, "등록 프레임에서 확인 안 됨"
    # 더 큰 값을 확신 있게 읽은 프레임이 있으면 서로 어긋난다 (윗자리를 낮게 읽은 경우)
    higher = sum(1 for i in idx if values[i] > final and conf[i] >= 0.5 and not d.blank[i] and on_lattice(int(values[i]), notes))
    if higher >= 2:
        return None, coverage, "더 큰 판독과 어긋남"
    # 마지막 증가가 그 판에서 본 가장 빠른 증가보다 터무니없이 크면 윗자리를 높게 읽은 것일 수 있다
    t = d.seconds[idx]
    first, last = {}, {}
    for j in good:
        first.setdefault(reads[j], j); last[reads[j]] = j
    rates = [(b - a) / (t[first[b]] - t[last[a]]) for a, b in zip(distinct, distinct[1:]) if 0 < t[first[b]] - t[last[a]] <= 1.0]
    prev = distinct[-2]
    gap = max(float(t[first[final]] - t[last[prev]]), 0.5)
    if rates and final - prev > 3 * max(rates) * gap:
        return None, coverage, "마지막 증가가 너무 큼"
    return final, coverage, ""


def augment(x: torch.Tensor) -> torch.Tensor:
    """위치·크기·밝기·색·흐림을 흔들어, 자르는 위치나 화질이 달라져도 읽게 한다."""
    b = x.shape[0]

    def rand(*shape):
        return torch.rand(*shape, device=x.device)

    scale = 1 + (rand(b) - 0.5) * 0.24
    theta = torch.zeros(b, 2, 3, device=x.device)
    theta[:, 0, 0] = scale; theta[:, 1, 1] = scale * (1 + (rand(b) - 0.5) * 0.1)
    theta[:, 0, 2] = (rand(b) - 0.5) * 0.16          # 가로 ±8%
    theta[:, 1, 2] = (rand(b) - 0.5) * 0.20          # 세로 ±10%
    theta[:, 0, 1] = (rand(b) - 0.5) * 0.10          # 약한 기울임
    grid = F.affine_grid(theta, list(x.shape), align_corners=False)
    x = F.grid_sample(x, grid, mode="bilinear", padding_mode="zeros", align_corners=False)
    swap = rand(b) < 0.3                             # 색이 아니라 모양으로 읽도록: 색 채널 섞기, 밝기 뒤집기
    if swap.any():
        x[swap] = x[swap][:, torch.randperm(3, device=x.device)]
    invert = rand(b) < 0.15
    if invert.any():
        x[invert] = 1 - x[invert]
    gain = 1 + (rand(b, 3, 1, 1) - 0.5) * 0.3        # 채널별 색 변화
    x = x * gain * (0.7 + rand(b, 1, 1, 1) * 0.6) + (rand(b, 1, 1, 1) - 0.5) * 0.2
    blur = rand(b) < 0.3
    if blur.any():
        x[blur] = F.avg_pool2d(x[blur], 3, 1, 1)
    gray = rand(b) < 0.15
    if gray.any():
        x[gray] = x[gray].mean(1, keepdim=True).expand(-1, 3, -1, -1)
    return (x + torch.randn_like(x) * 0.02).clamp(0, 1)


def to_tensor(images: np.ndarray) -> torch.Tensor:
    return torch.from_numpy(images).permute(0, 3, 1, 2).float() / 255


def train(model, images, labels: dict, steps: int, lr: float):
    """정해진 걸음 수만큼 학습한다. (라벨이 적을 때 에폭 수로 세면 걸음이 너무 적어 배우지 못한다)"""
    idx = np.array(sorted(labels)); targets = [labels[i] for i in idx]
    opt = torch.optim.AdamW(model.parameters(), lr=lr, weight_decay=1e-4)
    sched = torch.optim.lr_scheduler.OneCycleLR(opt, max_lr=lr, total_steps=steps)
    model.train(); done = 0; last = 0.0; recent = []
    while done < steps:
        order = np.random.permutation(len(idx))
        for s in range(0, len(order) - 95, 96):
            pick = order[s:s + 96]
            x = augment(to_tensor(images[idx[pick]]).to(DEVICE))
            digits = [[int(ch) for ch in str(targets[p])] if targets[p] != EMPTY else [] for p in pick]
            flat = torch.tensor([v for seq in digits for v in seq], dtype=torch.long)
            lengths = torch.tensor([len(seq) for seq in digits])
            logp = model(x).log_softmax(-1).transpose(0, 1).cpu()           # [T,B,C] (CTC 손실은 CPU 에서 계산)
            loss = F.ctc_loss(logp, flat, torch.full((len(pick),), logp.shape[0]), lengths, blank=10, zero_infinity=True)
            opt.zero_grad(); loss.backward(); torch.nn.utils.clip_grad_norm_(model.parameters(), 5.0); opt.step(); sched.step()
            done += 1; recent.append(loss.item())
            if done % 500 == 0 or done == steps:
                last = float(np.mean(recent)); recent = []
                print(f"      걸음 {done}/{steps}  손실 {last:.3f}", flush=True)
            if done >= steps:
                break
    return last


@torch.no_grad()
def predict(model, images):
    model.eval(); values, conf, texts = [], [], []
    for s in range(0, len(images), 512):
        v, c, t = decode(model(to_tensor(images[s:s + 512]).to(DEVICE)).cpu())
        values += v; conf += c; texts += t
    return np.array(values), np.array(conf), texts


def evaluate(d, values, conf, positions):
    """사례별: 플레이 프레임 중 규칙 통과 비율, 등록 시점에 낸 최종 점수가 맞는지."""
    rows = []
    for k, c in enumerate(d.cases):
        if c["position"] not in positions:
            continue
        final, coverage, why = decide_final(d, k, values, conf)
        rows.append({"id": c["id"], "gear": c["gear"], "name": c["name"], "position": c["position"],
                     "coverage": coverage, "final": final, "expected": c["expected"], "why": why,
                     "result": "abstain" if final is None else ("correct" if final == c["expected"] else "wrong")})
    return rows


def summary(rows):
    r = Counter(x["result"] for x in rows)
    covered = sum(x["coverage"] >= 0.5 for x in rows)
    return f"사례 {len(rows)}: 플레이 프레임 절반 이상 규칙 통과 {covered}, 최종 점수 맞음 {r['correct']} / 틀림 {r['wrong']} / 보류 {r['abstain']}"


def borrowed_predictions(d, run: str):
    """다른 실행이 남긴 판독(사례·파일 이름으로 맞춘다)."""
    z = np.load(ROOT / "data" / "runs" / run / "predictions.npz")
    known = {(a, b): (int(v), float(c)) for a, b, v, c in zip(z["ids"].tolist(), z["names"].tolist(), z["values"], z["conf"])}
    read = dict(zip(zip(z["ids"].tolist(), z["names"].tolist()), z["texts"].tolist())) if "texts" in z.files else None
    values = np.full(len(d.names), -1); conf = np.zeros(len(d.names)); texts = [""] * len(d.names)
    for i, (k, n) in enumerate(zip(d.case_of, d.names)):
        key = (d.cases[k]["id"], n)
        hit = known.get(key)
        if hit:
            values[i], conf[i] = hit
            if read:
                texts[i] = read[key]
    return values, conf, (texts if read else None)


def save_predictions(d, values, conf, texts, out: Path):
    np.savez_compressed(out / "predictions.npz", ids=np.array([d.cases[k]["id"] for k in d.case_of]), names=np.array(d.names),
                        values=values, conf=conf, texts=np.array(texts))


def seed_labels(d) -> dict:
    """모델과 무관한 출발 라벨: OCR 이 읽고 규칙을 통과한 값 + 직접 읽은 씨앗 라벨 (LEFT·CENTER 만)."""
    seed = {i: v for i, v in ocr_labels(d).items() if d.cases[d.case_of[i]]["position"] in TRAIN_POS and not d.blank[i]}
    print(f"출발 라벨(OCR+규칙, LEFT·CENTER): {len(seed)}장, 사례 {len({d.case_of[i] for i in seed})}개", flush=True)
    from .check_seeds import verified_seeds          # 직접 읽은 씨앗 라벨 (규칙을 통과한 것만)
    read = verified_seeds()
    hand = {i: read[key] for i, key in enumerate(zip((d.cases[k]["id"] for k in d.case_of), d.names))
            if key in read and d.cases[d.case_of[i]]["position"] in TRAIN_POS}
    seed.update(hand)
    print(f"직접 읽은 씨앗 라벨: {len(hand)}장, 사례 {len({d.case_of[i] for i in hand})}개", flush=True)
    return seed


def main():
    name = sys.argv[1] if len(sys.argv) > 1 else "run"
    rounds = int(sys.argv[2]) if len(sys.argv) > 2 else 4
    steps = int(sys.argv[3]) if len(sys.argv) > 3 else 2500
    threads = int(sys.argv[4]) if len(sys.argv) > 4 else 6
    borrow = sys.argv[5] if len(sys.argv) > 5 else None
    try:
        ctypes.windll.kernel32.SetPriorityClass(ctypes.windll.kernel32.GetCurrentProcess(), 0x4000)
    except Exception:
        pass
    torch.set_num_threads(threads); torch.manual_seed(0); np.random.seed(0)
    out = ROOT / "data" / "runs" / name; out.mkdir(parents=True, exist_ok=True)
    (out / "config.json").write_text(json.dumps({"data": DATA_NAME, "height": HEIGHT}), encoding="utf-8")
    d = load()
    print(f"사례 {len(d.cases)}개, 프레임 {len(d.images)}장, 빈 화면 {int(d.blank.sum())}장", flush=True)
    seed = seed_labels(d)

    def with_seed(found: dict) -> dict:
        merged = dict(found); merged.update(seed)
        return merged

    if borrow:
        b_values, b_conf, b_texts = borrowed_predictions(d, borrow)
        labels = with_seed(make_labels(d, b_values, b_conf, TRAIN_POS, b_texts))
        print(f"{borrow} 의 판독을 새 규칙으로 걸러 더한 라벨: {len(labels)}장", flush=True)
    else:
        labels = with_seed({int(i): EMPTY for i in np.where(d.blank)[0] if d.cases[d.case_of[i]]["position"] in TRAIN_POS})
    model = ScoreReader().to(DEVICE)            # 회차 사이에 이어서 학습한다
    print(f"계산 장치: {DEVICE}", flush=True)
    for rnd in range(rounds):
        started = time.time()
        loss = train(model, d.images, labels, steps, 2e-3 if rnd == 0 else 1e-3)
        values, conf, texts = predict(model, d.images)
        new = with_seed(make_labels(d, values, conf, TRAIN_POS, texts))
        rows_train = evaluate(d, values, conf, TRAIN_POS)
        rows_right = evaluate(d, values, conf, {"RIGHT"})
        print(f"[{rnd + 1}회차] 라벨 {len(labels)} → {len(new)}, 손실 {loss:.3f}, {time.time() - started:.0f}초", flush=True)
        print("   LEFT·CENTER  " + summary(rows_train), flush=True)
        print("   RIGHT(평가)  " + summary(rows_right), flush=True)
        (out / f"round{rnd + 1}.json").write_text(json.dumps({"train": rows_train, "right": rows_right}, ensure_ascii=False, indent=1), encoding="utf-8")
        torch.save(model.state_dict(), out / "model.pt")
        save_predictions(d, values, conf, texts, out)
        labels = new
    print("완료", flush=True)


if __name__ == "__main__":
    main()
