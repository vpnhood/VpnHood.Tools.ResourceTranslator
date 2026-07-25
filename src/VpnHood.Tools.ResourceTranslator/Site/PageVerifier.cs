using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace VpnHood.Tools.ResourceTranslator.Site;

/// <summary>
/// Fail-closed structural check of a translated page body against its source. Because these
/// translations ship without human review, anything the checks cannot prove intact is rejected:
/// same element tree, same attributes (translatable ones may change value but not disappear),
/// and a sane visible-text length. Both sides go through the same HTML5 parser, so any
/// model-introduced markup damage surfaces as a tree difference even after error recovery.
/// </summary>
public static class PageVerifier
{
    /// <summary>Attributes whose values are meant to be translated; all others must match exactly.</summary>
    private static readonly HashSet<string> TranslatableAttributes =
        new(StringComparer.OrdinalIgnoreCase) { "alt", "title", "aria-label", "placeholder" };

    /// <summary>Translated visible text must stay within these bounds relative to the source.</summary>
    private const double MinLengthRatio = 0.25;
    private const double MaxLengthRatio = 4.0;
    private const int MinLengthForRatioCheck = 40;

    /// <summary>Returns every verification error; an empty list means the page is safe to write.</summary>
    public static IReadOnlyList<string> Verify(string sourceBody, string translatedBody)
    {
        var errors = new List<string>();

        var sourceNodes = ParseFragment(sourceBody, out var sourceText);
        var translatedNodes = ParseFragment(translatedBody, out var translatedText);

        CompareChildren(sourceNodes, translatedNodes, "body", errors);
        CheckLengthRatio(sourceText, translatedText, errors);

        return errors;
    }

    private static IReadOnlyList<IElement> ParseFragment(string html, out string visibleText)
    {
        var parser = new HtmlParser();
        var document = parser.ParseDocument("<html><body></body></html>");
        var nodes = parser.ParseFragment(html, document.Body!);

        visibleText = string.Concat(nodes.Select(GetVisibleText));
        return nodes.OfType<IElement>().ToList();
    }

    /// <summary>Text as a reader would see it: script/style/svg content does not count.</summary>
    private static string GetVisibleText(INode node)
    {
        return node switch {
            IText text => text.Data,
            IHtmlScriptElement or IHtmlStyleElement => string.Empty,
            IElement { LocalName: "svg" } => string.Empty,
            _ => string.Concat(node.ChildNodes.Select(GetVisibleText))
        };
    }

    private static void CompareChildren(IReadOnlyList<IElement> source, IReadOnlyList<IElement> translated,
        string path, List<string> errors)
    {
        // Stop descending on a count mismatch: child errors would just repeat the same defect.
        if (source.Count != translated.Count) {
            errors.Add($"At {path}: expected {source.Count} child elements ({DescribeList(source)}) " +
                       $"but found {translated.Count} ({DescribeList(translated)}).");
            return;
        }

        for (var i = 0; i < source.Count; i++)
            CompareElement(source[i], translated[i], $"{path}>{Describe(source[i], i)}", errors);
    }

    private static void CompareElement(IElement source, IElement translated, string path, List<string> errors)
    {
        if (!string.Equals(source.LocalName, translated.LocalName, StringComparison.Ordinal)) {
            errors.Add($"At {path}: expected <{source.LocalName}> but found <{translated.LocalName}>.");
            return;
        }

        CompareAttributes(source, translated, path, errors);

        // Raw-text elements must come back byte-identical; they are never translated.
        if (source is IHtmlScriptElement or IHtmlStyleElement || source.LocalName == "svg") {
            if (!string.Equals(source.InnerHtml.Trim(), translated.InnerHtml.Trim(), StringComparison.Ordinal))
                errors.Add($"At {path}: <{source.LocalName}> content was modified; it must be preserved exactly.");
            return;
        }

        CompareChildren(
            source.Children.ToList(),
            translated.Children.ToList(),
            path, errors);
    }

    private static void CompareAttributes(IElement source, IElement translated, string path, List<string> errors)
    {
        foreach (var attribute in source.Attributes) {
            var translatedValue = translated.GetAttribute(attribute.Name);
            if (translatedValue == null) {
                errors.Add($"At {path}: attribute '{attribute.Name}' was removed.");
                continue;
            }

            if (TranslatableAttributes.Contains(attribute.Name))
                continue;

            if (!string.Equals(attribute.Value, translatedValue, StringComparison.Ordinal))
                errors.Add($"At {path}: attribute '{attribute.Name}' changed from '{Shorten(attribute.Value)}' " +
                           $"to '{Shorten(translatedValue)}'; only text may be translated.");
        }

        foreach (var attribute in translated.Attributes) {
            if (source.GetAttribute(attribute.Name) == null)
                errors.Add($"At {path}: attribute '{attribute.Name}' was added.");
        }
    }

    private static void CheckLengthRatio(string sourceText, string translatedText, List<string> errors)
    {
        var sourceLength = sourceText.AsSpan().Trim().Length;
        var translatedLength = translatedText.AsSpan().Trim().Length;

        if (sourceLength < MinLengthForRatioCheck)
            return;

        var ratio = (double)translatedLength / sourceLength;
        if (ratio is < MinLengthRatio or > MaxLengthRatio)
            errors.Add($"Translated visible text length looks wrong: {translatedLength} chars vs {sourceLength} " +
                       $"in the source (ratio {ratio:0.00}). The translation may be truncated or padded.");
    }

    private static string Describe(IElement element, int index)
    {
        var id = element.GetAttribute("id");
        return id != null ? $"{element.LocalName}#{id}" : $"{element.LocalName}[{index}]";
    }

    private static string DescribeList(IReadOnlyList<IElement> elements)
    {
        var names = elements.Take(8).Select(e => e.LocalName);
        return elements.Count == 0 ? "none" : string.Join(", ", names) + (elements.Count > 8 ? ", ..." : "");
    }

    private static string Shorten(string value)
    {
        return value.Length <= 60 ? value : value[..57] + "...";
    }
}
