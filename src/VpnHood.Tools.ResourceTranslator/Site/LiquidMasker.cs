using System.Text.RegularExpressions;

namespace VpnHood.Tools.ResourceTranslator.Site;

/// <summary>
/// Replaces Liquid tags (<c>{% ... %}</c> and <c>{{ ... }}</c>) with opaque tokens before a
/// page is sent to the model, and restores them afterwards. The model can then never corrupt
/// template syntax it was never shown; a missing or duplicated token is a hard verification
/// failure, not something to repair.
/// </summary>
public sealed partial class LiquidMasker
{
    [GeneratedRegex(@"\{%-?[\s\S]*?%\}|\{\{[\s\S]*?\}\}")]
    private static partial Regex LiquidTagRegex();

    private readonly List<string> _tokens;

    private LiquidMasker(string masked, List<string> tokens)
    {
        Masked = masked;
        _tokens = tokens;
    }

    /// <summary>The text with every Liquid tag replaced by a placeholder token.</summary>
    public string Masked { get; }

    public int TokenCount => _tokens.Count;

    public static LiquidMasker Mask(string text)
    {
        var tokens = new List<string>();
        var masked = LiquidTagRegex().Replace(text, match => {
            tokens.Add(match.Value);
            return TokenName(tokens.Count - 1);
        });

        return new LiquidMasker(masked, tokens);
    }

    /// <summary>Placeholder for token <paramref name="index" />. The brackets are characters no
    /// model output or site content plausibly contains on its own.</summary>
    public static string TokenName(int index) => $"⟦L{index}⟧";

    /// <summary>Errors for every token that does not appear exactly once, empty when intact.</summary>
    public IReadOnlyList<string> Validate(string translated)
    {
        var errors = new List<string>();
        for (var i = 0; i < _tokens.Count; i++) {
            var count = CountOccurrences(translated, TokenName(i));
            if (count != 1)
                errors.Add($"Placeholder {TokenName(i)} (for '{Shorten(_tokens[i])}') appears {count} times; it must appear exactly once.");
        }

        return errors;
    }

    /// <summary>Restores the original Liquid tags. Call only after <see cref="Validate" /> passed.</summary>
    public string Unmask(string translated)
    {
        for (var i = 0; i < _tokens.Count; i++)
            translated = translated.Replace(TokenName(i), _tokens[i], StringComparison.Ordinal);

        return translated;
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0) {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string Shorten(string value)
    {
        return value.Length <= 40 ? value : value[..37] + "...";
    }
}
