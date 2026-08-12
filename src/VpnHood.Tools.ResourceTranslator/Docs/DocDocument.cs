using System.Text;
using System.Text.RegularExpressions;
using VpnHood.Tools.ResourceTranslator.Site;

namespace VpnHood.Tools.ResourceTranslator.Docs;

/// <summary>
/// A markdown document split into optional YAML front matter and body. Unlike a Jekyll page
/// (<see cref="PageDocument" />), front matter is not required — a plain <c>.md</c> file is a
/// valid document — and WHICH front matter values are translated is the caller's choice, not a
/// fixed title/description pair. The front matter is never sent to the model as a whole: chosen
/// values are translated, every other line is copied verbatim, and the tool itself appends
/// <c>lang</c> plus the <c>auto_translated</c> marker.
/// </summary>
public sealed partial class DocDocument
{
    /// <summary>Front matter key whose value is appended to the prompt for this file only —
    /// how an author tells the model what KIND of text this document is (legal, guide, ...).</summary>
    public const string TranslatePromptKey = "translate_prompt";

    [GeneratedRegex(@"^(?<key>[A-Za-z0-9_-]+):\s*(?<value>.*?)\s*$")]
    private static partial Regex FrontMatterLineRegex();

    private DocDocument(List<string> frontMatterLines, string body, string newline)
    {
        FrontMatterLines = frontMatterLines;
        Body = body;
        Newline = newline;
    }

    /// <summary>Raw front matter lines between the two <c>---</c> markers; empty when the file
    /// has no front matter block.</summary>
    public IReadOnlyList<string> FrontMatterLines { get; }

    /// <summary>Newline-normalized (<c>\n</c>) body.</summary>
    public string Body { get; }

    /// <summary>Newline style of the source file, so generated files match it.</summary>
    public string Newline { get; }

    /// <summary>True when the front matter carries the generated-file marker. Checked on the
    /// front matter only — a fenced code sample SHOWING the marker must not trigger it.</summary>
    public bool IsAutoTranslated => GetValue(PageDocument.AutoTranslatedKey) is "true";

    /// <summary>True when the author opted the file out via <c>translate: false</c>.</summary>
    public bool DoNotTranslate =>
        GetValue(PageDocument.TranslateKey) is { } value &&
        value.ToLowerInvariant() is "false" or "no" or "off";

    /// <summary>The per-file prompt instructions, or null when the author wrote none.</summary>
    public string? TranslatePrompt => GetValue(TranslatePromptKey);

    public static DocDocument Parse(string content)
    {
        var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);

        if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
            return new DocDocument([], normalized, newline);

        var end = normalized.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end < 0)
            throw new TranslatorException(
                "Front matter is not closed by a second '---' line.", ExitCodes.ParseError);

        var frontMatter = normalized[4..end];
        var bodyStart = normalized.IndexOf('\n', end + 1);
        var body = bodyStart < 0 ? string.Empty : normalized[(bodyStart + 1)..];

        var lines = frontMatter.Length == 0 ? [] : frontMatter.Split('\n').ToList();
        return new DocDocument(lines, body, newline);
    }

    /// <summary>The unquoted value of one front matter key, or null when absent (or a block
    /// scalar, which this line model cannot rewrite safely and therefore never translates).</summary>
    public string? GetValue(string key)
    {
        foreach (var line in FrontMatterLines) {
            var match = FrontMatterLineRegex().Match(line);
            if (!match.Success || !string.Equals(match.Groups["key"].Value, key, StringComparison.Ordinal))
                continue;

            return PageDocument.UnquoteYaml(match.Groups["value"].Value);
        }

        return null;
    }

    /// <summary>
    /// Rebuilds the document with translated front matter values and body, the target language,
    /// and the generated-file marker. Every other front matter line is carried over byte for
    /// byte; a source without front matter gains a minimal block to carry the marker.
    /// </summary>
    public string Compose(
        IReadOnlyDictionary<string, string> translatedValues,
        string translatedBody,
        string languageCode)
    {
        var sb = new StringBuilder();
        sb.Append("---").Append(Newline);

        foreach (var line in FrontMatterLines) {
            var match = FrontMatterLineRegex().Match(line);
            var key = match.Success ? match.Groups["key"].Value : null;

            switch (key) {
                case { } when translatedValues.TryGetValue(key, out var translated):
                    sb.Append(key).Append(": ").Append(PageDocument.QuoteYaml(translated)).Append(Newline);
                    break;
                case "lang":
                case PageDocument.AutoTranslatedKey:
                    break; // replaced below
                default:
                    sb.Append(line).Append(Newline);
                    break;
            }
        }

        sb.Append("lang: ").Append(languageCode).Append(Newline);
        sb.Append(PageDocument.AutoTranslatedKey).Append(": true").Append(Newline);
        sb.Append("---").Append(Newline);

        // Model output may carry either ending; normalize first so files never end up mixed.
        var body = translatedBody.Replace("\r\n", "\n", StringComparison.Ordinal);
        sb.Append(Newline == "\n" ? body : body.Replace("\n", Newline, StringComparison.Ordinal));
        return sb.ToString();
    }
}
