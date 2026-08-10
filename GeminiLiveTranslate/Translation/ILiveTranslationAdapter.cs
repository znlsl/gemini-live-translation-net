namespace GeminiLiveTranslate.Translation;

internal interface ILiveTranslationAdapter : IAsyncDisposable
{
    TranslationProviderDescriptor Descriptor { get; }

    event Action<int, string>? InputTranscript;
    event Action<int, string>? OutputTranscript;
    event Action<int, byte[]>? AudioReceived;
    event Action<int, string, string>? StatusChanged;
    event Action<int>? Connected;
    event Action<int, string>? Disconnected;
    event Action<int, int, int>? StatsChanged;

    void Start(int sessionId, LiveTranslationSessionOptions options);
    void Stop();
    void SendAudio(byte[] pcm16, int sessionId);
}
