using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using ClassroomController.Services;

namespace ClassroomController.Views;

public partial class AppSelectionWindow : Window
{
    private readonly Dictionary<string, CheckBox> _checkBoxes = new();

    public HashSet<string>? SelectedPackages { get; private set; }

    // 디자이너용.
    public AppSelectionWindow() : this(new Dictionary<string, string>(), new HashSet<string>()) { }

    public AppSelectionWindow(IReadOnlyDictionary<string, string> appMap, ISet<string> currentAllowed)
    {
        InitializeComponent();
        BuildList(appMap, currentAllowed);
    }

    private void BuildList(IReadOnlyDictionary<string, string> appMap, ISet<string> currentAllowed)
    {
        AppList.Children.Clear();
        _checkBoxes.Clear();

        // 기본 패키지: 항상 표시, 기본 체크 (해제 가능).
        foreach (var pkg in AppCatalog.DefaultPackages)
        {
            var name = appMap.GetValueOrDefault(pkg, pkg);
            AddCheckBox(pkg, $"{name} (기본)", isChecked: true);
        }

        // 그 외 앱: 이름 정렬, 이전에 허용했던 것만 체크.
        foreach (var (pkg, name) in appMap
                     .Where(kv => !AppCatalog.DefaultPackages.Contains(kv.Key))
                     .OrderBy(kv => kv.Value, System.StringComparer.CurrentCulture))
        {
            AddCheckBox(pkg, name, isChecked: currentAllowed.Contains(pkg));
        }
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
        AppList.Children.Add(cb);
    }

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        SelectedPackages = _checkBoxes
            .Where(kv => kv.Value.IsChecked == true)
            .Select(kv => kv.Key)
            .ToHashSet();
        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
