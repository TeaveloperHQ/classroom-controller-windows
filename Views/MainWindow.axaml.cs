using System.Collections.Generic;
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

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoadedAsync;
    }

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;

    private async void OnLoadedAsync(object? sender, RoutedEventArgs e)
    {
        _notifications = new WindowNotificationManager(this)
        {
            Position = NotificationPosition.TopRight,
            MaxItems = 4,
        };

        if (Vm is null) return;

        Vm.AlertRaised += OnAlertRaised;
        Vm.EditRequestReceived += OnEditRequestReceived;
        Closing += (_, _) => Vm.Shutdown();

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

    private void OnAlertRaised(string title, string text)
    {
        // 앱 내 배너 + Windows 네이티브 토스트(가능한 경우).
        _notifications?.Show(new Notification(title, text, NotificationType.Warning));
        ClassroomController.Services.WindowsToast.Show(title, text);
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
