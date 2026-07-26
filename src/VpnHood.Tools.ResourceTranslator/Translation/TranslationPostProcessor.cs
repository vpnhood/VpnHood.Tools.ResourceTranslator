using System.Text.RegularExpressions;

namespace VpnHood.Tools.ResourceTranslator.Translation;

/// <summary>
/// Cleans up raw model output before it is written to a resource file. Models tend to wrap
/// results in quotes and occasionally drop placeholders; both would break the consuming app.
/// </summary>
public static partial class TranslationPostProcessor
{
    private static readonly HashSet<string> RtlLanguages =
        new(StringComparer.OrdinalIgnoreCase) { "fa", "ar", "he", "ur" };

    public static string PostProcess(string source, string? translated, string? targetLanguage = null)
    {
        if (translated == null)
            return string.Empty;

        translated = translated.Trim();
        translated = StripWrappingQuotes(translated);
        translated = RestoreMissingPlaceholders(source, translated);

        if (IsRtlLanguage(targetLanguage))
            translated = IsolateLatinPunctuation(translated);

        return translated;
    }

    public static bool IsRtlLanguage(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return false;

        var dash = languageCode.IndexOf('-');
        return RtlLanguages.Contains(dash > 0 ? languageCode[..dash] : languageCode);
    }

    /// <summary>
    /// In RTL text the bidi algorithm pulls a '!' or '?' that trails a Latin word (e.g. the
    /// brand "VpnHood!") out of the Latin run and renders it on the wrong side of the word.
    /// An invisible LEFT-TO-RIGHT MARK (U+200E) after the punctuation keeps it attached.
    /// Only added when the punctuation actually sits on a bidi boundary: the next word is
    /// RTL (ignoring neutral punctuation and HTML tags in between) or the text ends. When the
    /// next word stayed Latin — "VpnHood! CLIENT", "VpnHood!&lt;b&gt;CONNECT&lt;/b&gt;" — the
    /// punctuation is inside a Latin run and needs no mark. Idempotent.
    /// </summary>
    public static string IsolateLatinPunctuation(string text)
    {
        return LatinTrailingPunctuationRegex().Replace(text, "$1\u200E");
    }

    /// <summary>Extracts <c>{placeholder}</c> tokens, which must survive translation verbatim.</summary>
    public static List<string> ExtractPlaceholders(string s)
    {
        var list = new List<string>();
        if (string.IsNullOrEmpty(s))
            return list;

        for (var i = 0; i < s.Length; i++) {
            if (s[i] != '{')
                continue;

            var j = s.IndexOf('}', i + 1);
            if (j > i) {
                list.Add(s.Substring(i, j - i + 1));
                i = j;
            }
        }

        return list;
    }

    private static string StripWrappingQuotes(string translated)
    {
        if (translated.Length < 2)
            return translated;

        var isWrapped =
            (translated.StartsWith('"') && translated.EndsWith('"')) ||
            (translated.StartsWith('\'') && translated.EndsWith('\'')) ||
            (translated.StartsWith('`') && translated.EndsWith('`'));

        return isWrapped ? translated[1..^1] : translated;
    }

    private static string RestoreMissingPlaceholders(string source, string translated)
    {
        // Append any placeholder the model dropped, so runtime formatting cannot fail.
        foreach (var token in ExtractPlaceholders(source)) {
            if (!translated.Contains(token, StringComparison.Ordinal))
                translated = translated + (translated.EndsWith(' ') ? string.Empty : " ") + token;
        }

        return translated;
    }

    // Latin word + '!'/'?', not already marked, where scanning forward over HTML tags and
    // neutral characters (never over a Latin letter or digit) reaches an RTL character or the
    // end of the text.
    // ReSharper disable StringLiteralTypo
    [GeneratedRegex(
        @"([A-Za-z0-9][!?]+)(?!\u200E)(?=(?:<[^>]*>|[^A-Za-z0-9<])*(?:$|[\u0590-\u08FF\uFB1D-\uFDFF\uFE70-\uFEFF]))")]
    // ReSharper restore StringLiteralTypo
    private static partial Regex LatinTrailingPunctuationRegex();
}
