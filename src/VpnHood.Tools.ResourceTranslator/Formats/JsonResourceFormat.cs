using System.Diagnostics.CodeAnalysis;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VpnHood.Tools.ResourceTranslator.Formats;

public class JsonResourceFormat : IResourceFormat
{
    private static readonly JsonSerializerOptions OutputSerializerOptions = new() {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public bool TryLoad(string path, [NotNullWhen(true)] out List<KeyValuePair<string, string>>? entries, out string? error)
    {
        try {
            var text = File.ReadAllText(path);
            if (JsonNode.Parse(text) is not JsonObject obj)
                throw new Exception("Root is not a JSON object.");

            entries = obj
                .Select(kv => new KeyValuePair<string, string>(kv.Key, kv.Value?.GetValue<string>() ?? string.Empty))
                .ToList();
            error = null;
            return true;
        }
        catch (Exception ex) {
            entries = null;
            error = ex.Message;
            return false;
        }
    }

    public async Task SaveAsync(string path, IReadOnlyList<string> orderedKeys, IReadOnlyDictionary<string, string> map)
    {
        var obj = new JsonObject();
        foreach (var key in orderedKeys)
            obj[key] = map.GetValueOrDefault(key, string.Empty);

        await using var fs = File.Create(path);
        await JsonSerializer.SerializeAsync(fs, obj, OutputSerializerOptions);
    }

    public string GetLanguageCode(string path)
    {
        // Locale files are named {language-code}.json (e.g., en.json, fr.json)
        return Path.GetFileNameWithoutExtension(path);
    }

    public IEnumerable<string> FindSiblingLocaleFiles(string basePath)
    {
        var fullBase = Path.GetFullPath(basePath);
        var dir = Path.GetDirectoryName(fullBase)!;
        var baseFileName = Path.GetFileName(fullBase);
        return Directory.EnumerateFiles(dir, "*.json", SearchOption.TopDirectoryOnly)
            .Where(p => !Path.GetFileName(p).Equals(baseFileName, StringComparison.OrdinalIgnoreCase));
    }

    public string GetLocaleFilePath(string basePath, string languageCode)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(basePath))!;
        return Path.Combine(dir, $"{languageCode}.json");
    }
}
