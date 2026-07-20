using VpnHood.ResourceTranslator.Translation;

namespace VpnHood.ResourceTranslator.Configuration;

/// <summary>
/// Fully resolved settings for a run: command-line values merged over the config file,
/// with defaults applied and paths made absolute.
/// </summary>
public sealed class TranslatorOptions
{
    public required string BasePath { get; init; }
    public required TranslationEngine Engine { get; init; }
    public required string Model { get; init; }
    public required int BatchSize { get; init; }

    /// <summary>Absolute path to extra prompt instructions, or null when none apply.</summary>
    public string? ExtraPromptPath { get; init; }

    /// <summary>Explicit target languages from config; empty means "discover sibling locale files".</summary>
    public IReadOnlyList<string> Languages { get; init; } = [];

    /// <summary>
    /// Null until a translating command needs it — <c>--show-changes</c> and
    /// <c>--ignore-changes</c> deliberately work without any API key.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>Config file this run picked up, for diagnostics. Null when none was found.</summary>
    public string? ConfigPath { get; init; }

    public string GetRequiredApiKey()
    {
        if (!string.IsNullOrWhiteSpace(ApiKey))
            return ApiKey;

        var variableName = EngineModelSelector.GetApiKeyVariableName(Engine);
        throw new TranslatorException(
            $"Missing API key. Provide it via --api-key or the {variableName} environment variable.",
            ExitCodes.MissingApiKey);
    }
}
