using System.Text.RegularExpressions;
using VpnHood.Tools.ResourceTranslator.Cli;
using VpnHood.Tools.ResourceTranslator.Configuration;
using VpnHood.Tools.ResourceTranslator.Translation;
using VpnHood.Tools.ResourceTranslator.Watch;

namespace VpnHood.Tools.ResourceTranslator.Docs;

/// <summary>
/// Merges command-line input over the <c>docs</c> section of <c>vhtranslator.json</c> and
/// validates the result. Like the site resolver, the config file is mandatory — a docs run
/// has no single <c>--base</c> file to infer the layout from.
/// </summary>
public static partial class DocsOptionsResolver
{
    [GeneratedRegex("^[A-Za-z0-9_-]{1,20}$")]
    private static partial Regex LanguageCodeRegex();

    private const int DefaultChunkChars = 10_000;
    private static readonly string[] DefaultFilePatterns = ["**/*.md"];
    private static readonly string[] DefaultFrontMatterKeys = ["title", "description", "image_alt"];

    /// <summary>Runtime placeholders like <c>{appName}</c>; masking beats prompting because a
    /// restored token is correct by construction.</summary>
    private static readonly string[] DefaultMaskPatterns = [@"\{[A-Za-z0-9_]+\}"];

    /// <summary>Always excluded from discovery, on top of the config's own exclude globs.</summary>
    private static readonly string[] BuiltInExcludes = [
        ".git/**", "node_modules/**", WatchStore.PrivateFolderName + "/**"
    ];

    public static DocsOptions Resolve(CommandLineOptions commandLine)
    {
        var config = LoadConfig(commandLine);
        var docs = config.Docs ?? throw new TranslatorException(
            $"No \"docs\" section found. Add one to {TranslatorConfig.FileName}, or pass --config.");

        if (string.IsNullOrWhiteSpace(docs.Source))
            throw new TranslatorException("The \"docs\" section must name its \"source\" folder (e.g. \"posts/en\").");

        var sourceRoot = config.ResolvePath(docs.Source)
                         ?? throw new TranslatorException("The docs \"source\" folder could not be resolved.");
        if (!Directory.Exists(sourceRoot))
            throw new TranslatorException($"Docs source folder not found: {sourceRoot}", ExitCodes.FileNotFound);

        var languages = docs.Languages ?? [];
        if (languages.Length == 0)
            throw new TranslatorException("The \"docs\" section must list at least one target language.");

        // Language codes become path segments; anything else is dangerous.
        foreach (var language in languages) {
            if (!LanguageCodeRegex().IsMatch(language))
                throw new TranslatorException(
                    $"Invalid language code '{language}'. Use letters, digits, '-' or '_' (e.g. fr, de-DE).");
        }

        var sourceLanguage = ResolveSourceLanguage(docs);
        var outputPattern = docs.Output ?? BuildDefaultOutputPattern(docs.Source);
        if (!outputPattern.Contains("{lang}", StringComparison.Ordinal) ||
            !outputPattern.Contains("{path}", StringComparison.Ordinal))
            throw new TranslatorException("The docs \"output\" pattern must contain both {lang} and {path}.");

        // A target language whose output tree IS the source folder would overwrite the
        // hand-authored documents; refuse before any model is called.
        foreach (var language in languages) {
            var languageRoot = Path.GetFullPath(Path.Combine(config.BaseDirectory, outputPattern
                .Replace("{lang}", language, StringComparison.Ordinal)
                .Replace("/{path}", "", StringComparison.Ordinal)
                .Replace("{path}", "", StringComparison.Ordinal)));
            if (string.Equals(languageRoot, sourceRoot, StringComparison.OrdinalIgnoreCase))
                throw new TranslatorException(
                    $"Target language '{language}' would write into the docs source folder itself " +
                    $"({sourceRoot}). Remove it from \"languages\" or change \"output\".");
        }

        var chunkChars = docs.ChunkChars ?? DefaultChunkChars;
        if (chunkChars < 1000)
            throw new TranslatorException("docs \"chunkChars\" must be at least 1000.");

        var selection = SelectEngine(commandLine, config);

        return new DocsOptions {
            RootPath = config.BaseDirectory,
            SourceRoot = sourceRoot,
            FilePatterns = docs.Files is { Length: > 0 } ? docs.Files : DefaultFilePatterns,
            ExcludePatterns = [.. BuiltInExcludes, .. docs.Exclude ?? []],
            Languages = languages,
            OutputPattern = outputPattern,
            SourceLanguage = sourceLanguage,
            FrontMatterKeys = docs.FrontMatter ?? DefaultFrontMatterKeys,
            ChunkChars = chunkChars,
            MaskPatterns = CompileMaskPatterns(docs.MaskPatterns ?? DefaultMaskPatterns),
            Engine = selection.Engine,
            Model = selection.Model,
            ExtraPrompt = ResolveExtraPrompt(commandLine, config),
            ApiKey = ResolveApiKey(commandLine, selection.Engine),
            ConfigPath = config.SourcePath
        };
    }

