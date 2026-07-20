using VpnHood.ResourceTranslator.Configuration;
using VpnHood.ResourceTranslator.Formats;
using VpnHood.ResourceTranslator.Translation;
using VpnHood.ResourceTranslator.Watch;

namespace VpnHood.ResourceTranslator;

/// <summary>
/// Drives a translation run end to end: load the base file, work out what changed, translate
/// the affected entries in each target locale, then record the new baseline.
/// </summary>
public sealed class TranslationRunner
{
    private const int DefaultTranslateTimeoutSeconds = 100;
    private const int DefaultRetryCount = 5;

    /// <summary>Sentinel the model returns to decline translating an entry.</summary>
    private const string SkipMarker = "*";

    private readonly TranslatorOptions _options;
    private readonly ITranslationReporter _reporter;
    private readonly IResourceFormat _format;
    private readonly WatchStore _watchStore;
    private readonly Func<ITranslator> _translatorFactory;

    public TranslationRunner(
        TranslatorOptions options,
        ITranslationReporter? reporter = null,
        Func<ITranslator>? translatorFactory = null)
    {
        _options = options;
        _reporter = reporter ?? NullTranslationReporter.Instance;
        _format = ResourceFormatFactory.Create(options.BasePath);
        _watchStore = WatchStore.ForBaseFile(options.BasePath);
        _translatorFactory = translatorFactory
                             ?? (() => TranslatorFactory.Create(options.Engine, options.GetRequiredApiKey(), options.Model));
    }

    /// <summary>Rewrites the watch file so every current entry counts as already translated.</summary>
    public async Task<int> RebuildWatchFileAsync(CancellationToken cancellationToken = default)
    {
        var baseFile = await LoadBaseFileAsync(cancellationToken);
        await _watchStore.SaveAsync(baseFile.OrderedKeys, baseFile.Map, cancellationToken);

        _reporter.Info($"✓ Watch file rebuilt for {baseFile.OrderedKeys.Count} keys. All entries now marked as current.");
        return ExitCodes.Success;
    }

    /// <summary>Prints the keys whose source text changed since the last run, without translating.</summary>
    public async Task<int> ShowChangesAsync(CancellationToken cancellationToken = default)
    {
        var baseFile = await LoadBaseFileAsync(cancellationToken);
        var snapshot = await _watchStore.LoadAsync(cancellationToken);
        var changedKeys = snapshot.GetChangedKeys(baseFile.Map);

        _reporter.Info($"Changed keys since last translation: {changedKeys.Count}");
        foreach (var key in baseFile.OrderedKeys.Where(changedKeys.Contains))
            _reporter.Info($" - {key}");

        return ExitCodes.Success;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var baseFile = await LoadBaseFileAsync(cancellationToken);
        var snapshot = await _watchStore.LoadAsync(cancellationToken);
        var changedKeys = snapshot.GetChangedKeys(baseFile.Map);
        var sourceLanguage = _format.GetLanguageCode(_options.BasePath);
        var prompt = await LoadPromptAsync(cancellationToken);
        var extraPrompt = await LoadExtraPromptAsync(cancellationToken);
        var translator = _translatorFactory();

        var targets = ResolveTargets();
        if (targets.Count == 0)
            _reporter.Warn("No target locale files found to translate.");

        foreach (var target in targets) {
            await TranslateFileAsync(target, baseFile, changedKeys, translator, prompt, extraPrompt,
                sourceLanguage, cancellationToken);
        }

        // Only record the new baseline once every target succeeded, so a failed run retries next time.
        await _watchStore.SaveAsync(baseFile.OrderedKeys, baseFile.Map, cancellationToken);
        _reporter.Info("Done.");
        return ExitCodes.Success;
    }

