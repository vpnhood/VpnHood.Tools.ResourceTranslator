using System.CommandLine;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using VpnHood.ResourceTranslator.Formats;
using VpnHood.ResourceTranslator.Models;
using VpnHood.ResourceTranslator.Translators;

namespace VpnHood.ResourceTranslator;

internal static class Program
{
    private const int DefaultTranslateTimeoutSeconds = 100;

    private static readonly JsonSerializerOptions OutputSerializerOptions = new() {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static async Task<int> Main(string[] args)
    {
        var baseOption = new Option<string?>("--base", "-b") {
            Description = "Path to base language file (e.g., en.json, fr.json, or Strings.resx)"
        };
        var extraPromptOption = new Option<string?>("--extra-prompt", "-x") {
            Description = "Path to extra instructions text file for the AI prompt"
        };
        var showChangesOption = new Option<bool>("--show-changes", "-c") {
            Description = "Show changed keys since last translation and exit"
        };
        var rebuildLangOption = new Option<string?>("--rebuild-lang", "-r") {
            Description = "Force rebuild/translate all items for specific language (e.g., 'fr', 'es')"
        };
        var ignoreChangesOption = new Option<bool>("--ignore-changes", "-i") {
            Description = "Rebuild watch file to mark all entries as current (no translation)"
        };
        var apiKeyOption = new Option<string?>("--api-key", "-k") {
            Description = "API key (or set GEMINI_API_KEY/OPENAI_API_KEY/GROK_API_KEY env var)"
        };
        var modelOption = new Option<string?>("--model", "-m") {
            Description = "AI model (default: gemini-flash-lite-latest, grok-4-latest for grok engine)"
        };
        var engineOption = new Option<string?>("--engine", "-e") {
            Description = "Translation engine: gemini, gpt, or grok (default: auto-detected from model)"
        };
        var batchOption = new Option<int>("--batch", "-n") {
            Description = "Batch size for translation requests",
            DefaultValueFactory = _ => 20
        };
        batchOption.Validators.Add(result => {
            if (result.GetValueOrDefault<int>() <= 0)
                result.AddError("Batch size must be a positive number.");
        });

        var rootCommand = new RootCommand(
            "Translates i18n resource files (JSON or Microsoft .resx) using AI (Google Gemini, OpenAI ChatGPT, or Grok AI) " +
            "while preserving placeholders, HTML tags, and formatting. " +
            "The engine is auto-detected from the model name if not specified; " +
            "missing entries in target languages are always translated regardless of source changes.") {
            baseOption,
            extraPromptOption,
            showChangesOption,
            rebuildLangOption,
            ignoreChangesOption,
            apiKeyOption,
            modelOption,
            engineOption,
            batchOption
        };

        rootCommand.SetAction(async (parseResult, _) => {
            var options = new TranslatorOptions {
                BasePath = parseResult.GetValue(baseOption),
                ExtraPromptPath = parseResult.GetValue(extraPromptOption),
                ApiKey = parseResult.GetValue(apiKeyOption),
                Model = parseResult.GetValue(modelOption),
                Engine = parseResult.GetValue(engineOption),
                ShowChanges = parseResult.GetValue(showChangesOption),
                RebuildLang = parseResult.GetValue(rebuildLangOption),
                RebuildWatch = parseResult.GetValue(ignoreChangesOption),
                BatchSize = parseResult.GetValue(batchOption)
            };

            try {
                return await RunAsync(options);
            }
            catch (Exception ex) {
                await Console.Error.WriteLineAsync($"Error: {ex.Message}");
                return 10;
            }
        });

        return await rootCommand.Parse(args).InvokeAsync();
    }

    private static async Task<int> RunAsync(TranslatorOptions options)
    {
        // Get base path
        var basePath = options.BasePath;
        if (string.IsNullOrWhiteSpace(basePath)) {
            Console.Write("Enter path to base language file (e.g., en.json, Strings.resx): ");
            basePath = Console.ReadLine();
        }

        if (string.IsNullOrWhiteSpace(basePath)) {
            await Console.Error.WriteLineAsync("Error: Base language file path is required.");
            return 1;
        }

        basePath = Path.GetFullPath(basePath);
        if (!File.Exists(basePath)) {
            await Console.Error.WriteLineAsync($"Error: File not found: {basePath}");
            return 2;
        }

        // Select the resource format based on the file extension (.json or .resx)
        var format = ResourceFormatFactory.TryCreate(basePath);
        if (format == null) {
            await Console.Error.WriteLineAsync(
                $"Error: Unsupported file type '{Path.GetExtension(basePath)}'. Supported formats: .json, .resx");
            return 2;
        }

        // Select engine and model
        var (engine, model) = EngineModelSelector.SelectEngineAndModel(options.Engine, options.Model);

        // Determine source language from the base file (e.g., "en" from en.json, culture from Strings.fr.resx)
        var sourceLanguage = format.GetLanguageCode(basePath);

        // Load prompt files
        var promptFile = Path.Combine(AppContext.BaseDirectory, "translation-prompt.txt");
        var prompt = await File.ReadAllTextAsync(promptFile);

        string? extraPrompt = null;
        if (!string.IsNullOrWhiteSpace(options.ExtraPromptPath))
            extraPrompt = await File.ReadAllTextAsync(Path.GetFullPath(options.ExtraPromptPath));
        else if (File.Exists(GetCustomPromptFilePath(basePath)))
            extraPrompt = await File.ReadAllTextAsync(GetCustomPromptFilePath(basePath));

        var watchPath = GetWatchFilePath(basePath);

        // Load base language file
        if (!format.TryLoad(basePath, out var baseEntries, out var loadErr)) {
            await Console.Error.WriteLineAsync($"Error: Failed to parse base file: {loadErr}");
            return 3;
        }

        var orderedKeys = baseEntries.Select(e => e.Key).ToList();
        var baseMap = baseEntries.ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);

        // Load previous watch entries (source texts, or MD5 hashes for legacy watch files)
        var (previousEntries, previousAreHashes) = await LoadWatchEntriesAsync(watchPath);

        // Handle rebuild watch file only
        if (options.RebuildWatch) {
            await SaveWatchFileAsync(watchPath, orderedKeys, baseMap);
            Console.WriteLine($"✓ Watch file rebuilt for {orderedKeys.Count} keys. All entries now marked as current.");
            return 0;
        }

        var changedKeys = DetermineChangedKeys(baseMap, previousEntries, previousAreHashes);

        if (options.ShowChanges) {
            Console.WriteLine($"Changed keys since last translation: {changedKeys.Count}");
            foreach (var key in changedKeys) {
                Console.WriteLine($" - {key}");
            }
            return 0;
        }

        // Get API key
        var apiKey = options.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            apiKey = Environment.GetEnvironmentVariable(EngineModelSelector.GetEnvironmentVariableName(engine));

        if (string.IsNullOrWhiteSpace(apiKey)) {
            var envVarName = EngineModelSelector.GetEnvironmentVariableName(engine);
            await Console.Error.WriteLineAsync($"Error: Missing API key. Provide via --api-key or {envVarName} env var.");
            return 4;
        }

        // Create translator based on engine
        ITranslator translator = engine switch {
            "gpt" => new ChatGptTranslator(apiKey, model),
            "gemini" => new GeminiTranslator(apiKey, model),
            "grok" => new GrokAiTranslator(apiKey, model),
            _ => throw new ArgumentException($"Unknown engine: {engine}. Supported engines: gemini, gpt, grok")
        };

        // Handle rebuild specific language
        if (!string.IsNullOrWhiteSpace(options.RebuildLang)) {
            var rebuildPath = format.GetLocaleFilePath(basePath, options.RebuildLang);
            await TranslateFileAsync(rebuildPath, orderedKeys, baseMap, baseMap.Keys.ToHashSet(), translator, format,
                prompt: prompt, extraPrompt: extraPrompt, sourceLanguage: sourceLanguage, batchSize: options.BatchSize, isRebuild: true);
        }
        else {
            // Find sibling locale files for this format
            var files = format.FindSiblingLocaleFiles(basePath).ToList();

            if (files.Count == 0)
                Console.WriteLine("No sibling locale files found to translate.");

            foreach (var localePath in files) {
                await TranslateFileAsync(localePath, orderedKeys, baseMap, changedKeys, translator, format,
                    prompt: prompt, extraPrompt: extraPrompt, sourceLanguage: sourceLanguage, batchSize: options.BatchSize);
            }
        }

        // Save updated watch file (only after successful translations);
        // this also migrates legacy hash-based watch files to the source-text format
        await SaveWatchFileAsync(watchPath, orderedKeys, baseMap);
        Console.WriteLine("Done.");
        return 0;
    }

