namespace GeminiLiveTranslate.Settings;

public sealed class AppSettings
{
    public string ApiKey { get; set; } = "";
    public string ApiBase { get; set; } = "https://generativelanguage.googleapis.com";
    public string ProxyUrl { get; set; } = "";
    public string TargetLanguage { get; set; } = "zh-CN";
    public string AudioSource { get; set; } = "system";
    public int AudioDeviceNumber { get; set; } = -1;
    public int FontSize { get; set; } = 14;
    public string FontStyle { get; set; } = "Regular";
    public string FontFamily { get; set; } = "Segoe UI";
    public TextAppearanceSettings? SourceTextAppearance { get; set; }
    public TextAppearanceSettings? TranslationTextAppearance { get; set; }
    public double BackgroundOpacity { get; set; } = 0.72;
    public bool EchoTargetLanguage { get; set; }
    public double PlaybackVolume { get; set; } = 0.8;
    public string SystemPrompt { get; set; } = "";
    public bool ShowOriginal { get; set; }
    public string GeminiModel { get; set; } = "models/gemini-3.5-live-translate-preview";
    public WindowPlacement Hud { get; set; } = new();

    public void Normalize()
    {
        ApiKey = ApiKey.Trim();
        ApiBase = string.IsNullOrWhiteSpace(ApiBase) ? "https://generativelanguage.googleapis.com" : ApiBase.Trim();
        ProxyUrl = ProxyUrl.Trim();
        TargetLanguage = string.IsNullOrWhiteSpace(TargetLanguage) ? "zh-CN" : TargetLanguage.Trim();
        AudioSource = AudioSource is "mic" or "system" or "both" ? AudioSource : "system";
        FontSize = Math.Clamp(FontSize, 8, 60);
        FontStyle = string.IsNullOrWhiteSpace(FontStyle) ? "Regular" : FontStyle.Trim();
        FontFamily = string.IsNullOrWhiteSpace(FontFamily) ? "Segoe UI" : FontFamily.Trim();
        var legacyAppearance = new TextAppearanceSettings
        {
            FontSize = FontSize,
            FontStyle = FontStyle,
            FontFamily = FontFamily
        };
        SourceTextAppearance ??= legacyAppearance.Clone();
        TranslationTextAppearance ??= legacyAppearance.Clone();
        SourceTextAppearance.Normalize();
        TranslationTextAppearance.Normalize();
        BackgroundOpacity = Math.Clamp(BackgroundOpacity, 0.2, 0.95);
        PlaybackVolume = Math.Clamp(PlaybackVolume, 0, 1);
        GeminiModel = string.IsNullOrWhiteSpace(GeminiModel) ? "models/gemini-3.5-live-translate-preview" : GeminiModel.Trim();
    }
}

public sealed class TextAppearanceSettings
{
    public int FontSize { get; set; } = 14;
    public string FontStyle { get; set; } = "Regular";
    public string FontFamily { get; set; } = "Segoe UI";

    public TextAppearanceSettings Clone() => new()
    {
        FontSize = FontSize,
        FontStyle = FontStyle,
        FontFamily = FontFamily
    };

    public void Normalize()
    {
        FontSize = Math.Clamp(FontSize, 8, 60);
        FontStyle = string.IsNullOrWhiteSpace(FontStyle) ? "Regular" : FontStyle.Trim();
        FontFamily = string.IsNullOrWhiteSpace(FontFamily) ? "Segoe UI" : FontFamily.Trim();
    }
}

public sealed class WindowPlacement
{
    public double Left { get; set; } = 120;
    public double Top { get; set; } = 120;
    public double Width { get; set; }
    public double Height { get; set; }
}
