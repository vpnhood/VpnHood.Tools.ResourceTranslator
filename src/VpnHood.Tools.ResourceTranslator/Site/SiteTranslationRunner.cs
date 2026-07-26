using System.Security.Cryptography;
using System.Text;
using VpnHood.Tools.ResourceTranslator.Configuration;
using VpnHood.Tools.ResourceTranslator.Translation;
using VpnHood.Tools.ResourceTranslator.Watch;

namespace VpnHood.Tools.ResourceTranslator.Site;

/// <summary>
/// Drives a site run end to end: discover pages, work out which page/language pairs are stale,
/// translate each page as a single unit (title + description + masked body), verify the result
/// fail-closed, and write the locale tree. A page that cannot be verified is never written —
/// the previously committed translation simply stays in place.
/// </summary>
public sealed class SiteTranslationRunner
{
    private const int TranslateTimeoutSeconds = 180;
    private const int MaxAttemptsPerPage = 3;

    private const string TitleKey = "title";
    private const string DescriptionKey = "description";
    private const string BodyKey = "body";

    private readonly SiteOptions _options;
    private readonly ITranslationReporter _reporter;
    private readonly Func<ITranslator> _translatorFactory;
    private readonly WatchStore _watchStore;

    /// <summary>Pause between AI calls and the unit of retry backoff; zeroed in tests.</summary>
    internal TimeSpan PacingDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    public SiteTranslationRunner(
        SiteOptions options,
        ITranslationReporter? reporter = null,
        Func<ITranslator>? translatorFactory = null)
    {
        _options = options;
        _reporter = reporter ?? NullTranslationReporter.Instance;
        _translatorFactory = translatorFactory
                             ?? (() => TranslatorFactory.Create(options.Engine, options.GetRequiredApiKey(), options.Model));
        _watchStore = WatchStore.ForSiteRoot(options.RootPath);
    }

    /// <summary>Lists the stale page/language pairs without contacting the AI.</summary>
    public async Task<int> ShowChangesAsync(CancellationToken cancellationToken = default)
    {
        var workList = await BuildWorkListAsync(rebuildLanguage: null, cancellationToken);

        _reporter.Info($"Pages needing translation: {workList.Count(work => work.Languages.Count > 0)}");
        foreach (var work in workList.Where(work => work.Languages.Count > 0))
            _reporter.Info($" - {work.RelativePath} ({string.Join(", ", work.Languages)})");

        foreach (var dataRunner in CreateDataRunners())
            await dataRunner.ShowChangesAsync(cancellationToken);

        return ExitCodes.Success;
    }

    /// <summary>Marks every current page as translated without calling the AI.</summary>
    public async Task<int> RebuildWatchFileAsync(CancellationToken cancellationToken = default)
    {
        // Reuse the work-list filters (marker, translate: false) so the watch never tracks
        // pages that a normal run would not translate.
        var workList = await BuildWorkListAsync(rebuildLanguage: null, cancellationToken);
        var orderedKeys = workList.Select(work => work.RelativePath).ToList();
        var hashes = workList.ToDictionary(work => work.RelativePath, work => work.Hash, StringComparer.Ordinal);

        await _watchStore.SaveAsync(orderedKeys, hashes, cancellationToken);
        _reporter.Info($"✓ Site watch file rebuilt for {orderedKeys.Count} pages. All pages now marked as current.");

        foreach (var dataRunner in CreateDataRunners())
            await dataRunner.RebuildWatchFileAsync(cancellationToken);

        return ExitCodes.Success;
    }

