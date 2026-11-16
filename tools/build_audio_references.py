"""동명곡을 가리는 소리 기준(app\\runtime\\data\\audio\\<SourceId>.bin)을 직접 녹화한 영상의 게임 소리에서 다시 만들고 확인한다.

쓰는 법 (프로젝트 폴더에서. 먼저 앱을 Debug 로 빌드해 둔다):
    .venv\\Scripts\\python.exe tools\\build_audio_references.py <새 결과 이름> --build <Debug 빌드 폴더>
        [--other-plays <다른 플레이의 소리가 있는 폴더>] [--apply]
    .venv\\Scripts\\python.exe tools\\build_audio_references.py <있는 결과 이름> --build <빌드 폴더> --verify-build

하는 일
  1. tools\\audio_references.json 에 적힌 녹화 14개를 확인한다(있는지, 크기와 SHA-256, 오디오 트랙).
     녹화가 있는 폴더는 환경 변수 DJMAX_SAME_NAME_VIDEOS 나 저장소 맨 위 local-paths.json 의 same_name_videos 로 알려 준다
     (알려 주지 않으면 멈춘다). 녹화는 제작자가 직접 녹화한 것이고 저장소에 들어 있지 않다. 원본은 읽기만 한다.
  2. 녹화마다 정해 둔 트랙의, 곡이 나오는 구간(앞 5초·뒤 1초를 붙여)을 뽑아 data\\audio-rebuild\\<이름>\\wav\\<SourceId>.wav 로 쓴다.
     앱이 플레이 중에 게임 소리를 받는 방식과 같게 만든다: 44,100Hz 스테레오 → 두 채널의 평균 → 걸러 내지 않고 네 개에 하나씩(11,025Hz).
     (app\\LoopbackAudio.cs 가 그렇게 한다. 기준과 플레이 중의 소리가 같은 길을 거치게 하려는 것이다. 속도나 음색을 바꾸지 않는다)
  3. 앱의 개발용 빌드를 불러(--audio-reference-build) 앱이 쓰는 계산(AudioIdentity.Features)으로 bin 14개를 만든다 → …\\bin\\
  4. 앱의 개발용 빌드를 불러(--audio-reference-check) 앱이 쓰는 판단(AudioIdentity.Decide: 비교값 0.60 이상, 다음 후보와 0.12 이상 차이)으로 확인한다.
       same-play    기준을 만든 바로 그 녹화에서 자른 8초들(곡의 처음부터 끝까지 10초마다). **같은 녹화로 본 것이다. 다른 플레이에서도 된다는 뜻이 아니다**
       start        플레이가 시작될 때: 소리를 받기 시작한 때를 곡의 5초 전·3초 전·곡의 시작으로 놓고, 1초마다 직전 8초로 물어 언제 확정되는지
       other-song   다른 쌍의 곡을 들려주었을 때 확정하지 않는지(곡 밖의 소리)
       jingle       곡이 끝난 뒤 결과 화면의 소리를 들려주었을 때 확정하지 않는지
       other-play   (--other-plays 를 줬을 때) 다른 사람이 실제로 플레이한 소리의 8초들. 그 폴더의 video-manifest.json 과 youtube\\<영상>.wav 를 읽는다
     통과의 조건(judge): 틀리게 확정한 것이 없고, **검사하지 못한 물음(구간이 소리 밖 등)이 없고**, 물은 수만큼 답이 있고,
     same-play 는 곡마다 있고 모두 맞고, start 는 곡·조건마다 끝까지 한 번은 확정하고, other-play 가 있는 곡은 하나는 맞힌다.
     하나라도 어긋나면 종료 코드 1 이다. 곡이 시작하기 전이나 다른 플레이의 끝부분에서 확정하지 못한 것은 통과를 막지 않는다.
     통과시키려고 판단의 기준을 바꾸지 않는다.
  5. --apply 를 주면(그리고 위의 조건을 통과하면) bin 14개를 app\\runtime\\data\\audio\\ 에 복사한다(파일 이름은 그대로다).
  --verify-build: 빌드 폴더의 data\\audio 에 복사된 bin 이 이 결과의 bin 과 같은지(SHA-256) 보고, 그 파일로 same-play 를 다시 돌린다.
     같은 조건으로 판정한다(물음이 만들 때와 같은 수이고, 검사하지 못한 것이 없고, 곡마다 모두 맞아야 한다).

결과: data\\audio-rebuild\\<이름>\\summary.json (원본·뽑은 소리·bin 의 대응과 해시, 쓴 명령과 도구 버전, 확인 결과). wav 와 bin 도 그 폴더에 있다.
원본 녹화와 뽑은 소리는 git 과 배포물에 넣지 않는다(data\\ 는 git 밖이다).
"""
from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import subprocess
import sys
import wave
from datetime import datetime
from pathlib import Path

