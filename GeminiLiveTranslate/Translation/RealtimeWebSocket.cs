using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;

namespace GeminiLiveTranslate.Translation;

internal static class RealtimeWebSocket
{
    public static ClientWebSocket Create(string proxyUrl)
    {
        var socket = new ClientWebSocket();
        if (!string.IsNullOrWhiteSpace(proxyUrl))
        {
            var proxy = proxyUrl.Contains("://", StringComparison.Ordinal)
                ? proxyUrl
                : $"http://{proxyUrl}";
            socket.Options.Proxy = new WebProxy(proxy);
        }
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        return socket;
    }

    public static async Task<string> ReceiveTextAsync(
        ClientWebSocket socket,
        string providerName,
        CancellationToken token)
    {
        var buffer = new byte[64 * 1024];
        using var stream = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, token);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new WebSocketException($"{providerName} closed the WebSocket.");
            if (result.MessageType != WebSocketMessageType.Text)
                throw new WebSocketException($"{providerName} returned an unexpected binary message.");
            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
