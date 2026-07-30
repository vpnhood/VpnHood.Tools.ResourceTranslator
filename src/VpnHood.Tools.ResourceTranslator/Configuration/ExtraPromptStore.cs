namespace VpnHood.Tools.ResourceTranslator.Configuration;

/// <summary>
/// Locates and loads the project's extra prompt instructions: one shared file applied to every
/// language, plus optional per-language additions in a <c>prompts/</c> subfolder of the private
/// folder (e.g. <c>vh_translator/prompts/fa.prompt.txt</c>), appended after the shared text.
/// </summary>
public sealed class ExtraPromptStore
{
    /// <summary>Shared prompt file at the root of the private folder.</summary>
    public const string SharedPromptFileName = "prompt.txt";

    /// <summary>Shared prompt name used by older releases; honored when the new name is absent.</summary>
    public const string LegacySharedPromptFileName = "custom_prompt.txt";

    /// <summary>Subfolder of the private folder holding the per-language prompt files.</summary>
    public const string PromptsFolderName = "prompts";

    private readonly IReadOnlyList<string> _promptsFolders;
    private readonly Dictionary<string, string?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public static ExtraPromptStore Empty { get; } = new(null, []);

    /// <param name="sharedPromptPath">Absolute path of the shared prompt file, or null when none.</param>
    /// <param name="privateFolders">Private (vh_translator) folders to probe for per-language prompts, in priority order.</param>
    public ExtraPromptStore(string? sharedPromptPath, IReadOnlyList<string> privateFolders)
    {
        SharedPromptPath = sharedPromptPath;
        _promptsFolders = privateFolders.Select(folder => Path.Combine(folder, PromptsFolderName)).ToArray();
    }

    /// <summary>Absolute path of the shared prompt file, or null when the project has none.</summary>
    public string? SharedPromptPath { get; }

    /// <summary>Per-language prompt file name for one target language (e.g. <c>fa.prompt.txt</c>).</summary>
    public static string GetLanguagePromptFileName(string languageCode)
    {
        return $"{languageCode}.prompt.txt";
    }

    /// <summary>
    /// Builds a store from an explicit shared prompt path (which must exist — a configured file
    /// that is missing must never degrade silently to "no instructions") or, when none is given,
    /// from the conventional files inside <paramref name="privateFolders" />, preferring the
    /// current name over the legacy one within each folder.
    /// </summary>
    public static ExtraPromptStore Resolve(string? explicitSharedPath, IReadOnlyList<string> privateFolders)
    {
        if (explicitSharedPath != null) {
            if (!File.Exists(explicitSharedPath))
                throw new TranslatorException($"Extra prompt file not found: {explicitSharedPath}", ExitCodes.FileNotFound);
            return new ExtraPromptStore(explicitSharedPath, privateFolders);
        }

        var conventional = privateFolders
            .SelectMany(folder => new[] {
                Path.Combine(folder, SharedPromptFileName),
                Path.Combine(folder, LegacySharedPromptFileName)
            })
            .FirstOrDefault(File.Exists);

        return new ExtraPromptStore(conventional, privateFolders);
    }

    /// <summary>
    /// Every prompt file that can affect this run — the shared file plus the per-language files
    /// present — so it can be logged once at startup and users can tell from the log which
    /// instructions shaped a translation. A file in an earlier folder shadows the same name in
    /// a later one, exactly as <see cref="LoadAsync" /> resolves it.
    /// </summary>
    public IReadOnlyList<string> GetPromptFilePaths()
    {
        var paths = new List<string>();
        if (SharedPromptPath != null)
            paths.Add(SharedPromptPath);

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in _promptsFolders.Where(Directory.Exists)) {
            paths.AddRange(Directory.EnumerateFiles(folder, "*.prompt.txt")
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .Where(file => seenNames.Add(Path.GetFileName(file))));
        }

        return paths;
    }

    /// <summary>
    /// The extra prompt for one target language: the shared text, then the language's own file
    /// separated by a blank line. Null when the project has neither.
    /// </summary>
    public async Task<string?> LoadAsync(string languageCode, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(languageCode, out var cached))
            return cached;

        var parts = new List<string>(2);
        if (SharedPromptPath != null)
            parts.Add((await File.ReadAllTextAsync(SharedPromptPath, cancellationToken)).Trim());

        var languagePath = _promptsFolders
            .Select(folder => Path.Combine(folder, GetLanguagePromptFileName(languageCode)))
            .FirstOrDefault(File.Exists);
        if (languagePath != null)
            parts.Add((await File.ReadAllTextAsync(languagePath, cancellationToken)).Trim());

        var nonEmptyParts = parts.Where(part => part.Length > 0).ToArray();
        var prompt = nonEmptyParts.Length == 0 ? null : string.Join("\n\n", nonEmptyParts);
        _cache[languageCode] = prompt;
        return prompt;
    }
}
