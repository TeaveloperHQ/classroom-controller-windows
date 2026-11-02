using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using ClassroomController.Models;
using ClassroomController.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClassroomController.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly SettingsService _settings;
    private readonly AgentDiscovery _discovery;
    private readonly DispatcherTimer _pollTimer;

    // ── 자동 종료 ──
    private const int CountdownSeconds = 5 * 60; // 5분
    private int _peakAgentCount;
    private bool _autoStopPhase;
    private DateTime _countdownStart;
    private readonly DispatcherTimer _countdownTimer;

    // 학적 수정 요청: 다이얼로그 대기중인 주소 (중복 방지)
    private readonly HashSet<string> _editPending = new();
    // 우회 알림 중복 방지: address -> 마지막 SUSPICIOUS 값
    private readonly Dictionary<string, string> _lastSuspicious = new();

    private bool _started;

    // 수업 중 목록에서 사라진 태블릿 이름. 다시 나타나거나 수업이 끝날 때까지 경고로 남긴다
    // (학생이 에이전트를 끄면 목록에서 빠지므로, 이게 없으면 트레이가 '문제 없음'으로 돌아간다).
    private readonly HashSet<string> _lostInSession = new();

    public ObservableCollection<DeviceViewModel> Devices { get; } = new();

    [ObservableProperty] private string _subtitle = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    [NotifyPropertyChangedFor(nameof(CanStop))]
    private bool _isInSession;

    public bool CanStart => !IsInSession;

    // 수업 중에만 화면 꺼짐/절전 방지 (트레이 상주 앱이라 항상 막지는 않는다).
    partial void OnIsInSessionChanged(bool value)
    {
        _lostInSession.Clear();
        IssuesChanged?.Invoke();
        if (value) KeepAwake.Enable(); else KeepAwake.Disable();
    }
    public bool CanStop => IsInSession;

    [ObservableProperty] private bool _autoStopVisible;
    [ObservableProperty] private string _autoStopText = "";
    [ObservableProperty] private int _autoStopProgress; // 0..300

    // ── 뷰(View)와의 상호작용 이벤트 ──
    /// <summary>학적 수정 요청 도착. (address, deviceName)</summary>
    public event Action<string, string>? EditRequestReceived;
    /// <summary>경보 알림. (title, text)</summary>
    public event Action<string, string>? AlertRaised;
    /// <summary>문제 있는 태블릿 집계가 바뀔 수 있음 (트레이 아이콘 갱신용).</summary>
    public event Action? IssuesChanged;

    /// <summary>현재 문제가 있는 태블릿 수.</summary>
    public int IssueCount => Devices.Count(d => d.Issue != DeviceIssue.None) + _lostInSession.Count;

    /// <summary>문제 요약. 예: "우회 시도 1대 · 통신 끊김 2대" (문제 없으면 빈 문자열).</summary>
    public string IssueSummary => string.Join(" · ", Devices
        .Where(d => d.Issue != DeviceIssue.None)
        .GroupBy(d => d.Issue)
        .OrderBy(g => g.Key)
        .Select(g => $"{DeviceViewModel.IssueLabel(g.Key)} {g.Count()}대")
        .Concat(_lostInSession.Count > 0 ? new[] { $"연결 사라짐 {_lostInSession.Count}대" } : Array.Empty<string>()));

    /// <summary>현재 학급 표시 (예: "1학년 1반 · 태블릿 3대").</summary>
    public string ClassroomLabel => Subtitle;

    public bool NeedsClassroomSetup => !_settings.FilterSet;
    public bool NeedsWizard => !_settings.WizardDone;

    /// <summary>마법사가 직접 읽고 쓰는 설정 인스턴스.</summary>
    public SettingsService Settings => _settings;

    public MainWindowViewModel()
    {
        _settings = SettingsService.Load();

        _discovery = new AgentDiscovery(() => _settings.FilterPrefix);
        _discovery.DeviceFound += OnDeviceFound;
        _discovery.DeviceLost += OnDeviceLost;

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _pollTimer.Tick += (_, _) => PollAllAgents();

        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += (_, _) => CountdownTick();

        IsInSession = _settings.IsInSession;
        UpdateSubtitle();
    }

    /// <summary>뷰 로드 후 호출. 학급 설정이 되어 있으면 탐색/폴링 시작.</summary>
    public void Start()
    {
        if (_started) return;
        _started = true;
        _pollTimer.Start();
        if (_settings.FilterSet)
            _discovery.Start();
    }

    public void Shutdown()
    {
        KeepAwake.Disable();
        _discovery.Stop();
        _pollTimer.Stop();
        _countdownTimer.Stop();
    }

    // ──────────────────────────────────────────────
    // 학급 설정
    // ──────────────────────────────────────────────

    public (int grade, string classNum) CurrentClassroom => (_settings.Grade, _settings.ClassNum);

    public void ApplyClassroom(int grade, string classNum)
    {
        _settings.Grade = grade;
        _settings.ClassNum = classNum;
        _settings.FilterSet = true;
        _settings.Save();

        // 목록 초기화 후 재탐색.
        Devices.Clear();
        _editPending.Clear();
        _lastSuspicious.Clear();
        _lostInSession.Clear();
        UpdateSubtitle();
        IssuesChanged?.Invoke();
        _discovery.Restart();
        OnPropertyChanged(nameof(NeedsClassroomSetup));
    }

    /// <summary>마법사 완료 후 호출: 설정 반영 + 탐색 시작.</summary>
    public void ReloadAfterWizard()
    {
        Devices.Clear();
        _editPending.Clear();
        _lastSuspicious.Clear();
        _lostInSession.Clear();
        UpdateSubtitle();
        IssuesChanged?.Invoke();
        OnPropertyChanged(nameof(NeedsClassroomSetup));
        OnPropertyChanged(nameof(NeedsWizard));
        if (_settings.FilterSet)
            _discovery.Restart();
    }

    // ──────────────────────────────────────────────
    // 허용 앱
    // ──────────────────────────────────────────────

    public IReadOnlyDictionary<string, string> CachedApps => _settings.CachedAppMap;
    public ISet<string> AllowedApps => _settings.AllowedApps;

    /// <summary>
    /// 현재 발견된 기기에서 앱 목록을 수집·병합하여 표시용 맵을 반환.
    /// 기기가 없으면 캐시를 사용하고, 캐시도 비어 있으면 null.
    /// </summary>
    public async System.Threading.Tasks.Task<IReadOnlyDictionary<string, string>?> CollectAppsAsync()
    {
        var addresses = Devices.Select(d => d.Address).ToList();

        if (addresses.Count == 0)
            return _settings.CachedAppMap.Count > 0 ? _settings.CachedAppMap : null;

        var collected = await AppCatalog.CollectAsync(addresses).ConfigureAwait(false);

        // 캐시에 병합 (줄어들지 않게) 후 저장.
        foreach (var (pkg, name) in collected)
            _settings.CachedAppMap[pkg] = name;
        _settings.Save();

        return _settings.CachedAppMap;
    }

    /// <summary>허용 앱을 저장하고 모든 기기에 전송.</summary>
    public void SetAllowedApps(HashSet<string> selected)
    {
        _settings.AllowedApps = selected;
        _settings.Save();
        var command = "SET_ALLOWED_APPS|" + string.Join("|", selected);
        foreach (var device in Devices.ToList())
            SendCommand(device.Address, command);
    }

    // ──────────────────────────────────────────────
    // 탐색 이벤트
    // ──────────────────────────────────────────────

    private void OnDeviceFound(string address, string name) => Dispatcher.UIThread.Post(() =>
    {
        if (Devices.Any(d => d.Address == address)) return;

        _lostInSession.Remove(name);
        Devices.Add(new DeviceViewModel(address, name));

        // 허용 앱 목록이 있으면 즉시 전달 (원본 동작).
        if (_settings.AllowedApps.Count > 0)
            SendCommand(address, "SET_ALLOWED_APPS|" + string.Join("|", _settings.AllowedApps));

        UpdateSubtitle();
        CheckPeakAndAutoStop();
        IssuesChanged?.Invoke();
    });

    private void OnDeviceLost(string address) => Dispatcher.UIThread.Post(() =>
    {
        var device = Devices.FirstOrDefault(d => d.Address == address);
        if (device != null)
        {
            Devices.Remove(device);
            // 수업 중 태블릿이 사라짐 = 에이전트 종료/와이파이 끊김 가능성.
            if (IsInSession)
            {
                _lostInSession.Add(device.Name);
                AlertRaised?.Invoke("태블릿 연결 사라짐", $"{device.Name}: 목록에서 사라졌습니다");
            }
        }
        _editPending.Remove(address);
        _lastSuspicious.Remove(address);
        UpdateSubtitle();
        CheckPeakAndAutoStop();
        IssuesChanged?.Invoke();
    });

    // ──────────────────────────────────────────────
    // 폴링
    // ──────────────────────────────────────────────

    private void PollAllAgents()
    {
        CheckPeakAndAutoStop();
        foreach (var device in Devices.ToList())
            _ = PollOneAsync(device);
    }

    private async System.Threading.Tasks.Task PollOneAsync(DeviceViewModel device)
    {
        var response = await AgentClient.QueryStatusAsync(device.Address).ConfigureAwait(false);

        Dispatcher.UIThread.Post(() =>
        {
            if (!Devices.Contains(device)) return;

            var previousIssue = device.Issue;

            if (response == null)
            {
                device.MarkUnreachable();
                AfterStatusApplied(device, previousIssue);
                return;
            }

            var status = AgentStatus.Parse(response);

            // 학적 수정 요청 큐잉.
            if (status.EditRequest && !_editPending.Contains(device.Address))
            {
                _editPending.Add(device.Address);
                EditRequestReceived?.Invoke(device.Address, device.Name);
            }

            // 세션 불일치 보정 (체크된 기기만).
            if (device.IsChecked)
            {
                if (IsInSession && !status.InSession) SendCommand(device.Address, "START");
                else if (!IsInSession && status.InSession) SendCommand(device.Address, "STOP");
            }

            // 우회 시도 알림 (기기별 중복 제거).
            if (status.HasSuspicious)
            {
                if (_lastSuspicious.GetValueOrDefault(device.Address) != status.Suspicious)
                {
                    _lastSuspicious[device.Address] = status.Suspicious;
                    var time = status.SuspiciousTime;
                    var text = string.IsNullOrEmpty(time)
                        ? $"{device.Name} 학생이 차단된 앱 사용을 시도했습니다"
                        : $"{device.Name} 학생이 차단된 앱 사용을 시도했습니다 ({time})";
                    AlertRaised?.Invoke("우회 시도 감지", text);
                }
            }
            else
            {
                _lastSuspicious.Remove(device.Address);
            }

            device.Apply(status, ResolveAppName);
            AfterStatusApplied(device, previousIssue);
        });
    }

    /// <summary>
    /// 상태 반영 후: 새로 생긴 문제를 알리고 정렬·트레이를 갱신한다.
    /// 우회 시도는 위에서 이미 알렸고, 통신 끊김/권한 꺼짐은 수업 중에만 알린다
    /// (수업이 끝나 태블릿을 끈 것까지 알리면 소음이 되므로). 트레이 아이콘은 항상 실제 상태를 보여준다.
    /// </summary>
    private void AfterStatusApplied(DeviceViewModel device, DeviceIssue previousIssue)
    {
        var issue = device.Issue;
        if (issue != previousIssue && issue != DeviceIssue.None
            && issue != DeviceIssue.Bypass && IsInSession)
        {
            AlertRaised?.Invoke("태블릿 문제",
                $"{device.Name}: {DeviceViewModel.IssueLabel(issue)}");
        }
        SortDevices();
        IssuesChanged?.Invoke();
    }

    private string ResolveAppName(string pkg) => _settings.CachedAppMap.GetValueOrDefault(pkg, pkg);

    // ──────────────────────────────────────────────
    // 명령
    // ──────────────────────────────────────────────

    [RelayCommand]
    private void StartSession() => SendToAll("START");

    [RelayCommand]
    private void StopSession() => SendToAll("STOP");

    private void SendToAll(string command)
    {
        // 자동 종료 상태 초기화.
        CancelAutoStopCountdown();
        _peakAgentCount = 0;

        if (Devices.Count == 0 && command == "START")
        {
            AlertRaised?.Invoke("알림", "발견된 태블릿이 없습니다");
            return;
        }

        IsInSession = command == "START";
        _settings.IsInSession = IsInSession;
        _settings.Save();

        foreach (var device in Devices.Where(d => d.IsChecked).ToList())
            SendCommand(device.Address, command);
    }

    private static void SendCommand(string address, string command) =>
        _ = AgentClient.SendAsync(address, command, expectResponse: false);

    // ──────────────────────────────────────────────
    // 학적 수정 요청 응답
    // ──────────────────────────────────────────────

    public void ApproveEdit(string address)
    {
        _editPending.Remove(address);
        SendCommand(address, "EDIT_APPROVED");
    }

    public void RejectEdit(string address)
    {
        _editPending.Remove(address);
        SendCommand(address, "EDIT_REJECTED");
    }

    // ──────────────────────────────────────────────
    // 자동 종료
    // ──────────────────────────────────────────────

    private void CheckPeakAndAutoStop()
    {
        if (!IsInSession) return;

        var current = Devices.Count;
        if (current >= _peakAgentCount)
        {
            if (current > _peakAgentCount) _peakAgentCount = current;
            if (_autoStopPhase) CancelAutoStopCountdown();
        }
        else if (!_autoStopPhase)
        {
            if (_peakAgentCount > 0 && current <= _peakAgentCount / 3)
                StartAutoStopCountdown();
        }
    }

    private void StartAutoStopCountdown()
    {
        _autoStopPhase = true;
        _countdownStart = DateTime.UtcNow;
        AutoStopVisible = true;
        AutoStopProgress = 0;
        AutoStopText = $"자동 종료 대기중 · 현재 {Devices.Count}대";
        _countdownTimer.Start();
        AlertRaised?.Invoke("자동 수업 종료 예정",
            "태블릿 1/3 이상 오프라인. 5분 후 수업이 자동 종료됩니다");
    }

    private void CancelAutoStopCountdown()
    {
        _autoStopPhase = false;
        _countdownTimer.Stop();
        AutoStopVisible = false;
    }

    private void CountdownTick()
    {
        var elapsed = (int)(DateTime.UtcNow - _countdownStart).TotalSeconds;
        if (elapsed >= CountdownSeconds)
        {
            ExecuteAutoStop();
            return;
        }
        AutoStopProgress = Math.Min(elapsed, CountdownSeconds);
        AutoStopText = $"자동 종료 대기중 · 현재 {Devices.Count}대";
    }

    private void ExecuteAutoStop()
    {
        _autoStopPhase = false;
        _peakAgentCount = 0;
        _countdownTimer.Stop();
        AutoStopVisible = false;

        foreach (var device in Devices.Where(d => d.IsChecked).ToList())
            SendCommand(device.Address, "STOP");

        IsInSession = false;
        _settings.IsInSession = false;
        _settings.Save();
        AlertRaised?.Invoke("수업 자동 종료", "수업이 자동 종료되었습니다");
    }

    // ──────────────────────────────────────────────
    // 정렬 / 표시
    // ──────────────────────────────────────────────

    private void SortDevices()
    {
        var sorted = Devices
            .OrderBy(d => d.SortPriority)
            .ThenBy(d => d.SortPriority == 5 ? d.Name : "", StringComparer.Ordinal)
            .ToList();

        for (int i = 0; i < sorted.Count; i++)
        {
            var current = Devices.IndexOf(sorted[i]);
            if (current != i) Devices.Move(current, i);
        }
    }

    private void UpdateSubtitle()
    {
        var classNumInt = _settings.ClassNum.TrimStart('0');
        if (string.IsNullOrEmpty(classNumInt)) classNumInt = "0";
        Subtitle = $"{_settings.Grade}학년 {classNumInt}반 · 태블릿 {Devices.Count}대";
        OnPropertyChanged(nameof(ClassroomLabel));
    }
}