    /// <summary>Force-translates every entry of a single language, creating the file if needed.</summary>
    public async Task<int> RebuildLanguageAsync(string languageCode, CancellationToken cancellationToken = default)
    {
        var baseFile = await LoadBaseFileAsync(cancellationToken);
        var sourceLanguage = _format.GetLanguageCode(_options.BasePath);
        var prompt = await LoadPromptAsync(cancellationToken);
        var extraPrompt = await LoadExtraPromptAsync(cancellationToken);
        var translator = _translatorFactory();

        var target = new TranslationTarget(_format.GetLocaleFilePath(_options.BasePath, languageCode), IsRebuild: true);
        await TranslateFileAsync(target, baseFile, baseFile.Map.Keys.ToHashSet(StringComparer.Ordinal), translator,
            prompt, extraPrompt, sourceLanguage, cancellationToken);

        await _watchStore.SaveAsync(baseFile.OrderedKeys, baseFile.Map, cancellationToken);
        _reporter.Info("Done.");
        return ExitCodes.Success;
    }

    private IReadOnlyList<TranslationTarget> ResolveTargets()
    {
        // An explicit language list creates missing files; discovery only touches what exists.
        return _options.Languages.Count > 0
            ? _options.Languages
                .Select(lang => new TranslationTarget(_format.GetLocaleFilePath(_options.BasePath, lang), IsRebuild: false))
                .ToList()
            : _format.FindSiblingLocaleFiles(_options.BasePath)
                .Select(path => new TranslationTarget(path, IsRebuild: false))
                .ToList();
    }

