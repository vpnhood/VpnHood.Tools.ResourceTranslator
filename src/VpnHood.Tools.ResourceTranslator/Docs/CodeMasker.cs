using System.Text;
using System.Text.RegularExpressions;

namespace VpnHood.Tools.ResourceTranslator.Docs;

/// <summary>
/// Replaces the parts of a markdown body a model must never touch — fenced code blocks, inline
/// code spans, and configured placeholder patterns — with opaque tokens before translation, and
/// restores them afterward. The model can then never corrupt what it was never shown; a missing
/// or duplicated token is a hard verification failure, not something to repair. Fences are
/// masked whole (their inner blank lines included), which is also what makes blank-line
/// chunking of the masked text safe.
/// </summary>
public sealed partial class CodeMasker
{
    [GeneratedRegex(@"^ {0,3}(?<fence>`{3,}|~{3,})")]
    private static partial Regex FenceOpenRegex();

    [GeneratedRegex(@"(`{1,3})([^`\n]+?)\1")]
    private static partial Regex InlineCodeRegex();

    private readonly List<string> _tokens;

    private CodeMasker(string masked, List<string> tokens)
    {
        Masked = masked;
        _tokens = tokens;
    }

    /// <summary>The text with every protected region replaced by a placeholder token.</summary>
    public string Masked { get; }

    public int TokenCount => _tokens.Count;

    /// <summary>Placeholder for token <paramref name="index" />. The brackets are characters no
    /// model output or document content plausibly contains on its own.</summary>
    public static string TokenName(int index) => $"⟦C{index}⟧";

    public static CodeMasker Mask(string text, IReadOnlyList<Regex> extraPatterns)
    {
        var tokens = new List<string>();
        var masked = MaskFencedBlocks(text, tokens);
        masked = InlineCodeRegex().Replace(masked, match => Reserve(tokens, match.Value));
        foreach (var pattern in extraPatterns)
            masked = pattern.Replace(masked, match => Reserve(tokens, match.Value));

        return new CodeMasker(masked, tokens);
    }

    /// <summary>Errors for every token that does not appear exactly once, empty when intact.</summary>
    public IReadOnlyList<string> Validate(string translated)
    {
        var errors = new List<string>();
        for (var i = 0; i < _tokens.Count; i++) {
            var count = CountOccurrences(translated, TokenName(i));
            if (count != 1)
                errors.Add($"Placeholder {TokenName(i)} (for '{Shorten(_tokens[i])}') appears {count} times; " +
                           "it must appear exactly once.");
        }

        return errors;
    }

    /// <summary>Restores the original text of every token. Call only after <see cref="Validate" /> passed.</summary>
    public string Unmask(string translated)
    {
        for (var i = 0; i < _tokens.Count; i++)
            translated = translated.Replace(TokenName(i), _tokens[i], StringComparison.Ordinal);

        return translated;
    }

    private static string Reserve(List<string> tokens, string original)
    {
        tokens.Add(original);
        return TokenName(tokens.Count - 1);
    }

    /// <summary>
    /// A line scanner rather than a regex: a fence closes only on a run of the SAME character
    /// at least as long as the opener (CommonMark), which a single pattern cannot express.
    /// An unclosed fence runs to the end of the text, exactly as renderers treat it.
    /// </summary>
    private static string MaskFencedBlocks(string text, List<string> tokens)
    {
        var lines = text.Split('\n');
        var sb = new StringBuilder(text.Length);

        var index = 0;
        while (index < lines.Length) {
            var open = FenceOpenRegex().Match(lines[index]);
            if (!open.Success) {
                sb.Append(lines[index]);
                if (index < lines.Length - 1)
                    sb.Append('\n');
                index++;
                continue;
            }

            var fence = open.Groups["fence"].Value;
            var close = index + 1;
            while (close < lines.Length && !IsClosingFence(lines[close], fence))
                close++;

            var endExclusive = Math.Min(close + 1, lines.Length);
            var block = string.Join('\n', lines[index..endExclusive]);
            sb.Append(Reserve(tokens, block));
            if (endExclusive < lines.Length)
                sb.Append('\n');
            index = endExclusive;
        }

        return sb.ToString();
    }

    private static bool IsClosingFence(string line, string openingFence)
    {
        var match = FenceOpenRegex().Match(line);
        return match.Success &&
               match.Groups["fence"].Value[0] == openingFence[0] &&
               match.Groups["fence"].Value.Length >= openingFence.Length &&
               line.TrimEnd().TrimStart().All(c => c == openingFence[0]);
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
