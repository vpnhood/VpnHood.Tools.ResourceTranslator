using System.Security.Cryptography;
using System.Text;
using VpnHood.Tools.ResourceTranslator.Site;
using VpnHood.Tools.ResourceTranslator.Translation;
using VpnHood.Tools.ResourceTranslator.Watch;

namespace VpnHood.Tools.ResourceTranslator.Docs;

/// <summary>
/// Drives a docs run end to end: discover markdown documents, work out which document/language
/// pairs are stale, translate each document as a single unit (chosen front matter values +
/// masked body, chunked when long), verify the result fail-closed, and write the language
/// folders. A document that cannot be verified is never written — the previously committed
/// translation simply stays in place.
/// </summary>
public sealed class DocsTranslationRunner
{
    private const int TranslateTimeoutSeconds = 300;
    private const int MaxAttemptsPerDoc = 3;
    private const string BodyKeyPrefix = "body";

    private readonly DocsOptions _options;
    private readonly ITranslationReporter _reporter;
    private readonly Func<ITranslator> _translatorFactory;
    private readonly WatchStore _watchStore;

    /// <summary>Pause between AI calls and the unit of retry backoff; zeroed in tests.</summary>
    internal TimeSpan PacingDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    public DocsTranslationRunner(
        DocsOptions options,
        ITranslationReporter? reporter = null,
        Func<ITranslator>? translatorFactory = null)
    {
        _options = options;
        _reporter = reporter ?? NullTranslationReporter.Instance;
        _translatorFactory = translatorFactory
                             ?? (() => TranslatorFactory.Create(options.Engine, options.GetRequiredApiKey(), options.Model));
        _watchStore = WatchStore.ForDocsRoot(options.RootPath);
    }

    /// <summary>Lists the stale document/language pairs without contacting the AI.</summary>
    public async Task<int> ShowChangesAsync(CancellationToken cancellationToken = default)
    {
        var workList = await BuildWorkListAsync(rebuildLanguage: null, cancellationToken);

        _reporter.Info($"Documents needing translation: {workList.Count(work => work.Languages.Count > 0)}");
        foreach (var work in workList.Where(work => work.Languages.Count > 0))
            _reporter.Info($" - {work.RelativePath} ({string.Join(", ", work.Languages)})");

        return ExitCodes.Success;
    }

    /// <summary>Marks every current document as translated without calling the AI.</summary>
    public async Task<int> RebuildWatchFileAsync(CancellationToken cancellationToken = default)
    {
        // Reuse the work-list filters (marker, translate: false) so the watch never tracks
        // documents that a normal run would not translate.
        var workList = await BuildWorkListAsync(rebuildLanguage: null, cancellationToken);
        var orderedKeys = workList.Select(work => work.RelativePath).ToList();
        var hashes = workList.ToDictionary(work => work.RelativePath, work => work.Hash, StringComparer.Ordinal);

        await _watchStore.SaveAsync(orderedKeys, hashes, cancellationToken);
        _reporter.Info($"✓ Docs watch file rebuilt for {orderedKeys.Count} documents. All documents now marked as current.");
        return ExitCodes.Success;
    }

