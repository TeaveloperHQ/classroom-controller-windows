using System;
using System.Threading;
using Avalonia;

namespace ClassroomController;

internal static class Program
{
    /// <summary>이미 떠 있는 인스턴스에 '창 열기'를 요청하는 이벤트 이름.</summary>
    public const string ShowEventName = @"Local\ClassroomController.Show";

    // 초기화 코드. Avalonia, 서드파티 API 사용 전에는 어떤 코드도 두지 말 것.
    [STAThread]
    public static void Main(string[] args)
    {
        // 한 번만 상주: 이미 떠 있으면(작업표시줄 고정 아이콘·시작 메뉴 실행 등)
        // 기존 인스턴스에 창을 열라고 알리고 바로 끝낸다. (수업 메모 도구와 같은 구조)
        Mutex? single = null;
        if (OperatingSystem.IsWindows())
        {
            single = new Mutex(true, @"Local\ClassroomController", out bool first);
            if (!first)
            {
                if (Array.IndexOf(args, "--tray") < 0)
                    try { using var ev = EventWaitHandle.OpenExisting(ShowEventName); ev.Set(); } catch { }
                return;
            }
        }
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
        }
        finally { single?.Dispose(); }
    }

    // Avalonia 설정. 디자이너도 사용하므로 별도 메서드로 분리.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
