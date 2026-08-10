using GeminiLiveTranslate.Audio;
using GeminiLiveTranslate.Settings;
using GeminiLiveTranslate.Soniox;
using GeminiLiveTranslate.Translation;

var failures = new List<string>();
Run("PCM chunker emits 100 ms chunks", PcmChunkerEmitsOneHundredMillisecondChunks);
Run("Dual-source mixer does not wait for a silent source", MixerDoesNotWaitForSilentSource);
Run("Realtime audio buffer drops oldest backlog", AudioBufferDropsOldestBacklog);
Run("Realtime audio buffer drops stale chunks", AudioBufferDropsStaleChunks);
Run("Translation client selects and switches providers", TranslationClientSelectsAndSwitchesProviders);
Run("Soniox transcript accumulator replaces interim tokens", SonioxTranscriptAccumulatorReplacesInterimTokens);
Run("Soniox transcript accumulator starts fresh after an endpoint", SonioxTranscriptAccumulatorStartsFreshAfterEndpoint);
Run("Soniox endpoint and language are normalized", SonioxEndpointAndLanguageAreNormalized);
Run("Settings create provider-specific session options", SettingsCreateProviderSpecificSessionOptions);

if (failures.Count == 0)
{
    Console.WriteLine("All regression tests passed.");
    return 0;
}

Console.Error.WriteLine($"{failures.Count} regression test(s) failed:");
foreach (var failure in failures) Console.Error.WriteLine($"- {failure}");
return 1;

void Run(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine($"PASS: {name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{name}: {ex.Message}");
    }
}

static void PcmChunkerEmitsOneHundredMillisecondChunks()
{
    var chunks = new List<byte[]>();
    Equal(3200, AudioCaptureService.ChunkSize, "Capture must use the recommended 100 ms chunk size.");
    var chunker = new Pcm16Chunker(AudioCaptureService.ChunkSize, chunks.Add);

    chunker.Append(new byte[3199]);
    Equal(0, chunks.Count, "A partial 100 ms chunk must remain buffered.");

    chunker.Append([0x5a]);
    Equal(1, chunks.Count, "Exactly 3200 bytes must emit one chunk.");
    Equal(3200, chunks[0].Length, "The emitted chunk must contain 100 ms of PCM16 audio.");
}

static void MixerDoesNotWaitForSilentSource()
{
    var mixer = new Pcm16Mixer();
    var microphone = Enumerable.Repeat((short)1200, Pcm16Mixer.FrameSamples).ToArray();
    var bytes = new byte[microphone.Length * sizeof(short)];
    Buffer.BlockCopy(microphone, 0, bytes, 0, bytes.Length);

    mixer.Add(AudioCaptureChannel.Microphone, bytes);
    var mixed = mixer.ReadFrame();

    Equal(Pcm16Mixer.FrameBytes, mixed.Length, "One available source must emit a 20 ms frame.");
    Equal((short)1200, BitConverter.ToInt16(mixed, 0), "A single active source must retain its level.");
}

static void AudioBufferDropsOldestBacklog()
{
    var time = new ManualTimeProvider();
    var buffer = new RealtimeAudioBuffer(2, TimeSpan.FromMilliseconds(250), time);

    buffer.Enqueue([1]);
    buffer.Enqueue([2]);
    buffer.Enqueue([3]);

    Equal(2, buffer.PendingCount, "The bounded buffer must retain at most two chunks.");
    Equal(1, buffer.DroppedCount, "Overflow must drop the oldest chunk.");
    True(buffer.TryTakeFresh(out var second), "A fresh chunk should be available.");
    Equal((byte)2, second[0], "The oldest retained chunk should be returned first.");
    True(buffer.TryTakeFresh(out var third), "The newest chunk should be available.");
    Equal((byte)3, third[0], "The newest retained chunk should be returned second.");
}

static void AudioBufferDropsStaleChunks()
{
    var time = new ManualTimeProvider();
    var buffer = new RealtimeAudioBuffer(2, TimeSpan.FromMilliseconds(250), time);
    buffer.Enqueue([1]);
    time.Advance(TimeSpan.FromMilliseconds(251));

    True(!buffer.TryTakeFresh(out _), "Audio older than the realtime budget must not be sent.");
    Equal(1, buffer.DroppedCount, "Expired audio must be included in dropped statistics.");
}

