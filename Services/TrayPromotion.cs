using System;
using System.IO;

namespace ClassroomController.Services;

/// <summary>
/// Windows 11 은 새 트레이 아이콘을 숨김 영역(^)에 넣는다. 감시 앱의 경고 아이콘이 늘 보이도록
/// 처음 한 번 작업표시줄에 꺼내 둔다(HKCU\Control Panel\NotifyIconSettings\*\IsPromoted = 1).
/// 사용자가 이후 직접 숨기면(값이 0 으로 기록됨) 그 선택은 건드리지 않는다.
/// </summary>
public static class TrayPromotion
{
    private const string Key = @"Control Panel\NotifyIconSettings";

    /// <summary>아이콘을 꺼냈거나 이미 설정돼 있으면 true, 아직 Explorer 가 항목을 만들지 않았으면 false.</summary>
    public static bool TryPromote()
    {
        if (!OperatingSystem.IsWindows()) return true;
        var exe = Environment.ProcessPath;
        if (exe == null) return true;
        var fileName = Path.GetFileName(exe);

        try
        {
            using var root = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key);
            if (root == null) return true; // Windows 10: 해당 없음
            var found = false;
            foreach (var name in root.GetSubKeyNames())
            {
                using var sub = root.OpenSubKey(name, writable: true);
                if (sub?.GetValue("ExecutablePath") is not string path) continue;
                // 경로가 "{알려진폴더 GUID}\dotnet\dotnet.exe" 형태로 저장될 수 있어 파일 이름까지 함께 본다.
                var match = string.Equals(path, exe, StringComparison.OrdinalIgnoreCase)
                    || (path.StartsWith("{") && path.EndsWith("\\" + fileName, StringComparison.OrdinalIgnoreCase));
                if (!match) continue;
                found = true;
                if (sub.GetValue("IsPromoted") == null)
                    sub.SetValue("IsPromoted", 1, Microsoft.Win32.RegistryValueKind.DWord);
            }
            return found;
        }
        catch { return true; }
    }
}
