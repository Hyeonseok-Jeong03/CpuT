# CpuT

CPU 온도를 화면과 트레이 아이콘에 표시하고, 온도가 높아지면 CPU 최고 속도를 자동으로 낮추는 Windows 앱입니다.

쿨러 하나가 고장 난 노트북(MSI Creator Z16, i9-11900H)에서 CPU가 100°C에 닿아 전원이 꺼지는 문제를 막으려고 만들었습니다.

## 주요 기능

### 화면 표시
- **오버레이**: 화면 오른쪽 위(위치 변경 가능)에 투명 배경으로 표시되고, 클릭은 뒤 창으로 통과합니다.
  ```
  CPU 52°C                  ← 온도 (초록 <70 / 주황 70~ / 빨강 85~)
  CPU 12W · 배터리 18W      ← CPU 전력 · 충전 또는 배터리 사용 전력
  1시간 7분 남음            ← 충전 중이 아닐 때만
  부스트 켜짐 (최대 3.5GHz) ← 모드·속도 제한 상태 (해당할 때만)
  ```
  - 흰 배경에서도 보이도록 글자에 어두운 테두리가 있습니다.
  - 앱을 켜면 **항상 숨긴 상태로 시작**합니다. 숨겨 둔 상태에서도 **85°C 이상이면 자동으로 나타나고**, 80°C 이하로 식으면 다시 숨겨집니다.
- **트레이 아이콘**: 온도 숫자를 온도에 따른 색 바탕으로 표시합니다. 마우스를 올리면 온도와 전력이 보입니다.
- 온도·CPU 전력은 1초마다, 배터리 정보는 3초마다 갱신합니다.

### 트레이 메뉴 (아이콘 오른쪽 클릭)
| 메뉴 | 동작 |
|---|---|
| 절전 (최대 2.0GHz) | 가벼운 작업·유휴 때 온도를 더 낮춤 |
| 기본 (최대 3.0GHz) | 기본값. 4.9GHz 순간 부스트로 온도가 튀는 것을 막음 |
| 부스트 (최대 3.5GHz) | 충전기를 꽂았을 때 효과 있음 |
| 오버레이 표시 | 화면 표시만 켜기/끄기 (보호 기능은 계속 동작) |
| 위치 이동 | 켜면 오버레이를 끌어서 옮길 수 있음. 위치는 저장됨 |
| 종료 | 앱 종료 |

앱을 켜고 끌 때는 항상 **기본** 모드로 돌아갑니다.

### 과열 보호
| CPU 온도 | 동작 |
|---|---|
| 95°C 이상 | **비상 감속**: 최대 프로세서 상태를 30%로 낮춤 |
| 85°C 이상 | 1초마다 최고 속도를 0.2GHz씩 낮춤 (최저 1.2GHz). 제한이 실제 속도보다 한참 높으면 실제 속도 바로 아래부터 낮춤 |
| 80~85°C | 현재 속도 유지 |
| 80°C 이하 | 1초마다 0.2GHz씩 모드 최고 속도까지 다시 올림, 비상 감속 해제 |

속도 제한은 Windows 전원 설정(`최대 프로세서 주파수`, `최대 프로세서 상태`)을 바꾸는 방식이며, 전압을 직접 바꾸지 않습니다.

### 기타
- 한 번만 실행됩니다(이미 실행 중이면 새로 뜨지 않음).
- 앱을 종료하면 온도 제한을 풀고 기본 속도(3.0GHz)로 되돌립니다. 3.0GHz 제한은 Windows 설정이라 앱이 꺼져도 남지만, **85°C/95°C 자동 보호는 앱이 실행 중일 때만** 동작합니다.

## 실행 환경
- Windows 10/11
- [.NET 10 데스크톱 런타임](https://dotnet.microsoft.com/download/dotnet/10.0)
- [PawnIO](https://pawnio.eu) 드라이버 (CPU 온도·전력 측정용)
- 관리자 권한 (앱이 실행 시 자동으로 요청)

## 빌드
```bash
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o publish
```
결과물: `publish\CpuT.exe` (단일 파일)

실행 중인 CpuT가 있으면 exe를 덮어쓸 수 없으니 먼저 트레이 메뉴에서 종료하세요.

## 로그인 시 자동 실행
UAC 창 없이 관리자 권한으로 실행되도록 작업 스케줄러에 등록합니다. **관리자 권한 PowerShell**에서 실행하세요.

```powershell
$exe = 'C:\CpuT\publish\CpuT.exe'
$user = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$action = New-ScheduledTaskAction -Execute $exe -WorkingDirectory (Split-Path $exe)
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $user
$trigger.Delay = 'PT10S'
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
Register-ScheduledTask -TaskName 'CpuT' -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force -ErrorAction Stop
```
- 배터리에서도 실행되고, 충전기를 뽑아도 꺼지지 않으며, 실행 시간 제한이 없도록 설정합니다(작업 스케줄러 기본값은 반대).
- 해제: 작업 스케줄러에서 `CpuT` 작업을 사용 안 함 또는 삭제.

## 저장 파일
| 파일 | 내용 |
|---|---|
| `%LOCALAPPDATA%\CpuT\position.txt` | 옮긴 오버레이 위치. 지우면 오른쪽 위로 돌아감 |

## Windows 기본 설정으로 되돌리기
앱을 종료한 뒤 관리자 권한 터미널에서 실행합니다.
```bash
powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCFREQMAX 0
powercfg /setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCFREQMAX 0
powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX 100
powercfg /setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX 100
powercfg /setactive SCHEME_CURRENT
```

## 시험 결과 (MSI Creator Z16, 쿨러 1개 고장)
| 항목 | 결과 |
|---|---|
| 3.0GHz 제한 | 부하 중에도 3.0GHz를 넘지 않음 |
| 3.5GHz (충전기, 2코어) | 85°C 미만으로 안정 |
| 4.0GHz (2코어) | 약 3초 만에 95°C 초과 → 사용하지 않음 |
| 모든 코어 지속 부하 | 약 1.7~2.4GHz로 유지 (냉각 한계) |
| 배터리 사용 중 | MSI 펌웨어가 전력을 제한해 부하 시 약 1GHz |
| 메모리 | 약 40MB에서 더 늘지 않음 |

## 파일 구성
| 파일 | 설명 |
|---|---|
| `Program.cs` | 진입점, 중복 실행 방지 |
| `MainForm.cs` | 오버레이, 트레이 아이콘·메뉴, 온도 감시, 속도 제어 |
| `OutlinedLabel.cs` | 테두리 있는 글자 라벨 |
| `app.manifest` | 관리자 권한 요청 |
| `app.ico` | 앱 아이콘 |
