using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace ClassroomController.Views;

/// <summary>
/// 태블릿 문제 경고 팝업. 다른 창(발표·화면 공유 등)에 가려지지 않도록 항상 위에 뜨고,
/// 교사가 '확인'을 누를 때까지 화면 우측 상단에 남는다.
/// 입력 포커스는 뺏지 않으므로 타이핑·발표 중에도 작업이 끊기지 않는다.
/// 새 경고가 오면 같은 팝업에 쌓인다.
/// </summary>
public sealed class AlertPopup : Window
{
    private const int MaxShown = 6;
    private const int ScreenMargin = 16;

    private readonly List<(DateTime at, string title, string text)> _alerts = new();
    private readonly StackPanel _list = new() { Spacing = 8 };
    private readonly TextBlock _header = new() { FontSize = 15, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>'확인' 또는 '창 열기'로 닫힘. 인자: 창 열기를 눌렀는지.</summary>
    public event Action<bool>? Dismissed;

    public AlertPopup()
    {
        Title = "교실 관리 경고";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        SystemDecorations = SystemDecorations.None;
        Background = Brushes.Transparent;

        var warn = Res("TeavelWarningBrush", Brushes.OrangeRed);

        var stripe = new Border { Background = warn, Width = 6 };

        var openBtn = new Button
        {
            Content = "창 열기",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(0, 8, 0, 8),
        };
        openBtn.Classes.Add("outline");
        var okBtn = new Button
        {
            Content = "확인",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(0, 8, 0, 8),
        };
        okBtn.Classes.Add("primary");
        openBtn.Click += (_, _) => Dismiss(openMain: true);
        okBtn.Click += (_, _) => Dismiss(openMain: false);

        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 10 };
        Grid.SetColumn(openBtn, 0);
        Grid.SetColumn(okBtn, 1);
        buttons.Children.Add(openBtn);
        buttons.Children.Add(okBtn);

        var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        headerRow.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = warn, VerticalAlignment = VerticalAlignment.Center });
        headerRow.Children.Add(_header);

        var body = new StackPanel { Margin = new Thickness(16, 14, 16, 14), Spacing = 12 };
        body.Children.Add(headerRow);
        body.Children.Add(_list);
        body.Children.Add(buttons);

        var row = new DockPanel();
        DockPanel.SetDock(stripe, Dock.Left);
        row.Children.Add(stripe);
        row.Children.Add(body);

        Content = new Border
        {
            Background = Res("TeavelSurfaceBrush", Brushes.White),
            BorderBrush = warn,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true,
            Child = row,
        };

        Opened += (_, _) => PlaceTopRight();
    }

    private static IBrush Res(string key, IBrush fallback) =>
        Application.Current?.TryGetResource(key, null, out var v) == true && v is IBrush b ? b : fallback;

    /// <summary>경고를 추가하고 팝업을 맨 앞에 띄운다.</summary>
    public void Add(string title, string text)
    {
        _alerts.Add((DateTime.Now, title, text));
        Rebuild();

        if (!IsVisible) Show();
        else
        {
            // 이미 떠 있으면 다른 '항상 위' 창들보다 다시 위로 올린다.
            Topmost = false;
            Topmost = true;
        }
    }

    private void Rebuild()
    {
        _header.Text = _alerts.Count == 1 ? "태블릿 경고" : $"태블릿 경고 {_alerts.Count}건";
        _list.Children.Clear();

        var muted = Res("TeavelInkMutedBrush", Brushes.Gray);
        var start = Math.Max(0, _alerts.Count - MaxShown);
        // 최근 것이 위로.
        for (var i = _alerts.Count - 1; i >= start; i--)
        {
            var (at, title, text) = _alerts[i];
            var item = new StackPanel { Spacing = 2 };
            var top = new DockPanel();
            var time = new TextBlock { Text = at.ToString("HH:mm"), Foreground = muted, FontSize = 12 };
            DockPanel.SetDock(time, Dock.Right);
            top.Children.Add(time);
            top.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold });
            item.Children.Add(top);
            item.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
            _list.Children.Add(item);
        }
        if (start > 0)
            _list.Children.Add(new TextBlock { Text = $"이전 경고 {start}건 더 있음", Foreground = muted, FontSize = 12 });
    }

    private void PlaceTopRight()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen == null) return;
        var wa = screen.WorkingArea;
        var w = (int)Math.Ceiling(Width * screen.Scaling);
        var m = (int)(ScreenMargin * screen.Scaling);
        Position = new PixelPoint(wa.Right - w - m, wa.Y + m);
    }

    private void Dismiss(bool openMain)
    {
        _alerts.Clear();
        Hide();
        Dismissed?.Invoke(openMain);
    }
}
