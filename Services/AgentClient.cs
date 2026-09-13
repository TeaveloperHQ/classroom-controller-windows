using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ClassroomController.Services;

/// <summary>
/// 에이전트로 WebSocket 명령을 보내는 1회성 클라이언트.
/// 원본 컨트롤러처럼 명령마다 연결 → 전송 → (선택적으로) 응답 1건 수신 → 종료한다.
/// </summary>
public static class AgentClient
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(4);

    /// <summary>
    /// 명령을 전송한다. <paramref name="expectResponse"/> 가 true 면 서버 응답 1건을 반환,
    /// 실패하거나 응답이 없으면 null 을 반환한다.
    /// </summary>
    public static async Task<string?> SendAsync(string address, string command, bool expectResponse)
    {
        using var cts = new CancellationTokenSource(Timeout);
        using var ws = new ClientWebSocket();
        try
        {
            await ws.ConnectAsync(new Uri($"ws://{address}"), cts.Token).ConfigureAwait(false);
            var bytes = Encoding.UTF8.GetBytes(command);
            await ws.SendAsync(bytes, WebSocketMessageType.Text, true, cts.Token).ConfigureAwait(false);

            if (!expectResponse)
                return null;

            var response = await ReceiveTextAsync(ws, cts.Token).ConfigureAwait(false);

            try
            {
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, cts.Token)
                    .ConfigureAwait(false);
            }
            catch { /* 종료 실패는 무시 */ }

            return response;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>STATUS 를 질의하고 응답 문자열(or null)을 반환.</summary>
    public static Task<string?> QueryStatusAsync(string address) =>
        SendAsync(address, "STATUS", expectResponse: true);

    private static async Task<string?> ReceiveTextAsync(ClientWebSocket ws, CancellationToken token)
    {
        var buffer = new byte[8192];
        var sb = new StringBuilder();
        WebSocketReceiveResult result;
        do
        {
            result = await ws.ReceiveAsync(buffer, token).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
                return sb.Length > 0 ? sb.ToString() : null;
            sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
        }
        while (!result.EndOfMessage);

        return sb.ToString();
    }
}
