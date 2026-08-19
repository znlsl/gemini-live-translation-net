using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using GeminiLiveTranslate.Audio;
using GeminiLiveTranslate.Diagnostics;
using GeminiLiveTranslate.Settings;
using GeminiLiveTranslate.Translation;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace GeminiLiveTranslate.Ui;

public sealed class AppController : IDisposable
{
    private readonly SettingsStore _settingsStore;
    private readonly AppSettings _settings;
    private readonly HudWindow _hud;
    private readonly LiveTranslationClient _translator;
    private readonly AudioCaptureService _capture;
    private readonly AudioPlaybackService _player;
    private NotifyIcon? _tray;
    private Icon? _trayIcon;
    private readonly DispatcherTimer _subtitleTimer;
    private readonly PendingSubtitleUpdates _pendingSubtitles = new();
    private bool _running;
    private int _activeSessionId;

    public AppController(
        SettingsStore settingsStore,
        AppSettings settings,
        HudWindow hud,
        LiveTranslationClient translator,
        AudioCaptureService capture,
        AudioPlaybackService player)
    {
        _settingsStore = settingsStore;
        _settings = settings;
        _hud = hud;
        _translator = translator;
        _capture = capture;
        _player = player;
        _subtitleTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(60)
        };
        _subtitleTimer.Tick += (_, _) => FlushSubtitleUpdates();
        WireEvents();
    }

    public void StartUi()
    {
        _trayIcon = TrayIconLoader.Load();
        _tray = new NotifyIcon
        {
            Icon = _trayIcon,
            Text = "Live Translate",
            Visible = true,
            ContextMenuStrip = BuildTrayMenu()
        };
        _tray.DoubleClick += (_, _) => ShowHud();
        WindowSizeDiagnostics.Log("start-ui-before-show", _settings, _hud);
        _hud.Show();
        WindowSizeDiagnostics.Log("start-ui-after-show", _settings, _hud);
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Start / Stop", null, (_, _) => Toggle());
        menu.Items.Add("Settings...", null, (_, _) => OpenSettings());
        menu.Items.Add("Show HUD", null, (_, _) => ShowHud());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Shutdown());
        return menu;
    }

    private void WireEvents()
    {
        _hud.ToggleRequested += Toggle;
        _hud.SettingsRequested += OpenSettings;
        _hud.ExitRequested += Shutdown;

        _translator.InputTranscript += (sessionId, text) => OnUi(() =>
        {
            if (sessionId == _activeSessionId) QueueSubtitleUpdate(input: text, output: null);
        });
        _translator.OutputTranscript += (sessionId, text) => OnUi(() =>
        {
            if (sessionId == _activeSessionId) QueueSubtitleUpdate(input: null, output: text);
        });
        _translator.AudioReceived += (sessionId, data) =>
        {
            if (sessionId == _activeSessionId) _player.EnqueuePcm16(data);
        };
        _translator.StatusChanged += (sessionId, kind, message) => OnUi(() =>
        {
            if (sessionId == _activeSessionId) _hud.SetStatus(message, kind);
        });
        _translator.StatsChanged += (sessionId, pending, dropped) => OnUi(() =>
        {
            if (sessionId == _activeSessionId) _hud.SetStats(pending, dropped);
        });
        _translator.Connected += sessionId => OnUi(() =>
        {
            if (sessionId != _activeSessionId || !_running) return;
            _hud.SetStatus($"Connected to {_settings.ActiveProviderDisplayName}", "connected");
            StartCapture(sessionId);
        });
        _translator.Disconnected += (sessionId, reason) => OnUi(() =>
        {
            if (sessionId != _activeSessionId) return;
            _capture.Stop();
            _player.Stop();
            _running = false;
            _hud.SetRunning(false);
            _hud.SetStatus(string.IsNullOrWhiteSpace(reason) ? "Stopped" : reason, string.IsNullOrWhiteSpace(reason) ? "idle" : "error");
        });
    }

    private void Toggle()
    {
        if (_running) Stop();
        else Start();
    }

    private void Start()
    {
        _settings.Normalize();
        if (string.IsNullOrWhiteSpace(_settings.ActiveApiKey))
        {
            MessageBox.Show(
                $"Set a {_settings.ActiveProviderDisplayName} API key first.",
                "Live Translate",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            OpenSettings();
            return;
        }

        var capabilities = _translator.GetCapabilities(_settings.TranslationProvider);
        var playTranslatedAudio = _settings.EchoTargetLanguage &&
                                  _settings.AudioSource != "both" &&
                                  capabilities.SupportsTranslatedAudio;
        if (playTranslatedAudio) _player.Start(_settings.PlaybackVolume);
        ClearPendingSubtitles();
        _hud.ClearTranscripts();
        _activeSessionId = _translator.Start(_settings.CreateSessionOptions(playTranslatedAudio));
        _running = true;
        _hud.SetRunning(true);
        _hud.SetStatus($"Connecting to {_settings.ActiveProviderDisplayName}...", "connecting");
    }

    private void StartCapture(int sessionId)
    {
        try
        {
            _capture.Start(_settings.AudioSource, _settings.AudioDeviceNumber, bytes => _translator.SendAudio(bytes, sessionId));
        }
        catch (Exception ex)
        {
            Stop();
            MessageBox.Show($"Audio capture failed: {ex.Message}", "Live Translate", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Stop()
    {
        _running = false;
        FlushSubtitleUpdates();
        _subtitleTimer.Stop();
        _capture.Stop();
        _player.Stop();
        _translator.Stop();
        _hud.SetRunning(false);
        _hud.SetStatus("Stopped", "idle");
    }

    private void OpenSettings()
    {
        var wasRunning = _running;
        var dialog = new SettingsWindow(_settings) { Owner = _hud };
        if (dialog.ShowDialog() == true)
        {
            _settingsStore.Save(_settings);
            _hud.ApplySettings();
            _player.SetVolume(_settings.PlaybackVolume);
            if (wasRunning)
            {
                Stop();
                Start();
            }
        }
    }

    private void ShowHud()
    {
        _hud.Show();
        if (_hud.WindowState == WindowState.Minimized) _hud.WindowState = WindowState.Normal;
        _hud.Activate();
    }

    private void Shutdown()
    {
        _hud.SavePlacement();
        _settingsStore.Save(_settings);
        Dispose();
        Application.Current.Shutdown();
    }

    private static void OnUi(Action action)
    {
        var dispatcher = Application.Current.Dispatcher;
        if (dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }

    private void QueueSubtitleUpdate(string? input, string? output)
    {
        _pendingSubtitles.Enqueue(input, output);
        if (!_subtitleTimer.IsEnabled) _subtitleTimer.Start();
    }

    private void FlushSubtitleUpdates()
    {
        var batch = _pendingSubtitles.Drain();
        foreach (var output in batch.Outputs) _hud.SetOutput(output);
        foreach (var input in batch.Inputs) _hud.SetInput(input);
        if (batch.IsEmpty) _subtitleTimer.Stop();
    }

    private void ClearPendingSubtitles()
    {
        _pendingSubtitles.Clear();
        _subtitleTimer.Stop();
    }

    public void Dispose()
    {
        Stop();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        _trayIcon?.Dispose();
        _capture.Dispose();
        _player.Dispose();
        _translator.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
