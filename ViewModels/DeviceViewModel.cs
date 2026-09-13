using System;
using Avalonia.Media;
using ClassroomController.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassroomController.ViewModels;

/// <summary>기기 목록의 한 행. 상태 문자열/색상/정렬 우선순위는 원본 컨트롤러 로직을 따른다.</summary>
public sealed partial class DeviceViewModel : ObservableObject
{
    public string Address { get; }

    [ObservableProperty] private string _name;

    /// <summary>체크 = 명령 대상에 포함 (기본 true).</summary>
    [ObservableProperty] private bool _isChecked = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameWithBadge))]
    private bool _editRequest;

    [ObservableProperty] private string _statusText = "연결 확인중...";
    [ObservableProperty] private IBrush _statusBrush = new SolidColorBrush(Color.Parse("#9e9e9e"));

    /// <summary>정렬 우선순위 (작을수록 위). 0=우회, 1=통신끊김, 2=접근성경고, 3=오버레이경고, 4=대기중, 5=정상.</summary>
    public int SortPriority { get; private set; } = 5;

    public string NameWithBadge => EditRequest ? $"📝 {Name}" : Name;

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
            StatusText = "❌ 통신 끊김";
            SortPriority = 1;
            SetColor("#d32f2f");
            return;
        }

        if (status.HasSuspicious)
        {
            var pkg = status.SuspiciousPackage ?? "";
            var appName = appNameResolver(pkg);
            var time = status.SuspiciousTime;
            var timeStr = string.IsNullOrEmpty(time) ? "" : $" ({time})";
            StatusText = $"🚨 우회 시도 감지: {appName}{timeStr}";
            SortPriority = 0;
            SetColor("#9c27b0");
            return;
        }

        var session = status.InSession ? "수업중" : "대기중";
        var accessibility = status.Accessibility ? "✅접근성" : "⚠️접근성";
        var overlay = status.Overlay ? "✅오버레이" : "⚠️오버레이";
        StatusText = $"{session} | {accessibility} | {overlay}";

        if (!status.Accessibility) { SortPriority = 2; SetColor("#e65100"); }
        else if (!status.Overlay) { SortPriority = 3; SetColor("#e65100"); }
        else if (!status.InSession) { SortPriority = 4; SetColor("#9e9e9e"); }
        else { SortPriority = 5; SetColor("#2e7d32"); }
    }

    /// <summary>통신 끊김 상태로 표시.</summary>
    public void MarkUnreachable() => Apply(AgentStatus.Unreachable(), _ => string.Empty);

    private void SetColor(string hex) => StatusBrush = new SolidColorBrush(Color.Parse(hex));
}
