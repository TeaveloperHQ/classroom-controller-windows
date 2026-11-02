using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using ClassroomController.Services;
using ClassroomController.ViewModels;
using ClassroomController.Views;

namespace ClassroomController;

/// <summary>
/// 트레이 상주 앱. 창을 닫아도 감시는 계속되고, 트레이 메뉴의 '종료'로만 끝난다.
/// 태블릿에 문제가 생기면 트레이 아이콘을 경고 아이콘으로 바꾸고 알림을 띄운다.
/// </summary>
public partial class App : Application
{
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private MainWindowViewModel _vm = null!;
    private MainWindow _window = null!;
    private TrayIcon? _tray;
    private NativeMenuItem _statusItem = null!;
    private NativeMenuItem _startItem = null!;
    private NativeMenuItem _stopItem = null!;
    private NativeMenuItem _autostartItem = null!;
    private WindowIcon? _iconNormal, _iconWarn;
    private bool _exiting;

    // 새 문제 발생 시 트레이 아이콘 깜빡임 (교사가 아이콘을 눌러 확인할 때까지).
    private DispatcherTimer? _blinkTimer;
    private bool _blinkOn;

    // 알림 묶기: 짧은 시간에 여러 태블릿 문제가 오면 한 번에 알린다.
    private readonly List<(string title, string text)> _pendingAlerts = new();
    private DispatcherTimer? _alertFlushTimer;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _vm = new MainWindowViewModel();
            _window = new MainWindow { DataContext = _vm };
            _window.Closing += OnWindowClosing;

