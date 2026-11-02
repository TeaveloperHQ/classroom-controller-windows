using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ClassroomController.Views;

/// <summary>제목/메시지 + 확인·취소 버튼의 간단한 모달. bool 을 반환한다.</summary>
public static class ConfirmDialog
{
    public static Task<bool> ShowAsync(
        Window owner, string title, string message,
        string positiveText = "확인", string negativeText = "취소")
    {
        var positive = new Button
        {
            Content = positiveText,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(0, 10, 0, 10),
        };
        var negative = new Button
        {
            Content = negativeText,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(0, 10, 0, 10),
        };

        // 허락 = Teavel 그라데이션, 거절 = 빨간 텍스트 (Android 편집 요청 다이얼로그와 동일)
        positive.Classes.Add("primary");
        negative.Classes.Add("dangerText");

        var buttons = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 10,
            Margin = new Thickness(0, 8, 0, 0),
        };
        Grid.SetColumn(negative, 0);
        Grid.SetColumn(positive, 1);
        buttons.Children.Add(negative);
        buttons.Children.Add(positive);

        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 14 };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeight.Bold });
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(buttons);

        var dialog = new Window
        {
            Title = title,
            Width = 340,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel,
        };

        positive.Click += (_, _) => dialog.Close(true);
        negative.Click += (_, _) => dialog.Close(false);

        return dialog.ShowDialog<bool>(owner);
    }
}
