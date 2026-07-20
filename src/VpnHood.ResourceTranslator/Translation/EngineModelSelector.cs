namespace VpnHood.ResourceTranslator.Models;

public static class EngineModelSelector
{
    private const string DefaultEngine = "gemini";

    public static (string engine, string model) SelectEngineAndModel(string? requestedEngine, string? requestedModel)
    {
        // If engine is not explicitly set, auto-detect from model name
        var engine = string.IsNullOrWhiteSpace(requestedEngine)
            ? DetectEngineFromModel(requestedModel)
            : NormalizeEngine(requestedEngine);

        var model = string.IsNullOrWhiteSpace(requestedModel)
            ? GetDefaultModelForEngine(engine)
            : requestedModel;

        return (engine, model);
    }

    private static string GetDefaultModelForEngine(string engine)
    {
        return engine switch {
            "grok" => "grok-4-latest",
            "gpt" => "gpt-4o-mini",
            _ => "gemini-flash-lite-latest"
        };
    }

    private static string DetectEngineFromModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return DefaultEngine;

        var modelLower = model.ToLowerInvariant();

        if (modelLower.Contains("gemini"))
            return "gemini";

        if (modelLower.Contains("grok"))
            return "grok";

        // Default to ChatGPT for all other models
        return "gpt";
    }

    private static string NormalizeEngine(string engine)
    {
        return engine.ToLowerInvariant() switch {
            "chatgpt" or "openai" => "gpt",
            "grok-ai" or "grokai" or "x-ai" or "xai" => "grok",
            var normalized => normalized
        };
    }

    public static string GetEnvironmentVariableName(string engine)
    {
        return engine.ToLowerInvariant() switch {
            "gpt" or "chatgpt" => "OPENAI_API_KEY",
            "grok" or "grok-ai" or "x-ai" => "GROK_API_KEY",
            _ => "GEMINI_API_KEY" // fallback to Gemini
        };
    }
}
