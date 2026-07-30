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

    /// <summary>Subfolder of <see cref="PrivateFolderName" /> holding the watch files.</summary>
    public const string WatchesFolderName = "watches";

    private static readonly JsonSerializerOptions OutputSerializerOptions = new() {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string _path;

    /// <summary>Older locations (newest first); read when the new path is missing, removed on save.</summary>
    private readonly string[] _legacyPaths;

    private WatchStore(string path, string[]? legacyPaths = null)
    {
        _path = path;
        _legacyPaths = legacyPaths ?? [];
    }

    /// <summary>Watch file name for one base stem (e.g. <c>en.watch.json</c>).</summary>
    private static string GetWatchFileName(string stem)
    {
        return $"{stem}.watch.json";
    }

    /// <summary>Name used by pre-1.2.2 releases; still read and migrated on the next save.</summary>
    private static string GetLegacyWatchFileName(string stem)
    {
        return $"{stem}_watch.json";
    }

    private static WatchStore InPrivateFolder(string privateFolder, string stem)
    {
        return new WatchStore(
            Path.Combine(privateFolder, WatchesFolderName, GetWatchFileName(stem)),
            [
                Path.Combine(privateFolder, WatchesFolderName, GetLegacyWatchFileName(stem)),
                Path.Combine(privateFolder, GetLegacyWatchFileName(stem))
            ]);
    }

    /// <summary>
    /// A watch in a namespace subfolder of watches/ (e.g. watches/i18n/, watches/pages/).
    /// Older releases used an underscore name — first in the subfolder, before that a prefixed
    /// flat file inside watches/, and originally directly in the private folder; all are still
    /// read and are migrated on the next save.
    /// </summary>
    private static WatchStore InWatchesSubfolder(string privateFolder, string subfolder, string stem,
        string legacyFlatStem)
    {
        return new WatchStore(
            Path.Combine(privateFolder, WatchesFolderName, subfolder, GetWatchFileName(stem)),
            [
                Path.Combine(privateFolder, WatchesFolderName, subfolder, GetLegacyWatchFileName(stem)),
                Path.Combine(privateFolder, WatchesFolderName, GetLegacyWatchFileName(legacyFlatStem)),
                Path.Combine(privateFolder, GetLegacyWatchFileName(legacyFlatStem))
            ]);
    }

    public static WatchStore ForBaseFile(string basePath, string? configPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);
        var baseName = Path.GetFileNameWithoutExtension(basePath);
        if (string.IsNullOrWhiteSpace(configPath))
            return InPrivateFolder(GetPrivateFolderPath(basePath), baseName);

        // With a config the bookkeeping goes next to it (like folder mode), namespaced by the
        // base file's folder, so the resource tree itself stays clean. Base-adjacent locations
        // from config-less runs and older releases are still read and migrated on save.
        var baseAdjacent = GetPrivateFolderPath(basePath);
        var subfolder = Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(basePath)))!;
        return new WatchStore(
            Path.Combine(GetPrivateFolderForConfig(configPath), WatchesFolderName, subfolder, GetWatchFileName(baseName)),
            [
                Path.Combine(GetPrivateFolderForConfig(configPath), WatchesFolderName, subfolder, GetLegacyWatchFileName(baseName)),
                Path.Combine(baseAdjacent, WatchesFolderName, GetWatchFileName(baseName)),
                Path.Combine(baseAdjacent, WatchesFolderName, GetLegacyWatchFileName(baseName)),
                Path.Combine(baseAdjacent, GetLegacyWatchFileName(baseName))
            ]);
    }

    /// <summary>
    /// Watch file for one file of a language-folder base (e.g. <c>i18n/en/home.json</c>).
    /// Bookkeeping goes next to the config file when one exists, so data trees that are
    /// consumed verbatim (Jekyll <c>_data</c>, app bundles) are not polluted; without a config
    /// it falls back to a <c>vh_translator</c> folder beside the language folders.
    /// </summary>
    public static WatchStore ForLanguageFolderFile(string languageFolderPath, string baseFilePath, string? configPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(languageFolderPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseFilePath);

        var languageFolder = Path.GetFullPath(languageFolderPath);
        var folderParent = Path.GetDirectoryName(languageFolder)
                           ?? throw new ArgumentException($"Cannot determine the parent of '{languageFolderPath}'.",
                               nameof(languageFolderPath));

        var privateFolder = string.IsNullOrWhiteSpace(configPath)
            ? Path.Combine(folderParent, PrivateFolderName)
            : GetPrivateFolderForConfig(configPath);

        // A subfolder named after the language trees' parent keeps files from different
        // bases apart (watches/i18n/home.watch.json vs watches/locales/home.watch.json).
        var subfolder = Path.GetFileName(folderParent);
        var stem = Path.GetFileNameWithoutExtension(baseFilePath);
        return InWatchesSubfolder(privateFolder, subfolder, stem, $"{subfolder}_{stem}");
    }

    /// <summary>
    /// Bookkeeping folder for a run driven by a config file: the config's own folder when it
    /// already is <c>vh_translator</c>, otherwise a <c>vh_translator</c> folder beside it.
    /// </summary>
    public static string GetPrivateFolderForConfig(string configPath)
    {
        var configDir = Path.GetDirectoryName(Path.GetFullPath(configPath))
                        ?? throw new ArgumentException($"Cannot determine directory of '{configPath}'.", nameof(configPath));

        return string.Equals(Path.GetFileName(configDir), PrivateFolderName, StringComparison.OrdinalIgnoreCase)
            ? configDir
            : Path.Combine(configDir, PrivateFolderName);
    }

    /// <summary>
    /// Watch file for a site run: keys are page paths relative to the site root, values are
    /// content hashes rather than raw source text (pages are too large to inline).
    /// </summary>
    public static WatchStore ForSiteRoot(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        return InWatchesSubfolder(Path.Combine(Path.GetFullPath(rootPath), PrivateFolderName),
            "pages", "site", "site");
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
        // Fall back to older locations so existing projects stay incremental;
        // the next save migrates the file.
        var path = _path;
        if (!File.Exists(path))
            path = _legacyPaths.FirstOrDefault(File.Exists) ?? path;

        if (!File.Exists(path))
            return WatchSnapshot.Empty;

        try {
            var text = await File.ReadAllTextAsync(path, cancellationToken);
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

        // Complete the migration: legacy files would otherwise linger as stale duplicates.
        foreach (var legacyPath in _legacyPaths.Where(File.Exists))
            File.Delete(legacyPath);
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
