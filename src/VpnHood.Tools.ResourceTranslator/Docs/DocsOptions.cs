using System.Text.RegularExpressions;
using VpnHood.Tools.ResourceTranslator.Configuration;
using VpnHood.Tools.ResourceTranslator.Translation;

namespace VpnHood.Tools.ResourceTranslator.Docs;

/// <summary>
/// Fully resolved settings for a docs run: command-line values merged over the config file's
/// <c>docs</c> section, with defaults applied and paths made absolute.
/// </summary>
public sealed class DocsOptions
{
    /// <summary>The config file's directory; the output pattern resolves against it.</summary>
    public required string RootPath { get; init; }

    /// <summary>Absolute path of the source-language folder; document paths are relative to it.</summary>
    public required string SourceRoot { get; init; }

    public required IReadOnlyList<string> FilePatterns { get; init; }
    public required IReadOnlyList<string> ExcludePatterns { get; init; }
    public required IReadOnlyList<string> Languages { get; init; }

    /// <summary>Output path template containing <c>{lang}</c> and <c>{path}</c> tokens,
    /// relative to <see cref="RootPath" />.</summary>
    public required string OutputPattern { get; init; }

    public required string SourceLanguage { get; init; }

    /// <summary>Front matter keys whose values are translated.</summary>
    public required IReadOnlyList<string> FrontMatterKeys { get; init; }

    /// <summary>Bodies longer than this are translated in paragraph-aligned parts.</summary>
    public required int ChunkChars { get; init; }

    /// <summary>Compiled extra mask patterns (runtime placeholders and the like).</summary>
    public required IReadOnlyList<Regex> MaskPatterns { get; init; }

    public required TranslationEngine Engine { get; init; }
    public required string Model { get; init; }

    /// <summary>Project prompt instructions (shared + per-language); never null.</summary>
    public ExtraPromptStore ExtraPrompt { get; init; } = ExtraPromptStore.Empty;

    /// <summary>Null until a translating command needs it; --show-changes works without one.</summary>
    public string? ApiKey { get; init; }

    /// <summary>Config file this run picked up, for diagnostics.</summary>
    public string? ConfigPath { get; init; }

    /// <summary>Target file (relative to the root) for one document in one language.</summary>
    public string GetTargetRelativePath(string docRelativePath, string languageCode)
    {
        return OutputPattern
            .Replace("{lang}", languageCode, StringComparison.Ordinal)
            .Replace("{path}", docRelativePath, StringComparison.Ordinal);
    }

    public string GetTargetFullPath(string docRelativePath, string languageCode)
    {
        return Path.GetFullPath(Path.Combine(RootPath, GetTargetRelativePath(docRelativePath, languageCode)));
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
