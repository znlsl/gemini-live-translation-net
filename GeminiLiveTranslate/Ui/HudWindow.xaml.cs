using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;
using GeminiLiveTranslate.Settings;
using MediaColor = System.Windows.Media.Color;

namespace GeminiLiveTranslate.Ui;

public partial class HudWindow : Window
{
    private readonly AppSettings _settings;
    private readonly RollingTextTrack _translationTrack = new();
    private readonly RollingTextTrack _sourceTrack = new();

    public event Action? ToggleRequested;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    public HudWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        ApplySettings();
    }

    public void ApplySettings()
    {
        var workArea = SystemParameters.WorkArea;
        var width = _settings.Hud.Width;
        var height = _settings.Hud.Height;
        if (width <= 0 || height <= 0)
        {
            width = workArea.Width / 3;
            height = workArea.Height / 4;
            _settings.Hud.Width = width;
            _settings.Hud.Height = height;
            _settings.Hud.Left = workArea.Left + (workArea.Width - width) / 2;
            _settings.Hud.Top = workArea.Top + (workArea.Height - height) / 2;
        }

        Width = width;
        Height = height;
        Left = _settings.Hud.Left;
        Top = _settings.Hud.Top;
        OutputText.FontSize = _settings.FontSize;
        try
        {
            var fontFamily = new System.Windows.Media.FontFamily(_settings.FontFamily);
            OutputText.FontFamily = fontFamily;
            InputText.FontFamily = fontFamily;
        }
        catch (ArgumentException)
        {
            var fallback = new System.Windows.Media.FontFamily("Segoe UI");
            OutputText.FontFamily = fallback;
            InputText.FontFamily = fallback;
        }
        ApplyFontStyle();
        InputText.Visibility = _settings.ShowOriginal ? Visibility.Visible : Visibility.Collapsed;
        InputScroll.Visibility = _settings.ShowOriginal ? Visibility.Visible : Visibility.Collapsed;
        LaneDivider.Visibility = _settings.ShowOriginal ? Visibility.Visible : Visibility.Collapsed;
        var opacity = (byte)(Math.Clamp(_settings.BackgroundOpacity, 0.2, 0.95) * 255);
        RootPanel.Background = new SolidColorBrush(MediaColor.FromArgb(opacity, 17, 24, 39));
    }
    private void ApplyFontStyle()
    {
        var style = _settings.FontStyle;
        var weight = style.Contains("Bold", StringComparison.OrdinalIgnoreCase) ? FontWeights.Bold : FontWeights.Normal;
        var fontStyle = style.Contains("Italic", StringComparison.OrdinalIgnoreCase) ? FontStyles.Italic : FontStyles.Normal;
        var decorations = new TextDecorationCollection();
        if (style.Contains("Underline", StringComparison.OrdinalIgnoreCase)) decorations.Add(TextDecorations.Underline[0]);
        if (style.Contains("Strikeout", StringComparison.OrdinalIgnoreCase)) decorations.Add(TextDecorations.Strikethrough[0]);
        OutputText.FontWeight = weight;
        InputText.FontWeight = weight;
        OutputText.FontStyle = fontStyle;
        InputText.FontStyle = fontStyle;
        OutputText.TextDecorations = decorations;
        InputText.TextDecorations = decorations;
    }

    public void SavePlacement()
    {
        _settings.Hud.Left = Left;
        _settings.Hud.Top = Top;
        _settings.Hud.Width = Width;
        _settings.Hud.Height = Height;
    }

    public void SetRunning(bool running) => ToggleButton.Content = running ? "Stop" : "Start";

    public void SetStatus(string message, string kind)
    {
        StatusText.Text = message;
        StatusDot.Fill = new SolidColorBrush(kind switch
        {
            "connected" => MediaColor.FromRgb(34, 197, 94),
            "connecting" => MediaColor.FromRgb(250, 204, 21),
            "error" => MediaColor.FromRgb(248, 113, 113),
            "warning" => MediaColor.FromRgb(251, 146, 60),
            _ => MediaColor.FromRgb(100, 116, 139)
        });
    }

    public void ClearTranscripts()
    {
        _translationTrack.Clear();
        _sourceTrack.Clear();
        OutputText.Text = "Ready";
        InputText.Text = "";
        OutputScroll.ScrollToEnd();
        InputScroll.ScrollToEnd();
    }

    public void SetInput(string text)
    {
        InputText.Text = _sourceTrack.Update(text);
        InputScroll.ScrollToEnd();
    }

    public void SetOutput(string text)
    {
        OutputText.Text = _translationTrack.Update(text);
        OutputScroll.ScrollToEnd();
    }
    public void SetStats(int pending, int dropped) => StatsText.Text = $"Pending: {pending} / Dropped: {dropped}";

    private void ToggleButton_OnClick(object sender, RoutedEventArgs e) => ToggleRequested?.Invoke();
    private void SettingsButton_OnClick(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();
    private void ExitButton_OnClick(object sender, RoutedEventArgs e) => ExitRequested?.Invoke();
    private void MinimizeButton_OnClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void DragBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void RootPanel_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed || IsInteractiveElement(e.OriginalSource as DependencyObject)) return;
        DragMove();
    }

    private static bool IsInteractiveElement(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase
                or System.Windows.Controls.Primitives.TextBoxBase
                or System.Windows.Controls.Primitives.ScrollBar
                or System.Windows.Controls.ComboBox
                or System.Windows.Controls.PasswordBox)
            {
                return true;
            }
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }
}
