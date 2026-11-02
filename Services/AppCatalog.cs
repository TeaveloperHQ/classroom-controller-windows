using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ClassroomController.Services;

/// <summary>
/// 에이전트에서 설치 앱 목록(GET_APPS)을 수집·병합하는 공용 로직.
/// 마법사와 메인 화면의 "허용 앱 설정"이 공유한다.
/// </summary>
public static class AppCatalog
{
    /// <summary>기본 허용 패키지 (항상 체크 상태로 제안).</summary>
    public static readonly IReadOnlyList<string> DefaultPackages = new[]
    {
        "com.microsoft.office.onenote",
        "com.teaveloper.classroomagent",
        "com.android.systemui",
        "com.google.android.inputmethod.latin",
        "com.samsung.android.honeyboard",
        "com.android.launcher",
        "com.android.launcher2",
        "com.android.launcher3",
        "com.sec.android.app.launcher",
        "com.google.android.apps.nexuslauncher",
    };

    /// <summary>
    /// 무작위 최대 3대에서 GET_APPS 를 수집하여 공통(교집합) 앱 맵(pkg -> name)을 반환.
    /// 원본 컨트롤러 로직과 동일: 응답이 없으면 빈 맵.
    /// </summary>
    public static async Task<Dictionary<string, string>> CollectAsync(IEnumerable<string> addresses)
    {
        var selected = addresses.OrderBy(_ => System.Guid.NewGuid()).Take(3).ToList();
        if (selected.Count == 0) return new Dictionary<string, string>();

        var tasks = selected.Select(async addr =>
        {
            var resp = await AgentClient.SendAsync(addr, "GET_APPS", expectResponse: true)
                .ConfigureAwait(false);
            return ParseAppList(resp);
        });

        var responses = (await Task.WhenAll(tasks).ConfigureAwait(false))
            .Where(m => m.Count > 0)
            .ToList();

        if (responses.Count == 0) return new Dictionary<string, string>();

        // 모든 응답에 공통으로 존재하는 패키지만 채택.
        var commonPkgs = responses
            .Select(m => (IEnumerable<string>)m.Keys)
            .Aggregate((acc, keys) => acc.Intersect(keys).ToList());

        var result = new Dictionary<string, string>();
        foreach (var pkg in commonPkgs)
        {
            var name = responses.Select(m => m.GetValueOrDefault(pkg))
                .FirstOrDefault(n => !string.IsNullOrEmpty(n)) ?? pkg;
            result[pkg] = name;
        }
        return result;
    }

    /// <summary>"APP_LIST|앱이름:패키지|..." 를 pkg -> name 맵으로 파싱.</summary>
    private static Dictionary<string, string> ParseAppList(string? message)
    {
        var map = new Dictionary<string, string>();
        if (message == null || !message.StartsWith("APP_LIST")) return map;

        foreach (var entry in message.Split('|').Skip(1))
        {
            var idx = entry.IndexOf(':');
            if (idx > 0)
            {
                var name = entry[..idx];
                var pkg = entry[(idx + 1)..];
                map[pkg] = name;
            }
        }
        return map;
    }
}
