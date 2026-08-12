using System.Text.Json.Serialization;

namespace VpnHood.Tools.ResourceTranslator.Docs;

/// <summary>
/// The optional <c>"docs"</c> section of <c>vhtranslator.json</c>: settings for translating a
/// folder of standalone markdown documents (app content, blog posts) where each file — not a
/// key/value resource and not a Jekyll site — is the unit of translation. The layout is
/// folder-per-language: the source folder is named after its language (<c>posts/en</c>) and
/// each target language becomes a sibling folder (<c>posts/fa</c>).
/// All paths and globs are relative to the config file's directory.
/// </summary>
public sealed record DocsConfig
{
    /// <summary>The source-language folder holding the hand-authored documents (e.g.
    /// <c>posts/en</c> or <c>src/content/en</c>). Required.</summary>
    [JsonPropertyName("source")]
    public string? Source { get; init; }

    /// <summary>Globs selecting the documents inside <see cref="Source" /> (default: <c>**/*.md</c>).</summary>
    [JsonPropertyName("files")]
    public string[]? Files { get; init; }

    /// <summary>Globs excluded from discovery, relative to <see cref="Source" />.</summary>
    [JsonPropertyName("exclude")]
    public string[]? Exclude { get; init; }

    /// <summary>Target language codes. Each becomes a sibling folder of <see cref="Source" />
    /// (unless <see cref="Output" /> says otherwise).</summary>
    [JsonPropertyName("languages")]
    public string[]? Languages { get; init; }

    /// <summary>
    /// Output path template with <c>{lang}</c> and <c>{path}</c> tokens, relative to the config
    /// directory; <c>{path}</c> is the document's path relative to <see cref="Source" />.
    /// Default: the source folder's sibling — <c>posts/en</c> → <c>posts/{lang}/{path}</c>.
    /// </summary>
    [JsonPropertyName("output")]
    public string? Output { get; init; }

    /// <summary>Language code of the source documents (default: the leaf folder name of
    /// <see cref="Source" /> when it looks like a language code, else <c>en</c>).</summary>
    [JsonPropertyName("sourceLanguage")]
    public string? SourceLanguage { get; init; }

    /// <summary>Front matter keys whose values are translated; every other key is copied
    /// verbatim. Default: <c>title</c>, <c>description</c>, <c>image_alt</c>.</summary>
    [JsonPropertyName("frontMatter")]
    public string[]? FrontMatter { get; init; }

    /// <summary>
    /// Bodies longer than this many characters are split at paragraph boundaries and translated
    /// in parts, so a long document cannot silently exceed a model's output budget
    /// (default: 10000).
    /// </summary>
    [JsonPropertyName("chunkChars")]
    public int? ChunkChars { get; init; }

    /// <summary>
    /// Extra regex patterns masked from the model besides code, e.g. runtime placeholders.
    /// Replaces the default list (<c>\{[A-Za-z0-9_]+\}</c>); an empty array masks none.
    /// </summary>
    [JsonPropertyName("maskPatterns")]
    public string[]? MaskPatterns { get; init; }
}
