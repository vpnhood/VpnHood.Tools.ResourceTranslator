using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VpnHood.Tools.ResourceTranslator.Watch;

/// <summary>
/// Persists the source text last seen for every key, so a later run can tell which entries
/// actually changed. Older releases stored MD5 hashes instead; those files are still readable
/// and are migrated to the current format on the next successful save.
/// </summary>
public sealed class WatchStore
{
    /// <summary>Folder (next to the base resource file) holding translator bookkeeping.</summary>
    public const string PrivateFolderName = "vh_translator";

    private static readonly JsonSerializerOptions OutputSerializerOptions = new() {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string _path;

    private WatchStore(string path)
    {
        _path = path;
    }

    public static WatchStore ForBaseFile(string basePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);
        var baseName = Path.GetFileNameWithoutExtension(basePath);
        return new WatchStore(Path.Combine(GetPrivateFolderPath(basePath), $"{baseName}_watch.json"));
    }

    public static string GetPrivateFolderPath(string basePath)
    {
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(basePath))
                      ?? throw new ArgumentException($"Cannot determine directory of '{basePath}'.", nameof(basePath));
        return Path.Combine(baseDir, PrivateFolderName);
    }

    /// <summary>
    /// Loads the previous snapshot. A missing or corrupt watch file is treated as "nothing known
    /// yet", which makes the next run retranslate everything rather than silently skipping work.
    /// </summary>
    public async Task<WatchSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
            return WatchSnapshot.Empty;

        try {
            var text = await File.ReadAllTextAsync(_path, cancellationToken);
            if (JsonNode.Parse(text) is not JsonObject obj)
                return WatchSnapshot.Empty;

            // Current format: { "version": 1, "items": { key: sourceText } }
            if (obj.ContainsKey("version")) {
                var watch = obj.Deserialize<WatchFile>();
                return new WatchSnapshot(ToOrdinal(watch?.Items), EntriesAreHashes: false);
            }

            // Legacy format: flat { key: md5Hash }
            var legacy = obj.Deserialize<Dictionary<string, string>>();
            return new WatchSnapshot(ToOrdinal(legacy), EntriesAreHashes: true);
        }
        catch (Exception ex) when (ex is JsonException or IOException) {
            return WatchSnapshot.Empty;
        }
    }

    public async Task SaveAsync(
        IReadOnlyList<string> orderedKeys,
        IReadOnlyDictionary<string, string> baseMap,
        CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var items = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in orderedKeys)
            items[key] = baseMap.GetValueOrDefault(key, string.Empty);

        var text = JsonSerializer.Serialize(new WatchFile { Items = items }, OutputSerializerOptions);
        await File.WriteAllTextAsync(_path, text, cancellationToken);
    }

    private static Dictionary<string, string> ToOrdinal(Dictionary<string, string>? source)
    {
        return source == null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(source, StringComparer.Ordinal);
    }
}

/// <summary>The previously recorded state of the base file.</summary>
public sealed record WatchSnapshot(Dictionary<string, string> Entries, bool EntriesAreHashes)
{
    public static WatchSnapshot Empty { get; } = new(new Dictionary<string, string>(StringComparer.Ordinal), false);

    /// <summary>Keys whose source text differs from the last translated run (new keys count as changed).</summary>
    public HashSet<string> GetChangedKeys(IReadOnlyDictionary<string, string> baseMap)
    {
        var changed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, text) in baseMap) {
            var current = EntriesAreHashes ? ComputeMd5(text) : text;
            if (!string.Equals(current, Entries.GetValueOrDefault(key), StringComparison.Ordinal))
                changed.Add(key);
        }

        return changed;
    }

    private static string ComputeMd5(string value)
    {
        return Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(value)));
    }
}
