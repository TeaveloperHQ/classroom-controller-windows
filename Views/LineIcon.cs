using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace ClassroomController.Views;

/// <summary>
/// Teaveloper 브랜드 라인 아이콘 (24×24, 선 두께 2, 둥근 끝 — Feather 스타일).
/// 생기부 도우미 Gui/Icons.cs(SgbIcon)와 같은 방식. 컬러 이모지 대신 쓴다.
/// XAML: &lt;v:LineIcon Kind="settings" Stroke="White" Width="22" Height="22" /&gt;
/// </summary>
public class LineIcon : Decorator
{
    public static readonly StyledProperty<string?> KindProperty =
        AvaloniaProperty.Register<LineIcon, string?>(nameof(Kind));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<LineIcon, IBrush?>(nameof(Stroke), new SolidColorBrush(Color.Parse("#4F46E5")));

    public string? Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public IBrush? Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

    private static readonly Dictionary<string, string> Bodies = new()
    {
        ["grid"] = "<rect x='3' y='3' width='7' height='7' rx='1'/><rect x='14' y='3' width='7' height='7' rx='1'/><rect x='14' y='14' width='7' height='7' rx='1'/><rect x='3' y='14' width='7' height='7' rx='1'/>",
        ["settings"] = "<circle cx='12' cy='12' r='3'/><path d='M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-2 2 2 2 0 0 1-2-2v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1-2-2 2 2 0 0 1 2-2h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 2-2 2 2 0 0 1 2 2v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 2 2 2 2 0 0 1-2 2h-.09a1.65 1.65 0 0 0-1.51 1z'/>",
        ["home"] = "<path d='M3 9l9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z'/><path d='M9 22V12h6v10'/>",
        ["refresh"] = "<path d='M23 4v6h-6'/><path d='M1 20v-6h6'/><path d='M3.51 9a9 9 0 0 1 14.85-3.36L23 10'/><path d='M1 14l4.64 4.36A9 9 0 0 0 20.49 15'/>",
        ["arrow-left"] = "<path d='M19 12H5'/><path d='M12 19l-7-7 7-7'/>",
        ["check"] = "<path d='M20 6L9 17l-5-5'/>",
        ["check-circle"] = "<path d='M22 11.08V12a10 10 0 1 1-5.93-9.14'/><path d='M22 4L12 14.01l-3-3'/>",
        ["alert"] = "<path d='M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z'/><path d='M12 9v4'/><path d='M12 17h.01'/>",
        ["shield-alert"] = "<path d='M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z'/><path d='M12 8v4'/><path d='M12 16h.01'/>",
        ["wifi-off"] = "<path d='M1 1l22 22'/><path d='M16.72 11.06A10.94 10.94 0 0 1 19 12.55'/><path d='M5 12.55a10.94 10.94 0 0 1 5.17-2.39'/><path d='M10.71 5.05A16 16 0 0 1 22.58 9'/><path d='M1.42 9a15.91 15.91 0 0 1 4.7-2.88'/><path d='M8.53 16.11a6 6 0 0 1 6.95 0'/><path d='M12 20h.01'/>",
        ["pause-circle"] = "<circle cx='12' cy='12' r='10'/><path d='M10 15V9'/><path d='M14 15V9'/>",
        ["edit"] = "<path d='M12 20h9'/><path d='M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4 12.5-12.5z'/>",
    };

    private static readonly Regex PathRe = new(@"<path\s+d='([^']*)'", RegexOptions.Compiled);
    private static readonly Regex CircleRe = new(@"<circle\s+cx='([^']*)'\s+cy='([^']*)'\s+r='([^']*)'", RegexOptions.Compiled);
    private static readonly Regex RectRe = new(@"<rect\s+x='([^']*)'\s+y='([^']*)'\s+width='([^']*)'\s+height='([^']*)'(?:\s+rx='([^']*)')?", RegexOptions.Compiled);

    static LineIcon()
    {
        KindProperty.Changed.AddClassHandler<LineIcon>((i, _) => i.Rebuild());
        StrokeProperty.Changed.AddClassHandler<LineIcon>((i, _) => i.Rebuild());
    }

    public LineIcon()
    {
        Width = 18;
        Height = 18;
    }

    private static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    private void Rebuild()
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        if (Kind != null && Bodies.TryGetValue(Kind, out var body))
        {
            foreach (Match m in PathRe.Matches(body))
                canvas.Children.Add(Styled(new Path { Data = Geometry.Parse(m.Groups[1].Value) }));
            foreach (Match m in CircleRe.Matches(body))
            {
                double cx = D(m.Groups[1].Value), cy = D(m.Groups[2].Value), r = D(m.Groups[3].Value);
                var e = Styled(new Ellipse { Width = 2 * r, Height = 2 * r });
                Canvas.SetLeft(e, cx - r); Canvas.SetTop(e, cy - r);
                canvas.Children.Add(e);
            }
            foreach (Match m in RectRe.Matches(body))
            {
                double x = D(m.Groups[1].Value), y = D(m.Groups[2].Value), w = D(m.Groups[3].Value), h = D(m.Groups[4].Value);
                double rx = m.Groups[5].Success && m.Groups[5].Value.Length > 0 ? D(m.Groups[5].Value) : 0;
                var rc = Styled(new Rectangle { Width = w, Height = h, RadiusX = rx, RadiusY = rx });
                Canvas.SetLeft(rc, x); Canvas.SetTop(rc, y);
                canvas.Children.Add(rc);
            }
        }
        Child = new Viewbox { Child = canvas, Stretch = Stretch.Uniform };
    }

    private T Styled<T>(T shape) where T : Shape
    {
        shape.Stroke = Stroke;
        shape.StrokeThickness = 2;
        shape.StrokeLineCap = PenLineCap.Round;
        shape.StrokeJoin = PenLineJoin.Round;
        shape.Fill = null;
        return shape;
    }
}
