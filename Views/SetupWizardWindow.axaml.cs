using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using ClassroomController.Services;

namespace ClassroomController.Views;

public partial class SetupWizardWindow : Window
{
    private static readonly string[] StepTitles =
    {
        "교실 컨트롤러 설정", "학급 설정", "태블릿 탐색", "허용 앱 설정", "설정 완료",
    };

    private readonly SettingsService _settings;
    private readonly AgentDiscovery _discovery;

    // address -> deviceName (이 마법사 세션에서 발견한 기기)
    private readonly Dictionary<string, string> _discovered = new();
    private readonly Dictionary<string, CheckBox> _checkBoxes = new();

    private Control[] _steps = Array.Empty<Control>();
    private int _currentStep;

    // 디자이너용.
    public SetupWizardWindow() : this(new SettingsService()) { }

    public SetupWizardWindow(SettingsService settings)
    {
        _settings = settings;
        InitializeComponent();

        _steps = new Control[] { Step0, Step1, Step2, Step3, Step4 };

        GradeBox.ItemsSource = new[] { "1", "2", "3" };
        GradeBox.SelectedIndex = Math.Clamp(_settings.Grade - 1, 0, 2);
        var classes = Enumerable.Range(1, 20).Select(i => i.ToString("D2")).ToArray();
        ClassBox.ItemsSource = classes;
        var idx = Array.IndexOf(classes, _settings.ClassNum);
        ClassBox.SelectedIndex = idx >= 0 ? idx : 0;

        _discovery = new AgentDiscovery(() => _settings.FilterPrefix);
        _discovery.DeviceFound += OnDeviceFound;
        _discovery.DeviceLost += OnDeviceLost;

        Closing += (_, _) => _discovery.Stop();

        GoToStep(0);
    }

    // ──────────────────────────────────────────────
    // 단계 이동
    // ──────────────────────────────────────────────

    private void GoToStep(int step)
    {
        var wasDiscovery = _currentStep == 2;
        var nowDiscovery = step == 2;
        if (!wasDiscovery && nowDiscovery) StartDiscovery();
        if (wasDiscovery && !nowDiscovery) _discovery.Stop();

        _currentStep = step;
        for (int i = 0; i < _steps.Length; i++)
            _steps[i].IsVisible = i == step;

        TitleText.Text = StepTitles[step];
        StepText.Text = $"{step + 1}/5";
        BackButton.IsVisible = step > 0;
    }

    private void OnBack(object? sender, RoutedEventArgs e)
    {
        if (_currentStep > 0) GoToStep(_currentStep - 1);
    }

    private void OnStartWizard(object? sender, RoutedEventArgs e) => GoToStep(1);

    // ── Step 1: 학급 ──

    private void OnStep1Next(object? sender, RoutedEventArgs e)
    {
        _settings.Grade = GradeBox.SelectedIndex + 1;
        _settings.ClassNum = ClassBox.SelectedItem as string ?? "01";
        _settings.FilterSet = true;
        _settings.Save();
        GoToStep(2);
    }

    // ── Step 2: 탐색 ──

    private void StartDiscovery()
    {
        _discovered.Clear();
        UpdateDiscoveryCount();
        Step2Next.IsEnabled = false;
        _discovery.Restart();
    }

    private void OnRetryDiscovery(object? sender, RoutedEventArgs e) => StartDiscovery();

    private void OnStep2Next(object? sender, RoutedEventArgs e)
    {
        _discovery.Stop();
        GoToStep(3);
    }

    private void OnDeviceFound(string address, string name) => Dispatcher.UIThread.Post(() =>
    {
        _discovered[address] = name;
        UpdateDiscoveryCount();
        Step2Next.IsEnabled = _discovered.Count > 0;
    });

    private void OnDeviceLost(string address) => Dispatcher.UIThread.Post(() =>
    {
        _discovered.Remove(address);
        UpdateDiscoveryCount();
        Step2Next.IsEnabled = _discovered.Count > 0;
    });

    private void UpdateDiscoveryCount() => DiscoveryCount.Text = $"{_discovered.Count}대 발견됨";

    // ── Step 3: 허용 앱 ──

    private async void OnAnalyze(object? sender, RoutedEventArgs e)
    {
        AnalyzeButton.IsEnabled = false;
        AnalyzeButton.Content = "분석 중...";

        var collected = await AppCatalog.CollectAsync(_discovered.Keys.ToList());
        foreach (var (pkg, name) in collected)
            _settings.CachedAppMap[pkg] = name;
        _settings.Save();

        BuildAppChecklist(_settings.CachedAppMap);

        AnalyzeButton.IsVisible = false;
        AppListBorder.IsVisible = true;
        Step3Next.IsVisible = true;
        Step3Desc.Text = "수업 중 학생이 사용할 수 있는 앱을 선택하세요. (기본) 앱은 정상 동작에 필요합니다.";
    }

    private void BuildAppChecklist(IReadOnlyDictionary<string, string> appMap)
    {
        WizardAppList.Children.Clear();
        _checkBoxes.Clear();

        foreach (var pkg in AppCatalog.DefaultPackages)
            AddCheckBox(pkg, $"{appMap.GetValueOrDefault(pkg, pkg)} (기본)", true);

        foreach (var (pkg, name) in appMap
                     .Where(kv => !AppCatalog.DefaultPackages.Contains(kv.Key))
                     .OrderBy(kv => kv.Value, StringComparer.CurrentCulture))
            AddCheckBox(pkg, name, _settings.AllowedApps.Contains(pkg));
    }

    private void AddCheckBox(string pkg, string label, bool isChecked)
    {
        var cb = new CheckBox
        {
            Content = label,
            IsChecked = isChecked,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        _checkBoxes[pkg] = cb;
        WizardAppList.Children.Add(cb);
    }

    private void OnStep3Next(object? sender, RoutedEventArgs e)
    {
        _settings.AllowedApps = _checkBoxes
            .Where(kv => kv.Value.IsChecked == true)
            .Select(kv => kv.Key)
            .ToHashSet();
        _settings.Save();

        SummaryText.Text =
            $"{_settings.Grade}학년 {_settings.ClassNum.TrimStart('0')}반 · " +
            $"태블릿 {_discovered.Count}대 · 허용 앱 {_settings.AllowedApps.Count}개";

        GoToStep(4);
    }

    // ── Step 4: 완료 ──

    private void OnFinish(object? sender, RoutedEventArgs e)
    {
        if (_settings.AllowedApps.Count > 0)
        {
            var command = "SET_ALLOWED_APPS|" + string.Join("|", _settings.AllowedApps);
            foreach (var address in _discovered.Keys)
                _ = AgentClient.SendAsync(address, command, expectResponse: false);
        }

        _settings.WizardDone = true;
        _settings.Save();

        _discovery.Stop();
        Close(true);
    }
}
