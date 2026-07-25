using System.Text;
using System.Text.RegularExpressions;

namespace VpnHood.Tools.ResourceTranslator.Site;

/// <summary>
/// A Jekyll page split into YAML front matter and body. The front matter is never sent to the
/// model as a whole: only the <c>title</c> and <c>description</c> values are translated, every
/// other line is copied verbatim, and the tool itself appends <c>lang</c> plus the
/// <c>auto_translated</c> marker. Structure therefore cannot be corrupted by translation.
/// </summary>
public sealed partial class PageDocument
{
    /// <summary>Front matter key marking a file as tool-generated; guards against clobbering
    /// hand-authored pages that happen to sit at a target path.</summary>
    public const string AutoTranslatedKey = "auto_translated";

    /// <summary>Front matter key a page author sets to <c>false</c> to opt the page out of
    /// translation — the per-page alternative to a config exclude glob.</summary>
    public const string TranslateKey = "translate";

    [GeneratedRegex(@"^(?<key>[A-Za-z0-9_-]+):\s*(?<value>.*?)\s*$")]
    private static partial Regex FrontMatterLineRegex();

    [GeneratedRegex(@"^auto_translated:\s*true\s*$", RegexOptions.Multiline)]
    private static partial Regex AutoTranslatedRegex();

    [GeneratedRegex(@"^translate:\s*(false|no|off)\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex DoNotTranslateRegex();

    private PageDocument(List<string> frontMatterLines, string body, string newline)
    {
        FrontMatterLines = frontMatterLines;
        Body = body;
        Newline = newline;
        Title = GetValue("title");
        Description = GetValue("description");
    }

    /// <summary>Raw front matter lines between the two <c>---</c> markers.</summary>
    public IReadOnlyList<string> FrontMatterLines { get; }

    public string Body { get; }

    /// <summary>Newline style of the source file, so generated files match it.</summary>
    public string Newline { get; }

    public string? Title { get; }
    public string? Description { get; }

    public static PageDocument Parse(string content)
    {
        var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);

        if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
            throw new TranslatorException("Page has no YAML front matter (must start with '---').", ExitCodes.ParseError);

        var end = normalized.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end < 0)
            throw new TranslatorException("Page front matter is not closed by a second '---' line.", ExitCodes.ParseError);

        var frontMatter = normalized[4..end];
        var bodyStart = normalized.IndexOf('\n', end + 1);
        var body = bodyStart < 0 ? string.Empty : normalized[(bodyStart + 1)..];

        var lines = frontMatter.Length == 0 ? [] : frontMatter.Split('\n').ToList();
        return new PageDocument(lines, body, newline);
    }

    /// <summary>True when the file carries the <c>auto_translated: true</c> marker.</summary>
    public static bool HasAutoTranslatedMarker(string content)
    {
        return AutoTranslatedRegex().IsMatch(content.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    /// <summary>True when the page opts out of translation via <c>translate: false</c>.</summary>
    public static bool HasDoNotTranslateFlag(string content)
    {
        return DoNotTranslateRegex().IsMatch(content.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    /// <summary>
    /// Rebuilds the page with translated title/description/body, the target language, and the
    /// generated-file marker. Every other front matter line is carried over byte for byte.
    /// </summary>
    public string Compose(string? translatedTitle, string? translatedDescription, string translatedBody, string languageCode)
    {
        var sb = new StringBuilder();
        sb.Append("---").Append(Newline);

        foreach (var line in FrontMatterLines) {
            var match = FrontMatterLineRegex().Match(line);
            var key = match.Success ? match.Groups["key"].Value : null;

            switch (key) {
                case "title" when translatedTitle != null:
                    sb.Append("title: ").Append(QuoteYaml(translatedTitle)).Append(Newline);
                    break;
                case "description" when translatedDescription != null:
                    sb.Append("description: ").Append(QuoteYaml(translatedDescription)).Append(Newline);
                    break;
                case "lang":
                case AutoTranslatedKey:
                    break; // replaced below
                default:
                    sb.Append(line).Append(Newline);
                    break;
            }
        }

        sb.Append("lang: ").Append(languageCode).Append(Newline);
        sb.Append(AutoTranslatedKey).Append(": true").Append(Newline);
        sb.Append("---").Append(Newline);

        // Model output may carry either ending; normalize first so files never end up mixed.
        var body = translatedBody.Replace("\r\n", "\n", StringComparison.Ordinal);
        sb.Append(Newline == "\n" ? body : body.Replace("\n", Newline, StringComparison.Ordinal));
        return sb.ToString();
    }

    private string? GetValue(string key)
    {
        foreach (var line in FrontMatterLines) {
            var match = FrontMatterLineRegex().Match(line);
            if (!match.Success || !string.Equals(match.Groups["key"].Value, key, StringComparison.Ordinal))
                continue;

            return UnquoteYaml(match.Groups["value"].Value);
        }

        return null;
    }

    /// <summary>Minimal YAML scalar unquoting: the double/single-quoted styles Jekyll pages use.</summary>
    private static string? UnquoteYaml(string value)
    {
        if (value.Length == 0)
            return null;

        // Block scalars (title: > / |) span the following indented lines, which this simple
        // line model cannot rewrite safely — treat the value as untranslatable so the whole
        // block is copied through verbatim instead of being corrupted.
        if (value[0] is '>' or '|')
            return null;

        if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"')) {
            return value[1..^1]
                .Replace("\\\"", "\"", StringComparison.Ordinal)
                .Replace("\\\\", "\\", StringComparison.Ordinal);
        }

        if (value.Length >= 2 && value.StartsWith('\'') && value.EndsWith('\''))
            return value[1..^1].Replace("''", "'", StringComparison.Ordinal);

        return value;
    }

    /// <summary>Always double-quotes, so translated text can never break YAML syntax.</summary>
    private static string QuoteYaml(string value)
    {
        return "\"" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }
}
