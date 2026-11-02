using System;
using Avalonia.Media;
using ClassroomController.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassroomController.ViewModels;

/// <summary>태블릿 에이전트의 문제 종류. 트레이 경고에 쓰인다.</summary>
public enum DeviceIssue
{
    None,
    Bypass,          // 차단 앱 우회 시도
    Unreachable,     // 통신 끊김
    Accessibility,   // 접근성 권한 꺼짐
    Overlay,         // 오버레이 권한 꺼짐
}

/// <summary>기기 목록의 한 행. 상태 문자열/색상/정렬 우선순위는 원본 컨트롤러 로직을 따른다.</summary>
public sealed partial class DeviceViewModel : ObservableObject
{
    // Teavel 팔레트 (Android statusColorFor 와 동일)
    private const string Violet = "#8B5CF6";   // 우회 시도
    private const string Danger = "#E11D48";   // 통신 끊김
    private const string Warning = "#F97316";  // 접근성/오버레이 경고
    private const string Offline = "#94A3B8";  // 대기중 / 연결 확인중
    private const string Success = "#10B981";  // 수업중 정상

    public string Address { get; }

    [ObservableProperty] private string _name;

    /// <summary>체크 = 명령 대상에 포함 (기본 true).</summary>
    [ObservableProperty] private bool _isChecked = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameWithBadge))]
    private bool _editRequest;

    [ObservableProperty] private string _statusText = "연결 확인중...";

    /// <summary>상태 라인 아이콘 이름 (Views/LineIcon 의 Kind).</summary>
    [ObservableProperty] private string _statusIcon = "pause-circle";
    [ObservableProperty] private IBrush _statusBrush = new SolidColorBrush(Color.Parse(Offline));

    /// <summary>정렬 우선순위 (작을수록 위). 0=우회, 1=통신끊김, 2=접근성경고, 3=오버레이경고, 4=대기중, 5=정상.</summary>
    public int SortPriority { get; private set; } = 5;

    /// <summary>현재 문제 (없으면 None).</summary>
    public DeviceIssue Issue { get; private set; } = DeviceIssue.None;

    /// <summary>문제 종류의 짧은 한국어 이름.</summary>
    public static string IssueLabel(DeviceIssue issue) => issue switch
    {
        DeviceIssue.Bypass => "우회 시도",
        DeviceIssue.Unreachable => "통신 끊김",
        DeviceIssue.Accessibility => "접근성 꺼짐",
        DeviceIssue.Overlay => "오버레이 꺼짐",
        _ => "",
    };

    /// <summary>이름 표시. 학적 수정 요청 표시는 카드의 편집 아이콘(EditRequest)으로 한다.</summary>
    public string NameWithBadge => Name;

    public DeviceViewModel(string address, string name)
    {
        Address = address;
        _name = name;
    }

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(NameWithBadge));

    /// <summary>STATUS 파싱 결과를 반영. appNameResolver 로 차단 앱 패키지명을 표시명으로 변환.</summary>
    public void Apply(AgentStatus status, Func<string, string> appNameResolver)
    {
        EditRequest = status.EditRequest;

        if (!status.Reachable)
        {
            StatusText = "통신 끊김";
            StatusIcon = "wifi-off";
            SortPriority = 1;
            Issue = DeviceIssue.Unreachable;
            SetColor(Danger);
            return;
        }

        if (status.HasSuspicious)
        {
            var pkg = status.SuspiciousPackage ?? "";
            var appName = appNameResolver(pkg);
            var time = status.SuspiciousTime;
            var timeStr = string.IsNullOrEmpty(time) ? "" : $" ({time})";
            StatusText = $"우회 시도 감지: {appName}{timeStr}";
            StatusIcon = "shield-alert";
            SortPriority = 0;
            Issue = DeviceIssue.Bypass;
            SetColor(Violet);
            return;
        }

        var session = status.InSession ? "수업중" : "대기중";
        var accessibility = status.Accessibility ? "접근성 켜짐" : "접근성 꺼짐";
        var overlay = status.Overlay ? "오버레이 켜짐" : "오버레이 꺼짐";
        StatusText = $"{session} · {accessibility} · {overlay}";
        StatusIcon = !status.Accessibility || !status.Overlay ? "alert"
            : status.InSession ? "check-circle" : "pause-circle";

        if (!status.Accessibility) { SortPriority = 2; Issue = DeviceIssue.Accessibility; SetColor(Warning); }
        else if (!status.Overlay) { SortPriority = 3; Issue = DeviceIssue.Overlay; SetColor(Warning); }
        else if (!status.InSession) { SortPriority = 4; Issue = DeviceIssue.None; SetColor(Offline); }
        else { SortPriority = 5; Issue = DeviceIssue.None; SetColor(Success); }
    }

    /// <summary>통신 끊김 상태로 표시.</summary>
    public void MarkUnreachable() => Apply(AgentStatus.Unreachable(), _ => string.Empty);

    private void SetColor(string hex) => StatusBrush = new SolidColorBrush(Color.Parse(hex));
}