import numpy as np

from make_bundle import local_path

ROOT = Path(__file__).resolve().parents[1]
SPEC = Path(__file__).resolve().with_name("audio_references.json")
VIDEOS = local_path("same_name_videos", "DJMAX_SAME_NAME_VIDEOS")      # 알려 주지 않았으면 None
OUT = ROOT / "data" / "audio-rebuild"
TARGET = ROOT / "app" / "runtime" / "data" / "audio"
EXE = "DJMAXLocalRecords.exe"
RATE = 11025


class Stop(Exception):
    pass


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1 << 22), b""):
            digest.update(block)
    return digest.hexdigest()


def tool(name: str) -> str:
    found = shutil.which(name)
    if not found:
        raise Stop(f"{name} 을 찾지 못했다(PATH).")
    return found


def like_app(source: Path, track: int | None, start: float | None, end: float | None) -> tuple[np.ndarray, list[str]]:
    """소리를 앱이 받는 방식대로 푼다: 44,100Hz 스테레오 16비트 → 두 채널의 평균 → 네 개에 하나씩. (11,025Hz 의 값들, 쓴 명령)"""
    command = [tool("ffmpeg"), "-hide_banner", "-loglevel", "error"]
    if start is not None:
        command += ["-ss", f"{start:.3f}"]
    if end is not None:
        command += ["-to", f"{end:.3f}"]
    command += ["-i", str(source)] + ([] if track is None else ["-map", f"0:a:{track}"]) + ["-ac", "2", "-ar", "44100", "-f", "s16le", "-"]
    done = subprocess.run(command, capture_output=True)
    if done.returncode != 0:
        raise Stop(f"소리를 풀지 못했다: {source}\n{done.stderr.decode('utf-8', 'replace')}")
    stereo = np.frombuffer(done.stdout, "<i2").reshape(-1, 2).astype(np.float64)
    return ((stereo[:, 0] + stereo[:, 1]) / 2.0)[::4], command


def write_wav(samples: np.ndarray, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), "wb") as out:
        out.setnchannels(1)
        out.setsampwidth(2)
        out.setframerate(RATE)
        out.writeframes(np.clip(np.rint(samples), -32768, 32767).astype("<i2").tobytes())


def run_app(build: Path, flag: str, job: Path) -> None:
    error = build / "test-error.txt"
    if error.exists():
        error.unlink()
    done = subprocess.run([str(build / EXE), flag, str(job)], cwd=str(build), timeout=900)
    if done.returncode != 0 or error.exists():
        raise Stop(f"앱의 개발용 검사가 실패했다({flag}, 종료 코드 {done.returncode}).\n" + (error.read_text(encoding="utf-8")[:1500] if error.exists() else ""))


def probe(path: Path) -> list[dict]:
    done = subprocess.run([tool("ffprobe"), "-v", "error", "-print_format", "json", "-show_streams", str(path)], capture_output=True, text=True, encoding="utf-8")
    return [s for s in json.loads(done.stdout)["streams"] if s["codec_type"] == "audio"]


