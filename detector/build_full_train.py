"""전체 프레임에서 판독기 보강용 학습 자료를 뽑는다 (LEFT·CENTER 만. RIGHT 는 평가용이라 건드리지 않는다).

0.5초 간격 자료로는 화면 효과가 덮인 프레임, 효과 중에 색이 바뀐 점수판, 확신도가 낮던 글꼴을 거의 배우지 못했다.
라벨은 이전 실행의 전체 프레임 판독(data\\full\\<실행>)과 그 모델로 만든다.

  통과한 프레임   : 읽은 값 그대로 (8장에 1장)
  사이에 낀 프레임 : 앞뒤가 같은 값으로 통과 → 그 값 (점수는 줄지 않으므로 그 값일 수밖에 없다)
  못 읽은 프레임   : 앞뒤 통과 값 a, b 사이에서 산식으로 나올 수 있는 값들을 후보로 두고, 모델이 그 프레임에서
                   각 후보에 준 확률을 견준다. 한 후보가 나머지를 압도하고(후보 가운데 99% 이상) 모델이 그 숫자열에
                   준 확률도 아주 작지 않을 때(0.1% 이상)만 그 값을 라벨로 쓴다. 그 뒤 시간 순서로 줄지 않는지 다시 검사한다.
                   (모델이 가장 그럴듯하다고 본 값은 아니지만, 규칙에 맞는 값 가운데서는 뚜렷이 그 값으로 본 프레임이다.
                    완전히 가려진 프레임은 어느 후보에도 확률이 거의 0 이라 라벨이 붙지 않는다.)
  숫자가 없는 프레임 : 빈 화면, 그리고 "완전히 가림" 가운데 판정이 아주 확실한 것(차이 3.0 미만) → "숫자 없음"
  시작 전의 0     : 0 (10장에 1장)

산식은 여기서도 값을 지어내는 데 쓰지 않는다. 후보를 좁히는 데만 쓰고, 고르는 근거는 모델이 그 그림에서 본 것이다.
이 라벨은 학습에만 쓴다. 채점에서는 여전히 그 프레임 하나만 보고 읽은 값을 쓴다.

실행: .venv\\Scripts\\python.exe -m detector.build_full_train <이전 실행 이름>
출력: data\\full_train\\<사례>.npz (images, labels; 숫자 없음 = -1)
"""
import json
import sys
import time

import numpy as np
import torch
import torch.nn.functional as F

from .build_dataset import letterbox
from .full_frames import sources, stream
from .full_summary import play_window
from .model import BLANK
from .score_frames import READ_CONF
from .score_rules import MAX_SCORE, lattice_m, verify_sequence
from .selftrain import DATA_NAME, DEVICE, EMPTY, MIN_DISTINCT, ROOT, TRAIN_POS, load_model, notes_of, to_tensor

OUT = ROOT / "data" / "full_train"
SURE = 3.0            # "완전히 가림" 판정에서 이보다 작은 차이만 "숫자 없음" 라벨로 쓴다
WIDE = 30000          # 앞뒤 값이 이보다 벌어진 구간은 후보가 너무 많아 건너뛴다
DOMINANT = 0.99       # 후보 가운데 이만큼은 차지해야 한다
SEEN = 1e-3           # 모델이 그 숫자열에 준 확률이 이만큼은 돼야 한다
PER_CASE = 2500