    public Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        return RunAsync(rebuildLanguage: null, cancellationToken);
    }

    /// <summary>Force-retranslates every document for one language.</summary>
    public Task<int> RebuildLanguageAsync(string languageCode, CancellationToken cancellationToken = default)
    {
        if (!_options.Languages.Contains(languageCode, StringComparer.OrdinalIgnoreCase))
            throw new TranslatorException(
                $"Language '{languageCode}' is not one of the configured docs languages " +
                $"({string.Join(", ", _options.Languages)}). Add it to the \"languages\" list first.");

        return RunAsync(languageCode, cancellationToken);
    }

    private async Task<int> RunAsync(string? rebuildLanguage, CancellationToken cancellationToken)
    {
        var workList = await BuildWorkListAsync(rebuildLanguage, cancellationToken);
        var pending = workList.Where(work => work.Languages.Count > 0).ToList();
        var failedDocs = new List<string>();

        if (pending.Count == 0) {
            _reporter.Info("Docs: Up to date, no changes needed.");
        }
        else {
            var translator = _translatorFactory();
            var basePrompt = await LoadDocPromptAsync(cancellationToken);

            var done = 0;
            var total = pending.Sum(work => work.Languages.Count);
            foreach (var work in pending) {
                var docOk = true;
                foreach (var language in work.Languages) {
                    cancellationToken.ThrowIfCancellationRequested();
                    docOk &= await TranslateDocAsync(work, language, translator, basePrompt, cancellationToken);
                    _reporter.Progress(work.RelativePath, ++done, total);
                }

                if (!docOk)
                    failedDocs.Add(work.RelativePath);
            }
        }

        await SaveWatchAsync(workList, failedDocs, cancellationToken);
        await PruneOrphanedTargetsAsync(workList.Select(work => work.RelativePath).ToList(), cancellationToken);

        if (failedDocs.Count > 0) {
            _reporter.Warn($"{failedDocs.Count} document(s) failed verification and were NOT written: " +
                           string.Join(", ", failedDocs));
            return ExitCodes.VerificationFailed;
        }

        _reporter.Info("Done.");
        return ExitCodes.Success;
    }

    private async Task<bool> TranslateDocAsync(
        DocWork work,
        string language,
        ITranslator translator,
        string basePrompt,
        CancellationToken cancellationToken)
    {
        var targetPath = _options.GetTargetFullPath(work.RelativePath, language);
        var targetRelative = _options.GetTargetRelativePath(work.RelativePath, language);
        var extraPrompt = await _options.ExtraPrompt.LoadAsync(language, cancellationToken);

        // Never clobber a document a human wrote at this path; generated files carry a marker.
        if (File.Exists(targetPath) && !IsAutoTranslatedFile(await File.ReadAllTextAsync(targetPath, cancellationToken))) {
            _reporter.Warn($"  {targetRelative}: exists but has no '{PageDocument.AutoTranslatedKey}' marker; " +
                           "assuming it is hand-authored and leaving it alone.");
            return true;
        }

        DocDocument document;
        CodeMasker masker;
        IReadOnlyList<string> bodyChunks;
        TranslateItem[] items;
        try {
            document = DocDocument.Parse(work.Content);
            masker = CodeMasker.Mask(document.Body, _options.MaskPatterns);
            bodyChunks = MarkdownChunker.Split(masker.Masked, _options.ChunkChars);
            items = BuildItems(document, bodyChunks, language);
        }
        catch (TranslatorException ex) {
            // One malformed document must not abort the whole run; report it and keep going.
            _reporter.Warn($"✗ {work.RelativePath}: {ex.Message}");
            return false;
        }

        // Nothing translatable at all (empty body, no chosen front matter): copy-compose so the
        // target still exists, carries the marker, and prunes correctly.
        if (items.Length == 0) {
            var copied = document.Compose(new Dictionary<string, string>(), document.Body, language);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            await File.WriteAllTextAsync(targetPath, copied, new UTF8Encoding(false), cancellationToken);
            _reporter.Info($"✓ {targetRelative}: copied (nothing translatable).");
            return true;
        }

        var feedback = (string?)null;
        for (var attempt = 1; attempt <= MaxAttemptsPerDoc; attempt++) {
            var prompt = PromptBuilder.BuildOptions(items,
                ComposePrompt(basePrompt, feedback),
                ComposeExtraPrompt(extraPrompt, document.TranslatePrompt));
            IReadOnlyList<string> errors;

            try {
                var results = await TranslateWithTimeoutAsync(translator, prompt, cancellationToken);
                var composed = ComposeTranslatedDoc(document, masker, bodyChunks.Count, results, language, out errors);

                if (errors.Count == 0) {
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                    await File.WriteAllTextAsync(targetPath, composed, new UTF8Encoding(false), cancellationToken);
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

            _reporter.Warn($"  {targetRelative}: attempt {attempt}/{MaxAttemptsPerDoc} failed verification:");
            foreach (var error in errors.Take(5))
                _reporter.Warn($"    - {error}");

            feedback = string.Join("\n", errors.Take(10));
            await Task.Delay(PacingDelay * attempt, cancellationToken);
        }

        _reporter.Warn($"✗ {targetRelative}: giving up after {MaxAttemptsPerDoc} attempts; file not written.");
        return false;
    }

    private TranslateItem[] BuildItems(DocDocument document, IReadOnlyList<string> bodyChunks, string language)
    {
        var items = new List<TranslateItem>();

        foreach (var key in _options.FrontMatterKeys) {
            if (document.GetValue(key) is { } value)
                items.Add(NewItem(key, value, language));
        }

        if (bodyChunks.Count == 1 && string.IsNullOrWhiteSpace(bodyChunks[0]))
            return items.ToArray();

        for (var i = 0; i < bodyChunks.Count; i++)
            items.Add(NewItem(BodyKey(i), bodyChunks[i], language));

        return items.ToArray();
    }

    /// <summary>Chunk keys carry an index so the model returns parts we can reassemble in order.</summary>
    private static string BodyKey(int index) => $"{BodyKeyPrefix}[{index}]";

    private TranslateItem NewItem(string key, string text, string language)
    {
        return new TranslateItem {
            SourceLanguage = _options.SourceLanguage,
            TargetLanguage = language,
            Key = key,
            Text = text
        };
    }

    /// <summary>Validates the model output and, when everything holds, builds the final document.</summary>
    private string ComposeTranslatedDoc(
        DocDocument document,
        CodeMasker masker,
        int chunkCount,
        TranslateResult[] results,
        string language,
        out IReadOnlyList<string> errors)
    {
        var errorList = new List<string>();
        var map = results
            .GroupBy(result => result.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().TranslatedText, StringComparer.Ordinal);

        // A missing item must fail loudly; falling back to the source text would silently ship
        // an untranslated fragment inside a translated document.
        var translatedValues = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in _options.FrontMatterKeys) {
            var sourceValue = document.GetValue(key);
            if (sourceValue == null)
                continue;

            if (map.GetValueOrDefault(key) is { } translated && !string.IsNullOrWhiteSpace(translated))
                translatedValues[key] = TranslationPostProcessor.PostProcess(sourceValue, translated, language);
            else
                errorList.Add($"The response contains no '{key}' item.");
        }

        var hasBody = !string.IsNullOrWhiteSpace(document.Body);
        var translatedChunks = new List<string>();
        if (hasBody) {
            for (var i = 0; i < chunkCount; i++) {
                var chunk = map.GetValueOrDefault(BodyKey(i));
                if (string.IsNullOrWhiteSpace(chunk))
                    errorList.Add($"The response contains no '{BodyKey(i)}' item.");
                else
                    translatedChunks.Add(chunk);
            }
        }

        if (errorList.Count > 0) {
            errors = errorList;
            return string.Empty;
        }

        if (!hasBody) {
            errors = errorList;
            return document.Compose(translatedValues, document.Body, language);
        }

        var maskedBody = MarkdownChunker.Join(translatedChunks);
        errorList.AddRange(masker.Validate(maskedBody));
        if (errorList.Count > 0) {
            errors = errorList;
            return string.Empty;
        }

        var body = masker.Unmask(maskedBody);
        errorList.AddRange(MarkdownVerifier.Verify(document.Body, body));

        errors = errorList;
        return errorList.Count > 0 ? string.Empty : document.Compose(translatedValues, body, language);
    }

    private static string ComposePrompt(string basePrompt, string? feedback)
    {
        return feedback == null
            ? basePrompt
            : basePrompt +
              "\n\nYour previous attempt FAILED verification with these errors:\n" + feedback +
              "\nRegenerate the COMPLETE translation and fix every error above.";
    }

    /// <summary>Project prompt then the file's own: the more specific instruction reads last.</summary>
    private static string? ComposeExtraPrompt(string? extraPrompt, string? translatePrompt)
    {
        if (string.IsNullOrWhiteSpace(translatePrompt))
            return extraPrompt;

        return string.IsNullOrWhiteSpace(extraPrompt)
            ? translatePrompt
            : extraPrompt + "\n" + translatePrompt;
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

    private async Task<List<DocWork>> BuildWorkListAsync(string? rebuildLanguage, CancellationToken cancellationToken)
    {
        var docs = SitePageDiscovery.DiscoverUnder(_options.SourceRoot, _options.FilePatterns, _options.ExcludePatterns);
        var snapshot = await _watchStore.LoadAsync(cancellationToken);

        var workList = new List<DocWork>();
        foreach (var doc in docs) {
            var content = await File.ReadAllTextAsync(Path.Combine(_options.SourceRoot, doc), cancellationToken);

            // A generated document must never be treated as a source — translating a
            // translation would cascade into nested language trees. Front-matter-scoped, so a
            // code sample SHOWING the marker cannot trigger it.
            if (IsAutoTranslatedFile(content)) {
                _reporter.Warn($"  {doc}: skipped — carries the '{PageDocument.AutoTranslatedKey}' marker " +
                               "(generated document discovered as a source; check the source folder and excludes).");
                continue;
            }

            // Author opt-out. Dropping the document here also drops it from the prune set, so
            // any previously generated copies of it are cleaned up on this same run.
            if (TryParse(content) is { DoNotTranslate: true }) {
                _reporter.Info($"  {doc}: skipped ({PageDocument.TranslateKey}: false).");
                continue;
            }

            var hash = ComputeHash(content);
            var changed = !string.Equals(snapshot.Entries.GetValueOrDefault(doc), hash, StringComparison.Ordinal);

            var languages = _options.Languages
                .Where(language =>
                    (rebuildLanguage == null && (changed || !File.Exists(_options.GetTargetFullPath(doc, language)))) ||
                    string.Equals(rebuildLanguage, language, StringComparison.OrdinalIgnoreCase))
                .ToList();

            workList.Add(new DocWork(doc, content, hash, languages));
        }

        return workList;
    }

    /// <summary>
    /// Records the new baseline for documents whose every language succeeded; failed documents
    /// keep their old entry (or none), so the next run picks them up again.
    /// </summary>
    private async Task SaveWatchAsync(List<DocWork> workList, List<string> failedDocs, CancellationToken cancellationToken)
    {
        var snapshot = await _watchStore.LoadAsync(cancellationToken);
        var orderedKeys = new List<string>();
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var work in workList) {
            orderedKeys.Add(work.RelativePath);
            map[work.RelativePath] = failedDocs.Contains(work.RelativePath)
                ? snapshot.Entries.GetValueOrDefault(work.RelativePath, string.Empty)
                : work.Hash;
        }

        await _watchStore.SaveAsync(orderedKeys, map, cancellationToken);
    }

    /// <summary>
    /// Deletes generated documents whose source no longer exists (or is no longer included),
    /// so deleted content cannot keep shipping in other languages forever. Only files carrying
    /// the <see cref="PageDocument.AutoTranslatedKey" /> marker are ever deleted.
    /// </summary>
    private async Task PruneOrphanedTargetsAsync(IReadOnlyList<string> docs, CancellationToken cancellationToken)
    {
        // Pruning needs a per-language subtree that is safe to scan, which any pattern
        // ending in /{path} provides.
        if (!_options.OutputPattern.Contains("{lang}", StringComparison.Ordinal) ||
            !_options.OutputPattern.EndsWith("/{path}", StringComparison.Ordinal))
            return;

        var docSet = docs.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var language in _options.Languages) {
            var languageRootRelative = _options.OutputPattern
                .Replace("{lang}", language, StringComparison.Ordinal)
                .Replace("/{path}", "", StringComparison.Ordinal);
            var languageRoot = Path.GetFullPath(Path.Combine(_options.RootPath, languageRootRelative));
            if (!Directory.Exists(languageRoot))
                continue;

            foreach (var relativePath in SitePageDiscovery.DiscoverUnder(languageRoot, _options.FilePatterns)) {
                cancellationToken.ThrowIfCancellationRequested();
                if (docSet.Contains(relativePath))
                    continue;

                var fullPath = Path.Combine(languageRoot, relativePath);
                if (!IsAutoTranslatedFile(await File.ReadAllTextAsync(fullPath, cancellationToken)))
                    continue;

                File.Delete(fullPath);
                _reporter.Info($"  {languageRootRelative}/{relativePath}: pruned (source document no longer exists).");
            }
        }
    }

    /// <summary>Marker check via the parsed front matter; an unparseable file is treated as
    /// hand-authored, which fails safe — it is skipped, never overwritten or deleted.</summary>
    private static bool IsAutoTranslatedFile(string content)
    {
        return TryParse(content) is { IsAutoTranslated: true };
    }

    private static DocDocument? TryParse(string content)
    {
        try {
            return DocDocument.Parse(content);
        }
        catch (TranslatorException) {
            return null;
        }
    }

    private static async Task<string> LoadDocPromptAsync(CancellationToken cancellationToken)
    {
        var promptFile = Path.Combine(AppContext.BaseDirectory, "Resources", "doc-prompt.txt");
        if (!File.Exists(promptFile))
            throw new TranslatorException($"Built-in doc prompt template is missing: {promptFile}", ExitCodes.FileNotFound);

        return await File.ReadAllTextAsync(promptFile, cancellationToken);
    }

    /// <summary>Whole file, front matter included — a changed per-file prompt or title must
    /// retranslate. Newlines are normalized so cross-OS checkouts do not invalidate the watch.</summary>
    private static string ComputeHash(string content)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    private sealed record DocWork(string RelativePath, string Content, string Hash, List<string> Languages);
}
