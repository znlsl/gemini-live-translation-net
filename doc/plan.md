# Plan

## Progress

- [x] Create remote Windows workspace under `D:\work\ai\dev\gemini-live-translate-dotnet`.
- [x] Install workspace-local .NET 8 SDK.
- [x] Add WPF project scaffold.
- [x] Add architecture decision document.
- [x] Add module design document.
- [x] Add progress plan document.
- [x] Implement settings load/save.
- [x] Implement Gemini Live WebSocket client.
- [x] Implement NAudio capture/playback layer.
- [x] Implement transparent HUD window.
- [x] Implement settings window.
- [x] Implement tray/controller lifecycle.
- [x] Restore NuGet packages on Windows.
- [x] Fix compile errors from first build.
- [x] Verify `dotnet build` succeeds on Windows.
- [x] Publish Release build.
- [x] Diagnose HUD overwrite/source-caption freeze/window movement issues.
- [x] Implement rolling upper/lower subtitle tracks.
- [x] Make the HUD body draggable, excluding interactive controls.
- [x] Rebuild and republish after HUD fixes.
- [x] Diagnose growing subtitle latency.
- [x] Replace per-chunk audio send tasks with a bounded realtime queue.
- [x] Coalesce high-frequency subtitle updates before touching WPF text.
- [x] Rebuild, republish, and commit latency fix.
- [x] Diagnose no-subtitle regression from sender loop startup timing.
- [x] Move audio sender startup after Gemini setup and publish fix.
- [x] Add settings UI hint for HTTP-only proxy support.
- [x] Change default HUD font size to 15.
- [x] Add self-contained Windows publish output.
- [x] Add GitHub Actions workflow for automatic Windows exe artifacts.
- [x] Create GitHub Release automatically when pushing version tags.
- [x] Add README instructions for release ZIP usage.
- [x] Add a live translation provider seam with Gemini and Soniox Adapters.
- [x] Add Soniox low-latency source and translated subtitle streaming.
- [x] Add provider selection, provider-specific credentials, and capability-aware playback settings.
- [ ] Run live smoke test with microphone.
- [ ] Run live smoke test with WASAPI loopback.
- [ ] Add DPAPI/Credential Manager storage for API key.
- [ ] Improve resampling quality if needed.
- [ ] Add release publish script.

## Next verification checklist

- Build succeeds with `dotnet build`.
- App starts without API key and opens settings.
- Settings are saved to `%APPDATA%\gemini-live-translate-dotnet\settings.json`.
- Start validates missing API key.
- WebSocket setup reaches `setupComplete`.
- Audio capture starts only after Gemini connection is ready.
- Backpressure drops chunks instead of growing memory.
- Returned audio plays when interpretation is enabled.
- HUD remains topmost and draggable.
