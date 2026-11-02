using System;
using System.IO;
using System.Reflection;

namespace ClassroomController.Services;

/// <summary>
/// Windows 시작 시 자동 실행(HKCU\...\Run, 관리자 권한 불필요) + 시작 메뉴 바로가기.
/// 수업 메모 도구(saenggibu-helper Memo)와 같은 방식. 비Windows 에서는 아무것도 하지 않는다.
/// </summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "ClassroomController";
    private const string ShortcutName = "교실 관리.lnk";

    /// <summary>
    /// 실제 배포 exe 경로. 개발 중(Debug 빌드, dotnet 교실컨트롤러.dll 실행)이면 null —
    /// bin\Debug 의 exe 를 자동시작/바로가기로 등록하지 않는다. (dotnet run 은 exe 를 직접 띄우므로 Debug 로 거른다)
    /// </summary>
    public static string? ExePath
    {
        get
        {
#if DEBUG
            return null;
#else
            var exe = Environment.ProcessPath;
            if (exe == null) return null;
            return Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                ? null : exe;
#endif
        }
    }

    public static bool IsSupported => OperatingSystem.IsWindows() && ExePath != null;

    public static bool IsRegistered()
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(AppName) != null;
    }

    /// <summary>자동시작 등록 (항상 현재 exe 경로로 덮어써서 옮겨진 exe 도 바로잡는다). 부팅 시엔 트레이에만 상주.</summary>
    public static void Register()
    {
        if (!OperatingSystem.IsWindows() || ExePath is not { } exe) return;
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
            k?.SetValue(AppName, $"\"{exe}\" --tray");
        }
        catch { /* 자동시작 실패는 치명적이지 않음 */ }
    }

    public static void Unregister()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            k?.DeleteValue(AppName, throwOnMissingValue: false);
        }
        catch { }
    }

    /// <summary>시작 메뉴 '교실 관리' 바로가기. 시작 메뉴에서 우클릭 → 작업 표시줄에 고정할 수 있다.</summary>
    public static void EnsureStartMenuShortcut()
    {
        if (!OperatingSystem.IsWindows() || ExePath is not { } exe) return;
        try
        {
            var lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ShortcutName);
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            var shell = Activator.CreateInstance(shellType)!;
            var sc = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk })!;
            void Set(string prop, string val) =>
                sc.GetType().InvokeMember(prop, BindingFlags.SetProperty, null, sc, new object[] { val });
            Set("TargetPath", exe);
            Set("WorkingDirectory", Path.GetDirectoryName(exe)!);
            Set("Description", "교실 관리 — 학생 태블릿 수업 모드 관리·감시");
            sc.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
        }
        catch { /* 바로가기 실패는 치명적이지 않음 */ }
    }
}
