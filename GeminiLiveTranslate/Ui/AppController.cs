using System.Drawing;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Forms;
using GeminiLiveTranslate.Audio;
using GeminiLiveTranslate.Gemini;
using GeminiLiveTranslate.Settings;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace GeminiLiveTranslate.Ui;

public sealed class AppController : IDisposable
{
    private readonly SettingsStore _settingsStore;
    private readonly AppSettings _settings;
    private readonly HudWindow _hud;
    private readonly GeminiLiveClient _gemini;
    private readonly AudioCaptureService _capture;
    private readonly AudioPlaybackService _player;
    private NotifyIcon? _tray;
    private readonly DispatcherTimer _subtitleTimer;
    private readonly object _subtitleGate = new();
    private string _pendingInput = "";
    private string _pendingOutput = "";
    private bool _hasPendingInput;
    private bool _hasPendingOutput;
    private bool _running;
    private int _activeSessionId;

    public AppController(
        SettingsStore settingsStore,
        AppSettings settings,
        HudWindow hud,
        GeminiLiveClient gemini,
        AudioCaptureService capture,
        AudioPlaybackService player)
    {
        _settingsStore = settingsStore;
        _settings = settings;
        _hud = hud;
        _gemini = gemini;
        _capture = capture;
        _player = player;
        _subtitleTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(180)
        };
        _subtitleTimer.Tick += (_, _) => FlushSubtitleUpdates();
        WireEvents();
    }

    public void StartUi()
    {
        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Gemini Live Translate",
            Visible = true,
            ContextMenuStrip = BuildTrayMenu()
        };
        _tray.DoubleClick += (_, _) => ShowHud();
        _hud.Show();
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

        _gemini.InputTranscript += (sessionId, text) => OnUi(() =>
        {
            if (sessionId == _activeSessionId) QueueSubtitleUpdate(input: text, output: null);
        });
        _gemini.OutputTranscript += (sessionId, text) => OnUi(() =>
        {
            if (sessionId == _activeSessionId) QueueSubtitleUpdate(input: null, output: text);
        });
        _gemini.AudioReceived += (sessionId, data) =>
        {
            if (sessionId == _activeSessionId) _player.EnqueuePcm16(data);
        };
        _gemini.StatusChanged += (sessionId, kind, message) => OnUi(() =>
        {
            if (sessionId == _activeSessionId) _hud.SetStatus(message, kind);
        });
        _gemini.StatsChanged += (sessionId, pending, dropped) => OnUi(() =>
        {
            if (sessionId == _activeSessionId) _hud.SetStats(pending, dropped);
        });
        _gemini.Connected += sessionId => OnUi(() =>
        {
            if (sessionId != _activeSessionId) return;
            _hud.SetStatus("Connected", "connected");
            StartCapture(sessionId);
        });
        _gemini.Disconnected += (sessionId, reason) => OnUi(() =>
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
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            MessageBox.Show("Set a Gemini API key first.", "Gemini Live Translate", MessageBoxButton.OK, MessageBoxImage.Warning);
            OpenSettings();
            return;
        }

        if (_settings.EchoTargetLanguage) _player.Start(_settings.PlaybackVolume);
        ClearPendingSubtitles();
        _hud.ClearTranscripts();
        _activeSessionId = _gemini.Start(new GeminiSessionOptions(
            _settings.ApiKey,
            _settings.ApiBase,
            _settings.ProxyUrl,
            _settings.GeminiModel,
            _settings.TargetLanguage,
            _settings.SystemPrompt,
            _settings.EchoTargetLanguage));
        _running = true;
        _hud.SetRunning(true);
        _hud.SetStatus("Connecting...", "connecting");
    }

    private void StartCapture(int sessionId)
    {
        try
        {
            _capture.Start(_settings.AudioSource, _settings.AudioDeviceNumber, bytes => _gemini.SendAudio(bytes, sessionId));
        }
        catch (Exception ex)
        {
            Stop();
            MessageBox.Show($"Audio capture failed: {ex.Message}", "Gemini Live Translate", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Stop()
    {
        _running = false;
        ClearPendingSubtitles();
        _capture.Stop();
        _player.Stop();
        _gemini.Stop();
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
        lock (_subtitleGate)
        {
            if (input is not null)
            {
                _pendingInput = input;
                _hasPendingInput = true;
            }
            if (output is not null)
            {
                _pendingOutput = output;
                _hasPendingOutput = true;
            }
        }

        if (!_subtitleTimer.IsEnabled) _subtitleTimer.Start();
    }

    private void FlushSubtitleUpdates()
    {
        string input;
        string output;
        bool hasInput;
        bool hasOutput;
        lock (_subtitleGate)
        {
            input = _pendingInput;
            output = _pendingOutput;
            hasInput = _hasPendingInput;
            hasOutput = _hasPendingOutput;
            _pendingInput = "";
            _pendingOutput = "";
            _hasPendingInput = false;
            _hasPendingOutput = false;
        }

        if (hasOutput) _hud.SetOutput(output);
        if (hasInput) _hud.SetInput(input);
        if (!hasInput && !hasOutput) _subtitleTimer.Stop();
    }

    private void ClearPendingSubtitles()
    {
        lock (_subtitleGate)
        {
            _pendingInput = "";
            _pendingOutput = "";
            _hasPendingInput = false;
            _hasPendingOutput = false;
        }
        _subtitleTimer.Stop();
    }

    public void Dispose()
    {
        Stop();
        _tray?.Dispose();
        _capture.Dispose();
        _player.Dispose();
        _gemini.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
