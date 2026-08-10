using System.Text;

namespace GeminiLiveTranslate.Soniox;

internal sealed record SonioxToken(string Text, bool IsFinal, string TranslationStatus);

internal sealed record SonioxTranscriptUpdate(string? InputText, string? OutputText);

internal sealed class SonioxTranscriptAccumulator
{
    private readonly StringBuilder _finalInput = new();
    private readonly StringBuilder _finalOutput = new();

    public SonioxTranscriptUpdate Apply(IEnumerable<SonioxToken> tokens)
    {
        var interimInput = new StringBuilder();
        var interimOutput = new StringBuilder();
        var hasInput = false;
        var hasOutput = false;
        var endpointReached = false;

        foreach (var token in tokens)
        {
            if (string.IsNullOrEmpty(token.Text))
                continue;
            if (token.IsFinal && string.Equals(token.Text.Trim(), "<end>", StringComparison.OrdinalIgnoreCase))
            {
                endpointReached = true;
                continue;
            }

            var isTranslation = string.Equals(token.TranslationStatus, "translation", StringComparison.OrdinalIgnoreCase);
            var final = isTranslation ? _finalOutput : _finalInput;
            var interim = isTranslation ? interimOutput : interimInput;
            if (token.IsFinal) final.Append(token.Text);
            else interim.Append(token.Text);

            if (isTranslation) hasOutput = true;
            else hasInput = true;
        }

        var update = new SonioxTranscriptUpdate(
            hasInput ? _finalInput.ToString() + interimInput : null,
            hasOutput ? _finalOutput.ToString() + interimOutput : null);
        if (endpointReached)
        {
            _finalInput.Clear();
            _finalOutput.Clear();
        }
        return update;
    }
}
