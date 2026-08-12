using System.Text.RegularExpressions;

namespace VpnHood.Tools.ResourceTranslator.Docs;

/// <summary>
/// Splits a MASKED markdown body into pieces small enough to translate in one request each.
/// Splitting only ever happens at blank-line boundaries, which is safe because fenced code —
/// the one construct whose inner blank lines matter — has already been collapsed into single
/// opaque tokens by <see cref="CodeMasker" />. Pieces are rejoined with a plain blank line,
/// which renders identically to any longer blank run (CommonMark collapses them).
/// </summary>
internal static partial class MarkdownChunker
{
    [GeneratedRegex(@"\n{2,}")]
    private static partial Regex BlankRunRegex();

    /// <summary>
    /// Greedy paragraph packing: blocks accumulate until the next would push a chunk past
    /// <paramref name="chunkChars" />. A single oversized block stays whole — there is no safe
    /// boundary inside it, and the verifier will catch a truncated translation of it anyway.
    /// </summary>
    public static IReadOnlyList<string> Split(string maskedBody, int chunkChars)
    {
        if (maskedBody.Length <= chunkChars)
            return [maskedBody];

        var blocks = BlankRunRegex().Split(maskedBody);
        var chunks = new List<string>();
        var current = string.Empty;

        foreach (var block in blocks) {
            if (block.Length == 0)
                continue;

            if (current.Length == 0) {
                current = block;
                continue;
            }

            if (current.Length + 2 + block.Length <= chunkChars) {
                current += "\n\n" + block;
                continue;
            }

            chunks.Add(current);
            current = block;
        }

        if (current.Length > 0)
            chunks.Add(current);

        return chunks.Count > 0 ? chunks : [maskedBody];
    }

    /// <summary>Reassembles translated chunks in order.</summary>
    public static string Join(IReadOnlyList<string> chunks)
    {
        return string.Join("\n\n", chunks.Select(chunk => chunk.Trim('\n')));
    }
}
