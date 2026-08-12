using Markdig;
using VpnHood.Tools.ResourceTranslator.Site;

namespace VpnHood.Tools.ResourceTranslator.Docs;

/// <summary>
/// Fail-closed structural check of a translated markdown body against its source. Markdown has
/// no element tree of its own to compare, so both sides are rendered to HTML with the SAME
/// pinned pipeline and handed to <see cref="PageVerifier" /> — a model that drops a heading,
/// breaks a list, or mangles a link changes the rendered tree and is rejected. Only the two
/// renders are ever compared with each other, so this renderer never needs to agree with
/// whatever the consuming app renders markdown with.
/// </summary>
public static class MarkdownVerifier
{
    /// <summary>
    /// GitHub-flavored constructs the documents actually use. AutoIdentifiers is deliberately
    /// absent: it derives heading ids from the heading TEXT, so a correctly translated heading
    /// would fail the attribute comparison.
    /// </summary>
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseEmphasisExtras()
        .UseAutoLinks()
        .Build();

    /// <summary>Returns every verification error; an empty list means the body is safe to write.</summary>
    public static IReadOnlyList<string> Verify(string sourceBody, string translatedBody)
    {
        return PageVerifier.Verify(ToHtml(sourceBody), ToHtml(translatedBody));
    }

    internal static string ToHtml(string markdownBody)
    {
        return Markdown.ToHtml(markdownBody, Pipeline);
    }
}
