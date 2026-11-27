# 개발자 안내

## 1. 개발 환경

| 작업 | 필요한 환경 |
| --- | --- |
| Windows 앱 빌드 | Windows x64, .NET 9 SDK |
| 검사 도구 실행·배포 ZIP 제작 | Python 3.12, Git |
| SCORE 모델 학습·검증 | 같은 가상환경에 `requirements.txt`를 설치 |

앱을 빌드할 때는 저장소에 포함된 SCORE 모델을 그대로 사용합니다.

이 안내의 명령은 저장소 최상위 폴더에서 PowerShell로 실행합니다. Python 도구를 사용할 때는 먼저 가상환경을 만듭니다.

```powershell
py -3.12 -m venv .venv
```

SCORE 모델을 학습·검증하려면 같은 가상환경에 `requirements.txt`를 설치합니다. 검사 도구와 배포 묶음 도구는 이 설치 없이 실행됩니다.

```powershell
.\.venv\Scripts\python.exe -m pip install -r requirements.txt
```

## 2. 소스 구성

| 폴더 | 역할 |
| --- | --- |
| `app/` | Windows 앱(WinForms), OBS 오버레이, 실행에 필요한 파일 |
| `dotnet/ScoreModule/` | 게임 화면에서 SCORE를 찾아 읽는 앱용 인식 모듈 |
| `dotnet/*Check/` | 인식 모듈의 동작을 검사하고 결과를 비교하는 도구 |
| `detector/` | SCORE 모델을 학습하고 녹화 영상으로 인식 결과를 검증하는 Python 도구 |
| `tools/` | 개발용 검사를 실행하고 배포 ZIP을 만드는 보조 도구 |

개발 중에 쓴 작업 기록(`docs\`)은 공개 소스에 포함되지 않습니다. `detector/` 도구에 따로 필요한 자료는 `detector/__init__.py`에 정리되어 있습니다. `app/runtime/DJMAXLocalRecords.Compatibility.dll`은 이전에 만든 자체 프로그램의 빌드 결과로, 대응하는 소스는 남아 있지 않습니다.

## 3. 앱 빌드하기

저장소 최상위 폴더에서 PowerShell로 다음 명령을 실행합니다.

```powershell
dotnet build app\DJMAXLadderRecords.csproj -c Release
```

빌드가 끝나면 다음 위치에 실행 파일이 생성됩니다.

```text
app\bin\Release\net9.0-windows\DJMAXLocalRecords.exe
```

실행에 필요한 파일도 함께 복사되므로 **빌드 결과 폴더 전체를 유지합니다.** 개인용 앱이 실행 중이면 빌드한 앱을 열기 전에 종료합니다. 두 앱은 같은 OBS 포트와 단축키를 사용합니다.

## 4. 기본 검사 실행하기

저장소 최상위 폴더에서 **Debug 빌드**를 만든 뒤 검사를 실행합니다. 검사 도구는 테스트용 기록을 생성해 기록 창, 단축키, 오버레이, 저장·가져오기 동작을 확인합니다. 검사 중에는 프로그램 창이 여러 번 열렸다 닫힙니다. 끝날 때까지 키보드와 마우스를 쓰지 않습니다.

```powershell
dotnet build app\DJMAXLadderRecords.csproj -c Debug
.\.venv\Scripts\python.exe tools\run_dev_checks.py check-001
```

검사가 끝나면 `data\devchecks\check-001\summary.tsv`를 엽니다. **결과** 열의 모든 항목이 `통과`이면 기본 검사를 통과한 것입니다. `실패`, `실행 불가`, `시간 초과`가 있으면 같은 행의 **까닭**을 확인합니다. 같은 폴더의 `summary.json`에는 검사에 사용한 빌드와 커밋 정보도 남습니다.

다시 검사할 때는 `check-002`처럼 새 결과 이름을 사용합니다. 코드를 수정했다면 먼저 Debug 빌드를 다시 만듭니다.

## 5. 배포 묶음 만들기

배포할 변경 사항을 모두 커밋한 뒤, 저장소 최상위 폴더에서 다음 명령을 실행합니다. 도구는 현재 커밋을 기준으로 .NET 실행 환경을 포함한 Windows x64용 ZIP을 만듭니다.

```powershell
.\.venv\Scripts\python.exe tools\make_bundle.py 배포용 --version 1.2.0 --out .\dist
```

**GitHub 릴리즈에 올릴 파일**은 `dist\배포용\DJMAXLocalRecords-v1.2.0-win-x64.zip`입니다. 압축 전 프로그램 폴더는 `dist\배포용\DJMAXLocalRecords\`에 생성됩니다.
