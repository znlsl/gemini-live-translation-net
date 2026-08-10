using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace GeminiLiveTranslate.Audio;

public sealed class AudioCaptureService : IDisposable
{
    internal const int ChunkSize = 3200; // 100 ms at 16 kHz mono PCM16.
    private const int MixerPeriodMilliseconds = 20;
    private readonly object _stateGate = new();
    private readonly object _chunkerGate = new();
    private IWaveIn? _microphoneCapture;
    private IWaveIn? _systemCapture;
    private Pcm16Chunker? _chunker;
    private Pcm16Mixer? _mixer;
    private System.Threading.Timer? _mixTimer;
    private int _mixTickActive;
    private int _droppedChunks;

    public int DroppedChunks => Volatile.Read(ref _droppedChunks);

    public IReadOnlyList<string> ListInputDevices()
    {
        var names = new List<string> { "Default system audio (WASAPI loopback)" };
        for (var i = 0; i < WaveIn.DeviceCount; i++)
        {
            names.Add($"{i}: {WaveIn.GetCapabilities(i).ProductName}");
        }
        return names;
    }

    public void Start(string source, int deviceNumber, Action<byte[]> onChunk)
    {
        Stop();
        Interlocked.Exchange(ref _droppedChunks, 0);
        var both = string.Equals(source, "both", StringComparison.OrdinalIgnoreCase);
        lock (_stateGate)
        {
            _chunker = new Pcm16Chunker(ChunkSize, onChunk);
            _mixer = both ? new Pcm16Mixer() : null;
            _mixTimer = both
                ? new System.Threading.Timer(MixNextFrame, null, MixerPeriodMilliseconds, MixerPeriodMilliseconds)
                : null;
        }

        try
        {
            if (source is "mic" or "both") StartMicrophone(deviceNumber);
            if (source is not "mic") StartSystemAudio();
        }
        catch
        {
            Stop();
            throw;
        }
    }

    private void StartMicrophone(int deviceNumber)
    {
        var waveIn = new WaveInEvent
        {
            DeviceNumber = deviceNumber >= 0 ? deviceNumber : 0,
            WaveFormat = new WaveFormat(16000, 16, 1),
            BufferMilliseconds = 50
        };
        waveIn.DataAvailable += MicrophoneDataAvailable;
        lock (_stateGate) _microphoneCapture = waveIn;
        waveIn.StartRecording();
    }

    private void StartSystemAudio()
    {
        using var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var loopback = new WasapiLoopbackCapture(device);
        loopback.DataAvailable += SystemDataAvailable;
        lock (_stateGate) _systemCapture = loopback;
        loopback.StartRecording();
    }

    public void Stop()
    {
        IWaveIn? microphone;
        IWaveIn? system;
        System.Threading.Timer? mixTimer;
        Pcm16Chunker? chunker;
        lock (_stateGate)
        {
            microphone = _microphoneCapture;
            system = _systemCapture;
            mixTimer = _mixTimer;
            chunker = _chunker;
            _microphoneCapture = null;
            _systemCapture = null;
            _mixTimer = null;
            _mixer?.Reset();
            _mixer = null;
            _chunker = null;
        }

        mixTimer?.Dispose();
        lock (_chunkerGate) chunker?.Reset();
        StopCapture(microphone, MicrophoneDataAvailable);
        StopCapture(system, SystemDataAvailable);
    }

    private static void StopCapture(IWaveIn? capture, EventHandler<WaveInEventArgs> handler)
    {
        if (capture is null) return;
        try
        {
            capture.DataAvailable -= handler;
            capture.StopRecording();
            capture.Dispose();
        }
        catch
        {
            // Stop should be best effort during app shutdown.
        }
    }

    private void MicrophoneDataAvailable(object? sender, WaveInEventArgs e)
        => OnDataAvailable(CaptureChannel.Microphone, e);

    private void SystemDataAvailable(object? sender, WaveInEventArgs e)
        => OnDataAvailable(CaptureChannel.System, e);

    private void OnDataAvailable(CaptureChannel channel, WaveInEventArgs e)
    {
        try
        {
            lock (_stateGate)
            {
                var capture = channel == CaptureChannel.Microphone ? _microphoneCapture : _systemCapture;
                var chunker = _chunker;
                if (capture is null || chunker is null) return;

                var data = e.Buffer.AsSpan(0, e.BytesRecorded).ToArray();
                var pcm = Pcm16Processor.ConvertToMono16KhzPcm(data, capture.WaveFormat);
                if (_mixer is null)
                {
                    AppendChunk(chunker, pcm);
                    return;
                }

                _mixer.Add(
                    channel == CaptureChannel.Microphone
                        ? AudioCaptureChannel.Microphone
                        : AudioCaptureChannel.System,
                    pcm);
            }
        }
        catch
        {
            Interlocked.Increment(ref _droppedChunks);
        }
    }

    private void MixNextFrame(object? _)
    {
        if (Interlocked.Exchange(ref _mixTickActive, 1) != 0) return;
        try
        {
            lock (_stateGate)
            {
                if (_mixer is null || _chunker is null) return;
                AppendChunk(_chunker, _mixer.ReadFrame());
            }
        }
        catch
        {
            Interlocked.Increment(ref _droppedChunks);
        }
        finally
        {
            Volatile.Write(ref _mixTickActive, 0);
        }
    }

    private void AppendChunk(Pcm16Chunker chunker, byte[] pcm)
    {
        if (pcm.Length == 0) return;
        lock (_chunkerGate)
        {
            chunker.Append(pcm);
        }
    }

    public void Dispose() => Stop();

    private enum CaptureChannel
    {
        Microphone,
        System
    }
}
