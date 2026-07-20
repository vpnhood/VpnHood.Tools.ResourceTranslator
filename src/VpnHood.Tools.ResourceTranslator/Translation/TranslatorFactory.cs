namespace VpnHood.ResourceTranslator.Translation;

public static class TranslatorFactory
{
    public static ITranslator Create(TranslationEngine engine, string apiKey, string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        return engine switch {
            TranslationEngine.Gpt => new ChatGptTranslator(apiKey, model),
            TranslationEngine.Gemini => new GeminiTranslator(apiKey, model),
            TranslationEngine.Grok => new GrokAiTranslator(apiKey, model),
            _ => throw new ArgumentOutOfRangeException(nameof(engine), engine, "Unhandled engine.")
        };
    }
}
