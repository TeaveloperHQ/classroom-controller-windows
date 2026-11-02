using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ClassroomController.Services;

/// <summary>
/// Android SharedPreferences("classroom") 대응. %AppData%/ClassroomController/settings.json 에 저장.
/// </summary>
public sealed class SettingsService
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClassroomController");

    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public int Grade { get; set; } = 1;
    public string ClassNum { get; set; } = "01";
    public bool FilterSet { get; set; }
    public bool WizardDone { get; set; }
    public bool IsInSession { get; set; }

    /// <summary>사용자가 트레이 메뉴에서 자동 실행을 끈 경우 true (다시 등록하지 않음).</summary>
    public bool AutostartDisabled { get; set; }

    /// <summary>허용 앱 패키지명 목록.</summary>
    public HashSet<string> AllowedApps { get; set; } = new();

    /// <summary>packageName -> appName 캐시.</summary>
    public Dictionary<string, string> CachedAppMap { get; set; } = new();

    /// <summary>탐색 필터 접두어 = "{grade}{classNum}".</summary>
    public string FilterPrefix => $"{Grade}{ClassNum}";

    public static SettingsService Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<SettingsService>(json);
                if (loaded != null) return loaded;
            }
        }
        catch
        {
            // 손상된 설정은 무시하고 기본값으로.
        }
        return new SettingsService();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts));
        }
        catch
        {
            // 저장 실패는 조용히 무시 (권한/디스크 문제).
        }
    }
}
