using System.Text.Json;
using System.Text.Json.Serialization;

namespace VpnHood.Tools.ResourceTranslator.Configuration;

/// <summary>
/// Optional per-repository settings file (<c>vhtranslator.json</c>), discovered by walking up
/// from the base resource file (or the working directory). Every value is overridable on the
/// command line; the file exists so that CI and contributors can just run <c>vhtranslator</c>.
/// </summary>
public sealed record TranslatorConfig
{
    public const string FileName = "vhtranslator.json";

    private static readonly JsonSerializerOptions ReadOptions = new() {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>Path to the base language file, relative to this config file.</summary>
    [JsonPropertyName("base")]
    public string? Base { get; init; }

    [JsonPropertyName("engine")]
    public string? Engine { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("batch")]
    public int? Batch { get; init; }

    /// <summary>Path to extra prompt instructions, relative to this config file.</summary>
    [JsonPropertyName("extraPrompt")]
    public string? ExtraPrompt { get; init; }

    /// <summary>
    /// Explicit target languages. When set, these files are created if missing instead of
    /// relying on discovery of sibling locale files.
    /// </summary>
    [JsonPropertyName("languages")]
    public string[]? Languages { get; init; }

    /// <summary>Settings for the <c>site</c> command; null when the repo has no site section.</summary>
    [JsonPropertyName("site")]
    public Site.SiteConfig? Site { get; init; }

    /// <summary>Directory the config was loaded from; relative paths resolve against it.</summary>
    [JsonIgnore]
    public string BaseDirectory { get; private init; } = Directory.GetCurrentDirectory();

    [JsonIgnore]
    public string? SourcePath { get; private init; }

    public static TranslatorConfig Empty { get; } = new();

    /// <summary>Loads a config from an explicit path, failing loudly if it is missing or malformed.</summary>
    public static TranslatorConfig Load(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new TranslatorException($"Config file not found: {fullPath}", ExitCodes.FileNotFound);

        return Parse(fullPath);
    }

    /// <summary>
    /// Searches <paramref name="startDirectory" /> and each parent for a config file — either
    /// directly in the folder or tucked away in its <c>vh_translator/</c> folder.
    /// Returns <see cref="Empty" /> when none exists — the config file is entirely optional.
    /// </summary>
    public static TranslatorConfig Discover(string startDirectory)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(startDirectory));
        while (dir != null) {
            var candidate = Path.Combine(dir.FullName, FileName);
            if (File.Exists(candidate))
                return Parse(candidate);

            var nested = Path.Combine(dir.FullName, Watch.WatchStore.PrivateFolderName, FileName);
            if (File.Exists(nested))
                return Parse(nested);

            dir = dir.Parent;
        }

        return Empty;
    }

    /// <summary>Resolves a config-relative path against the config's own directory.</summary>
    public string? ResolvePath(string? relativeOrAbsolute)
    {
        return string.IsNullOrWhiteSpace(relativeOrAbsolute)
            ? null
            : Path.GetFullPath(Path.Combine(BaseDirectory, relativeOrAbsolute));
    }

    private static TranslatorConfig Parse(string fullPath)
    {
        try {
            var json = File.ReadAllText(fullPath);
            var config = JsonSerializer.Deserialize<TranslatorConfig>(json, ReadOptions)
                         ?? throw new TranslatorException($"Config file is empty: {fullPath}", ExitCodes.ParseError);

            var baseDirectory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();

            // A config kept inside the tool's own folder belongs to the folder's parent: paths
            // stay relative to the repo/site root, exactly as if the file sat there itself.
            if (string.Equals(Path.GetFileName(baseDirectory), Watch.WatchStore.PrivateFolderName, StringComparison.OrdinalIgnoreCase) &&
                Path.GetDirectoryName(baseDirectory) is { } parent)
                baseDirectory = parent;

            return config with {
                BaseDirectory = baseDirectory,
                SourcePath = fullPath
            };
        }
        catch (JsonException ex) {
            throw new TranslatorException($"Failed to parse {fullPath}: {ex.Message}", ExitCodes.ParseError);
        }
    }
}
