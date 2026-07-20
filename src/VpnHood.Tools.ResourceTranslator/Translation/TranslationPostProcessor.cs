namespace VpnHood.Tools.ResourceTranslator.Translation;

/// <summary>
/// Cleans up raw model output before it is written to a resource file. Models tend to wrap
/// results in quotes and occasionally drop placeholders; both would break the consuming app.
/// </summary>
public static class TranslationPostProcessor
{
    public static string PostProcess(string source, string? translated)
    {
        if (translated == null)
            return string.Empty;

        translated = translated.Trim();
        translated = StripWrappingQuotes(translated);
        return RestoreMissingPlaceholders(source, translated);
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
}
