using System;
using System.Runtime.InteropServices;

namespace ClassroomController.Services;

/// <summary>
/// Android 의 FLAG_KEEP_SCREEN_ON 대응. 컨트롤러 창이 떠 있는 동안 화면 꺼짐/절전 진입을 막는다.
/// Windows 외 OS 에서는 아무것도 하지 않는다.
/// </summary>
public static class KeepAwake
{
    private const uint EsContinuous = 0x80000000;
    private const uint EsSystemRequired = 0x00000001;
    private const uint EsDisplayRequired = 0x00000002;

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint esFlags);

    public static void Enable()
    {
        if (!OperatingSystem.IsWindows()) return;
        SetThreadExecutionState(EsContinuous | EsSystemRequired | EsDisplayRequired);
    }

    public static void Disable()
    {
        if (!OperatingSystem.IsWindows()) return;
        SetThreadExecutionState(EsContinuous);
    }
}
