using VpnHood.Tools.ResourceTranslator.Translation;

namespace VpnHood.Tools.ResourceTranslator.Tests;

/// <summary>
/// Deterministic stand-in for a real engine: echoes each item back with a language marker,
/// so tests can assert exactly which entries were sent for translation.
/// </summary>
public sealed class FakeTranslator : ITranslator
{
    private readonly Func<TranslateItem, string>? _translate;

    public FakeTranslator(Func<TranslateItem, string>? translate = null)
    {
        _translate = translate;
    }

    /// <summary>Every key handed to the translator, in call order.</summary>
    public List<string> TranslatedKeys { get; } = [];

    public int CallCount { get; private set; }

    public Task<TranslateResult[]> TranslateAsync(PromptOptions promptOptions, CancellationToken cancellationToken)
    {
        CallCount++;

        var results = promptOptions.Items.Select(item => {
            TranslatedKeys.Add(item.Key);
            return new TranslateResult {
                Key = item.Key,
                SourceText = item.Text,
                SourceLanguage = item.SourceLanguage,
                TargetLanguage = item.TargetLanguage,
                TranslatedText = _translate?.Invoke(item) ?? $"[{item.TargetLanguage}] {item.Text}"
            };
        }).ToArray();

        return Task.FromResult(results);
    }
}
