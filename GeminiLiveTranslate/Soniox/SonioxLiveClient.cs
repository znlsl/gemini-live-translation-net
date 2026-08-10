using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using GeminiLiveTranslate.Translation;

namespace GeminiLiveTranslate.Soniox;

internal sealed class SonioxLiveClient : ILiveTranslationAdapter
{
    private const int MaxQueuedAudioChunks = 2;
    private static readonly TimeSpan MaxAudioChunkAge = TimeSpan.FromMilliseconds(250);
    private readonly object _gate = new();
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _sessionCts;
    private RealtimeAudioBuffer? _audioBuffer;
    private int _sessionId;

    public TranslationProviderDescriptor Descriptor { get; } = new(
        TranslationProviderIds.Soniox,
        "Soniox",
        new TranslationProviderCapabilities(SupportsTranslatedAudio: false));

    public event Action<int, string>? InputTranscript;
    public event Action<int, string>? OutputTranscript;
    public event Action<int, byte[]>? AudioReceived
    {
        add { }
        remove { }
    }
    public event Action<int, string, string>? StatusChanged;
    public event Action<int>? Connected;
    public event Action<int, string>? Disconnected;
    public event Action<int, int, int>? StatsChanged;

    public void Start(int sessionId, LiveTranslationSessionOptions options)
    {
        Stop();
        var cts = new CancellationTokenSource();
        var audioBuffer = new RealtimeAudioBuffer(MaxQueuedAudioChunks, MaxAudioChunkAge);
        lock (_gate)
        {
            _sessionCts = cts;
            _audioBuffer = audioBuffer;
            _sessionId = sessionId;
        }

        _ = Task.Run(() => RunSessionAsync(sessionId, options, audioBuffer, cts.Token));
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
        LiveTranslationSessionOptions options,
        RealtimeAudioBuffer audioBuffer,
        CancellationToken token)
    {
        var reconnectDelay = TimeSpan.FromSeconds(1);
        while (!token.IsCancellationRequested)
        {
            try
            {
                StatusChanged?.Invoke(sessionId, "connecting", "Connecting to Soniox...");
                using var socket = RealtimeWebSocket.Create(options.ProxyUrl);
                lock (_gate)
                {
                    if (sessionId != _sessionId || !ReferenceEquals(_audioBuffer, audioBuffer)) return;
                    _socket = socket;
                    audioBuffer.Clear();
                }

                await socket.ConnectAsync(BuildUri(options.Endpoint), token);
                await SendConfigAsync(socket, options, token);
                StatusChanged?.Invoke(sessionId, "connected", "Connected to Soniox");

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

        Disconnected?.Invoke(sessionId, token.IsCancellationRequested ? "" : "Soniox session ended");
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
                    await socket.SendAsync(pcm16, WebSocketMessageType.Binary, true, token);
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

    private static Task SendConfigAsync(
        ClientWebSocket socket,
        LiveTranslationSessionOptions options,
        CancellationToken token)
    {
        var config = new
        {
            api_key = options.ApiKey,
            model = options.Model,
            audio_format = "pcm_s16le",
            sample_rate = 16000,
            num_channels = 1,
            enable_language_identification = true,
            enable_endpoint_detection = true,
            max_endpoint_delay_ms = 500,
            translation = new
            {
                type = "one_way",
                target_language = NormalizeTargetLanguage(options.TargetLanguage)
            }
        };
        var json = JsonSerializer.Serialize(config);
        return socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, token);
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, int sessionId, CancellationToken token)
    {
        var transcripts = new SonioxTranscriptAccumulator();
        while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            var text = await RealtimeWebSocket.ReceiveTextAsync(socket, "Soniox", token);
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            ThrowIfSonioxError(root);
            HandleRoot(sessionId, root, transcripts);
            if (root.TryGetProperty("finished", out var finished) && finished.ValueKind == JsonValueKind.True) return;
        }
    }

    private void HandleRoot(int sessionId, JsonElement root, SonioxTranscriptAccumulator transcripts)
    {
        if (!root.TryGetProperty("tokens", out var tokens) || tokens.ValueKind != JsonValueKind.Array) return;

        var parsed = new List<SonioxToken>();
        foreach (var token in tokens.EnumerateArray())
        {
            if (!token.TryGetProperty("text", out var textElement)) continue;
            var text = textElement.GetString();
            if (string.IsNullOrEmpty(text)) continue;
            var isFinal = token.TryGetProperty("is_final", out var finalElement) && finalElement.ValueKind == JsonValueKind.True;
            var status = token.TryGetProperty("translation_status", out var statusElement)
                ? statusElement.GetString() ?? "none"
                : "none";
            parsed.Add(new SonioxToken(text, isFinal, status));
        }

        var update = transcripts.Apply(parsed);
        if (update.InputText is not null) InputTranscript?.Invoke(sessionId, update.InputText);
        if (update.OutputText is not null) OutputTranscript?.Invoke(sessionId, update.OutputText);
    }

    private static void ThrowIfSonioxError(JsonElement root)
    {
        if (!root.TryGetProperty("error_code", out var code) || code.ValueKind == JsonValueKind.Null) return;
        var message = root.TryGetProperty("error_message", out var messageElement)
            ? messageElement.GetString()
            : "Unknown Soniox error";
        var type = root.TryGetProperty("error_type", out var typeElement) ? typeElement.GetString() : null;
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(type) ? message : $"{message} ({type})");
    }

    internal static Uri BuildUri(string endpoint)
    {
        var value = endpoint.Trim();
        if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            value = "wss://" + value["https://".Length..];
        else if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            value = "ws://" + value["http://".Length..];

        var builder = new UriBuilder(value);
        if (string.IsNullOrWhiteSpace(builder.Path) || builder.Path == "/") builder.Path = "/transcribe-websocket";
        return builder.Uri;
    }

    internal static string NormalizeTargetLanguage(string language)
    {
        var normalized = language.Trim().Replace('_', '-');
        var separator = normalized.IndexOf('-');
        return (separator > 0 ? normalized[..separator] : normalized).ToLowerInvariant();
    }

    public ValueTask DisposeAsync()
    {
        Stop();
        return ValueTask.CompletedTask;
    }
}