def version(name: str) -> str:
    return subprocess.run([tool(name), "-version"], capture_output=True, text=True, encoding="utf-8", errors="replace").stdout.splitlines()[0]


def windows(first_end: float, last_end: float, step: float) -> list[float]:
    ends, at = [], first_end
    while at <= last_end + 1e-9:
        ends.append(round(at, 3))
        at += step
    return ends


def tally(rows: list[dict]) -> dict:
    counts = {"right": 0, "undecided": 0, "wrong": 0, "refused": 0}
    for row in rows:
        if row["refused"]:
            counts["refused"] += 1
        elif row["decided"] is None:
            counts["undecided"] += 1
        elif row["decided"] == row["expect"]:
            counts["right"] += 1
        else:
            counts["wrong"] += 1
    return counts


START_LABELS = ("5초 전", "3초 전", "곡의 시작")


def judge(rows: list[dict], asked: int, songs: list[str], whole: bool) -> list[str]:
    """확인이 통과하지 못한 까닭들(비어 있으면 통과). 만든 뒤의 확인, 빌드 확인, --apply 가 모두 이 판정을 쓴다.

    검사하지 못한 것(refused: 비교 자체를 하지 못했다)과 검사한 뒤의 미확정을 가른다.
      - 물은 수와 답의 수가 다르거나 검사하지 못한 물음이 하나라도 있으면 통과가 아니다.
      - 틀리게 확정한 것이 하나라도 있으면 통과가 아니다(확정하지 않아야 하는 물음을 확정한 것도 틀린 것이다).
      - 같은 녹화의 물음은 곡마다 있어야 하고 모두 맞아야 한다.
      - whole(만든 뒤의 확인): 곡이 시작할 때의 물음도 곡·조건마다 있어야 하고 20초까지 한 번은 확정해야 한다.
        다른 플레이의 물음이 있는 곡은 그 가운데 하나는 맞혀야 한다.
    곡이 시작하기 전이나 다른 플레이의 끝부분에서 확정하지 못한 것은 통과를 막지 않는다. 판단의 기준(0.60, 0.12)은 여기서 바꾸지 않는다.
    """
    problems = []
    if len(rows) != asked:
        problems.append(f"물은 것은 {asked}개인데 답은 {len(rows)}개다")
    refused = [r for r in rows if r["refused"]]
    if refused:
        problems.append(f"검사하지 못한 물음이 {len(refused)}개 있다(비교 자체를 하지 못했다): {', '.join(sorted({r['refused'] for r in refused}))}")
    wrong = [r for r in rows if not r["refused"] and r["decided"] is not None and r["decided"] != r["expect"]]
    if wrong:
        problems.append(f"틀리게 확정한 것이 {len(wrong)}개 있다")
    for ident in songs:
        same = [r for r in rows if r["group"] == "same-play" and r["expect"] == ident]
        missed = [r for r in same if r["decided"] != ident]
        if not same:
            problems.append(f"같은 녹화의 물음이 없다: {ident}")
        elif missed:
            problems.append(f"같은 녹화의 구간 {len(same)}개 가운데 {len(missed)}개를 맞히지 못했다: {ident}")
        if not whole:
            continue
        for label in START_LABELS:
            seq = [r for r in rows if r["group"] == f"start/{label}" and r["expect"] == ident]
            if not seq:
                problems.append(f"곡이 시작할 때의 물음이 없다({label}): {ident}")
            elif not any(r["decided"] == ident for r in seq):
                problems.append(f"곡이 시작할 때({label}) 끝까지 한 번도 확정하지 못했다: {ident}")
        for kind in ("reference", "query"):
            other = [r for r in rows if r["group"] == f"other-play/{kind}" and r["expect"] == ident]
            if other and not any(r["decided"] == ident for r in other):
                problems.append(f"다른 플레이({kind})의 구간 {len(other)}개를 하나도 맞히지 못했다: {ident}")
    if whole and not any(r["group"] == "other-song" for r in rows):
        problems.append("다른 쌍의 곡을 들려주는 물음이 없다")
    return problems


