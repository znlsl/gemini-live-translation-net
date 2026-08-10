using GeminiLiveTranslate.Translation;

namespace GeminiLiveTranslate.Settings;

public sealed class AppSettings
{
    public string TranslationProvider { get; set; } = TranslationProviderIds.Gemini;
    public string ApiKey { get; set; } = "";
    public string ApiBase { get; set; } = "https://generativelanguage.googleapis.com";
    public string GeminiModel { get; set; } = "models/gemini-3.5-live-translate-preview";
    public string SonioxApiKey { get; set; } = "";
    public string SonioxEndpoint { get; set; } = "wss://stt-rt.soniox.com/transcribe-websocket";
    public string SonioxModel { get; set; } = "stt-rt-v5";
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
    public WindowPlacement Hud { get; set; } = new();

    public string ActiveApiKey => TranslationProvider == TranslationProviderIds.Soniox ? SonioxApiKey : ApiKey;

    public string ActiveProviderDisplayName =>
        TranslationProvider == TranslationProviderIds.Soniox ? "Soniox" : "Gemini Live";

    public LiveTranslationSessionOptions CreateSessionOptions(bool requestTranslatedAudio)
    {
        var soniox = TranslationProvider == TranslationProviderIds.Soniox;
        return new LiveTranslationSessionOptions(
            TranslationProvider,
            soniox ? SonioxApiKey : ApiKey,
            soniox ? SonioxEndpoint : ApiBase,
            ProxyUrl,
            soniox ? SonioxModel : GeminiModel,
            TargetLanguage,
            soniox ? "" : SystemPrompt,
            requestTranslatedAudio);
    }

    public void Normalize()
    {
        TranslationProvider = TranslationProviderIds.Normalize(TranslationProvider);
        ApiKey = (ApiKey ?? "").Trim();
        ApiBase = string.IsNullOrWhiteSpace(ApiBase) ? "https://generativelanguage.googleapis.com" : ApiBase.Trim();
        GeminiModel = string.IsNullOrWhiteSpace(GeminiModel) ? "models/gemini-3.5-live-translate-preview" : GeminiModel.Trim();
        SonioxApiKey = (SonioxApiKey ?? "").Trim();
        SonioxEndpoint = string.IsNullOrWhiteSpace(SonioxEndpoint)
            ? "wss://stt-rt.soniox.com/transcribe-websocket"
            : SonioxEndpoint.Trim();
        SonioxModel = string.IsNullOrWhiteSpace(SonioxModel) ? "stt-rt-v5" : SonioxModel.Trim();
        ProxyUrl = (ProxyUrl ?? "").Trim();
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
        SystemPrompt = (SystemPrompt ?? "").Trim();
        Hud ??= new WindowPlacement();
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
