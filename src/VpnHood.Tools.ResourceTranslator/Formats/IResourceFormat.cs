using System.Diagnostics.CodeAnalysis;

namespace VpnHood.Tools.ResourceTranslator.Formats;

public interface IResourceFormat
{
    bool TryLoad(string path, [NotNullWhen(true)] out List<KeyValuePair<string, string>>? entries, out string? error);
    Task SaveAsync(string path, IReadOnlyList<string> orderedKeys, IReadOnlyDictionary<string, string> map);
    string GetLanguageCode(string path);
    IEnumerable<string> FindSiblingLocaleFiles(string basePath);
    string GetLocaleFilePath(string basePath, string languageCode);
}