            _vm.AlertRaised += OnAlertRaised;
            _vm.IssuesChanged += UpdateTray;
            _vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(MainWindowViewModel.IsInSession)
                    or nameof(MainWindowViewModel.ClassroomLabel))
                    UpdateTray();
            };

            SetupTray();
            SetupAutostart();
            ListenForShowRequests();

            // 설정이 끝나 있으면 창 없이도 바로 감시 시작.
            var setupDone = !_vm.NeedsWizard && !_vm.NeedsClassroomSetup;
            if (setupDone) _vm.Start();

            // 자동시작(--tray)은 트레이에만 상주. 직접 실행했거나 아직 설정 전이면 창을 연다.
            var trayOnly = desktop.Args?.Contains("--tray") == true;
            if (!trayOnly || !setupDone)
                Dispatcher.UIThread.Post(ShowMainWindow);
        }

        base.OnFrameworkInitializationCompleted();
    }

    // ──────────────────────────────────────────────
    // 창
    // ──────────────────────────────────────────────

    public void ShowMainWindow()
    {
        StopBlink();
        if (!_window.IsVisible) _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    /// <summary>트레이 아이콘 클릭: 창이 떠 있으면 트레이로 숨기고, 숨어 있으면 띄운다.</summary>
    private void ToggleMainWindow()
    {
        // 경고 중(깜빡임)에 누르면 확인하려는 것이므로 항상 띄운다.
        var wasBlinking = _blinkTimer?.IsEnabled == true;
        if (!wasBlinking && _window.IsVisible && _window.WindowState != WindowState.Minimized)
            _window.Hide();
        else
            ShowMainWindow();
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_exiting) return;
        // 닫기 = 트레이로 숨기기. 감시는 계속된다.
        e.Cancel = true;
        _window.Hide();
    }

    private async void ExitApp()
    {
        if (_vm.IsInSession)
        {
            ShowMainWindow();
            var ok = await ConfirmDialog.ShowAsync(_window, "교실 관리 종료",
                "수업 중입니다. 종료하면 태블릿 감시와 알림이 멈춥니다.\n(태블릿의 수업 모드는 그대로 유지됩니다)",
                "종료", "취소");
            if (!ok) return;
        }

        _exiting = true;
        _vm.Shutdown();
        if (_tray != null)
        {
            _tray.IsVisible = false;
            _tray.Dispose();
        }
        _desktop?.Shutdown();
    }

    /// <summary>두 번째 실행(작업표시줄 고정 아이콘 클릭 등)이 보내는 '창 열기' 요청을 받는다.</summary>
    private void ListenForShowRequests()
    {
        if (!OperatingSystem.IsWindows()) return;
        var ev = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowEventName);
        new Thread(() =>
        {
            while (ev.WaitOne()) Dispatcher.UIThread.Post(ShowMainWindow);
        })
        { IsBackground = true, Name = "show-request" }.Start();
    }

    // ──────────────────────────────────────────────
    // 트레이
    // ──────────────────────────────────────────────

    private void SetupTray()
    {
        _iconNormal = LoadIcon("tray.png");
        _iconWarn = LoadIcon("tray_warn.png");

        _statusItem = new NativeMenuItem("") { IsEnabled = false };
        var open = new NativeMenuItem("교실 관리 열기");
        open.Click += (_, _) => ShowMainWindow();
        _startItem = new NativeMenuItem("수업 시작");
        _startItem.Click += (_, _) => _vm.StartSessionCommand.Execute(null);
        _stopItem = new NativeMenuItem("수업 종료");
        _stopItem.Click += (_, _) => _vm.StopSessionCommand.Execute(null);
        _autostartItem = new NativeMenuItem("Windows 시작 시 자동 실행")
        {
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsVisible = Autostart.IsSupported,
        };
        _autostartItem.Click += (_, _) => ToggleAutostart();
        var quit = new NativeMenuItem("종료");
        quit.Click += (_, _) => ExitApp();

        _tray = new TrayIcon
        {
            Icon = _iconNormal,
            Menu = new NativeMenu
            {
                Items =
                {
                    _statusItem,
                    new NativeMenuItemSeparator(),
                    open, _startItem, _stopItem,
                    new NativeMenuItemSeparator(),
                    _autostartItem, quit,
                },
            },
        };
        _tray.Clicked += (_, _) => ToggleMainWindow();
        TrayIcon.SetIcons(this, new TrayIcons { _tray });
        UpdateTray();

        // Explorer 가 아이콘 항목을 만든 뒤에 꺼낼 수 있으므로 몇 번 재시도.
        var tries = 0;
        var promote = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        promote.Tick += (_, _) =>
        {
            if (TrayPromotion.TryPromote() || ++tries >= 15) promote.Stop();
        };
        promote.Start();
    }

    private static WindowIcon? LoadIcon(string name)
    {
        try { return new WindowIcon(AssetLoader.Open(new Uri($"avares://교실컨트롤러/Assets/{name}"))); }
        catch { return null; }
    }

    /// <summary>문제 집계에 맞춰 트레이 아이콘·툴팁·메뉴를 갱신.</summary>
    private void UpdateTray()
    {
        if (_tray == null) return;
        var count = _vm.IssueCount;
        var session = _vm.IsInSession ? "수업중" : "대기중";

        if (_blinkTimer?.IsEnabled != true)
            _tray.Icon = count > 0 ? _iconWarn : _iconNormal;
        var status = count > 0
            ? $"문제 태블릿 {count}대: {_vm.IssueSummary}"
            : "문제 없음";
        // 트레이 툴팁은 Windows 에서 최대 127자.
        var tip = $"교실 관리 · {session}\n{_vm.ClassroomLabel}\n{status}";
        _tray.ToolTipText = tip.Length > 127 ? tip[..126] + "…" : tip;

        _statusItem.Header = $"{status}  ({session})";
        _startItem.IsEnabled = _vm.CanStart;
        _stopItem.IsEnabled = _vm.CanStop;
    }

    // ──────────────────────────────────────────────
    // 자동 실행
    // ──────────────────────────────────────────────

    private void SetupAutostart()
    {
        if (!Autostart.IsSupported) return;
        Autostart.EnsureStartMenuShortcut();
        if (!_vm.Settings.AutostartDisabled) Autostart.Register();
        _autostartItem.IsChecked = Autostart.IsRegistered();
    }

    private void ToggleAutostart()
    {
        if (Autostart.IsRegistered())
        {
            Autostart.Unregister();
            _vm.Settings.AutostartDisabled = true;
        }
        else
        {
            Autostart.Register();
            _vm.Settings.AutostartDisabled = false;
        }
        _vm.Settings.Save();
        _autostartItem.IsChecked = Autostart.IsRegistered();
    }

    // ──────────────────────────────────────────────
    // 경고 알림
    // ──────────────────────────────────────────────

    private void StartBlink()
    {
        if (_blinkTimer == null)
        {
            _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _blinkTimer.Tick += (_, _) =>
            {
                _blinkOn = !_blinkOn;
                if (_tray != null) _tray.Icon = _blinkOn ? _iconWarn : _iconNormal;
            };
        }
        if (!_blinkTimer.IsEnabled) _blinkTimer.Start();
    }

    private void StopBlink()
    {
        if (_blinkTimer?.IsEnabled != true) return;
        _blinkTimer.Stop();
        UpdateTray();
    }

    private void OnAlertRaised(string title, string text)
    {
        // 창을 보고 있지 않을 때만 깜빡인다 (창이 앞에 있으면 이미 보고 있는 것).
        if (!_window.IsActive) StartBlink();
        _window.ShowInAppAlert(title, text);
        _window.FlashTaskbar();

        _pendingAlerts.Add((title, text));
        if (_alertFlushTimer == null)
        {
            _alertFlushTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _alertFlushTimer.Tick += (_, _) => FlushAlerts();
        }
        if (!_alertFlushTimer.IsEnabled) _alertFlushTimer.Start();
    }

    /// <summary>모인 알림을 Windows 토스트로 보낸다 (여러 건이면 한 장으로 묶음).</summary>
    private void FlushAlerts()
    {
        _alertFlushTimer?.Stop();
        if (_pendingAlerts.Count == 0) return;

        if (_pendingAlerts.Count == 1)
        {
            var (title, text) = _pendingAlerts[0];
            WindowsToast.Show(title, text);
        }
        else
        {
            var lines = _pendingAlerts.Take(4).Select(a => a.text).ToList();
            if (_pendingAlerts.Count > 4) lines.Add($"외 {_pendingAlerts.Count - 4}건");
            WindowsToast.Show($"태블릿 알림 {_pendingAlerts.Count}건", string.Join("\n", lines));
        }
        _pendingAlerts.Clear();
    }
}