def main() -> int:
    parser = argparse.ArgumentParser(description="동명곡을 가리는 소리 기준을 직접 녹화한 영상에서 다시 만들고 확인한다.")
    parser.add_argument("name", help="결과 이름(data\\audio-rebuild\\<이름>)")
    parser.add_argument("--build", type=Path, required=True, help="앱의 Debug 빌드 폴더(DJMAXLocalRecords.exe 가 있는 곳)")
    parser.add_argument("--other-plays", type=Path, help="다른 플레이의 소리가 있는 폴더(video-manifest.json 과 youtube\\<영상>.wav)")
    parser.add_argument("--apply", action="store_true", help="확인이 끝나면 bin 14개를 app\\runtime\\data\\audio 에 복사한다")
    parser.add_argument("--verify-build", action="store_true", help="있는 결과의 bin 이 빌드 폴더의 data\\audio 에 그대로 복사됐는지 본다")
    parser.add_argument("--hash-only", action="store_true", help="--verify-build 에서 해시만 견준다(Release 빌드에는 개발용 검사가 없다)")
    args = parser.parse_args()
    for stream in (sys.stdout, sys.stderr):
        stream.reconfigure(errors="replace", line_buffering=True)
    try:
        return verify_build(args) if args.verify_build else build(args)
    except Stop as stop:
        print(f"멈춤: {stop}", file=sys.stderr)
        return 1