    public Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        return RunAsync(rebuildLanguage: null, cancellationToken);
    }

    /// <summary>Force-retranslates every page for one language.</summary>
    public Task<int> RebuildLanguageAsync(string languageCode, CancellationToken cancellationToken = default)
    {
        // An unlisted language would generate a tree that discovery does not exclude,
        // so its output would be picked up as source pages on the next run.
        if (!_options.Languages.Contains(languageCode, StringComparer.OrdinalIgnoreCase))
            throw new TranslatorException(
                $"Language '{languageCode}' is not one of the configured site languages " +
                $"({string.Join(", ", _options.Languages)}). Add it to the \"languages\" list first.");

        return RunAsync(languageCode, cancellationToken);
    }

    private async Task<int> RunAsync(string? rebuildLanguage, CancellationToken cancellationToken)
    {
        var workList = await BuildWorkListAsync(rebuildLanguage, cancellationToken);
        var pending = workList.Where(work => work.Languages.Count > 0).ToList();
        var failedPages = new List<string>();

        if (pending.Count == 0) {
            _reporter.Info("Site pages: Up to date, no changes needed.");
        }
        else {
            var translator = _translatorFactory();
            var extraPrompt = _options.ExtraPromptPath == null
                ? null
                : await File.ReadAllTextAsync(_options.ExtraPromptPath, cancellationToken);
            var basePrompt = await LoadSitePromptAsync(cancellationToken);

            var done = 0;
            var total = pending.Sum(work => work.Languages.Count);
            foreach (var work in pending) {
                var pageOk = true;
                foreach (var language in work.Languages) {
                    cancellationToken.ThrowIfCancellationRequested();
                    pageOk &= await TranslatePageAsync(work, language, translator, basePrompt, extraPrompt, cancellationToken);
                    _reporter.Progress(work.RelativePath, ++done, total);
                }

                if (!pageOk)
                    failedPages.Add(work.RelativePath);
            }
        }

        await SaveWatchAsync(workList, failedPages, cancellationToken);
        await PruneOrphanedTargetsAsync(workList.Select(work => work.RelativePath).ToList(), cancellationToken);

        // A language rebuild must force the data files too, not just the pages.
        foreach (var dataRunner in CreateDataRunners())
            await (rebuildLanguage == null
                ? dataRunner.RunAsync(cancellationToken)
                : dataRunner.RebuildLanguageAsync(rebuildLanguage, cancellationToken));

        if (failedPages.Count > 0) {
            _reporter.Warn($"{failedPages.Count} page(s) failed verification and were NOT written: " +
                           string.Join(", ", failedPages));
            return ExitCodes.VerificationFailed;
        }

        _reporter.Info("Done.");
        return ExitCodes.Success;
    }

    private async Task<bool> TranslatePageAsync(
        PageWork work,
        string language,
        ITranslator translator,
        string basePrompt,
        string? extraPrompt,
        CancellationToken cancellationToken)
    {
        var targetPath = _options.GetTargetFullPath(work.RelativePath, language);
        var targetRelative = _options.GetTargetRelativePath(work.RelativePath, language);

        // Never clobber a page a human wrote at this path; generated files carry a marker.
        if (File.Exists(targetPath) &&
            !PageDocument.HasAutoTranslatedMarker(await File.ReadAllTextAsync(targetPath, cancellationToken))) {
            _reporter.Warn($"  {targetRelative}: exists but has no '{PageDocument.AutoTranslatedKey}' marker; " +
                           "assuming it is hand-authored and leaving it alone.");
            return true;
        }

        PageDocument document;
        LiquidMasker masker;
        TranslateItem[] items;
        try {
            document = PageDocument.Parse(work.Content);
            if (document.Title != null && _options.TitleMustContain != null &&
                !document.Title.Contains(_options.TitleMustContain, StringComparison.Ordinal))
                _reporter.Warn($"  {work.RelativePath}: source title does not contain " +
                               $"\"{_options.TitleMustContain}\" — fix the source page; the rule is skipped here.");

            masker = LiquidMasker.Mask(document.Body);
            items = BuildItems(document, masker, language);
        }
        catch (TranslatorException ex) {
            // One malformed page must not abort the whole run; report it and keep going.
            _reporter.Warn($"✗ {work.RelativePath}: {ex.Message}");
            return false;
        }

        // Copy mode with no title/description: nothing needs the model at all.
        if (items.Length == 0) {
            var copied = document.Compose(null, null, document.Body, language,
                BuildPermalink(work.RelativePath, language));
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            await File.WriteAllTextAsync(targetPath, copied, new UTF8Encoding(false), cancellationToken);
            _reporter.Info($"✓ {targetRelative}: copied (no translatable metadata).");
            return true;
        }

        var feedback = (string?)null;
        for (var attempt = 1; attempt <= MaxAttemptsPerPage; attempt++) {
            var prompt = PromptBuilder.BuildOptions(items, ComposePrompt(basePrompt, feedback), extraPrompt);
            IReadOnlyList<string> errors;

            try {
                var results = await TranslateWithTimeoutAsync(translator, prompt, cancellationToken);
                var page = ComposeTranslatedPage(document, masker, results, language,
                    BuildPermalink(work.RelativePath, language), out errors);

                if (errors.Count == 0) {
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                    await File.WriteAllTextAsync(targetPath, page, new UTF8Encoding(false), cancellationToken);
                    _reporter.Info($"✓ {targetRelative}: translated.");
                    return true;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                throw;
            }
            catch (Exception ex) {
                errors = [ex.Message];
            }

            _reporter.Warn($"  {targetRelative}: attempt {attempt}/{MaxAttemptsPerPage} failed verification:");
            foreach (var error in errors.Take(5))
                _reporter.Warn($"    - {error}");

            feedback = string.Join("\n", errors.Take(10));
            await Task.Delay(PacingDelay * attempt, cancellationToken);
        }

        _reporter.Warn($"✗ {targetRelative}: giving up after {MaxAttemptsPerPage} attempts; file not written.");
        return false;
    }

    private TranslateItem[] BuildItems(PageDocument document, LiquidMasker masker, string language)
    {
        var items = new List<TranslateItem>();

        if (document.Title != null)
            items.Add(NewItem(TitleKey, document.Title, language));
        if (document.Description != null)
            items.Add(NewItem(DescriptionKey, document.Description, language));

        // In copy mode the body ships byte-identical, so the model never sees it.
        if (_options.PageBodyMode == PageBodyMode.Translate)
            items.Add(NewItem(BodyKey, masker.Masked, language));

        return items.ToArray();
    }

    private TranslateItem NewItem(string key, string text, string language)
    {
        return new TranslateItem {
            SourceLanguage = _options.SourceLanguage,
            TargetLanguage = language,
            Key = key,
            Text = text
        };
    }

    /// <summary>
    /// Served URL of a generated page: always <c>/{lang}/{source dir}/</c>, independent of
    /// where the output pattern physically places the file.
    /// </summary>
    private static string BuildPermalink(string relativePath, string language)
    {
        var url = relativePath.Replace('\\', '/');
        const string indexName = "index.html";
        if (url.EndsWith(indexName, StringComparison.OrdinalIgnoreCase))
            url = url[..^indexName.Length];

        return "/" + language + "/" + url;
    }

    /// <summary>Validates the model output and, when everything holds, builds the final page.</summary>
    private string ComposeTranslatedPage(
        PageDocument document,
        LiquidMasker masker,
        TranslateResult[] results,
        string language,
        string permalink,
        out IReadOnlyList<string> errors)
    {
        var errorList = new List<string>();
        var map = results
            .GroupBy(result => result.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().TranslatedText, StringComparer.Ordinal);

        var translateBody = _options.PageBodyMode == PageBodyMode.Translate;
        var maskedBody = map.GetValueOrDefault(BodyKey);
        if (translateBody && string.IsNullOrWhiteSpace(maskedBody)) {
            errors = ["The response contains no 'body' item."];
            return string.Empty;
        }

        // A missing metadata item must fail loudly; falling back to the source text would
        // silently ship an untranslated title on a translated page.
        if (document.Title != null && !map.ContainsKey(TitleKey))
            errorList.Add("The response contains no 'title' item.");
        if (document.Description != null && !map.ContainsKey(DescriptionKey))
            errorList.Add("The response contains no 'description' item.");

        if (translateBody)
            errorList.AddRange(masker.Validate(maskedBody!));

        var title = document.Title == null
            ? null
            : TranslationPostProcessor.PostProcess(document.Title, map.GetValueOrDefault(TitleKey, document.Title), language);
        var description = document.Description == null
            ? null
            : TranslationPostProcessor.PostProcess(document.Description, map.GetValueOrDefault(DescriptionKey, document.Description), language);

        // Only enforceable when the source obeys the rule itself; a source title without the
        // required text is the site's defect, reported separately, not a translation failure.
        if (title != null && _options.TitleMustContain != null &&
            document.Title!.Contains(_options.TitleMustContain, StringComparison.Ordinal) &&
            !title.Contains(_options.TitleMustContain, StringComparison.Ordinal))
            errorList.Add($"The translated title must contain \"{_options.TitleMustContain}\" but is: {title}");

        if (errorList.Count > 0) {
            errors = errorList;
            return string.Empty;
        }

        // Copy mode: the body is the source body by construction, so there is nothing to verify.
        if (!translateBody) {
            errors = errorList;
            return document.Compose(title, description, document.Body, language, permalink);
        }

        var body = masker.Unmask(maskedBody!.Trim());
        errorList.AddRange(PageVerifier.Verify(document.Body, body));

        errors = errorList;
        return errorList.Count > 0 ? string.Empty : document.Compose(title, description, body, language, permalink);
    }

    private static string ComposePrompt(string basePrompt, string? feedback)
    {
        return feedback == null
            ? basePrompt
            : basePrompt +
              "\n\nYour previous attempt FAILED verification with these errors:\n" + feedback +
              "\nRegenerate the COMPLETE translation and fix every error above.";
    }

    private async Task<TranslateResult[]> TranslateWithTimeoutAsync(
        ITranslator translator, PromptOptions prompt, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(TranslateTimeoutSeconds));

        // Brief pause between calls keeps us under provider rate limits.
        await Task.Delay(PacingDelay, cancellationToken);
        return await translator.TranslateAsync(prompt, timeoutCts.Token);
    }

    private async Task<List<PageWork>> BuildWorkListAsync(string? rebuildLanguage, CancellationToken cancellationToken)
    {
        var pages = SitePageDiscovery.Discover(_options);
        var snapshot = await _watchStore.LoadAsync(cancellationToken);

        var workList = new List<PageWork>();
        foreach (var page in pages) {
            var content = await ReadPageAsync(page, cancellationToken);

            // A generated page must never be treated as a source, whatever the globs say —
            // translating a translation would cascade into nested locale trees.
            if (PageDocument.HasAutoTranslatedMarker(content)) {
                _reporter.Warn($"  {page}: skipped — carries the '{PageDocument.AutoTranslatedKey}' marker " +
                               "(generated page discovered as a source; check the exclude globs).");
                continue;
            }

            // Author opt-out. Dropping the page here also drops it from the prune set, so any
            // previously generated copies of it are cleaned up on this same run.
            if (PageDocument.HasDoNotTranslateFlag(content)) {
                _reporter.Info($"  {page}: skipped ({PageDocument.TranslateKey}: false).");
                continue;
            }

            var hash = ComputeHash(content);
            var changed = !string.Equals(snapshot.Entries.GetValueOrDefault(page), hash, StringComparison.Ordinal);

            var languages = _options.Languages
                .Where(language =>
                    (rebuildLanguage == null && (changed || !File.Exists(_options.GetTargetFullPath(page, language)))) ||
                    string.Equals(rebuildLanguage, language, StringComparison.OrdinalIgnoreCase))
                .ToList();

            workList.Add(new PageWork(page, content, hash, languages));
        }

        return workList;
    }

    /// <summary>
    /// Records the new baseline for pages who's every language succeeded; failed pages keep
    /// their old entry (or none), so the next run picks them up again.
    /// </summary>
    private async Task SaveWatchAsync(List<PageWork> workList, List<string> failedPages, CancellationToken cancellationToken)
    {
        var snapshot = await _watchStore.LoadAsync(cancellationToken);
        var orderedKeys = new List<string>();
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var work in workList) {
            orderedKeys.Add(work.RelativePath);
            map[work.RelativePath] = failedPages.Contains(work.RelativePath)
                ? snapshot.Entries.GetValueOrDefault(work.RelativePath, string.Empty)
                : work.Hash;
        }

        await _watchStore.SaveAsync(orderedKeys, map, cancellationToken);
    }

    /// <summary>
    /// Deletes generated pages whose source page no longer exists (or is no longer included),
    /// so deleted content cannot keep shipping in other languages forever. Only files carrying
    /// the <see cref="PageDocument.AutoTranslatedKey" /> marker are ever deleted.
    /// </summary>
    private async Task PruneOrphanedTargetsAsync(IReadOnlyList<string> pages, CancellationToken cancellationToken)
    {
        // Pruning needs a per-language subtree that is safe to scan, which any pattern
        // ending in /{path} provides ({lang}/{path}, _langs/{lang}/{path}, ...).
        if (!_options.OutputPattern.Contains("{lang}", StringComparison.Ordinal) ||
            !_options.OutputPattern.EndsWith("/{path}", StringComparison.Ordinal))
            return;

        var pageSet = pages.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var language in _options.Languages) {
            var languageRootRelative = _options.OutputPattern
                .Replace("{lang}", language, StringComparison.Ordinal)
                .Replace("/{path}", "", StringComparison.Ordinal);
            var languageRoot = Path.GetFullPath(Path.Combine(_options.RootPath, languageRootRelative));
            if (!Directory.Exists(languageRoot))
                continue;

            foreach (var relativePath in SitePageDiscovery.DiscoverUnder(languageRoot, _options.PagePatterns)) {
                cancellationToken.ThrowIfCancellationRequested();
                if (pageSet.Contains(relativePath))
                    continue;

                var fullPath = Path.Combine(languageRoot, relativePath);
                if (!PageDocument.HasAutoTranslatedMarker(await File.ReadAllTextAsync(fullPath, cancellationToken)))
                    continue;

                File.Delete(fullPath);
                _reporter.Info($"  {languageRootRelative}/{relativePath}: pruned (source page no longer exists).");
            }
        }
    }

    /// <summary>Classic key/value runs for the site's data files (shared UI strings, ...).</summary>
    private IEnumerable<TranslationRunner> CreateDataRunners()
    {
        foreach (var dataFile in _options.DataFiles) {
            var options = new TranslatorOptions {
                BasePath = dataFile,
                Engine = _options.Engine,
                Model = _options.Model,
                BatchSize = _options.BatchSize,
                ExtraPromptPath = _options.ExtraPromptPath,
                Languages = _options.Languages,
                ApiKey = _options.ApiKey,
                ConfigPath = _options.ConfigPath
            };

            yield return new TranslationRunner(options, _reporter, _translatorFactory);
        }
    }

    private async Task<string> ReadPageAsync(string relativePath, CancellationToken cancellationToken)
    {
        return await File.ReadAllTextAsync(Path.Combine(_options.RootPath, relativePath), cancellationToken);
    }

    private static async Task<string> LoadSitePromptAsync(CancellationToken cancellationToken)
    {
        var promptFile = Path.Combine(AppContext.BaseDirectory, "Resources", "site-page-prompt.txt");
        if (!File.Exists(promptFile))
            throw new TranslatorException($"Built-in site prompt template is missing: {promptFile}", ExitCodes.FileNotFound);

        return await File.ReadAllTextAsync(promptFile, cancellationToken);
    }

    /// <summary>Newlines are normalized first so cross-OS checkouts do not invalidate the watch.</summary>
    private static string ComputeHash(string content)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    private sealed record PageWork(string RelativePath, string Content, string Hash, List<string> Languages);
}
