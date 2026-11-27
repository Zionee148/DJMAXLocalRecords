"""숫자열 판독 모델.

점수 영역 그림(64x256) 하나를 받아 숫자열을 통째로 읽는다. 글자를 미리 잘라 주지 않아도 되므로
글꼴의 폭·기울기·정렬이 달라도 같은 모델을 쓸 수 있다 (CTC 방식).
"""
from __future__ import annotations

import torch
import torch.nn as nn

BLANK = 10
HEIGHT, WIDTH = 64, 256      # 1차 학습(v2)은 48x192. 작게 줄어든 숫자가 뭉개져 키웠다


def _block(inp: int, out: int, pool=None):
    layers = [nn.Conv2d(inp, out, 3, padding=1, bias=False), nn.BatchNorm2d(out), nn.ReLU(inplace=True)]
    if pool:
        layers.append(nn.MaxPool2d(pool))
    return layers


class ScoreReader(nn.Module):
    def __init__(self, hidden: int = 96, height: int = HEIGHT):
        super().__init__()
        self.features = nn.Sequential(
            *_block(3, 32, (2, 2)),      # 높이 1/2, 너비 1/2
            *_block(32, 64, (2, 2)),     # 높이 1/4, 너비 1/4
            *_block(64, 96, (2, 1)),     # 높이 1/8
            *_block(96, 128, (2, 1)),    # 높이 1/16
            *_block(128, 128),
        )
        self.squeeze = nn.Conv2d(128, 128, (height // 16, 1), bias=False)   # 높이 1 로
        self.rnn = nn.GRU(128, hidden, bidirectional=True, batch_first=True)
        self.head = nn.Linear(hidden * 2, 11)                    # 0~9 + 빈칸

    def forward(self, x: torch.Tensor) -> torch.Tensor:           # x: [B,3,높이,너비] 0~1
        f = torch.relu(self.squeeze(self.features(x))).squeeze(2).transpose(1, 2)
        out, _ = self.rnn(f)
        return self.head(out)                                     # [B,너비/4,11]


def to_value(text: str) -> int:
    """읽힌 숫자열을 점수로. 점수일 수 없으면 -1."""
    if not text or len(text) > 7 or (text[0] == "0" and len(text) > 1):
        return -1
    v = int(text)
    return v if v <= 1_000_000 else -1


@torch.no_grad()
def decode(logits: torch.Tensor):
    """가장 그럴듯한 숫자열의 값, 확신도(0~1), 읽힌 글자 그대로를 돌려준다. 못 읽으면 값은 -1."""
    logp = logits.log_softmax(-1).cpu()
    texts = []
    for row in logp.argmax(-1).tolist():
        digits, last = [], BLANK
        for c in row:
            if c != last and c != BLANK:
                digits.append(str(c))
            last = c
        texts.append("".join(digits))
    # 확신도 = 모델이 그 숫자열에 준 확률 (글자 경계가 한 칸 밀리는 경우까지 모두 더한 값).
    # 칸마다의 확률을 곱하면 경계가 애매하다는 이유만으로 맞게 읽은 값의 확신도가 0.2 까지 떨어진다.
    flat = torch.tensor([int(ch) for t in texts for ch in t], dtype=torch.long)
    lengths = torch.tensor([len(t) for t in texts])
    steps = torch.full((logp.shape[0],), logp.shape[1])
    loss = nn.functional.ctc_loss(logp.transpose(0, 1), flat, steps, lengths, blank=BLANK, reduction="none", zero_infinity=True)
    return [to_value(t) for t in texts], (-loss).exp().tolist(), texts
