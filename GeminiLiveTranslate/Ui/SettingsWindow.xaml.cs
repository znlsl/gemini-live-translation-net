using System.Windows;
using System.Windows.Controls;
using GeminiLiveTranslate.Settings;
using GeminiLiveTranslate.Translation;
using DrawingFont = System.Drawing.Font;
using DrawingFontStyle = System.Drawing.FontStyle;
using FormsDialogResult = System.Windows.Forms.DialogResult;
using FormsFontDialog = System.Windows.Forms.FontDialog;
using WpfComboBox = System.Windows.Controls.ComboBox;

namespace GeminiLiveTranslate.Ui;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    public SettingsWindow(AppSettings settings)
    {
        _settings = settings;
        _settings.Normalize();
        InitializeComponent();
        LoadSettings();
    }

    private void LoadSettings()
    {
        SelectTaggedCombo(ProviderBox, _settings.TranslationProvider);
        ApiKeyBox.Password = _settings.ApiKey;
        ApiBaseBox.Text = _settings.ApiBase;
        ModelBox.Text = _settings.GeminiModel;
        PromptBox.Text = _settings.SystemPrompt;
        SonioxApiKeyBox.Password = _settings.SonioxApiKey;
        SonioxEndpointBox.Text = _settings.SonioxEndpoint;
        SonioxModelBox.Text = _settings.SonioxModel;
        ProxyBox.Text = _settings.ProxyUrl;
        SelectCombo(LanguageBox, _settings.TargetLanguage);
        SelectCombo(AudioSourceBox, _settings.AudioSource);
        DeviceBox.Text = _settings.AudioDeviceNumber.ToString();
        EchoBox.IsChecked = _settings.EchoTargetLanguage;
        ShowOriginalBox.IsChecked = _settings.ShowOriginal;
        VolumeBox.Text = _settings.PlaybackVolume.ToString("0.##");
        TextRoleBox.SelectedIndex = 1;
        UpdateFontSummary();
        OpacityBox.Text = _settings.BackgroundOpacity.ToString("0.##");
        UpdateProviderPanels();
    }

    private void ProviderBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GeminiProviderPanel is not null) UpdateProviderPanels();
    }

    private void UpdateProviderPanels()
    {
        var soniox = GetSelectedProvider() == TranslationProviderIds.Soniox;
        GeminiProviderPanel.Visibility = soniox ? Visibility.Collapsed : Visibility.Visible;
        SonioxProviderPanel.Visibility = soniox ? Visibility.Visible : Visibility.Collapsed;
        EchoBox.IsEnabled = !soniox;
        ProviderCapabilityText.Text = soniox
            ? "Translated playback is not available in the Soniox Adapter yet."
            : "Gemini Live supports translated PCM audio playback.";
    }

    private void FontButton_OnClick(object sender, RoutedEventArgs e)
    {
        var appearance = GetSelectedTextAppearance();
        using var currentFont = CreateDrawingFont(appearance);
        using var dialog = new FormsFontDialog
        {
            FontMustExist = true,
            ShowColor = false,
            ShowEffects = true,
            AllowScriptChange = true,
            Font = currentFont
        };
        if (dialog.ShowDialog() != FormsDialogResult.OK) return;

        appearance.FontFamily = dialog.Font.FontFamily.Name;
        appearance.FontSize = Math.Clamp((int)Math.Round(dialog.Font.Size), 8, 60);
        appearance.FontStyle = dialog.Font.Style.ToString();
        UpdateFontSummary();
    }

    private TextAppearanceSettings GetSelectedTextAppearance()
    {
        var isSource = string.Equals(
            (TextRoleBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(),
            "Source",
            StringComparison.OrdinalIgnoreCase);
        return isSource ? _settings.SourceTextAppearance! : _settings.TranslationTextAppearance!;
    }

    private static DrawingFont CreateDrawingFont(TextAppearanceSettings appearance)
    {
        var style = Enum.TryParse<DrawingFontStyle>(appearance.FontStyle, true, out var parsed)
            ? parsed
            : DrawingFontStyle.Regular;
        try
        {
            return new DrawingFont(appearance.FontFamily, Math.Max(1, appearance.FontSize), style);
        }
        catch (ArgumentException)
        {
            return new DrawingFont("Segoe UI", Math.Max(1, appearance.FontSize), style);
        }
    }

    private void UpdateFontSummary()
    {
        var appearance = GetSelectedTextAppearance();
        FontSummaryText.Text = $"{appearance.FontFamily}, {appearance.FontSize} pt, {appearance.FontStyle}";
    }

    private void TextRoleBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FontSummaryText is not null) UpdateFontSummary();
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        _settings.TranslationProvider = GetSelectedProvider();
        _settings.ApiKey = ApiKeyBox.Password;
        _settings.ApiBase = ApiBaseBox.Text;
        _settings.GeminiModel = ModelBox.Text;
        _settings.SystemPrompt = PromptBox.Text;
        _settings.SonioxApiKey = SonioxApiKeyBox.Password;
        _settings.SonioxEndpoint = SonioxEndpointBox.Text;
        _settings.SonioxModel = SonioxModelBox.Text;
        _settings.ProxyUrl = ProxyBox.Text;
        _settings.TargetLanguage = ((ComboBoxItem?)LanguageBox.SelectedItem)?.Content?.ToString() ?? "zh-CN";
        _settings.AudioSource = ((ComboBoxItem?)AudioSourceBox.SelectedItem)?.Content?.ToString() ?? "system";
        _settings.AudioDeviceNumber = int.TryParse(DeviceBox.Text, out var device) ? device : -1;
        _settings.EchoTargetLanguage = EchoBox.IsChecked == true;
        _settings.ShowOriginal = ShowOriginalBox.IsChecked == true;
        _settings.PlaybackVolume = double.TryParse(VolumeBox.Text, out var volume) ? volume : 0.8;
        _settings.BackgroundOpacity = double.TryParse(OpacityBox.Text, out var opacity) ? opacity : 0.72;
        _settings.Normalize();
        DialogResult = true;
    }

    private string GetSelectedProvider() =>
        TranslationProviderIds.Normalize((ProviderBox.SelectedItem as ComboBoxItem)?.Tag?.ToString());

    private static void SelectTaggedCombo(WpfComboBox combo, string value)
    {
        foreach (var item in combo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
        combo.SelectedIndex = 0;
    }

    private static void SelectCombo(WpfComboBox combo, string value)
    {
        foreach (var item in combo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
        combo.SelectedIndex = 0;
    }
}
