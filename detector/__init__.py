"""SCORE 위치 탐지·숫자 판독 모델의 학습 및 검증 도구다. 앱은 이 도구 없이 실행되고, 학습된 모델만 사용한다.

도구는 저장소 맨 위에서 `.venv\\Scripts\\python.exe -m detector.<도구> …` 로 돌린다(쓰는 법은 도구마다 파일 맨 위에 있다).

저장소에 들어 있지 않아 따로 있어야 하는 자료 (없으면 도구가 파일을 찾지 못하거나 입력 목록이 비어 멈춘다. 통과로 끝나지 않는다)
  - 원본 녹화 영상(기어 표본). 있는 폴더를 환경 변수 DJMAX_SOURCE_VIDEOS 나 저장소 맨 위 local-paths.json 의 source_videos 로 알려 준다.
  - testdata\\ladder-records\\  검사 설정(사례 목록과 사례별 결과, 추출 목록, 기어 목록)과 영상에서 0.5초마다 뽑은 PNG.
  - truth\\  정답 라벨, 확대 이미지를 직접 판독한 초기 학습 라벨, 못 읽은 프레임의 판정.
  - data\\  위의 것에서 도구들이 차례로 만드는 중간 결과(잘라 낸 그림, 학습한 모델, 판독 결과).
"""
import sys

_HINT = ("\n필요한 파일이 없다. 원본 녹화, testdata\\ladder-records(검사 설정과 PNG), truth(라벨), data(앞 단계의 결과)는 저장소에 들어 있지 않다.\n"
         "무엇이 필요한지는 detector\\__init__.py 와 돌린 도구의 맨 위 설명에 있다.")


def _explain(kind, error, trace):
    """파일이 없어 멈췄을 때, 오류 뒤에 무엇이 필요한지 한 번 더 알린다. 종료 코드는 그대로 실패다."""
    sys.__excepthook__(kind, error, trace)
    if issubclass(kind, FileNotFoundError):
        print(_HINT, file=sys.stderr)


sys.excepthook = _explain


def inputs(folder, pattern, what):
    """폴더에서 입력 파일의 목록을 얻는다(이름순). 하나도 없으면 무엇이 없는지 알리고 실패로 끝낸다.

    폴더를 훑는 것은 폴더나 파일이 없어도 오류 없이 빈 목록을 준다. 그대로 두면 도구가 사례 0개로 성공해 버린다.

    파일을 찾지 못한 보통의 오류(FileNotFoundError)를 낸다. 프로세스를 바로 끝내는 SystemExit 를 쓰지 않는다:
    여러 프로세스로 나눠 일하는 도구(build_dataset)의 작업 프로세스 안에서 그렇게 끝내면 부모가 결과를 받지 못해 끝없이 기다린다.
    보통의 오류는 부모로 전해져 명령 전체가 실패로 끝나고, 위의 _explain 이 무엇이 필요한지 덧붙인다."""
    from pathlib import Path
    found = sorted(Path(folder).glob(pattern))
    if not found:
        raise FileNotFoundError(f"입력이 하나도 없다({what}): {Path(folder) / pattern}")
    return found
