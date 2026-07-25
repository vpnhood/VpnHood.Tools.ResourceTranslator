using System.Text.Json.Serialization;

namespace VpnHood.Tools.ResourceTranslator.Site;

/// <summary>
/// The optional <c>"site"</c> section of <c>vhtranslator.json</c>: settings for translating a
/// static (Jekyll-style) website where whole pages, not key/value resources, are the unit of
/// translation. All paths and globs are relative to the config file's directory.
/// </summary>
public sealed record SiteConfig
{
    /// <summary>Globs selecting the source pages (default: <c>**/index.html</c>).</summary>
    [JsonPropertyName("pages")]
    public string[]? Pages { get; init; }

    /// <summary>Globs excluded from page discovery (build output, legal pages, ...).</summary>
    [JsonPropertyName("exclude")]
    public string[]? Exclude { get; init; }

    /// <summary>Target language codes. Each becomes a top-level output folder.</summary>
    [JsonPropertyName("languages")]
    public string[]? Languages { get; init; }

    /// <summary>
    /// Output path template with <c>{lang}</c> and <c>{path}</c> tokens
    /// (default: <c>{lang}/{path}</c>).
    /// </summary>
    [JsonPropertyName("output")]
    public string? Output { get; init; }

    /// <summary>
    /// Base key/value resources translated with the classic pipeline as part of a site run,
    /// using the same target languages. Each entry is a resource file (e.g.
    /// <c>_data/i18n/en.json</c>) or a language folder (e.g. <c>_data/i18n/en</c>) whose files
    /// are translated to sibling language folders.
    /// </summary>
    [JsonPropertyName("data")]
    public string[]? Data { get; init; }

    /// <summary>Language code of the source pages (default: <c>en</c>).</summary>
    [JsonPropertyName("sourceLanguage")]
    public string? SourceLanguage { get; init; }

    /// <summary>
    /// What to do with page bodies: <c>translate</c> (default) sends the whole body to the
    /// model; <c>copy</c> keeps the body byte-identical and translates only the front-matter
    /// title and description — for sites whose page text lives in i18n data files and
    /// self-localizes through the page's <c>lang</c>.
    /// </summary>
    [JsonPropertyName("pageBody")]
    public string? PageBody { get; init; }

    /// <summary>
    /// Text every translated page title must contain (e.g. a brand name). Verification fails
    /// for the page when the translated title lost it.
    /// </summary>
    [JsonPropertyName("titleMustContain")]
    public string? TitleMustContain { get; init; }
}
