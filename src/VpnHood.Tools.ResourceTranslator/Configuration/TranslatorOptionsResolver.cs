using VpnHood.Tools.ResourceTranslator.Cli;
using VpnHood.Tools.ResourceTranslator.Formats;
using VpnHood.Tools.ResourceTranslator.Translation;

namespace VpnHood.Tools.ResourceTranslator.Configuration;

/// <summary>
/// Merges command-line input over the optional <c>vhtranslator.json</c> and validates the
/// result, so everything downstream can assume a complete, absolute, supported configuration.
/// </summary>
public static class TranslatorOptionsResolver
{
    private const int DefaultBatchSize = 20;

    public static TranslatorOptions Resolve(CommandLineOptions commandLine)
    {
        var config = LoadConfig(commandLine);
        var basePath = ResolveBasePath(commandLine, config);

        // Fail before any network call if the base cannot be translated at all. A folder base
        // (language folder, e.g. i18n/en) is validated by the runner, which enumerates its files.
        var isFolderBase = Directory.Exists(basePath);
        if (!isFolderBase && !File.Exists(basePath))
            throw new TranslatorException($"File or folder not found: {basePath}", ExitCodes.FileNotFound);

        if (!isFolderBase && ResourceFormatFactory.TryCreate(basePath) == null)
            throw new TranslatorException(
                $"Unsupported file type '{Path.GetExtension(basePath)}'. " +
                $"Supported formats: {string.Join(", ", ResourceFormatFactory.SupportedExtensions)}",
                ExitCodes.FileNotFound);

        var selection = SelectEngine(commandLine, config);
        var batchSize = commandLine.BatchSize ?? config.Batch ?? DefaultBatchSize;
        if (batchSize <= 0)
            throw new TranslatorException("Batch size must be a positive number.");

        return new TranslatorOptions {
            BasePath = basePath,
            Engine = selection.Engine,
            Model = selection.Model,
            BatchSize = batchSize,
            ExtraPrompt = ResolveExtraPrompt(commandLine, config, basePath),
            Languages = config.Languages ?? [],
            ApiKey = ResolveApiKey(commandLine, selection.Engine),
            ConfigPath = config.SourcePath
        };
    }

    private static TranslatorConfig LoadConfig(CommandLineOptions commandLine)
    {
        if (!string.IsNullOrWhiteSpace(commandLine.ConfigPath))
            return TranslatorConfig.Load(commandLine.ConfigPath);

        // Search from the base file's folder when we know it, otherwise from the working directory.
        var startDirectory = string.IsNullOrWhiteSpace(commandLine.BasePath)
            ? Directory.GetCurrentDirectory()
            : Path.GetDirectoryName(Path.GetFullPath(commandLine.BasePath)) ?? Directory.GetCurrentDirectory();

        return TranslatorConfig.Discover(startDirectory);
    }

    private static string ResolveBasePath(CommandLineOptions commandLine, TranslatorConfig config)
    {
        if (!string.IsNullOrWhiteSpace(commandLine.BasePath))
            return Path.GetFullPath(commandLine.BasePath);

        var fromConfig = config.ResolvePath(config.Base);
        if (!string.IsNullOrWhiteSpace(fromConfig))
            return fromConfig;

        throw new TranslatorException(
            $"No base language file specified. Pass --base, or add \"base\" to a {TranslatorConfig.FileName} file.");
    }

    private static EngineSelection SelectEngine(CommandLineOptions commandLine, TranslatorConfig config)
    {
        var engine = commandLine.Engine ?? config.Engine;
        var model = commandLine.Model ?? config.Model;

        if (!string.IsNullOrWhiteSpace(engine) && !EngineModelSelector.TryParseEngine(engine, out _))
            throw new TranslatorException(EngineModelSelector.DescribeUnknownEngine(engine));

        return EngineModelSelector.Select(engine, model);
    }

    private static ExtraPromptStore ResolveExtraPrompt(CommandLineOptions commandLine, TranslatorConfig config, string basePath)
    {
        // Private folders in priority order: next to the base file, then next to the config —
        // so a base anywhere in the tree still picks up the project's shared instructions.
        var privateFolders = new List<string> { Watch.WatchStore.GetPrivateFolderPath(basePath) };
        if (!string.IsNullOrWhiteSpace(config.SourcePath)) {
            var configFolder = Watch.WatchStore.GetPrivateFolderForConfig(config.SourcePath);
            if (!privateFolders.Contains(configFolder, StringComparer.OrdinalIgnoreCase))
                privateFolders.Add(configFolder);
        }

        var explicitPath = !string.IsNullOrWhiteSpace(commandLine.ExtraPromptPath)
            ? Path.GetFullPath(commandLine.ExtraPromptPath)
            : config.ResolvePath(config.ExtraPrompt);

        return ExtraPromptStore.Resolve(explicitPath, privateFolders);
    }

    private static string? ResolveApiKey(CommandLineOptions commandLine, TranslationEngine engine)
    {
        return !string.IsNullOrWhiteSpace(commandLine.ApiKey)
            ? commandLine.ApiKey
            : Environment.GetEnvironmentVariable(EngineModelSelector.GetApiKeyVariableName(engine));
    }
}