    private static async Task TranslateFileAsync(
        string localePath,
        List<string> orderedKeys,
        Dictionary<string, string> baseMap,
        HashSet<string> changedKeys,
        ITranslator translator,
        IResourceFormat format,
        string prompt,
        string? extraPrompt,
        string sourceLanguage,
        int batchSize,
        bool isRebuild = false)
    {
        var localeCode = format.GetLanguageCode(localePath);
        var localeFileName = Path.GetFileName(localePath);

        var localeMap = new Dictionary<string, string>(StringComparer.Ordinal);
        if (format.TryLoad(localePath, out var localeEntries, out _)) {
            foreach (var kv in localeEntries)
                localeMap[kv.Key] = kv.Value;
        }

        var output = new Dictionary<string, string>(StringComparer.Ordinal);
        var translatedCount = 0;
        var missingCount = orderedKeys.Count(key => !localeMap.ContainsKey(key) || string.IsNullOrWhiteSpace(localeMap[key]));

        if (isRebuild)
            Console.WriteLine($"Rebuilding {localeFileName} ({localeCode}) - translating all {orderedKeys.Count} keys...");
        else if (missingCount > 0)
            Console.WriteLine($"Processing {localeFileName} ({localeCode}) - {changedKeys.Count} changed, {missingCount} missing entries...");

        // Pre-populate output with existing values and collect items that need translation
        var itemsToTranslate = new List<TranslateItem>(orderedKeys.Count);
        foreach (var key in orderedKeys) {
            var baseText = baseMap[key];
            var hasExisting = localeMap.TryGetValue(key, out var existingValue) && !string.IsNullOrWhiteSpace(existingValue);
            var needsTranslation = isRebuild || changedKeys.Contains(key) || !hasExisting;

            // Nothing to translate for empty source values; copy them as-is
            if (needsTranslation && string.IsNullOrWhiteSpace(baseText)) {
                output[key] = baseText;
                continue;
            }

            // default output is the existing translation if any, or empty until translated
            output[key] = hasExisting ? existingValue! : string.Empty;

            if (needsTranslation) {
                itemsToTranslate.Add(new TranslateItem {
                    SourceLanguage = sourceLanguage,
                    TargetLanguage = localeCode,
                    Key = key,
                    Text = baseText
                });
            }
        }

        // Batch translate items
        var effectiveBatchSize = Math.Max(1, batchSize);
        for (var i = 0; i < itemsToTranslate.Count; i += effectiveBatchSize) {
            var batch = itemsToTranslate.Skip(i).Take(effectiveBatchSize).ToArray();

            var promptOptions = BuildPromptOptionsForBatch(batch, prompt, extraPrompt);
            var results = await TranslateBatchWithRetryAsync(translator, promptOptions, localePath, i);

            // check results count
            if (results.Length != batch.Length)
                throw new Exception($"Translation result count mismatch for {localeFileName} at batch starting index {i}. Expected {batch.Length}, got {results.Length}.");

            foreach (var res in results) {
                // "*" means the AI skipped this item; keep the existing value, or fall back to the source text
                if (res.TranslatedText.Trim() == "*") {
                    if (string.IsNullOrWhiteSpace(output.GetValueOrDefault(res.Key)))
                        output[res.Key] = baseMap.GetValueOrDefault(res.Key, string.Empty);
                    continue;
                }

                // Post-process and apply
                var baseText = baseMap.GetValueOrDefault(res.Key, string.Empty);
                output[res.Key] = TranslateUtils.PostProcessTranslation(baseText, res.TranslatedText);
                translatedCount++;
            }

            if (isRebuild) {
                var done = Math.Min(i + batch.Length, itemsToTranslate.Count);
                Console.WriteLine($"  Progress: {done}/{itemsToTranslate.Count} ({done * 100 / itemsToTranslate.Count}%)");
            }
        }

        // Write the file preserving base key order
        await format.SaveAsync(localePath, orderedKeys, output);

        if (isRebuild)
            Console.WriteLine($"✓ {localeFileName}: Rebuilt with {translatedCount} translations.");
        else if (translatedCount > 0)
            Console.WriteLine($"✓ {localeFileName}: {translatedCount} translated/updated.");
        else
            Console.WriteLine($"  {localeFileName}: Up to date, no changes needed.");
    }

