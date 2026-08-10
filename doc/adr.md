# Architecture Decision Record

## ADR-001: Rewrite target

Status: accepted

Decision: rewrite `gemini-live-translate` as a Windows-first C#/.NET 8 desktop app using WPF.

Reasons:

- The app targets Windows 11 and depends on Windows audio APIs, tray behavior, transparent topmost windows, and desktop packaging.
- .NET has first-class Windows Desktop support and a mature runtime already available on the target OS.
- WPF is a stable fit for the floating HUD, settings dialog, resize/drag behavior, and DPI-aware text rendering.
- C# provides better runtime efficiency and lower packaging risk than Python + PySide6 for this app.

Rejected alternatives:

- Rust: better raw efficiency, but higher GUI/audio integration cost.
- Go: good runtime profile, weaker Windows desktop GUI/audio ecosystem.
- Electron/TypeScript: easier UI iteration, but high memory use for a small tray/HUD utility.
- C++/Qt: powerful but higher maintenance and packaging complexity.

## ADR-002: Windows audio stack

Status: accepted

Decision: use NAudio for WASAPI loopback capture, microphone capture, and PCM playback.

Reasons:

- NAudio is mature and widely used for Windows audio.
- It exposes `WasapiLoopbackCapture`, microphone capture, and `WaveOutEvent` playback without requiring custom COM interop.
- It keeps the first version focused on product behavior rather than low-level WASAPI wrappers.

Tradeoff:

- The initial resampler is linear and simpler than Python's SciPy polyphase resampler. If recognition quality suffers, replace it with a higher-quality resampler while keeping the same `AudioCaptureService` boundary.

## ADR-003: Gemini Live protocol

Status: accepted

Decision: implement Gemini Live over raw `ClientWebSocket` rather than relying on a high-level SDK wrapper.

Reasons:

- The existing Python and Edge projects both use the raw WebSocket protocol successfully.
- Raw WebSocket keeps support for custom API base URLs and proxies.
- The protocol surface needed here is small: setup message, PCM16 chunks, transcript messages, returned audio chunks, reconnect/backpressure.

## ADR-004: Settings persistence

Status: accepted

Decision: store settings as JSON under `%APPDATA%\gemini-live-translate-dotnet\settings.json`.

Reasons:

- Matches Windows application conventions.
- Keeps migration and debugging simple.

Follow-up:

- API keys are currently stored in JSON to match the Python app behavior. Replace with Windows DPAPI or Credential Manager before distribution.

## ADR-005: Swappable live translation providers

Status: accepted

Decision: route live translation through a deep `LiveTranslationClient` Module with an internal provider seam. Gemini Live and Soniox are separate Adapters at that seam.

Reasons:

- Provider switching changes authentication, WebSocket protocol, model configuration, transcript semantics, reconnect behavior, and translated-audio support; it is more than a model-name change.
- A common session Interface keeps provider knowledge out of `AppController` and gives tests one seam for selection, session identity, audio forwarding, and capability checks.
- Two Adapters make the seam real: Gemini preserves the raw `ClientWebSocket` decision in ADR-003, while Soniox uses its own raw WebSocket protocol.

Capability decision:

- Translated audio is optional. Gemini advertises it; the initial Soniox Adapter provides low-latency source and translated subtitles only.
- Soniox speech-to-speech translation requires a second TTS WebSocket and can be added behind the same provider seam later.

Operational decision:

- Switching providers stops the current session and starts a new one.
- Do not silently fail over between providers because that changes billing, privacy, and output semantics.
