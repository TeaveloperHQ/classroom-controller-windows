using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Zeroconf;

namespace ClassroomController.Services;

/// <summary>
/// Android NSD 의 대체. mDNS 로 "_classroomagent._tcp" 서비스를 주기적으로 탐색하여
/// 학년/반 접두어로 필터링한 뒤 발견/소멸 이벤트를 발생시킨다.
/// </summary>
public sealed class AgentDiscovery
{
    private const string ServiceType = "_classroomagent._tcp.local.";
    private static readonly TimeSpan ScanTime = TimeSpan.FromSeconds(2);
    private const int LostThreshold = 2; // 연속 N회 미발견 시 소멸 처리 (깜빡임 방지)

    private readonly Func<string> _prefixProvider;

    // address(ip:port) -> deviceName
    private readonly Dictionary<string, string> _known = new();
    private readonly Dictionary<string, int> _missCount = new();

    private CancellationTokenSource? _cts;

    /// <summary>기기 발견. (address, deviceName)</summary>
    public event Action<string, string>? DeviceFound;

    /// <summary>기기 소멸. (address)</summary>
    public event Action<string>? DeviceLost;

    public AgentDiscovery(Func<string> prefixProvider)
    {
        _prefixProvider = prefixProvider;
    }

    public void Start()
    {
        Stop();
        _cts = new CancellationTokenSource();
        _ = RunLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    /// <summary>학급 변경 등으로 목록을 처음부터 다시 탐색.</summary>
    public void Restart()
    {
        _known.Clear();
        _missCount.Clear();
        Start();
    }

    private async Task RunLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // 개별 스윕 실패는 무시하고 다음 주기로.
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task SweepAsync(CancellationToken token)
    {
        var prefix = _prefixProvider();
        IReadOnlyList<IZeroconfHost> hosts =
            await ZeroconfResolver.ResolveAsync(ServiceType, ScanTime).ConfigureAwait(false);

        // 이번 스윕에서 실제로 보인 기기들.
        var seen = new Dictionary<string, string>();
        foreach (var host in hosts)
        {
            var deviceName = host.DisplayName;
            if (string.IsNullOrEmpty(deviceName) || !deviceName.StartsWith(prefix))
                continue;

            var service = host.Services.Values.FirstOrDefault();
            if (service == null) continue;

            var ip = host.IPAddress;
            if (string.IsNullOrEmpty(ip)) continue;

            seen[$"{ip}:{service.Port}"] = deviceName;
        }

        if (token.IsCancellationRequested) return;

        // 신규 발견.
        foreach (var (address, name) in seen)
        {
            _missCount[address] = 0;
            if (!_known.ContainsKey(address))
            {
                _known[address] = name;
                DeviceFound?.Invoke(address, name);
            }
        }

        // 소멸 판정: 이번 스윕에 없던 기존 기기의 miss 카운트 증가.
        foreach (var address in _known.Keys.ToList())
        {
            if (seen.ContainsKey(address)) continue;
            var miss = _missCount.GetValueOrDefault(address) + 1;
            _missCount[address] = miss;
            if (miss >= LostThreshold)
            {
                _known.Remove(address);
                _missCount.Remove(address);
                DeviceLost?.Invoke(address);
            }
        }
    }
}
