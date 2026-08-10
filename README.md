# Gemini Live Translate .NET

Windows 11 desktop client for realtime speech translation. It captures system audio or a microphone, sends 16 kHz PCM16 audio to a selected Translation Provider, and displays live source and translated subtitles in a floating HUD.

Supported providers:

- Gemini Live: translated subtitles and optional translated audio playback.
- Soniox: low-latency translated subtitles. Soniox TTS playback is planned but not enabled yet.

## Download and Run

Open the GitHub Releases page and download one of the ZIP files:

- `GeminiLiveTranslate-win-x64.zip`
  - Smaller download.
  - Requires the .NET 8 Desktop Runtime to be installed on the Windows machine.
  - Extract the whole ZIP folder, then run `GeminiLiveTranslate.exe` from the extracted folder.

- `GeminiLiveTranslate-win-x64-self-contained.zip`
  - Larger download.
  - Does not require a separately installed .NET runtime.
  - Extract the whole ZIP folder, then run `GeminiLiveTranslate.exe` from the extracted folder.

Do not run the executable directly from inside the ZIP preview window. Extract the ZIP first so the executable can load its companion files correctly.

## First Use

1. Run `GeminiLiveTranslate.exe`.
2. Open `Settings`.
3. Choose `Gemini Live` or `Soniox` as the Translation Provider.
4. Enter the API key for the selected provider.
5. Choose the target language and audio source.
6. Click `Start`.

Changing the provider while translation is running stops the current session and starts a new session with the selected provider.

Settings are saved under:

```text
%APPDATA%\gemini-live-translate-dotnet\settings.json
```

## Proxy Setting

The `Proxy URL` field currently supports HTTP proxy syntax.

Examples:

```text
http://127.0.0.1:7890
sercomm.f0g.dev:2802
```

If only `host:port` is entered, it is treated as HTTP. SOCKS proxy support is not implemented yet.

## Build

Framework-dependent publish:

```powershell
dotnet publish .\GeminiLiveTranslate\GeminiLiveTranslate.csproj -c Release -r win-x64 --self-contained false
```

Self-contained publish:

```powershell
dotnet publish .\GeminiLiveTranslate\GeminiLiveTranslate.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## Release Workflow

Pushing a version tag such as `v0.1.0` triggers GitHub Actions to build and publish release ZIP files.
