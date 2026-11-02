using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using ClassroomController.ViewModels;

namespace ClassroomController.Views;

public partial class MainWindow : Window
{
    private WindowNotificationManager? _notifications;

    // 학적 수정 요청 다이얼로그 직렬화 큐.
    private readonly Queue<(string address, string name)> _editQueue = new();
    private bool _editDialogActive;
    private bool _initialized;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoadedAsync;
    }

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        // 학적 수정 요청은 창이 숨어 있어도 받아야 하므로 로드 전에 구독한다.
        if (Vm is not null)
        {
            Vm.EditRequestReceived -= OnEditRequestReceived;
            Vm.EditRequestReceived += OnEditRequestReceived;
        }
    }

    private async void OnLoadedAsync(object? sender, RoutedEventArgs e)
    {
        // 트레이에서 다시 열 때마다 Loaded 가 올 수 있으므로 최초 1회만.
        if (_initialized) return;
        _initialized = true;

        _notifications = new WindowNotificationManager(this)
        {
            Position = NotificationPosition.TopRight,
            MaxItems = 4,
        };

        if (Vm is null) return;

        // 최초 실행 시 설정 마법사, 이후엔 학급 설정만 필요하면 표시.
        if (Vm.NeedsWizard)
        {
            var wizard = new SetupWizardWindow(Vm.Settings);
            await wizard.ShowDialog<bool>(this);
            Vm.ReloadAfterWizard();
        }
        else if (Vm.NeedsClassroomSetup)
        {
            await OpenClassroomSetupAsync(cancelable: false);
        }

        Vm.Start();
    }

    /// <summary>창이 떠 있으면 우측 상단 배너로도 표시 (토스트는 App 이 띄운다).</summary>
    public void ShowInAppAlert(string title, string text)
    {
        if (IsVisible)
            _notifications?.Show(new Notification(title, text, NotificationType.Warning));
    }

    // ── 작업표시줄 버튼 깜빡임 (창이 떠 있지만 다른 창을 보고 있을 때) ──

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

    private const uint FLASHW_ALL = 3;        // 제목줄 + 작업표시줄 버튼
    private const uint FLASHW_TIMERNOFG = 12; // 창이 앞으로 올 때까지 계속

    public void FlashTaskbar()
    {
        if (!OperatingSystem.IsWindows() || !IsVisible || IsActive) return;
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero) return;
        var info = new FLASHWINFO
        {
            cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
            hwnd = handle,
            dwFlags = FLASHW_ALL | FLASHW_TIMERNOFG,
        };
        FlashWindowEx(ref info);
    }

    private async void OnClassroomSettingsClick(object? sender, RoutedEventArgs e) =>
        await OpenClassroomSetupAsync(cancelable: true);

    private async void OnRerunWizardClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var wizard = new SetupWizardWindow(Vm.Settings);
        await wizard.ShowDialog<bool>(this);
        Vm.ReloadAfterWizard();
    }

    private async void OnAppSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;

        var appMap = await Vm.CollectAppsAsync();
        if (appMap is null)
        {
            _notifications?.Show(new Notification(
                "허용 앱 설정", "발견된 태블릿이 없고 저장된 앱 목록도 없습니다",
                NotificationType.Information));
            return;
        }

        var dialog = new AppSelectionWindow(appMap, Vm.AllowedApps);
        var ok = await dialog.ShowDialog<bool>(this);
        if (ok && dialog.SelectedPackages is { } selected)
            Vm.SetAllowedApps(selected);
    }

    private async System.Threading.Tasks.Task OpenClassroomSetupAsync(bool cancelable)
    {
        if (Vm is null) return;
        var (grade, classNum) = Vm.CurrentClassroom;
        var dialog = new ClassroomSetupWindow(grade, classNum, cancelable);
        var ok = await dialog.ShowDialog<bool>(this);
        if (ok && dialog.ResultGrade is int g && dialog.ResultClass is string c)
            Vm.ApplyClassroom(g, c);
    }

    // ── 학적 수정 요청 처리 ──

    private void OnEditRequestReceived(string address, string name)
    {
        // 교사 응답이 필요하므로 숨어 있으면 창을 띄운다.
        (Avalonia.Application.Current as App)?.ShowMainWindow();
        _editQueue.Enqueue((address, name));
        if (!_editDialogActive)
            _ = ShowNextEditRequestAsync();
    }

    private async System.Threading.Tasks.Task ShowNextEditRequestAsync()
    {
        if (_editQueue.Count == 0) { _editDialogActive = false; return; }
        _editDialogActive = true;

        var (address, name) = _editQueue.Dequeue();
        var approved = await ConfirmDialog.ShowAsync(
            this, "학적 수정 요청", $"{name} 학적 수정 요청", "허락", "거절");

        if (Vm is not null)
        {
            if (approved) Vm.ApproveEdit(address);
            else Vm.RejectEdit(address);
        }

        await ShowNextEditRequestAsync();
    }
}