def build(args) -> int:
    spec = json.loads(SPEC.read_text(encoding="utf-8"))
    out = OUT / args.name
    if out.exists():
        raise Stop(f"이미 있다. 새 이름을 쓴다: {out}")
    if not (args.build / EXE).is_file():
        raise Stop(f"앱의 Debug 빌드를 찾지 못했다: {args.build / EXE}")
    lead, tail, track = spec["lead_seconds"], spec["tail_seconds"], spec["track"]
    sources = spec["sources"]
    if VIDEOS is None:
        raise Stop("녹화가 있는 폴더를 알려 주지 않았다. 환경 변수 DJMAX_SAME_NAME_VIDEOS 나 저장소 맨 위 local-paths.json 의 same_name_videos 로 알려 준다.\n"
                   "녹화(tools\\audio_references.json 에 적힌 14개)는 저장소에 들어 있지 않다. 없으면 소리 기준을 다시 만들 수 없다.")
    missing = [s["file"] for s in sources if not (VIDEOS / s["file"]).is_file()]
    if missing:
        raise Stop(f"녹화를 찾지 못했다({VIDEOS}): {', '.join(missing)}")
    out.mkdir(parents=True)
    summary = {"made_at": f"{datetime.now():%Y-%m-%d %H:%M:%S}", "videos": str(VIDEOS), "ffmpeg": version("ffmpeg"), "ffprobe": version("ffprobe"),
               "build": str(args.build), "track": track, "lead_seconds": lead, "tail_seconds": tail, "sources": [], "checks": {}}

    # 1·2. 원본을 확인하고 소리를 뽑는다
    wavs, outros, lengths = {}, {}, {}
    for s in sources:
        video, ident = VIDEOS / s["file"], f"varchive:{s['source_id']}"
        print(f"{s['name']} — {s['artist']}  ({s['file']})")
        streams = probe(video)
        if track >= len(streams):
            raise Stop(f"오디오 트랙이 {len(streams)}개뿐이다: {s['file']}")
        start, end = s["song"][0] - lead, s["song"][1] + tail
        samples, command = like_app(video, track, start, end)
        rms, peak = float(np.sqrt(np.mean((samples / 32768.0) ** 2))), float(np.abs(samples).max() / 32768.0)
        if rms < 0.005:
            raise Stop(f"고른 트랙의 그 구간이 무음에 가깝다(RMS {rms:.5f}): {s['file']}")
        wav = out / "wav" / f"{s['source_id']}.wav"
        write_wav(samples, wav)
        wavs[ident], lengths[ident] = wav, len(samples) / RATE
        # 곡이 끝난 뒤의 소리(결과 화면). 기준에는 넣지 않고, "곡 밖의 소리"로 물어볼 때만 쓴다
        after, _ = like_app(video, track, s["song"][1] + 1.5, None)
        outro = out / "outro" / f"{s['source_id']}.wav"
        write_wav(after, outro)
        outros[ident] = (outro, len(after) / RATE)
        summary["sources"].append({"id": ident, "name": s["name"], "artist": s["artist"], "file": s["file"], "played": s["played"], "bytes": video.stat().st_size, "sha256": sha256(video),
                                   "audio_streams": [{"index": a["index"], "codec": a["codec_name"], "rate": a["sample_rate"], "channels": a["channels"], "title": a.get("tags", {}).get("title")} for a in streams],
                                   "stream_used": streams[track]["index"], "song": s["song"], "cut": [round(start, 3), round(end, 3)], "command": " ".join(f'"{c}"' if " " in c else c for c in command),
                                   "wav": str(wav.relative_to(out)), "wav_sha256": sha256(wav), "wav_seconds": round(len(samples) / RATE, 3), "rms": round(rms, 5), "peak": round(peak, 4)})

    # 3. 앱의 계산으로 bin 을 만든다
    job = out / "build-job.json"
    job.write_text(json.dumps({"items": [{"id": ident, "wav": str(wav), "bin": str(out / "bin" / (ident.split(":")[1] + ".bin"))} for ident, wav in wavs.items()],
                               "report": str(out / "build-report.json")}, ensure_ascii=False, indent=1), encoding="utf-8")
    run_app(args.build, "--audio-reference-build", job)
    built = {row["id"]: row for row in json.loads((out / "build-report.json").read_text(encoding="utf-8"))}
    for entry in summary["sources"]:
        row = built[entry["id"]]
        if row["wav_sha256"] != entry["wav_sha256"]:
            raise Stop("앱이 읽은 소리가 뽑은 소리와 다르다: " + entry["id"])
        entry.update({"bin": f"bin/{entry['id'].split(':')[1]}.bin", "bin_sha256": row["sha256"], "bin_bytes": row["bytes"], "frames": row["frames"], "silent_frames": row["silent_frames"]})
        old = TARGET / f"{entry['id'].split(':')[1]}.bin"
        entry["replaces_sha256"] = sha256(old) if old.exists() else None

    # 4. 앱의 판단으로 확인한다
    pairs = [[f"varchive:{a}", f"varchive:{b}"] for a, b in spec["pairs"]]
    pair_of = {ident: i for i, pair in enumerate(pairs) for ident in pair}
    queries = []
    for ident, wav in wavs.items():
        for end in windows(lead + 8, lengths[ident] - tail, 10):            # 곡의 처음 8초부터 10초마다
            queries.append({"group": "same-play", "expect": ident, "pair": pair_of[ident], "wav": str(wav), "start": end - 8, "seconds": 8})
        for begin, label in zip((0.0, 2.0, lead), START_LABELS):
            for k in range(0, 13):                                              # 받기 시작한 지 8초 ~ 20초
                queries.append({"group": f"start/{label}", "expect": ident, "pair": pair_of[ident], "wav": str(wav), "start": begin + k, "seconds": 8})
        for other, pair in enumerate(pairs):
            if other == pair_of[ident]:
                continue
            for share in (0.3, 0.5, 0.7):
                queries.append({"group": "other-song", "expect": None, "pair": other, "wav": str(wav), "start": round(lead + (lengths[ident] - lead - tail - 8) * share, 3), "seconds": 8})
        outro, seconds = outros[ident]
        if seconds >= 6:
            for index in range(len(pairs)):
                queries.append({"group": "jingle", "expect": None, "pair": index, "wav": str(outro), "start": 0, "seconds": round(min(8.0, seconds) - 0.01, 2)})
    other_info = None
    if args.other_plays:
        manifest = json.loads((args.other_plays / "video-manifest.json").read_text(encoding="utf-8"))
        other_info = {"folder": str(args.other_plays), "videos": []}
        for entry in manifest:
            for kind in ("reference", "query"):
                source = args.other_plays / "youtube" / (entry[kind] + ".wav")
                if not source.is_file() or entry["id"] not in pair_of:
                    continue
                samples, _ = like_app(source, None, None, None)
                wav = out / "other" / f"{entry[kind]}.wav"
                write_wav(samples, wav)
                seconds = len(samples) / RATE
                ends = windows(20, seconds - 12, 10)
                other_info["videos"].append({"id": entry["id"], "kind": kind, "video": entry[kind], "seconds": round(seconds, 1), "windows": len(ends)})
                for end in ends:
                    queries.append({"group": f"other-play/{kind}", "expect": entry["id"], "pair": pair_of[entry["id"]], "wav": str(wav), "start": end - 8, "seconds": 8})
    job = out / "check-job.json"
    job.write_text(json.dumps({"references": {ident: str(out / "bin" / (ident.split(":")[1] + ".bin")) for ident in wavs}, "pairs": pairs, "queries": queries,
                               "report": str(out / "check-report.json")}, ensure_ascii=False, indent=1), encoding="utf-8")
    run_app(args.build, "--audio-reference-check", job)
    rows = json.loads((out / "check-report.json").read_text(encoding="utf-8"))["rows"]
    names = {f"varchive:{s['source_id']}": f"{s['name']} — {s['artist']}" for s in sources}

    wrong = 0
    print()
    for group in sorted({row["group"].split("/")[0] for row in rows}):
        part = [row for row in rows if row["group"].split("/")[0] == group]
        counts = tally(part)
        if group in ("other-song", "jingle"):       # 확정하지 않아야 맞다. 검사하지 못한 것(refused)은 맞은 것으로 세지 않는다
            counts = {"not_decided(맞음)": counts["undecided"], "decided(틀림)": counts["right"] + counts["wrong"], "refused": counts["refused"]}
            wrong += counts["decided(틀림)"]
        else:
            wrong += counts["wrong"]
        summary["checks"][group] = {"queries": len(part), **counts}
        print(f"[{group}] 물음 {len(part)}개: {counts}")
    # 곡마다: 같은 녹화, 다른 플레이
    per_song = {}
    for ident in wavs:
        same = [r for r in rows if r["group"] == "same-play" and r["expect"] == ident]
        decided = [r for r in same if r["decided"] == ident]
        entry = {"same_play": tally(same), "same_play_best_min": min((r["best"] for r in decided), default=None), "same_play_margin_min": min((r["margin"] for r in decided), default=None)}
        firsts = {}
        for label in START_LABELS:
            seq = sorted((r for r in rows if r["group"] == f"start/{label}" and r["expect"] == ident), key=lambda r: r["start"])
            first = next((i for i, r in enumerate(seq) if r["decided"] == ident), None)
            firsts[label] = None if first is None else 8 + first        # 받기 시작한 뒤 몇 초에 처음 확정되는가
            if any(r["decided"] not in (None, ident) for r in seq):
                firsts[label + " 틀림"] = True
        entry["first_decided_after_seconds"] = firsts
        for kind in ("reference", "query"):
            other = [r for r in rows if r["group"] == f"other-play/{kind}" and r["expect"] == ident]
            if other:
                right = [r for r in other if r["decided"] == ident]
                entry[f"other_play_{kind}"] = {**tally(other), "best_median": round(float(np.median([r["best"] for r in right])), 3) if right else None,
                                               "margin_median": round(float(np.median([r["margin"] for r in right])), 3) if right else None}
        per_song[ident] = entry
        line = f"  {names[ident]:<36} 같은 녹화 {entry['same_play']}  확정까지 {firsts}"
        for kind, label in (("reference", "다른 플레이(4B)"), ("query", "다른 플레이(8B SC)")):
            if f"other_play_{kind}" in entry:
                line += f"  {label} {entry[f'other_play_{kind}']}"
        print(line)
    summary["per_song"] = per_song
    summary["other_plays"] = other_info
    problems = judge(rows, len(queries), list(wavs), whole=True)
    summary["wrong_decisions"] = wrong
    summary["problems"] = problems
    summary["passed"] = not problems
    summary["applied"] = False
    if not problems and args.apply:
        for entry in summary["sources"]:
            shutil.copyfile(out / entry["bin"], TARGET / Path(entry["bin"]).name)
        summary["applied"] = True
        print(f"\nbin {len(summary['sources'])}개를 복사했다 → {TARGET}")
    (out / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=1), encoding="utf-8")
    print(f"\n틀리게 확정한 것: {wrong}개.  결과: {out / 'summary.json'}")
    for problem in problems:
        print("통과하지 못함: " + problem, file=sys.stderr)
    if problems and args.apply:
        print(f"확인이 통과하지 못해 {TARGET} 의 파일을 바꾸지 않았다.", file=sys.stderr)
    return 0 if not problems else 1


