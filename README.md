# 교실 컨트롤러 (Windows)

Android `ClassroomController`(교사용 교실 관리 앱)의 Windows 데스크톱 포팅. Avalonia(.NET 8) 기반.

같은 네트워크의 학생 태블릿에서 실행되는 `ClassroomAgent`를 mDNS로 탐색하고, WebSocket으로
수업 시작/종료, 상태 모니터링, 허용 앱 설정, 학적 수정 요청 승인 등을 수행한다.

## 동작 개요

- **탐색**: mDNS 서비스 `_classroomagent._tcp` 를 주기적으로 탐색하고, 기기 이름이
  `{학년}{반}`(예: `101`) 으로 시작하는 것만 표시한다.
- **폴링**: 5초마다 각 에이전트에 `STATUS`를 보내 상태(수업중/접근성/오버레이/우회 시도)를 갱신한다.
- **명령**: `ws://{ip}:{port}` 로 `START` / `STOP` / `SET_ALLOWED_APPS|...` /
  `EDIT_APPROVED` / `EDIT_REJECTED` 를 전송한다.
- **세션 보정**: 컨트롤러 세션 상태와 에이전트 상태가 다르면 자동으로 START/STOP 재전송(체크된 기기만).
- **자동 종료**: 접속 태블릿이 최고치의 1/3 이하로 떨어지면 5분 카운트다운 후 수업 자동 종료.
- **학적 수정 요청**: 에이전트가 요청 시 승인/거절 팝업을 순차 표시.
- **우회 시도 알림**: 차단 앱 사용 시도를 감지하면 우측 상단 알림으로 표시.

## 프로토콜 (에이전트 응답)

```
STATUS  → IN_SESSION|ACCESSIBILITY:true|OVERLAY:true|EDIT_REQUEST:false|SUSPICIOUS:none|VPN:false
GET_APPS → APP_LIST|앱이름:패키지|...
START/STOP → OK: START
```

## 구조

```
Program.cs / App.axaml       진입점, Avalonia 부트스트랩
Models/AgentStatus.cs        STATUS 응답 파싱
Services/SettingsService.cs  설정 저장 (%AppData%/ClassroomController/settings.json)
Services/AgentDiscovery.cs   mDNS 탐색 (Zeroconf)
Services/AgentClient.cs      WebSocket 명령 전송 (System.Net.WebSockets)
ViewModels/                  MainWindowViewModel, DeviceViewModel
Views/                       MainWindow, ClassroomSetupWindow, ConfirmDialog
```

## 빌드 / 실행

이 개발 머신의 시스템 `dotnet`(/usr/lib64/dotnet)에는 SDK가 없다. `~/.dotnet`의 SDK 8.0을 사용한다:

```bash
export DOTNET_ROOT=$HOME/.dotnet
export PATH=$DOTNET_ROOT:$PATH

dotnet restore
dotnet build
dotnet run
```

### Windows 단일 exe 배포

```bash
dotnet publish -c Release -r win-x64
# 출력: bin/Release/net8.0/win-x64/publish/교실컨트롤러.exe
```

## 구현 완료 (원본 대비)

- ✅ **허용 앱 선택 UI** — 헤더 📱 버튼 → `GET_APPS` 수집(무작위 3대 교집합) → 체크박스 다이얼로그
  ([AppSelectionWindow](Views/AppSelectionWindow.axaml), [AppCatalog](Services/AppCatalog.cs)).
- ✅ **초기 설정 마법사** — 5단계(환영→학급→탐색→허용앱→완료). 최초 실행 시 자동, ⚙ 메뉴에서 재실행
  ([SetupWizardWindow](Views/SetupWizardWindow.axaml)).
- ✅ **Windows 토스트 알림** — Windows 에서 PowerShell 로 WinRT 토스트 표시(+ 앱 내 배너 병행)
  ([WindowsToast](Services/WindowsToast.cs)). 리눅스 빌드는 그대로 유지.
- ✅ **전용 아이콘** — [Assets/app.ico](Assets/app.ico) (교실 모니터링 테마, PIL 생성). 미리보기: `Assets/app.png`.

## 남은 개선 여지

- 토스트 클릭 시 창 포커스 등 상호작용 (현재는 표시 전용).
