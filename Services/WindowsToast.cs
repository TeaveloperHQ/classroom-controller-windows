using System;
using System.Diagnostics;

namespace ClassroomController.Services;

/// <summary>
/// Windows 네이티브 토스트 알림. 컴파일 타임 Windows 의존성 없이(리눅스 빌드 유지),
/// 런타임에 Windows 에서만 PowerShell 로 WinRT 토스트를 띄운다. 그 외 OS 에서는 무시된다.
/// </summary>
public static class WindowsToast
{
    // PowerShell 의 잘 알려진 AUMID — 별도 등록 없이 토스트를 표시할 수 있다.
    private const string AppId =
        "{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\\WindowsPowerShell\\v1.0\\powershell.exe";

    private const string Script = @"
$ErrorActionPreference = 'Stop'
[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType=WindowsRuntime] | Out-Null
$t = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02)
$nodes = $t.GetElementsByTagName('text')
$nodes.Item(0).AppendChild($t.CreateTextNode($env:TOAST_TITLE)) | Out-Null
$nodes.Item(1).AppendChild($t.CreateTextNode($env:TOAST_MSG)) | Out-Null
$toast = [Windows.UI.Notifications.ToastNotification]::new($t)
[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($env:TOAST_APPID).Show($toast)
";

    public static void Show(string title, string message)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -WindowStyle Hidden -Command -",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
            };
            // 값은 환경변수로 전달 → 따옴표/이스케이프 문제 및 주입 방지.
            psi.Environment["TOAST_TITLE"] = title;
            psi.Environment["TOAST_MSG"] = message;
            psi.Environment["TOAST_APPID"] = AppId;

            var process = Process.Start(psi);
            if (process == null) return;
            process.StandardInput.Write(Script);
            process.StandardInput.Close();
        }
        catch
        {
            // 토스트 실패는 무시 (앱 내 알림이 대체 역할).
        }
    }
}
