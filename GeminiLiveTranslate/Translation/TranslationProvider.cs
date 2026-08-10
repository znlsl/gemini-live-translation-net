namespace GeminiLiveTranslate.Translation;

public static class TranslationProviderIds
{
    public const string Gemini = "gemini";
    public const string Soniox = "soniox";

    public static string Normalize(string? value) =>
        string.Equals(value, Soniox, StringComparison.OrdinalIgnoreCase) ? Soniox : Gemini;
}

public sealed record TranslationProviderCapabilities(bool SupportsTranslatedAudio);

public sealed record TranslationProviderDescriptor(
    string Id,
    string DisplayName,
    TranslationProviderCapabilities Capabilities);

public sealed record LiveTranslationSessionOptions(
    string ProviderId,
    string ApiKey,
    string Endpoint,
    string ProxyUrl,
    string Model,
    string TargetLanguage,
    string SystemPrompt,
    bool RequestTranslatedAudio);