    /// <summary>The layout convention makes the source folder's own name the best default:
    /// <c>posts/en</c> is English without anyone saying so.</summary>
    private static string ResolveSourceLanguage(DocsConfig docs)
    {
        if (!string.IsNullOrWhiteSpace(docs.SourceLanguage))
            return docs.SourceLanguage;

        var leaf = Path.GetFileName(docs.Source!.TrimEnd('/', '\\'));
        return LanguageCodeRegex().IsMatch(leaf) ? leaf : "en";
    }

    /// <summary>Sibling-of-source by default: <c>posts/en</c> → <c>posts/{lang}/{path}</c>.</summary>
    private static string BuildDefaultOutputPattern(string source)
    {
        var parent = Path.GetDirectoryName(source.TrimEnd('/', '\\').Replace('\\', '/'))?.Replace('\\', '/');
        return string.IsNullOrEmpty(parent) ? "{lang}/{path}" : parent + "/{lang}/{path}";
    }

    private static IReadOnlyList<Regex> CompileMaskPatterns(string[] patterns)
    {
        var compiled = new List<Regex>();
        foreach (var pattern in patterns) {
            try {
                compiled.Add(new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(5)));
            }
            catch (ArgumentException ex) {
                throw new TranslatorException($"Invalid docs \"maskPatterns\" regex '{pattern}': {ex.Message}");
            }
        }

        return compiled;
    }

    private static TranslatorConfig LoadConfig(CommandLineOptions commandLine)
    {
        return !string.IsNullOrWhiteSpace(commandLine.ConfigPath)
            ? TranslatorConfig.Load(commandLine.ConfigPath)
            : TranslatorConfig.Discover(Directory.GetCurrentDirectory());
    }

    private static EngineSelection SelectEngine(CommandLineOptions commandLine, TranslatorConfig config)
    {
        var engine = commandLine.Engine ?? config.Engine;
        var model = commandLine.Model ?? config.Model;

        if (!string.IsNullOrWhiteSpace(engine) && !EngineModelSelector.TryParseEngine(engine, out _))
            throw new TranslatorException(EngineModelSelector.DescribeUnknownEngine(engine));

        return EngineModelSelector.Select(engine, model);
    }

    private static ExtraPromptStore ResolveExtraPrompt(CommandLineOptions commandLine, TranslatorConfig config)
    {
        var explicitPath = !string.IsNullOrWhiteSpace(commandLine.ExtraPromptPath)
            ? Path.GetFullPath(commandLine.ExtraPromptPath)
            : config.ResolvePath(config.ExtraPrompt);

        // Same convention as the other pipelines: the vh_translator folder at the config root.
        return ExtraPromptStore.Resolve(explicitPath,
            [Path.Combine(config.BaseDirectory, WatchStore.PrivateFolderName)]);
    }

    private static string? ResolveApiKey(CommandLineOptions commandLine, TranslationEngine engine)
    {
        return !string.IsNullOrWhiteSpace(commandLine.ApiKey)
            ? commandLine.ApiKey
            : Environment.GetEnvironmentVariable(EngineModelSelector.GetApiKeyVariableName(engine));
    }
}
