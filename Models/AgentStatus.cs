using System.Collections.Generic;
using System.Linq;

namespace ClassroomController.Models;

/// <summary>
/// 에이전트(학생 태블릿)의 STATUS 응답을 파싱한 결과.
/// 원본 응답 형식:
///   IN_SESSION|ACCESSIBILITY:true|OVERLAY:true|EDIT_REQUEST:false|SUSPICIOUS:none|VPN:false
/// 또는 IDLE|...
/// </summary>
public sealed class AgentStatus
{
    public bool Reachable { get; init; } = true;
    public bool InSession { get; init; }
    public bool Accessibility { get; init; }
    public bool Overlay { get; init; }
    public bool EditRequest { get; init; }
    public bool Vpn { get; init; }

    /// <summary>SUSPICIOUS 필드 원본 값 ("none" 이면 정상). "pkg:HH:mm" 형태.</summary>
    public string Suspicious { get; init; } = "none";
    public bool HasSuspicious => Suspicious != "none";

    /// <summary>통신이 끊긴 상태를 나타내는 표식.</summary>
    public static AgentStatus Unreachable() => new() { Reachable = false };

    public static AgentStatus Parse(string message)
    {
        var parts = message.Split('|');

        bool Flag(string key) =>
            parts.FirstOrDefault(p => p.StartsWith(key + ":"))?.EndsWith("true") == true;

        string suspicious = parts
            .FirstOrDefault(p => p.StartsWith("SUSPICIOUS:"))
            ?.Substring("SUSPICIOUS:".Length) ?? "none";

        return new AgentStatus
        {
            Reachable = true,
            InSession = parts.FirstOrDefault() == "IN_SESSION",
            Accessibility = Flag("ACCESSIBILITY"),
            Overlay = Flag("OVERLAY"),
            EditRequest = Flag("EDIT_REQUEST"),
            Vpn = Flag("VPN"),
            Suspicious = suspicious,
        };
    }

    /// <summary>차단된 앱 패키지명 (없으면 null).</summary>
    public string? SuspiciousPackage =>
        HasSuspicious ? Suspicious.Split(':').FirstOrDefault() : null;

    /// <summary>차단 시도 시각 "HH:mm" (없으면 빈 문자열).</summary>
    public string SuspiciousTime
    {
        get
        {
            if (!HasSuspicious) return string.Empty;
            var t = Suspicious.Split(':');
            return t.Length >= 3 ? $"{t[1]}:{t[2]}" : string.Empty;
        }
    }
}
