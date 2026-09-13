using System;
using Avalonia;

namespace ClassroomController;

internal static class Program
{
    // 초기화 코드. Avalonia, 서드파티 API 사용 전에는 어떤 코드도 두지 말 것.
    // SynchronizationContext 관련 초기화보다 먼저 실행되어야 하므로 여기 둔다.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia 설정. 디자이너도 사용하므로 별도 메서드로 분리.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