def plan(case_id: str, z, classes):
    """프레임 위치 → 라벨, 그리고 후보를 견줘 볼 프레임 위치 → (앞 통과 값, 뒤 통과 값)."""
    values, conf, blank = z["values"], z["conf"], z["blank"]
    raw = np.where((conf >= READ_CONF) & ~blank & (values > 0), values, -1)
    keep = verify_sequence([int(v) for v in raw], notes_of(case_id))
    good = np.where(keep)[0]
    picks: dict[int, int] = {int(i): EMPTY for i in np.where(blank)[0][::10][:100]}
    if len({int(raw[i]) for i in good}) < MIN_DISTINCT:
        return picks, {}, raw, keep
    start, stop = play_window(z, raw, keep)
    hidden = np.zeros(len(raw), bool); sure = np.zeros(len(raw), bool)
    if classes is not None:
        hidden = classes["kind"] == list(classes["names"]).index("완전히 가림") + 1
        sure = hidden & (classes["seen"] < SURE)
    for i in np.where((values[:start] == 0) & (conf[:start] >= READ_CONF) & ~blank[:start])[0][::10][:30]:
        picks[int(i)] = 0
    for i in good[::8]:
        picks[int(i)] = int(raw[i])
    for i in np.where(sure)[0][::max(1, int(sure.sum()) // 150)][:150]:
        picks[int(i)] = EMPTY
    before = np.maximum.accumulate(np.where(keep, np.arange(len(raw)), -1))
    after = np.minimum.accumulate(np.where(keep, np.arange(len(raw)), len(raw))[::-1])[::-1]
    hard = {}
    for i in range(start, stop):
        if keep[i] or hidden[i] or blank[i]:
            continue
        a, b = int(raw[before[i]]), int(raw[after[i]])
        if a == b:
            picks[i] = a                                   # 같은 값 사이에 낀 프레임
        elif b - a <= WIDE:
            hard[i] = (a, b)
    return picks, hard, raw, keep


def candidates(a: int, b: int, notes: int) -> list[int]:
    """a 와 b 사이(양 끝 포함)에서 산식으로 나올 수 있는 값. 10% 단위 판정으로 이어지는 것만 (끝자리가 a 나 b 와 같은 누적 합)."""
    den = notes * 100
    ma, mb = lattice_m(a, notes), lattice_m(b, notes)
    return sorted({(m * MAX_SCORE) // den for m in range(ma, mb + 1) if m % 10 in (ma % 10, mb % 10)})


@torch.no_grad()
def decide(model, images: np.ndarray, spans: list, notes: int) -> list:
    """프레임마다 후보를 견줘 라벨을 고른다. 못 고르면 None."""
    out = []
    for s in range(0, len(images), 256):
        logp = model(to_tensor(images[s:s + 256]).to(DEVICE)).log_softmax(-1)           # [B,T,C]
        for k in range(logp.shape[0]):
            a, b = spans[s + k]
            cand = candidates(a, b, notes)
            texts = [str(v) for v in cand]
            flat = torch.tensor([int(ch) for t in texts for ch in t], dtype=torch.long)
            lengths = torch.tensor([len(t) for t in texts])
            one = logp[k].unsqueeze(1).expand(-1, len(cand), -1)                            # [T,후보,C]
            nll = F.ctc_loss(one, flat, torch.full((len(cand),), one.shape[0]), lengths, blank=BLANK, reduction="none", zero_infinity=True)
            p = (-nll).exp().double()
            best = int(p.argmax())
            out.append(cand[best] if p[best] >= SEEN and p[best] / p.sum() >= DOMINANT else None)
    return out


def main():
    run = sys.argv[1]
    folder = ROOT / "data" / "full" / run
    OUT.mkdir(parents=True, exist_ok=True)
    model, _ = load_model(run)
    model.eval()
    cases = [c for c in json.loads((ROOT / "data" / DATA_NAME / "cases.json").read_text(encoding="utf-8")) if c["position"] in TRAIN_POS]
    table = sources()
    started = time.time(); total = chosen_total = 0
    for n, c in enumerate(cases, 1):
        case_id = c["id"]
        source = folder / f"{case_id}.npz"
        if not source.exists() or (OUT / f"{case_id}.npz").exists():
            continue
        z = np.load(source)
        path = folder / "classes" / f"{case_id}.npz"
        picks, hard, raw, keep = plan(case_id, z, np.load(path) if path.exists() else None)
        kept = {}
        log = OUT / f"{case_id}.log"
        for i, part in enumerate(stream(case_id, table[case_id], log)):
            if i in picks or i in hard:
                kept[i] = letterbox(part)[0]
        log.unlink()
        chosen = 0
        if hard:
            order = sorted(hard)
            found = decide(model, np.stack([kept[i] for i in order]), [hard[i] for i in order], notes_of(case_id))
            # 고른 값을 통과 값들 사이에 끼워 넣고, 시간 순서로 줄지 않는지·증가폭이 맞는지 다시 검사한다
            merged = np.where(keep, raw, -1)
            for i, v in zip(order, found):
                if v is not None:
                    merged[i] = v
            again = verify_sequence([int(v) for v in merged], notes_of(case_id))
            ok = [i for i, v in zip(order, found) if v is not None and again[i]]
            for i in ok[::max(1, -(-len(ok) // PER_CASE))]:
                picks[i] = int(merged[i]); chosen += 1
        index = sorted(i for i in picks if i in kept)
        np.savez_compressed(OUT / f"{case_id}.npz", images=np.stack([kept[i] for i in index]),
                            labels=np.array([picks[i] for i in index], np.int32), index=np.array(index, np.int32))
        total += len(index); chosen_total += chosen
        print(f"[{n}/{len(cases)}] {case_id}: {len(index)}장 (후보에서 고른 것 {chosen} / 못 읽은 프레임 {len(hard)}), 누적 {total}장, {time.time() - started:.0f}초", flush=True)
    print(f"완료: {total}장, 후보에서 고른 라벨 {chosen_total}장", flush=True)


if __name__ == "__main__":
    main()
