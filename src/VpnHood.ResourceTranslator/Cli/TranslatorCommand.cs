using System.CommandLine;
using VpnHood.ResourceTranslator.Configuration;
using VpnHood.ResourceTranslator.Formats;
using VpnHood.ResourceTranslator.Translation;

namespace VpnHood.ResourceTranslator.Cli;

/// <summary>
/// Defines the command-line surface and maps it onto <see cref="TranslationRunner" />.
/// </summary>
public static class TranslatorCommand
{
    public static RootCommand Create()
    {
        var baseOption = new Option<string?>("--base", "-b") {
            Description = $"Path to the base language file ({string.Join(" / ", ResourceFormatFactory.SupportedExtensions)})"
        };
        var configOption = new Option<string?>("--config") {
            Description = $"Path to a {TranslatorConfig.FileName} file (default: nearest one found in parent folders)"
        };
        var extraPromptOption = new Option<string?>("--extra-prompt", "-x") {
            Description = "Path to extra instructions appended to the AI prompt"
        };
        var showChangesOption = new Option<bool>("--show-changes", "-c") {
            Description = "Show changed keys since the last translation and exit"
        };
        var rebuildLangOption = new Option<string?>("--rebuild-lang", "-r") {
            Description = "Force retranslation of every entry for one language (e.g. 'fr')"
        };
        var ignoreChangesOption = new Option<bool>("--ignore-changes", "-i") {
            Description = "Mark all current entries as translated without calling the AI"
        };
        var apiKeyOption = new Option<string?>("--api-key", "-k") {
            Description = "API key (or set GEMINI_API_KEY / OPENAI_API_KEY / GROK_API_KEY)"
        };
        var modelOption = new Option<string?>("--model", "-m") {
            Description = "AI model (default depends on the engine)"
        };
        var engineOption = new Option<string?>("--engine", "-e") {
            Description = $"Translation engine: {string.Join(", ", EngineModelSelector.PublicEngineNames)} (default: detected from the model name)"
        };
        var batchOption = new Option<int?>("--batch", "-n") {
            Description = "Batch size for translation requests (default: 20)"
        };
        batchOption.Validators.Add(result => {
            if (result.GetValueOrDefault<int?>() is <= 0)
                result.AddError("Batch size must be a positive number.");
        });

        var rootCommand = new RootCommand(
            "Translates i18n resource files (JSON or Microsoft .resx) using AI while preserving " +
            "placeholders, HTML tags and formatting. Only entries whose source text changed are " +
            "retranslated; missing entries in a target language are always filled in.") {
            baseOption,
            configOption,
            extraPromptOption,
            showChangesOption,
            rebuildLangOption,
            ignoreChangesOption,
            apiKeyOption,
            modelOption,
            engineOption,
            batchOption
        };

        rootCommand.SetAction(async (parseResult, cancellationToken) => {
            var commandLine = new CommandLineOptions {
                BasePath = parseResult.GetValue(baseOption),
                ConfigPath = parseResult.GetValue(configOption),
                ExtraPromptPath = parseResult.GetValue(extraPromptOption),
                ApiKey = parseResult.GetValue(apiKeyOption),
                Model = parseResult.GetValue(modelOption),
                Engine = parseResult.GetValue(engineOption),
                BatchSize = parseResult.GetValue(batchOption),
                ShowChanges = parseResult.GetValue(showChangesOption),
                RebuildLang = parseResult.GetValue(rebuildLangOption),
                RebuildWatch = parseResult.GetValue(ignoreChangesOption)
            };

            return await ExecuteAsync(commandLine, cancellationToken);
        });

        return rootCommand;
    }

    private static async Task<int> ExecuteAsync(CommandLineOptions commandLine, CancellationToken cancellationToken)
    {
        var reporter = new ConsoleTranslationReporter();

        try {
            var options = TranslatorOptionsResolver.Resolve(commandLine);
            if (options.ConfigPath != null)
                reporter.Info($"Using config: {options.ConfigPath}");

            var runner = new TranslationRunner(options, reporter);

            if (commandLine.RebuildWatch)
                return await runner.RebuildWatchFileAsync(cancellationToken);

            if (commandLine.ShowChanges)
                return await runner.ShowChangesAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(commandLine.RebuildLang))
                return await runner.RebuildLanguageAsync(commandLine.RebuildLang, cancellationToken);

            return await runner.RunAsync(cancellationToken);
        }
        catch (TranslatorException ex) {
            reporter.Warn($"Error: {ex.Message}");
            return ex.ExitCode;
        }
        catch (OperationCanceledException) {
            reporter.Warn("Cancelled.");
            return ExitCodes.TranslationFailed;
        }
    }
}