def verify_build(args) -> int:
    out = OUT / args.name
    summary = json.loads((out / "summary.json").read_text(encoding="utf-8"))
    bad = []
    for entry in summary["sources"]:
        copied = args.build / "data" / "audio" / Path(entry["bin"]).name
        source = TARGET / Path(entry["bin"]).name
        if not copied.is_file() or sha256(copied) != entry["bin_sha256"] or sha256(source) != entry["bin_sha256"]:
            bad.append(entry["id"])
    extra = sorted(p.name for p in (args.build / "data" / "audio").glob("*") if p.name not in {Path(e["bin"]).name for e in summary["sources"]})
    print(f"빌드 폴더의 bin {len(summary['sources']) - len(bad)}/{len(summary['sources'])}개가 이 결과와 같다(SHA-256). 그 밖의 파일: {extra or '없음'}")
    if bad or extra:
        raise Stop(f"빌드 폴더의 자료가 이 결과와 다르다: {bad} {extra}")
    if args.hash_only:
        return 0
    # 빌드에 복사된 파일을 앱이 읽게 해서 같은 녹화의 물음을 다시 돌린다
    job = json.loads((out / "check-job.json").read_text(encoding="utf-8"))
    job["references"] = {ident: "app" for ident in job["references"]}
    job["queries"] = [q for q in job["queries"] if q["group"] == "same-play"]
    job["report"] = str(out / f"verify-{args.build.parent.name}-report.json")
    path = out / f"verify-{args.build.parent.name}-job.json"
    path.write_text(json.dumps(job, ensure_ascii=False, indent=1), encoding="utf-8")
    run_app(args.build, "--audio-reference-check", path)
    report = json.loads(Path(job["report"]).read_text(encoding="utf-8"))
    counts = tally(report["rows"])
    hashes = all(report["references"][e["id"]]["sha256"] == e["bin_sha256"] for e in summary["sources"])
    print(f"앱이 빌드 폴더의 data\\audio 에서 읽은 파일의 해시가 같음: {hashes}.  같은 녹화의 물음 {len(report['rows'])}개: {counts}")
    # 만든 뒤의 확인과 같은 판정을 쓴다: 물음이 빠지거나 검사하지 못했으면 통과가 아니다
    problems = judge(report["rows"], len(job["queries"]), [e["id"] for e in summary["sources"]], whole=False)
    made = summary["checks"].get("same-play", {}).get("queries")
    if len(job["queries"]) != made:
        problems.append(f"같은 녹화의 물음이 만들 때({made}개)와 다르다: {len(job['queries'])}개")
    if not hashes:
        problems.append("앱이 읽은 기준 파일이 이 결과의 bin 과 다르다")
    for problem in problems:
        print("통과하지 못함: " + problem, file=sys.stderr)
    return 0 if not problems else 1


if __name__ == "__main__":
    sys.exit(main())
