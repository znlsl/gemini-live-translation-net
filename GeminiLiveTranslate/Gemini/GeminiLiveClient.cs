using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace GeminiLiveTranslate.Gemini;

public sealed class GeminiLiveClient : IAsyncDisposable
{
    private const int MaxQueuedAudioChunks = 2;
    private static readonly TimeSpan MaxAudioChunkAge = TimeSpan.FromMilliseconds(250);
    private readonly object _gate = new();
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _sessionCts;
    private RealtimeAudioBuffer? _audioBuffer;
    private int _sessionId;

    public event Action<int, string>? InputTranscript;
    public event Action<int, string>? OutputTranscript;
    public event Action<int, byte[]>? AudioReceived;
    public event Action<int, string, string>? StatusChanged;
    public event Action<int>? Connected;
    public event Action<int, string>? Disconnected;
    public event Action<int, int, int>? StatsChanged;

    public int DroppedChunks
    {
        get
        {
            lock (_gate) return _audioBuffer?.DroppedCount ?? 0;
        }
    }

    public int Start(GeminiSessionOptions options)
    {
        Stop();
        var cts = new CancellationTokenSource();
        var audioBuffer = new RealtimeAudioBuffer(MaxQueuedAudioChunks, MaxAudioChunkAge);
        int sessionId;
        lock (_gate)
        {
            _sessionCts = cts;
            _audioBuffer = audioBuffer;
            _sessionId++;
            sessionId = _sessionId;
        }

        _ = Task.Run(() => RunSessionAsync(sessionId, options, audioBuffer, cts.Token));
        return sessionId;
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        ClientWebSocket? socket;
        RealtimeAudioBuffer? audioBuffer;
        lock (_gate)
        {
            cts = _sessionCts;
            socket = _socket;
            audioBuffer = _audioBuffer;
            _sessionCts = null;
            _socket = null;
            _audioBuffer = null;
        }

        audioBuffer?.Clear();
        try { cts?.Cancel(); } catch { }
        try { socket?.Abort(); socket?.Dispose(); } catch { }
        cts?.Dispose();
    }

    public void SendAudio(byte[] pcm16, int sessionId)
    {
        if (pcm16.Length == 0) return;
        int pending;
        int dropped;
        lock (_gate)
        {
            if (sessionId != _sessionId || _sessionCts is null || _socket is null || _audioBuffer is null) return;
            if (_socket.State != WebSocketState.Open) return;
            _audioBuffer.Enqueue(pcm16);
            pending = _audioBuffer.PendingCount;
            dropped = _audioBuffer.DroppedCount;
        }

        StatsChanged?.Invoke(sessionId, pending, dropped);
    }

    private async Task RunSessionAsync(
        int sessionId,
        GeminiSessionOptions options,
        RealtimeAudioBuffer audioBuffer,
        CancellationToken token)
    {
        var reconnectDelay = TimeSpan.FromSeconds(1);
        while (!token.IsCancellationRequested)
        {
            try
            {
                StatusChanged?.Invoke(sessionId, "connecting", "Connecting to Gemini Live...");
                using var socket = CreateSocket(options);
                lock (_gate)
                {
                    if (sessionId != _sessionId || !ReferenceEquals(_audioBuffer, audioBuffer)) return;
                    _socket = socket;
                    audioBuffer.Clear();
                }

                await socket.ConnectAsync(BuildUri(options), token);
                await SendSetupAsync(socket, options, token);
                await WaitForSetupAsync(socket, sessionId, token);
                using (var senderCts = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    var senderTask = Task.Run(
                        () => SendAudioLoopAsync(socket, audioBuffer, sessionId, senderCts.Token),
                        senderCts.Token);
                    Connected?.Invoke(sessionId);
                    reconnectDelay = TimeSpan.FromSeconds(1);
                    try
                    {
                        await ReceiveLoopAsync(socket, sessionId, token);
                    }
                    finally
                    {
                        senderCts.Cancel();
                        try { await senderTask; } catch (OperationCanceledException) { }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(sessionId, "error", ex.Message);
                lock (_gate)
                {
                    if (sessionId == _sessionId && ReferenceEquals(_audioBuffer, audioBuffer)) _socket = null;
                    audioBuffer.Clear();
                }
                await Task.Delay(reconnectDelay, token).ContinueWith(_ => { }, CancellationToken.None);
                reconnectDelay = TimeSpan.FromSeconds(Math.Min(reconnectDelay.TotalSeconds * 2, 30));
                continue;
            }

            break;
        }

        Disconnected?.Invoke(sessionId, token.IsCancellationRequested ? "" : "Session ended");
    }

    private async Task SendAudioLoopAsync(
        ClientWebSocket socket,
        RealtimeAudioBuffer audioBuffer,
        int sessionId,
        CancellationToken token)
    {
        while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            try
            {
                await audioBuffer.WaitForDataAsync(token);
                while (audioBuffer.TryTakeFresh(out var pcm16))
                {
                    var payload = Convert.ToBase64String(pcm16);
                    var json = JsonSerializer.Serialize(new
                    {
                        realtimeInput = new
                        {
                            audio = new
                            {
                                data = payload,
                                mimeType = "audio/pcm;rate=16000"
                            }
                        }
                    });
                    await socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, token);
                    StatsChanged?.Invoke(sessionId, audioBuffer.PendingCount, audioBuffer.DroppedCount);
                }
                StatsChanged?.Invoke(sessionId, audioBuffer.PendingCount, audioBuffer.DroppedCount);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(sessionId, "warning", $"Audio send delayed: {ex.Message}");
                await Task.Delay(150, token).ContinueWith(_ => { }, CancellationToken.None);
            }
        }
    }

