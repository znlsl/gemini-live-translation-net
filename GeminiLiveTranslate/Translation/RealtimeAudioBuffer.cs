namespace GeminiLiveTranslate.Translation;

internal sealed class RealtimeAudioBuffer
{
    private readonly object _gate = new();
    private readonly Queue<BufferedAudioChunk> _queue = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly int _capacity;
    private readonly TimeSpan _maxAge;
    private readonly TimeProvider _timeProvider;
    private int _droppedCount;

    public RealtimeAudioBuffer(int capacity, TimeSpan maxAge, TimeProvider? timeProvider = null)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (maxAge <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maxAge));
        _capacity = capacity;
        _maxAge = maxAge;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public int PendingCount
    {
        get
        {
            lock (_gate) return _queue.Count;
        }
    }

    public int DroppedCount
    {
        get
        {
            lock (_gate) return _droppedCount;
        }
    }

    public void Enqueue(byte[] data)
    {
        if (data.Length == 0) return;
        lock (_gate)
        {
            while (_queue.Count >= _capacity)
            {
                _queue.Dequeue();
                _droppedCount++;
            }
            _queue.Enqueue(new BufferedAudioChunk(data, _timeProvider.GetTimestamp()));
        }
        _signal.Release();
    }

    public ValueTask WaitForDataAsync(CancellationToken token) => new(_signal.WaitAsync(token));

    public bool TryTakeFresh(out byte[] data)
    {
        lock (_gate)
        {
            while (_queue.Count > 0)
            {
                var chunk = _queue.Dequeue();
                if (_timeProvider.GetElapsedTime(chunk.EnqueuedAt) > _maxAge)
                {
                    _droppedCount++;
                    continue;
                }

                data = chunk.Data;
                return true;
            }
        }

        data = [];
        return false;
    }

    public void Clear()
    {
        lock (_gate) _queue.Clear();
        while (_signal.CurrentCount > 0 && _signal.Wait(0)) { }
    }

    private sealed record BufferedAudioChunk(byte[] Data, long EnqueuedAt);
}
