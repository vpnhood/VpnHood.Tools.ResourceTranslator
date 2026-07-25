using System.Diagnostics.CodeAnalysis;
using VpnHood.Tools.ResourceTranslator.Watch;

namespace VpnHood.Tools.ResourceTranslator.Formats;

/// <summary>
/// Folder-per-language convention: the base file lives in a folder named after its language
/// (e.g. <c>i18n/en/home.json</c>) and each target language is the same file name inside a
/// sibling folder (<c>i18n/fa/home.json</c>). Reading and writing the file itself is delegated
/// to the per-extension format, so the convention works for every supported file type.
/// </summary>
public sealed class LanguageFolderResourceFormat(IResourceFormat inner) : IResourceFormat
{
    public bool TryLoad(string path, [NotNullWhen(true)] out List<KeyValuePair<string, string>>? entries, out string? error)
    {
        return inner.TryLoad(path, out entries, out error);
    }

    public async Task SaveAsync(string path, IReadOnlyList<string> orderedKeys, IReadOnlyDictionary<string, string> map)
    {
        // A target language folder does not exist until its first translation lands.
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await inner.SaveAsync(path, orderedKeys, map);
    }

    public string GetLanguageCode(string path)
    {
        return Path.GetFileName(GetLanguageFolder(path));
    }

    public IEnumerable<string> FindSiblingLocaleFiles(string basePath)
    {
        var fullBase = Path.GetFullPath(basePath);
        var languageFolder = GetLanguageFolder(fullBase);
        var parent = Path.GetDirectoryName(languageFolder);
        if (parent == null)
            yield break;

        var fileName = Path.GetFileName(fullBase);
        foreach (var folder in Directory.EnumerateDirectories(parent)) {
            if (string.Equals(folder, languageFolder, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.Equals(Path.GetFileName(folder), WatchStore.PrivateFolderName, StringComparison.OrdinalIgnoreCase))
                continue;

            var candidate = Path.Combine(folder, fileName);
            if (File.Exists(candidate))
                yield return candidate;
        }
    }

    public string GetLocaleFilePath(string basePath, string languageCode)
    {
        var fullBase = Path.GetFullPath(basePath);
        var languageFolder = GetLanguageFolder(fullBase);
        var parent = Path.GetDirectoryName(languageFolder)
                     ?? throw new TranslatorException(
                         $"A language folder needs a parent to place sibling languages in: {languageFolder}");

        return Path.Combine(parent, languageCode, Path.GetFileName(fullBase));
    }

    private static string GetLanguageFolder(string path)
    {
        return Path.GetDirectoryName(Path.GetFullPath(path))
               ?? throw new TranslatorException($"Cannot determine the language folder of '{path}'.");
    }
}
