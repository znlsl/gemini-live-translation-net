namespace GeminiLiveTranslate.Translation;

public sealed class LiveTranslationClient : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ILiveTranslationAdapter> _adapters;
    private ILiveTranslationAdapter? _activeAdapter;
    private int _activeSessionId;
    private int _nextSessionId;

    internal LiveTranslationClient(IEnumerable<ILiveTranslationAdapter> adapters)
    {
        _adapters = new Dictionary<string, ILiveTranslationAdapter>(StringComparer.OrdinalIgnoreCase);
        foreach (var adapter in adapters)
        {
            if (!_adapters.TryAdd(adapter.Descriptor.Id, adapter))
                throw new ArgumentException($"Duplicate translation provider: {adapter.Descriptor.Id}", nameof(adapters));
            WireEvents(adapter);
        }

        if (_adapters.Count == 0) throw new ArgumentException("At least one translation provider is required.", nameof(adapters));
        Providers = _adapters.Values.Select(adapter => adapter.Descriptor).ToArray();
    }

    public IReadOnlyList<TranslationProviderDescriptor> Providers { get; }

    public event Action<int, string>? InputTranscript;
    public event Action<int, string>? OutputTranscript;
    public event Action<int, byte[]>? AudioReceived;
    public event Action<int, string, string>? StatusChanged;
    public event Action<int>? Connected;
    public event Action<int, string>? Disconnected;
    public event Action<int, int, int>? StatsChanged;

    public TranslationProviderCapabilities GetCapabilities(string providerId)
    {
        if (_adapters.TryGetValue(providerId, out var adapter)) return adapter.Descriptor.Capabilities;
        throw new ArgumentOutOfRangeException(nameof(providerId), providerId, "Unknown translation provider.");
    }

    public int Start(LiveTranslationSessionOptions options)
    {
        Stop();
        if (!_adapters.TryGetValue(options.ProviderId, out var adapter))
            throw new ArgumentOutOfRangeException(nameof(options), options.ProviderId, "Unknown translation provider.");

        var sessionId = Interlocked.Increment(ref _nextSessionId);
        lock (_gate)
        {
            _activeAdapter = adapter;
            _activeSessionId = sessionId;
        }

        try
        {
            adapter.Start(sessionId, options);
            return sessionId;
        }
        catch
        {
            lock (_gate)
            {
                if (ReferenceEquals(_activeAdapter, adapter) && _activeSessionId == sessionId)
                {
                    _activeAdapter = null;
                    _activeSessionId = 0;
                }
            }
            throw;
        }
    }

    public void SendAudio(byte[] pcm16, int sessionId)
    {
        ILiveTranslationAdapter? adapter;
        lock (_gate)
        {
            if (sessionId != _activeSessionId) return;
            adapter = _activeAdapter;
        }
        adapter?.SendAudio(pcm16, sessionId);
    }

    public void Stop()
    {
        ILiveTranslationAdapter? adapter;
        lock (_gate)
        {
            adapter = _activeAdapter;
            _activeAdapter = null;
            _activeSessionId = 0;
        }
        adapter?.Stop();
    }

    private void WireEvents(ILiveTranslationAdapter adapter)
    {
        adapter.InputTranscript += (sessionId, text) => InputTranscript?.Invoke(sessionId, text);
        adapter.OutputTranscript += (sessionId, text) => OutputTranscript?.Invoke(sessionId, text);
        adapter.AudioReceived += (sessionId, data) => AudioReceived?.Invoke(sessionId, data);
        adapter.StatusChanged += (sessionId, kind, message) => StatusChanged?.Invoke(sessionId, kind, message);
        adapter.Connected += sessionId => Connected?.Invoke(sessionId);
        adapter.Disconnected += (sessionId, reason) => Disconnected?.Invoke(sessionId, reason);
        adapter.StatsChanged += (sessionId, pending, dropped) => StatsChanged?.Invoke(sessionId, pending, dropped);
    }

    public async ValueTask DisposeAsync()
    {
        Stop();
        foreach (var adapter in _adapters.Values) await adapter.DisposeAsync();
    }
}