    private async Task TranslateFileAsync(
        TranslationTarget target,
        BaseFile baseFile,
        HashSet<string> changedKeys,
        ITranslator translator,
        string prompt,
        string? extraPrompt,
        string sourceLanguage,
        CancellationToken cancellationToken)
    {
        var localePath = target.Path;
        var localeCode = _format.GetLanguageCode(localePath);
        var localeFileName = Path.GetFileName(localePath);

        var localeMap = LoadLocaleMap(localePath);
        var output = new Dictionary<string, string>(StringComparer.Ordinal);
        var itemsToTranslate = new List<TranslateItem>(baseFile.OrderedKeys.Count);

        foreach (var key in baseFile.OrderedKeys) {
            var baseText = baseFile.Map[key];
            var hasExisting = localeMap.TryGetValue(key, out var existingValue) && !string.IsNullOrWhiteSpace(existingValue);
            var needsTranslation = target.IsRebuild || changedKeys.Contains(key) || !hasExisting;

            // Empty source values have nothing to translate; carry them across as-is.
            if (needsTranslation && string.IsNullOrWhiteSpace(baseText)) {
                output[key] = baseText;
                continue;
            }

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

        var missingCount = baseFile.OrderedKeys.Count(key =>
            !localeMap.ContainsKey(key) || string.IsNullOrWhiteSpace(localeMap[key]));

        if (target.IsRebuild)
            _reporter.Info($"Rebuilding {localeFileName} ({localeCode}) - translating all {itemsToTranslate.Count} keys...");
        else if (missingCount > 0 || changedKeys.Count > 0)
            _reporter.Info($"Processing {localeFileName} ({localeCode}) - {changedKeys.Count} changed, {missingCount} missing entries...");

        var translatedCount = await TranslateItemsAsync(itemsToTranslate, output, baseFile, translator, prompt,
            extraPrompt, localeFileName, target.IsRebuild, cancellationToken);

        await _format.SaveAsync(localePath, baseFile.OrderedKeys, output);

        if (target.IsRebuild)
            _reporter.Info($"✓ {localeFileName}: Rebuilt with {translatedCount} translations.");
        else if (translatedCount > 0)
            _reporter.Info($"✓ {localeFileName}: {translatedCount} translated/updated.");
        else
            _reporter.Info($"  {localeFileName}: Up to date, no changes needed.");
    }

    private async Task<int> TranslateItemsAsync(
        List<TranslateItem> itemsToTranslate,
        Dictionary<string, string> output,
        BaseFile baseFile,
        ITranslator translator,
        string prompt,
        string? extraPrompt,
        string localeFileName,
        bool reportProgress,
        CancellationToken cancellationToken)
    {
        var translatedCount = 0;
        var batchSize = Math.Max(1, _options.BatchSize);

        for (var i = 0; i < itemsToTranslate.Count; i += batchSize) {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = itemsToTranslate.Skip(i).Take(batchSize).ToArray();
            var promptOptions = PromptBuilder.BuildOptions(batch, prompt, extraPrompt);
            var results = await TranslateBatchWithRetryAsync(translator, promptOptions, localeFileName, i, cancellationToken);

            if (results.Length != batch.Length)
                throw new TranslatorException(
                    $"Translation result count mismatch for {localeFileName} at batch starting index {i}. " +
                    $"Expected {batch.Length}, got {results.Length}.",
                    ExitCodes.TranslationFailed);

            foreach (var result in results) {
                if (result.TranslatedText.Trim() == SkipMarker) {
                    // The model declined this entry: keep what is there, else fall back to source text.
                    if (string.IsNullOrWhiteSpace(output.GetValueOrDefault(result.Key)))
                        output[result.Key] = baseFile.Map.GetValueOrDefault(result.Key, string.Empty);
                    continue;
                }

                var baseText = baseFile.Map.GetValueOrDefault(result.Key, string.Empty);
                output[result.Key] = TranslationPostProcessor.PostProcess(baseText, result.TranslatedText);
                translatedCount++;
            }

            if (reportProgress)
                _reporter.Progress(localeFileName, Math.Min(i + batch.Length, itemsToTranslate.Count), itemsToTranslate.Count);
        }

        return translatedCount;
    }

    private async Task<TranslateResult[]> TranslateBatchWithRetryAsync(
        ITranslator translator,
        PromptOptions promptOptions,
        string localeFileName,
        int batchStartIndex,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= DefaultRetryCount; attempt++) {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(DefaultTranslateTimeoutSeconds));

            try {
                // Brief pause between calls keeps us under provider rate limits.
                await Task.Delay(500, cancellationToken);
                return await translator.TranslateAsync(promptOptions, timeoutCts.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                throw;
            }
            catch (OperationCanceledException) {
                _reporter.Warn($"Timeout translating {localeFileName} batch at index {batchStartIndex} (attempt {attempt}/{DefaultRetryCount}).");
            }
            catch (Exception ex) {
                _reporter.Warn($"Error translating {localeFileName} batch at index {batchStartIndex} (attempt {attempt}/{DefaultRetryCount}): {ex.Message}");
            }

            // Back off further after each failure.
            await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), cancellationToken);
        }

        throw new TranslatorException(
            $"Failed to translate {localeFileName} batch starting at index {batchStartIndex} after {DefaultRetryCount} attempts.",
            ExitCodes.TranslationFailed);
    }

    private Dictionary<string, string> LoadLocaleMap(string localePath)
    {
        var localeMap = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!_format.TryLoad(localePath, out var localeEntries, out _))
            return localeMap;

        foreach (var entry in localeEntries)
            localeMap[entry.Key] = entry.Value;

        return localeMap;
    }

    private Task<BaseFile> LoadBaseFileAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_format.TryLoad(_options.BasePath, out var entries, out var error))
            throw new TranslatorException($"Failed to parse base file: {error}", ExitCodes.ParseError);

        var orderedKeys = entries.Select(e => e.Key).ToList();
        var map = entries.ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);
        return Task.FromResult(new BaseFile(orderedKeys, map));
    }

    private static async Task<string> LoadPromptAsync(CancellationToken cancellationToken)
    {
        var promptFile = Path.Combine(AppContext.BaseDirectory, "Resources", "translation-prompt.txt");
        if (!File.Exists(promptFile))
            throw new TranslatorException($"Built-in prompt template is missing: {promptFile}", ExitCodes.FileNotFound);

        return await File.ReadAllTextAsync(promptFile, cancellationToken);
    }

    private async Task<string?> LoadExtraPromptAsync(CancellationToken cancellationToken)
    {
        return string.IsNullOrWhiteSpace(_options.ExtraPromptPath)
            ? null
            : await File.ReadAllTextAsync(_options.ExtraPromptPath, cancellationToken);
    }

    private sealed record BaseFile(List<string> OrderedKeys, Dictionary<string, string> Map);

    private sealed record TranslationTarget(string Path, bool IsRebuild);
}
