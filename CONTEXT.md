# Domain Context

## Live Translation Session

A live translation session accepts a continuous 16 kHz mono PCM16 audio stream and publishes source transcripts, translated transcripts, connection state, and realtime queue statistics. A session may also publish translated 24 kHz PCM16 audio when its selected Translation Provider supports that capability.

Changing the Translation Provider stops the current live translation session and starts a new one. Session identity prevents late events from an older session from reaching the HUD or audio playback.

## Translation Provider

A Translation Provider is an external realtime engine used by a live translation session. Each provider owns its authentication, endpoint, model, protocol messages, transcript semantics, reconnect behavior, and supported capabilities.

Current providers:

- Gemini Live: source text, translated text, and optional translated audio.
- Soniox: source text and translated text. Soniox translated audio requires a separate TTS stream and is not enabled yet.

Provider selection is explicit. The application does not automatically fail over because doing so would change billing, privacy, and translation behavior.
