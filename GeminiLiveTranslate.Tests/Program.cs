using GeminiLiveTranslate.Audio;
using GeminiLiveTranslate.Gemini;

var failures = new List<string>();
Run("PCM chunker emits 100 ms chunks", PcmChunkerEmitsOneHundredMillisecondChunks);
Run("Dual-source mixer does not wait for a silent source", MixerDoesNotWaitForSilentSource);
Run("Realtime audio buffer drops oldest backlog", AudioBufferDropsOldestBacklog);
Run("Realtime audio buffer drops stale chunks", AudioBufferDropsStaleChunks);

if (failures.Count == 0)
{
    Console.WriteLine("All latency regression tests passed.");
    return 0;
}

Console.Error.WriteLine($"{failures.Count} latency regression test(s) failed:");
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
    Equal(3200, AudioCaptureService.ChunkSize, "Capture must use Gemini's recommended 100 ms chunk size.");
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

