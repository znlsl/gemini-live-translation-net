using System.Windows;
using GeminiLiveTranslate.Audio;
using GeminiLiveTranslate.Gemini;
using GeminiLiveTranslate.Settings;
using GeminiLiveTranslate.Soniox;
using GeminiLiveTranslate.Translation;
using GeminiLiveTranslate.Ui;

namespace GeminiLiveTranslate;

public partial class App : System.Windows.Application
{
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var settingsStore = new SettingsStore();
        var settings = settingsStore.Load();
        var hud = new HudWindow(settings);
        var translator = new LiveTranslationClient([
            new GeminiLiveClient(),
            new SonioxLiveClient()
        ]);
        var audio = new AudioCaptureService();
        var player = new AudioPlaybackService();
        _controller = new AppController(settingsStore, settings, hud, translator, audio, player);
        _controller.StartUi();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        base.OnExit(e);
    }
}
