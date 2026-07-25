using VpnHood.Tools.ResourceTranslator.Translation;

namespace VpnHood.Tools.ResourceTranslator.Site;

/// <summary>
/// Fully resolved settings for a site run: command-line values merged over the config file's
/// <c>site</c> section, with defaults applied and paths made absolute.
/// </summary>
public sealed class SiteOptions
{
    /// <summary>Site root every page path is relative to (the config file's directory).</summary>
    public required string RootPath { get; init; }

    public required IReadOnlyList<string> PagePatterns { get; init; }
    public required IReadOnlyList<string> ExcludePatterns { get; init; }
    public required IReadOnlyList<string> Languages { get; init; }

    /// <summary>Output path template containing <c>{lang}</c> and <c>{path}</c> tokens.</summary>
    public required string OutputPattern { get; init; }

    /// <summary>Absolute paths of key/value data files translated with the classic pipeline.</summary>
    public required IReadOnlyList<string> DataFiles { get; init; }

    public required string SourceLanguage { get; init; }
    public required TranslationEngine Engine { get; init; }
    public required string Model { get; init; }
    public required int BatchSize { get; init; }

    /// <summary>Text every translated title must keep (typically the brand name), or null.</summary>
    public string? TitleMustContain { get; init; }

    /// <summary>Absolute path to extra prompt instructions, or null when none apply.</summary>
    public string? ExtraPromptPath { get; init; }

    /// <summary>Null until a translating command needs it; --show-changes works without one.</summary>
    public string? ApiKey { get; init; }

    /// <summary>Config file this run picked up, for diagnostics.</summary>
    public string? ConfigPath { get; init; }

    /// <summary>Target file (relative to the root) for one page in one language.</summary>
    public string GetTargetRelativePath(string pageRelativePath, string languageCode)
    {
        return OutputPattern
            .Replace("{lang}", languageCode, StringComparison.Ordinal)
            .Replace("{path}", pageRelativePath, StringComparison.Ordinal);
    }

    public string GetTargetFullPath(string pageRelativePath, string languageCode)
    {
        return Path.GetFullPath(Path.Combine(RootPath, GetTargetRelativePath(pageRelativePath, languageCode)));
    }

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