    private static ClientWebSocket CreateSocket(GeminiSessionOptions options)
    {
        var socket = new ClientWebSocket();
        if (!string.IsNullOrWhiteSpace(options.ProxyUrl))
        {
            var proxy = options.ProxyUrl.Contains("://", StringComparison.Ordinal)
                ? options.ProxyUrl
                : $"http://{options.ProxyUrl}";
            socket.Options.Proxy = new WebProxy(proxy);
        }
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        return socket;
    }

    private static Uri BuildUri(GeminiSessionOptions options)
    {
        var baseUrl = options.ApiBase.TrimEnd('/');
        if (baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            baseUrl = "wss://" + baseUrl["https://".Length..];
        else if (baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            baseUrl = "ws://" + baseUrl["http://".Length..];

        return new Uri($"{baseUrl}/ws/google.ai.generativelanguage.v1beta.GenerativeService.BidiGenerateContent?key={Uri.EscapeDataString(options.ApiKey)}");
    }

    private static Task SendSetupAsync(ClientWebSocket socket, GeminiSessionOptions options, CancellationToken token)
    {
        var setup = new Dictionary<string, object?>
        {
            ["model"] = options.Model,
            ["generationConfig"] = new
            {
                responseModalities = new[] { "AUDIO" },
                translationConfig = new
                {
                    targetLanguageCode = options.TargetLanguage,
                    echoTargetLanguage = options.EchoTargetLanguage
                }
            },
            ["inputAudioTranscription"] = new { },
            ["outputAudioTranscription"] = new { },
            ["contextWindowCompression"] = new
            {
                triggerTokens = "0",
                slidingWindow = new { targetTokens = "0" }
            }
        };
        if (!string.IsNullOrWhiteSpace(options.SystemPrompt))
        {
            setup["systemInstruction"] = new { parts = new[] { new { text = options.SystemPrompt } } };
        }

        var json = JsonSerializer.Serialize(new { setup });
        return socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, token);
    }

    private async Task WaitForSetupAsync(ClientWebSocket socket, int sessionId, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        while (!timeout.Token.IsCancellationRequested)
        {
            var text = await ReceiveTextAsync(socket, timeout.Token);
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            ThrowIfGeminiError(root);
            if (root.TryGetProperty("setupComplete", out _))
            {
                StatusChanged?.Invoke(sessionId, "connected", "Connected");
                return;
            }
            HandleRoot(sessionId, root);
        }
        throw new TimeoutException("Gemini Live setup timed out.");
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, int sessionId, CancellationToken token)
    {
        while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            var text = await ReceiveTextAsync(socket, token);
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            ThrowIfGeminiError(root);
            HandleRoot(sessionId, root);
        }
    }

    private static async Task<string> ReceiveTextAsync(ClientWebSocket socket, CancellationToken token)
    {
        var buffer = new byte[64 * 1024];
        using var stream = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, token);
            if (result.MessageType == WebSocketMessageType.Close) throw new WebSocketException("Gemini closed the WebSocket.");
            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private void HandleRoot(int sessionId, JsonElement root)
    {
        if (!root.TryGetProperty("serverContent", out var content)) return;

        if (content.TryGetProperty("inputTranscription", out var input) &&
            input.TryGetProperty("text", out var inputText))
        {
            InputTranscript?.Invoke(sessionId, inputText.GetString() ?? "");
        }

        if (content.TryGetProperty("outputTranscription", out var output) &&
            output.TryGetProperty("text", out var outputText))
        {
            OutputTranscript?.Invoke(sessionId, outputText.GetString() ?? "");
        }

        if (!content.TryGetProperty("modelTurn", out var modelTurn) ||
            !modelTurn.TryGetProperty("parts", out var parts) ||
            parts.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var text))
            {
                OutputTranscript?.Invoke(sessionId, text.GetString() ?? "");
            }
            if (part.TryGetProperty("inlineData", out var inlineData) &&
                inlineData.TryGetProperty("data", out var audioData))
            {
                var data = audioData.GetString();
                if (!string.IsNullOrEmpty(data))
                {
                    AudioReceived?.Invoke(sessionId, Convert.FromBase64String(data));
                }
            }
        }
    }

    private static void ThrowIfGeminiError(JsonElement root)
    {
        if (root.TryGetProperty("error", out var error))
        {
            var message = error.TryGetProperty("message", out var m) ? m.GetString() : "Unknown Gemini error";
            throw new InvalidOperationException(message);
        }
    }

    public ValueTask DisposeAsync()
    {
        Stop();
        return ValueTask.CompletedTask;
    }
}
