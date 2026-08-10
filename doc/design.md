# Design

## Runtime flow

```text
WPF HUD / tray
  -> AppController
  -> LiveTranslationClient
  -> selected provider Adapter
     -> Gemini Live WebSocket
     -> Soniox STT/translation WebSocket

AudioCaptureService
  -> NAudio WASAPI loopback or mic
  -> Pcm16Processor
  -> Pcm16Chunker
  -> LiveTranslationClient.SendAudio()

LiveTranslationClient
  -> transcript events
  -> HUD text
  -> optional translated PCM16 audio
  -> AudioPlaybackService
```

## Modules

### `Ui`

- `HudWindow`: transparent topmost subtitle window.
- `SettingsWindow`: selects a Translation Provider and edits provider, language, proxy, audio, and HUD settings.
- `AppController`: orchestrates tray menu, start/stop lifecycle, settings persistence, audio capture, playback, and provider-neutral translation events.

### `Settings`

- `AppSettings`: serializable runtime settings.
- `SettingsStore`: JSON load/save under `%APPDATA%`.

### `Translation`

- `LiveTranslationClient`: deep Module that selects one Adapter, owns cross-provider session identity, routes events, and exposes provider capabilities.
- `ILiveTranslationAdapter`: internal provider seam used by the Gemini and Soniox Adapters.
- `RealtimeAudioBuffer`: shared bounded low-latency queue that drops old audio under backpressure.
- `RealtimeWebSocket`: shared WebSocket proxy, keepalive, and text-frame handling.

### `Gemini`

- `GeminiLiveClient`: Gemini Adapter for the live translation provider seam.

Responsibilities:

- Build WebSocket URL from API base.
- Send setup message.
- Send 16 kHz mono PCM16 audio chunks.
- Parse input/output transcripts.
- Parse returned PCM16 audio.
- Reconnect with bounded exponential delay.
- Drop audio chunks under send backpressure.

### `Soniox`

- `SonioxLiveClient`: Soniox realtime STT/translation Adapter.
- `SonioxTranscriptAccumulator`: merges final and revisable tokens into stable source and translated text streams.

Responsibilities:

- Send raw 16 kHz mono PCM16 as binary WebSocket frames.
- Configure one-way translation with endpoint detection and a 500 ms maximum endpoint delay.
- Separate original and translated tokens using `translation_status`.
- Preserve final tokens while replacing non-final tokens as Soniox revisions arrive.
- Reconnect with bounded exponential delay and share the same realtime backpressure policy as Gemini.

Translated Soniox audio is not implemented yet. It requires a second TTS WebSocket that consumes translation tokens and publishes 24 kHz PCM16 behind the existing optional-audio capability.

### `Audio`

- `AudioCaptureService`: starts/stops WASAPI loopback or microphone capture and runs a clocked non-blocking mixer when both are selected.
- `Pcm16Processor`: converts captured bytes into 16 kHz mono PCM16.
- `Pcm16Mixer`: emits 20 ms frames without waiting for an inactive source and bounds each source to 200 ms of buffered audio.
- `Pcm16Chunker`: emits fixed 3200-byte (100 ms) chunks, matching Gemini Live Translation guidance.
- `AudioPlaybackService`: plays returned 24 kHz PCM16 through NAudio.

## Subtitle export design (planned)

The application should optionally persist final translated text as SubRip (`.srt`) subtitles during each translation session. This section records the design for a later implementation.

### Translation segment model

The translation pipeline should publish a stable segment event rather than writing directly to a file:

- `Sequence`: monotonically increasing segment number within a session and language.
- `TargetLanguage`: normalized language tag such as `zh-CN` or `en-US`.
- `StartTime` and `EndTime`: subtitle timing relative to the session start.
- `Text`: final translated text.
- `IsFinal`: distinguishes a committed translation from an interim result.

Interim results should update the HUD only. Only final results should be persisted, preventing duplicate subtitles when a live translation is revised.

### Session and file management

`SubtitleSessionManager` should own the active export session and maintain one `SrtWriter` per target language. A multilingual session therefore produces separate files, for example:

```text
translation_20260804_094200_zh-CN.srt
translation_20260804_094200_en-US.srt
translation_20260804_094200_ja-JP.srt
```

The manager should create writers when `Start` begins, flush and close them when `Stop` completes, and prevent a new session from overwriting an older session. The default output directory should be configurable and initially default to `%APPDATA%\\gemini-live-translate-dotnet\\translations\\`.

### SRT writer responsibilities

`SrtWriter` should serialize each committed segment as:

```text
1
00:00:01,000 --> 00:00:03,500
Translated text.
```

It should own subtitle numbering, SRT timestamp formatting, UTF-8 encoding, text cleanup, and serialized/asynchronous writes. Each committed segment should be flushed promptly so that an unexpected application exit does not lose the complete session history already received.

### Timing strategy

The preferred timestamps are the speech segment start and end times provided by the recognition layer. If an end time is unavailable, use the next segment's start time when possible. For the final segment, use a conservative duration estimate based on text length until accurate audio timing is available.

### Proposed runtime flow

```text
LiveTranslationClient final translation event
  -> TranslationSegment
  -> SubtitleSessionManager
  -> SrtWriter for TargetLanguage
  -> language-specific .srt file
```

The export feature should be controlled by settings, expose the output directory in the settings UI, and provide the active session file paths or an "open output folder" action after stopping.

## Current limitations

- Subtitle export is designed above but not implemented yet.
- Soniox translated audio playback is not implemented yet; the Soniox Adapter currently provides source and translated subtitles.
- No DPAPI protection for provider API keys yet.
- Resampling uses linear interpolation. This is buildable and low-cost, but not final quality.
- Audio device selection is minimal: default system loopback or microphone device number.
- No installer; publish output is the first distribution unit.
- Proxy settings currently use .NET `WebProxy`, so the settings UI treats `host:port` as HTTP proxy syntax. SOCKS proxy support is not implemented yet.

## Build

Remote workspace:

```text
D:\work\ai\dev\gemini-live-translate-dotnet
```

Use the workspace-local SDK:

```powershell
$env:DOTNET_ROOT='D:\work\ai\dev\gemini-live-translate-dotnet\.dotnet'
$env:PATH="$env:DOTNET_ROOT;$env:PATH"
dotnet build .\GeminiLiveTranslate.sln
```

Publish:

```powershell
dotnet publish .\GeminiLiveTranslate\GeminiLiveTranslate.csproj -c Release -r win-x64 --self-contained false
```

Self-contained publish, for machines without a separately installed .NET Desktop Runtime:

```powershell
dotnet publish .\GeminiLiveTranslate\GeminiLiveTranslate.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```
