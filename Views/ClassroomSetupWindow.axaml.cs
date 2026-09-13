using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ClassroomController.Views;

public partial class ClassroomSetupWindow : Window
{
    public int? ResultGrade { get; private set; }
    public string? ResultClass { get; private set; }

    // 디자이너용 기본 생성자.
    public ClassroomSetupWindow() : this(1, "01", true) { }

    public ClassroomSetupWindow(int currentGrade, string currentClass, bool cancelable)
    {
        InitializeComponent();

        GradeBox.ItemsSource = new[] { "1", "2", "3" };
        GradeBox.SelectedIndex = Math.Clamp(currentGrade - 1, 0, 2);

        var classes = Enumerable.Range(1, 20).Select(i => i.ToString("D2")).ToArray();
        ClassBox.ItemsSource = classes;
        var idx = Array.IndexOf(classes, currentClass);
        ClassBox.SelectedIndex = idx >= 0 ? idx : 0;

        CancelButton.IsVisible = cancelable;
    }

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        ResultGrade = (GradeBox.SelectedIndex + 1);
        ResultClass = ClassBox.SelectedItem as string ?? "01";
        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
