# 교실 컨트롤러 (Windows)

Android `ClassroomController`(교사용 교실 관리 앱)의 Windows 데스크톱 포팅. Avalonia(.NET 8) 기반.

같은 네트워크의 학생 태블릿에서 실행되는 `ClassroomAgent`를 mDNS로 탐색하고, WebSocket으로
수업 시작/종료, 상태 모니터링, 허용 앱 설정, 학적 수정 요청 승인 등을 수행한다.

## 트레이 상주 (수업 메모 도구와 같은 구조)

- **한 번만 실행**: 이미 떠 있으면 두 번째 실행(작업표시줄 고정 아이콘·시작 메뉴)은 기존 창을 띄우고 끝난다.
- **트레이 상주**: 창의 X 는 트레이로 숨기기일 뿐 감시는 계속된다. 트레이 메뉴의 **종료**로만 끝나며,
  수업 중이면 종료 전에 확인한다.
- **자동 실행**: 배포 exe 를 실행하면 `HKCU\...\Run` 에 `"exe" --tray` 로 등록(트레이 메뉴에서 끌 수 있음)하고
  시작 메뉴에 `교실 관리` 바로가기를 만든다(우클릭 → 작업 표시줄에 고정). 개발(Debug) 빌드는 등록하지 않는다.
- **경고**: 태블릿에 문제가 생기면 트레이 아이콘이 빨간 경고 아이콘으로 바뀌고 툴팁/메뉴에 요약
  (예: `⚠️ 문제 태블릿 2대: 우회 시도 1대 · 통신 끊김 1대`)을 보여준다. 새 문제는 Windows 알림으로 알리고
  (2초 안에 여러 건이면 한 장으로 묶음), 창이 열려 있으면 작업표시줄 버튼도 깜빡인다.
  - **경고 팝업**: 교실 관리 창을 보고 있지 않으면(트레이에 숨김·다른 창에 가려짐) 화면 우측 상단에
    항상 위 팝업을 띄운다([AlertPopup](Views/AlertPopup.cs)). 입력 포커스는 뺏지 않고, '확인'을 누를 때까지
    남으며 새 경고는 같은 팝업에 쌓인다. `--test-alert` 로 실행하면 3초 뒤 샘플 경고로 확인할 수 있다.
  - 문제 종류: 우회 시도, 통신 끊김, 접근성 꺼짐, 오버레이 꺼짐, 수업 중 연결 사라짐
  - 우회 시도는 항상 알리고, 나머지는 **수업 중에만** 알린다(수업 후 태블릿을 끄는 것까지 알리지 않도록).
    트레이 아이콘은 항상 현재 상태를 보여준다.
- **깜빡임**: 새 문제가 생기면 트레이 아이콘이 교사가 누를 때까지 깜빡인다(발표·화면 공유 중에는
  Windows 가 알림 팝업을 숨길 수 있어서). 아이콘을 누르면 창이 열리고, 열린 상태에서 다시 누르면 트레이로 숨는다.
- **작업표시줄에 표시**: Windows 11 은 새 트레이 아이콘을 숨김(^) 영역에 넣으므로, 처음 한 번 작업표시줄에 꺼내 둔다
  ([TrayPromotion](Services/TrayPromotion.cs)). 사용자가 다시 숨기면 그대로 둔다.
- **학적 수정 요청**: 창이 숨어 있어도 창을 띄워 승인/거절을 묻는다.

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
Program.cs                   진입점, 단일 인스턴스
App.axaml(.cs)               팔레트·스타일, 트레이 아이콘·경고 알림
Models/AgentStatus.cs        STATUS 응답 파싱
Services/SettingsService.cs  설정 저장 (%AppData%/ClassroomController/settings.json)
Services/AgentDiscovery.cs   mDNS 탐색 (Zeroconf)
Services/AgentClient.cs      WebSocket 명령 전송 (System.Net.WebSockets)
Services/KeepAwake.cs        화면 꺼짐/절전 방지 (Windows)
Services/Autostart.cs        자동 실행 등록 + 시작 메뉴 바로가기 (Windows)
ViewModels/                  MainWindowViewModel, DeviceViewModel
Views/                       MainWindow, ClassroomSetupWindow, ConfirmDialog
```

## 빌드 / 실행

.NET SDK 8 이상(10 도 가능)과 .NET 8 런타임이 필요하다.

```powershell
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

- ✅ **Teavel 리스타일 (Android a4bb9ad 대응)** — 그라데이션 헤더(indigo→violet→cyan), 그라데이션 기본 버튼 /
  아웃라인 보조 버튼, 테두리 카드 + 좌측 상태 띠·상태 점, 라벤더→민트 창 배경.
  팔레트와 버튼 스타일은 [App.axaml](App.axaml) 에 정의(`primary`, `outline`, `dangerText`, `onBrand`, `card`).
- ✅ **새 아이콘 (Android a89444f 대응)** — X자 쇠사슬 + 열쇠. Android `branding/icon-1024.png` 에서 생성.
- ✅ **에이전트 패키지명 갱신 (Android ef1f41c 대응)** — 기본 허용 앱 `com.teaveloper.classroomagent`.
- ✅ **화면 꺼짐 방지** — Android `FLAG_KEEP_SCREEN_ON` 대응. 트레이 상주 앱이라 **수업 중에만** 화면 끄기/절전 방지
  ([KeepAwake](Services/KeepAwake.cs)).

- ✅ **Teaveloper 라인 아이콘** — 컬러 이모지 대신 생기부 도우미와 같은 Feather 스타일 라인 아이콘
  ([LineIcon](Views/LineIcon.cs)). Windows 알림·트레이 문구는 글자만 쓴다.

## 남은 개선 여지

- 토스트 클릭 시 창 포커스 등 상호작용 (현재는 표시 전용, 트레이 아이콘 클릭으로 창 열기).