    private static PromptOptions BuildPromptOptionsForBatch(TranslateItem[] items, string prompt, string? extraPrompt)
    {
        var promptBuilder = new StringBuilder(prompt);

        if (!string.IsNullOrWhiteSpace(extraPrompt)) {
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("Additional guidelines:");
            promptBuilder.AppendLine(extraPrompt);
        }

        return new PromptOptions {
            Prompt = promptBuilder.ToString(),
            Items = items
        };
    }

    private static async Task<TranslateResult[]> TranslateBatchWithRetryAsync(
        ITranslator translator,
        PromptOptions promptOptions,
        string localePath,
        int batchStartIndex,
        int retryCount = 5)
    {
        var localeFileName = Path.GetFileName(localePath);

        for (var attempt = 1; attempt <= retryCount; attempt++) {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(DefaultTranslateTimeoutSeconds));
            try {
                await Task.Delay(500, CancellationToken.None); // brief pause to avoid rate limits
                return await translator.TranslateAsync(promptOptions, cts.Token);
            }
            catch (OperationCanceledException) {
                await Console.Error.WriteLineAsync($"Timeout while translating {localeFileName} batch starting at index {batchStartIndex} (attempt {attempt}/{retryCount}).");
            }
            catch (Exception ex) {
                await Console.Error.WriteLineAsync($"Error while translating {localeFileName} batch starting at index {batchStartIndex} (attempt {attempt}/{retryCount}): {ex.Message}");
            }

            // back off a bit more after each failed attempt to avoid rate limits
            await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), CancellationToken.None);
        }

        throw new Exception($"Failed to translate {localeFileName} batch starting at index {batchStartIndex} after {retryCount} attempts.");
    }

    private static string ComputeMd5(string value)
    {
        return Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static async Task<(Dictionary<string, string> Entries, bool AreHashes)> LoadWatchEntriesAsync(string path)
    {
        var empty = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path))
            return (empty, false);

        try {
            var txt = await File.ReadAllTextAsync(path);
            if (JsonNode.Parse(txt) is not JsonObject obj)
                return (empty, false);

            // Versioned format: { "version": 1, "items": { key: sourceText } }
            if (obj.ContainsKey("version")) {
                var watch = obj.Deserialize<WatchFile>();
                return (new Dictionary<string, string>(watch?.Items ?? new(), StringComparer.Ordinal), false);
            }

            // Legacy format: flat { key: md5Hash }
            var legacy = obj.Deserialize<Dictionary<string, string>>() ?? new();
            return (new Dictionary<string, string>(legacy, StringComparer.Ordinal), true);
        }
        catch {
            return (empty, false);
        }
    }

    private static async Task SaveWatchFileAsync(string path, List<string> orderedKeys, Dictionary<string, string> baseMap)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var items = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in orderedKeys)
            items[key] = baseMap[key];

        var watch = new WatchFile { Items = items };
        var txt = JsonSerializer.Serialize(watch, OutputSerializerOptions);
        await File.WriteAllTextAsync(path, txt);
    }

    private static HashSet<string> DetermineChangedKeys(
        Dictionary<string, string> baseMap,
        Dictionary<string, string> previousEntries,
        bool previousAreHashes)
    {
        var changed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, text) in baseMap) {
            var current = previousAreHashes ? ComputeMd5(text) : text;
            if (!string.Equals(current, previousEntries.GetValueOrDefault(key), StringComparison.Ordinal))
                changed.Add(key);
        }
        return changed;
    }

    private static string GetPrivateFolderPath(string basePath)
    {
        var baseDir = Path.GetDirectoryName(basePath)!;
        return Path.Combine(baseDir, "vh_translator");
    }

    private static string GetCustomPromptFilePath(string basePath)
    {
        return Path.Combine(GetPrivateFolderPath(basePath), "custom_prompt.txt");
    }

    private static string GetWatchFilePath(string basePath)
    {
        // Location: <baseDir>/vh_translator/<baseName>_watch.json
        var baseName = Path.GetFileNameWithoutExtension(basePath);
        return Path.Combine(GetPrivateFolderPath(basePath), $"{baseName}_watch.json");
    }
}
