namespace GeminiLiveTranslate.Audio;

internal enum AudioCaptureChannel
{
    Microphone,
    System
}

internal sealed class Pcm16Mixer
{
    internal const int FrameSamples = 320; // 20 ms at 16 kHz.
    internal const int FrameBytes = FrameSamples * sizeof(short);
    private const int MaxBufferedSamples = 3200; // 200 ms per source.
    private readonly Queue<short> _microphone = new();
    private readonly Queue<short> _system = new();

    public void Add(AudioCaptureChannel channel, byte[] pcm)
    {
        Enqueue(channel == AudioCaptureChannel.Microphone ? _microphone : _system, pcm);
        Trim(_microphone);
        Trim(_system);
    }

    public byte[] ReadFrame()
    {
        var microphoneReady = _microphone.Count >= FrameSamples;
        var systemReady = _system.Count >= FrameSamples;
        if (!microphoneReady && !systemReady) return [];

        var mixed = new short[FrameSamples];
        for (var i = 0; i < FrameSamples; i++)
        {
            var sample = 0;
            if (microphoneReady) sample += _microphone.Dequeue();
            if (systemReady) sample += _system.Dequeue();
            mixed[i] = (short)Math.Clamp(sample, short.MinValue, short.MaxValue);
        }

        var output = new byte[FrameBytes];
        Buffer.BlockCopy(mixed, 0, output, 0, output.Length);
        return output;
    }

    public void Reset()
    {
        _microphone.Clear();
        _system.Clear();
    }

    private static void Enqueue(Queue<short> queue, byte[] pcm)
    {
        var sampleCount = pcm.Length / sizeof(short);
        if (sampleCount == 0) return;
        var samples = new short[sampleCount];
        Buffer.BlockCopy(pcm, 0, samples, 0, sampleCount * sizeof(short));
        foreach (var sample in samples) queue.Enqueue(sample);
    }

    private static void Trim(Queue<short> queue)
    {
        while (queue.Count > MaxBufferedSamples) queue.Dequeue();
    }
}
