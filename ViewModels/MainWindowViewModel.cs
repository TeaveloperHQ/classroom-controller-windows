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

    public ObservableCollection<DeviceViewModel> Devices { get; } = new();

    [ObservableProperty] private string _subtitle = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    [NotifyPropertyChangedFor(nameof(CanStop))]
    private bool _isInSession;

    public bool CanStart => !IsInSession;
    public bool CanStop => IsInSession;

    [ObservableProperty] private bool _autoStopVisible;
    [ObservableProperty] private string _autoStopText = "";
    [ObservableProperty] private int _autoStopProgress; // 0..300

    // ── 뷰(View)와의 상호작용 이벤트 ──
    /// <summary>학적 수정 요청 도착. (address, deviceName)</summary>
    public event Action<string, string>? EditRequestReceived;
    /// <summary>경보 알림. (title, text)</summary>
    public event Action<string, string>? AlertRaised;

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
        UpdateSubtitle();
        _discovery.Restart();
        OnPropertyChanged(nameof(NeedsClassroomSetup));
    }

    /// <summary>마법사 완료 후 호출: 설정 반영 + 탐색 시작.</summary>
    public void ReloadAfterWizard()
    {
        Devices.Clear();
        _editPending.Clear();
        _lastSuspicious.Clear();
        UpdateSubtitle();
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

        Devices.Add(new DeviceViewModel(address, name));

        // 허용 앱 목록이 있으면 즉시 전달 (원본 동작).
        if (_settings.AllowedApps.Count > 0)
            SendCommand(address, "SET_ALLOWED_APPS|" + string.Join("|", _settings.AllowedApps));

        UpdateSubtitle();
        CheckPeakAndAutoStop();
    });

    private void OnDeviceLost(string address) => Dispatcher.UIThread.Post(() =>
    {
        var device = Devices.FirstOrDefault(d => d.Address == address);
        if (device != null) Devices.Remove(device);
        _editPending.Remove(address);
        _lastSuspicious.Remove(address);
        UpdateSubtitle();
        CheckPeakAndAutoStop();
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

            if (response == null)
            {
                device.MarkUnreachable();
                SortDevices();
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
                    AlertRaised?.Invoke("⚠️ 우회 시도 감지", text);
                }
            }
            else
            {
                _lastSuspicious.Remove(device.Address);
            }

            device.Apply(status, ResolveAppName);
            SortDevices();
        });
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
        AlertRaised?.Invoke("⏳ 자동 수업 종료 예정",
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
        AlertRaised?.Invoke("🔓 수업 종료", "수업이 자동 종료되었습니다");
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
    }
}
