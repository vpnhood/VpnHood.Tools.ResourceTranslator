using System.Text.RegularExpressions;
using VpnHood.Tools.ResourceTranslator.Cli;
using VpnHood.Tools.ResourceTranslator.Configuration;
using VpnHood.Tools.ResourceTranslator.Translation;
using VpnHood.Tools.ResourceTranslator.Watch;

namespace VpnHood.Tools.ResourceTranslator.Site;

/// <summary>
/// Merges command-line input over the <c>site</c> section of <c>vhtranslator.json</c> and
/// validates the result. Unlike the classic resolver, the config file is mandatory here — a
/// site run has no equivalent of a single <c>--base</c> file to infer everything from.
/// </summary>
public static partial class SiteOptionsResolver
{
    [GeneratedRegex("^[A-Za-z0-9_-]{1,20}$")]
    private static partial Regex LanguageCodeRegex();

    private const int DefaultBatchSize = 20;
    private const string DefaultOutputPattern = "{lang}/{path}";
    private static readonly string[] DefaultPagePatterns = ["**/index.html"];

    /// <summary>Always excluded from discovery, on top of the config's own exclude globs.</summary>
    private static readonly string[] BuiltInExcludes = [
        ".git/**", "_site/**", "_includes/**", "node_modules/**",
        WatchStore.PrivateFolderName + "/**"
    ];

    public static SiteOptions Resolve(CommandLineOptions commandLine)
    {
        var config = LoadConfig(commandLine);
        var site = config.Site ?? throw new TranslatorException(
            $"No \"site\" section found. Add one to {TranslatorConfig.FileName}, or pass --config.");

        var languages = site.Languages ?? [];
        if (languages.Length == 0)
            throw new TranslatorException("The \"site\" section must list at least one target language.");

        // Language codes become path segments and exclude globs; anything else is dangerous.
        foreach (var language in languages) {
            if (!LanguageCodeRegex().IsMatch(language))
                throw new TranslatorException(
                    $"Invalid language code '{language}'. Use letters, digits, '-' or '_' (e.g. fr, de-DE).");
        }

        var outputPattern = site.Output ?? DefaultOutputPattern;
        if (!outputPattern.Contains("{lang}", StringComparison.Ordinal) ||
            !outputPattern.Contains("{path}", StringComparison.Ordinal))
            throw new TranslatorException("The site \"output\" pattern must contain both {lang} and {path}.");

        var selection = SelectEngine(commandLine, config);
        var batchSize = commandLine.BatchSize ?? config.Batch ?? DefaultBatchSize;
        if (batchSize <= 0)
            throw new TranslatorException("Batch size must be a positive number.");

        var rootPath = config.BaseDirectory;

        // Generated locale trees must never be rediscovered as source pages. The excluded is
        // derived from the output pattern itself, so custom layouts stay covered too.
        var excludes = BuiltInExcludes
            .Concat(languages.Select(lang => outputPattern
                .Replace("{lang}", lang, StringComparison.Ordinal)
                .Replace("{path}", "**", StringComparison.Ordinal)))
            .Concat(site.Exclude ?? [])
            .ToList();

        return new SiteOptions {
            RootPath = rootPath,
            PagePatterns = site.Pages is { Length: > 0 } ? site.Pages : DefaultPagePatterns,
            ExcludePatterns = excludes,
            Languages = languages,
            OutputPattern = outputPattern,
            DataFiles = ResolveDataFiles(config, site),
            SourceLanguage = string.IsNullOrWhiteSpace(site.SourceLanguage) ? "en" : site.SourceLanguage,
            Engine = selection.Engine,
            Model = selection.Model,
            BatchSize = batchSize,
            TitleMustContain = site.TitleMustContain,
            PageBodyMode = ParsePageBodyMode(site.PageBody),
            ExtraPrompt = ResolveExtraPrompt(commandLine, config, rootPath),
            ApiKey = ResolveApiKey(commandLine, selection.Engine),
            ConfigPath = config.SourcePath
        };
    }

    private static PageBodyMode ParsePageBodyMode(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch {
            null or "" or "translate" => PageBodyMode.Translate,
            "copy" => PageBodyMode.Copy,
            _ => throw new TranslatorException(
                $"Unknown site.pageBody value '{value}'. Use \"translate\" or \"copy\".")
        };
    }

    private static TranslatorConfig LoadConfig(CommandLineOptions commandLine)
    {
        return !string.IsNullOrWhiteSpace(commandLine.ConfigPath)
            ? TranslatorConfig.Load(commandLine.ConfigPath)
            : TranslatorConfig.Discover(Directory.GetCurrentDirectory());
    }

    private static IReadOnlyList<string> ResolveDataFiles(TranslatorConfig config, SiteConfig site)
    {
        var dataFiles = new List<string>();
        foreach (var entry in site.Data ?? []) {
            var fullPath = config.ResolvePath(entry);
            if (fullPath == null)
                continue;
            // A data entry may be a single resource file or a language folder (e.g. _data/i18n/en).
            if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
                throw new TranslatorException($"Site data file or folder not found: {fullPath}", ExitCodes.FileNotFound);

            dataFiles.Add(fullPath);
        }

        return dataFiles;
    }

    private static EngineSelection SelectEngine(CommandLineOptions commandLine, TranslatorConfig config)
    {
        var engine = commandLine.Engine ?? config.Engine;
        var model = commandLine.Model ?? config.Model;

        if (!string.IsNullOrWhiteSpace(engine) && !EngineModelSelector.TryParseEngine(engine, out _))
            throw new TranslatorException(EngineModelSelector.DescribeUnknownEngine(engine));

        return EngineModelSelector.Select(engine, model);
    }

    private static ExtraPromptStore ResolveExtraPrompt(CommandLineOptions commandLine, TranslatorConfig config, string rootPath)
    {
        var explicitPath = !string.IsNullOrWhiteSpace(commandLine.ExtraPromptPath)
            ? Path.GetFullPath(commandLine.ExtraPromptPath)
            : config.ResolvePath(config.ExtraPrompt);

        // Same convention as the classic pipeline: the vh_translator folder at the site root.
        return ExtraPromptStore.Resolve(explicitPath, [Path.Combine(rootPath, WatchStore.PrivateFolderName)]);
    }

    private static string? ResolveApiKey(CommandLineOptions commandLine, TranslationEngine engine)
    {
        return !string.IsNullOrWhiteSpace(commandLine.ApiKey)
            ? commandLine.ApiKey
            : Environment.GetEnvironmentVariable(EngineModelSelector.GetApiKeyVariableName(engine));
    }
}
