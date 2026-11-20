# 개발자 안내

소스를 직접 빌드하거나 검사 도구를 사용하는 개발자를 위한 안내입니다. **저장소 최상위 폴더에서 PowerShell을 열고** 아래 명령을 실행합니다.

## 1. 개발 환경

| 용도 | 필요한 환경 |
| --- | --- |
| 앱 빌드·실행 | Windows x64, .NET 9 SDK |
| 검사·배포 묶음 도구 | Python 3.12, Git |
| SCORE 모델 학습·검증 | `requirements.txt`에 따른 별도 Python 환경 |

앱을 빌드할 때 모델을 다시 학습할 필요는 없습니다.

Python 도구를 사용할 경우 가상환경을 준비합니다.

```powershell
py -3.12 -m venv .venv
```

## 2. 소스 구성

| 위치 | 내용 |
| --- | --- |
| `app/` | WinForms 앱, OBS 오버레이, 실행 자료와 참조 DLL |
| `dotnet/ScoreModule/` | 앱에서 사용하는 SCORE 인식 모듈 |
| `dotnet/*Check/` | 모듈 대조·검사 도구 |
| `detector/` | SCORE 모델 학습·영상 검증 도구 |
| `tools/` | 개발용 검사, 배포 묶음 제작 등 보조 도구 |

소스의 주석과 도구 설명에서 가리키는 `docs\` 아래 문서는 내부 작업 기록으로, 공개 소스에는 포함되지 않습니다. `detector/` 도구에 따로 필요한 자료는 `detector/__init__.py`에 정리되어 있습니다.

## 3. 앱 빌드하기

```powershell
dotnet build app\DJMAXLadderRecords.csproj -c Release
```

실행 파일은 다음 위치에 생성됩니다.

```text
app\bin\Release\net9.0-windows\DJMAXLocalRecords.exe
```

실행에 필요한 자료가 함께 복사되므로 **빌드 결과 폴더 전체를 유지합니다.** 기록과 설정은 실행 파일 옆에 저장됩니다.

개발용 앱을 실행하기 전에 사용 중인 개인용 앱을 종료합니다. 두 앱이 같은 OBS 포트와 전역 단축키를 사용합니다.

## 4. 기본 검사 실행하기

개발용 검사는 **Debug 빌드**에서 실행합니다.

```powershell
dotnet build app\DJMAXLadderRecords.csproj -c Debug
.venv\Scripts\python.exe tools\run_dev_checks.py check-001
```

테스트 데이터를 생성해 기록 창, 단축키 동작, 오버레이 상태, 저장과 가져오기를 검사합니다. 개인 기록은 사용하지 않습니다.

- 결과는 `data\devchecks\check-001\summary.json`과 `summary.tsv`에 저장됩니다.
- 다시 검사할 때는 새 결과 이름을 사용합니다.
- 코드를 수정했다면 먼저 다시 빌드합니다.
- **실행 불가·시간 초과는 통과가 아닙니다.**

실제 게임의 인식, 실제 키 입력, 화면 디자인은 별도 확인이 필요합니다. 원본 녹화·학습 자료와 일부 시험 자료는 공개 소스에 포함되지 않으므로, 해당 자료를 사용하는 검사는 별도로 준비해야 합니다.

## 5. 배포 묶음 만들기

다음은 **1.1.0 배포용 묶음**을 만드는 예시입니다.

```powershell
.venv\Scripts\python.exe tools\make_bundle.py 배포용 --version 1.1.0 --out .\dist
```

`dist\배포용` 아래에 .NET 실행 환경을 포함한 Windows x64 프로그램 폴더와 배포 ZIP이 생성됩니다. 개인 기록·설정은 포함하지 않습니다.

- 묶음은 **커밋된 HEAD**로 만듭니다. 커밋하지 않은 변경은 반영되지 않습니다.
- 버전 번호는 제작할 버전에 맞게 지정합니다. 생략하면 자동 업데이트를 확인하지 않는 `0.0.0` 개발용 빌드가 됩니다.
- 파일 목록과 해시는 함께 생성되는 `묶음정보.txt`에서 확인합니다.
- 실행 확인은 **완성된 묶음의 사본**으로 진행합니다. 배포할 원본에 기록·진단 파일이 생기는 것을 방지하기 위한 것입니다.