static void TranslationClientSelectsAndSwitchesProviders()
{
    var gemini = new FakeTranslationAdapter(TranslationProviderIds.Gemini, supportsTranslatedAudio: true);
    var soniox = new FakeTranslationAdapter(TranslationProviderIds.Soniox, supportsTranslatedAudio: false);
    var client = new LiveTranslationClient([gemini, soniox]);
    try
    {
        var geminiSession = client.Start(SessionOptions(TranslationProviderIds.Gemini));
        client.SendAudio([1], geminiSession);
        Equal(1, gemini.StartCount, "Gemini must receive the first session.");
        Equal(1, gemini.AudioCount, "The active Adapter must receive audio.");

        var sonioxSession = client.Start(SessionOptions(TranslationProviderIds.Soniox));
        client.SendAudio([2], geminiSession);
        client.SendAudio([3], sonioxSession);
        Equal(1, gemini.StopCount, "Switching providers must stop the previous Adapter.");
        Equal(1, soniox.StartCount, "Soniox must receive the replacement session.");
        Equal(1, soniox.AudioCount, "Stale session audio must be ignored and current audio must be forwarded.");
        True(client.GetCapabilities(TranslationProviderIds.Gemini).SupportsTranslatedAudio, "Gemini must advertise translated audio.");
        True(!client.GetCapabilities(TranslationProviderIds.Soniox).SupportsTranslatedAudio, "Soniox STT must not advertise TTS yet.");
    }
    finally
    {
        client.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}

static void SonioxTranscriptAccumulatorReplacesInterimTokens()
{
    var accumulator = new SonioxTranscriptAccumulator();
    var interim = accumulator.Apply([
        new SonioxToken("Hel", false, "original"),
        new SonioxToken("你", false, "translation")
    ]);
    Equal("Hel", interim.InputText!, "Original interim text must stream immediately.");
    Equal("你", interim.OutputText!, "Translated interim text must stream immediately.");

    var finalized = accumulator.Apply([
        new SonioxToken("Hello", true, "original"),
        new SonioxToken("你好", true, "translation")
    ]);
    Equal("Hello", finalized.InputText!, "Final original tokens must replace the previous interim view.");
    Equal("你好", finalized.OutputText!, "Final translation tokens must replace the previous interim view.");

    var next = accumulator.Apply([new SonioxToken(" world", false, "original")]);
    Equal("Hello world", next.InputText!, "New interim tokens must follow committed tokens.");
    True(next.OutputText is null, "A response without translated tokens must not overwrite the translated HUD.");
}

static void SonioxTranscriptAccumulatorStartsFreshAfterEndpoint()
{
    var accumulator = new SonioxTranscriptAccumulator();
    var first = accumulator.Apply([
        new SonioxToken("First sentence.", true, "original"),
        new SonioxToken("第一句。", true, "translation"),
        new SonioxToken("<end>", true, "translation")
    ]);
    Equal("First sentence.", first.InputText!, "The completed source utterance must be emitted once.");
    Equal("第一句。", first.OutputText!, "The completed translated utterance must be emitted once.");

    var second = accumulator.Apply([
        new SonioxToken("Second", false, "original"),
        new SonioxToken("第二句", false, "translation")
    ]);
    Equal("Second", second.InputText!, "A new source utterance must not include previous final text.");
    Equal("第二句", second.OutputText!, "A new translation must not include previous final text.");
}

static void SonioxEndpointAndLanguageAreNormalized()
{
    Equal(
        "wss://stt-rt.soniox.com/transcribe-websocket",
        SonioxLiveClient.BuildUri("https://stt-rt.soniox.com").AbsoluteUri,
        "An HTTPS Soniox host must become the realtime WebSocket endpoint.");
    Equal("zh", SonioxLiveClient.NormalizeTargetLanguage("zh-CN"), "Soniox uses the base Chinese language code.");
    Equal("pt", SonioxLiveClient.NormalizeTargetLanguage("pt_BR"), "Regional tags must normalize to Soniox base language codes.");
}

static void SettingsCreateProviderSpecificSessionOptions()
{
    var settings = new AppSettings
    {
        TranslationProvider = TranslationProviderIds.Soniox,
        ApiKey = "gemini-key",
        SonioxApiKey = "soniox-key",
        SonioxEndpoint = "wss://example.test/transcribe-websocket",
        SonioxModel = "stt-test",
        SystemPrompt = "Gemini only"
    };
    settings.Normalize();
    var options = settings.CreateSessionOptions(requestTranslatedAudio: false);

    Equal(TranslationProviderIds.Soniox, options.ProviderId, "The selected provider must reach the session Interface.");
    Equal("soniox-key", options.ApiKey, "The selected provider must use its own credential.");
    Equal("stt-test", options.Model, "The selected provider must use its own model.");
    Equal("", options.SystemPrompt, "Gemini instructions must not leak into Soniox context semantics.");
}

static LiveTranslationSessionOptions SessionOptions(string providerId) => new(
    providerId,
    "key",
    "wss://example.test",
    "",
    "model",
    "zh-CN",
    "",
    false);

static void Equal<T>(T expected, T actual, string message) where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{message} Expected: {expected}; actual: {actual}.");
}

static void True(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class ManualTimeProvider : TimeProvider
{
    private long _timestamp;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _timestamp;

    public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
}

sealed class FakeTranslationAdapter : ILiveTranslationAdapter
{
    public FakeTranslationAdapter(string id, bool supportsTranslatedAudio)
    {
        Descriptor = new TranslationProviderDescriptor(
            id,
            id,
            new TranslationProviderCapabilities(supportsTranslatedAudio));
    }

    public TranslationProviderDescriptor Descriptor { get; }
    public int StartCount { get; private set; }
    public int StopCount { get; private set; }
    public int AudioCount { get; private set; }

#pragma warning disable CS0067
    public event Action<int, string>? InputTranscript;
    public event Action<int, string>? OutputTranscript;
    public event Action<int, byte[]>? AudioReceived;
    public event Action<int, string, string>? StatusChanged;
    public event Action<int>? Connected;
    public event Action<int, string>? Disconnected;
    public event Action<int, int, int>? StatsChanged;
#pragma warning restore CS0067

    public void Start(int sessionId, LiveTranslationSessionOptions options) => StartCount++;
    public void Stop() => StopCount++;
    public void SendAudio(byte[] pcm16, int sessionId) => AudioCount++;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
