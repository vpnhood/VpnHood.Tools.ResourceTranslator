namespace VpnHood.ResourceTranslator.Translation;

/// <summary>
/// Resolves which engine and model to use from (optionally absent) user input.
/// The engine is inferred from the model name when it is not stated explicitly.
/// </summary>
public static class EngineModelSelector
{
    private const TranslationEngine DefaultEngine = TranslationEngine.Gemini;

    private static readonly Dictionary<string, TranslationEngine> EngineAliases = new(StringComparer.OrdinalIgnoreCase) {
        ["gemini"] = TranslationEngine.Gemini,
        ["google"] = TranslationEngine.Gemini,
        ["gpt"] = TranslationEngine.Gpt,
        ["chatgpt"] = TranslationEngine.Gpt,
        ["openai"] = TranslationEngine.Gpt,
        ["grok"] = TranslationEngine.Grok,
        ["grok-ai"] = TranslationEngine.Grok,
        ["grokai"] = TranslationEngine.Grok,
        ["x-ai"] = TranslationEngine.Grok,
        ["xai"] = TranslationEngine.Grok
    };

    /// <summary>Engine names accepted on the command line, for help text and error messages.</summary>
    public static IReadOnlyCollection<string> PublicEngineNames { get; } = ["gemini", "gpt", "grok"];

    public static EngineSelection Select(string? requestedEngine, string? requestedModel)
    {
        var engine = string.IsNullOrWhiteSpace(requestedEngine)
            ? DetectEngineFromModel(requestedModel)
            : ParseEngine(requestedEngine);

        var model = string.IsNullOrWhiteSpace(requestedModel)
            ? GetDefaultModel(engine)
            : requestedModel;

        return new EngineSelection(engine, model);
    }

    /// <summary>Maps an engine name or alias onto <see cref="TranslationEngine" />, failing loudly when unknown.</summary>
    public static TranslationEngine ParseEngine(string engine)
    {
        return TryParseEngine(engine, out var parsed)
            ? parsed
            : throw new ArgumentException(DescribeUnknownEngine(engine), nameof(engine));
    }

    public static bool TryParseEngine(string? engine, out TranslationEngine parsed)
    {
        if (!string.IsNullOrWhiteSpace(engine))
            return EngineAliases.TryGetValue(engine.Trim(), out parsed);

        parsed = default;
        return false;
    }

    /// <summary>Message for an unrecognised engine, without the exception's parameter suffix.</summary>
    public static string DescribeUnknownEngine(string? engine)
    {
        return $"Unknown engine '{engine}'. Supported engines: {string.Join(", ", PublicEngineNames)}.";
    }

    public static string GetDefaultModel(TranslationEngine engine)
    {
        return engine switch {
            TranslationEngine.Grok => "grok-4-latest",
            TranslationEngine.Gpt => "gpt-4o-mini",
            TranslationEngine.Gemini => "gemini-flash-lite-latest",
            _ => throw new ArgumentOutOfRangeException(nameof(engine), engine, "Unhandled engine.")
        };
    }

    public static string GetApiKeyVariableName(TranslationEngine engine)
    {
        return engine switch {
            TranslationEngine.Gpt => "OPENAI_API_KEY",
            TranslationEngine.Grok => "GROK_API_KEY",
            TranslationEngine.Gemini => "GEMINI_API_KEY",
            _ => throw new ArgumentOutOfRangeException(nameof(engine), engine, "Unhandled engine.")
        };
    }

    private static TranslationEngine DetectEngineFromModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return DefaultEngine;

        if (model.Contains("gemini", StringComparison.OrdinalIgnoreCase))
            return TranslationEngine.Gemini;

        if (model.Contains("grok", StringComparison.OrdinalIgnoreCase))
            return TranslationEngine.Grok;

        // Everything else is assumed to be an OpenAI-compatible chat model.
        return TranslationEngine.Gpt;
    }
}
