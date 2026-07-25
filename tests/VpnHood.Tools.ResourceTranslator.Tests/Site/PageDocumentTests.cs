using VpnHood.Tools.ResourceTranslator.Site;

namespace VpnHood.Tools.ResourceTranslator.Tests.Site;

[TestClass]
public class PageDocumentTests
{
    private const string SamplePage =
        """
        ---
        layout: none
        title: Free VPN Download – VpnHood!
        description: "Download the free VPN: fast and open source."
        nav_active: free-vpn
        ---
        {% include header.html %}
        <section id="sp-main-body"><p>Hello</p></section>
        {% include footer.html %}
        """;

    [TestMethod]
    public void Parse_splits_front_matter_and_body()
    {
        var document = PageDocument.Parse(SamplePage);

        Assert.AreEqual("Free VPN Download – VpnHood!", document.Title);
        Assert.AreEqual("Download the free VPN: fast and open source.", document.Description);
        Assert.IsTrue(document.Body.StartsWith("{% include header.html %}"));
        Assert.IsTrue(document.Body.Contains("sp-main-body"));
    }

    [TestMethod]
    public void Parse_rejects_pages_without_front_matter()
    {
        Assert.ThrowsExactly<TranslatorException>(() => PageDocument.Parse("<p>No front matter</p>"));
    }

    [TestMethod]
    public void Compose_replaces_metadata_and_appends_marker()
    {
        var document = PageDocument.Parse(SamplePage);
        var output = document.Compose("Téléchargement – VpnHood!", "Téléchargez le VPN.", document.Body, "fr");

        Assert.IsTrue(output.Contains("title: \"Téléchargement – VpnHood!\""));
        Assert.IsTrue(output.Contains("description: \"Téléchargez le VPN.\""));
        Assert.IsTrue(output.Contains("layout: none"), "Untranslated front matter lines must be carried over");
        Assert.IsTrue(output.Contains("nav_active: free-vpn"));
        Assert.IsTrue(output.Contains("lang: fr"));
        Assert.IsTrue(PageDocument.HasAutoTranslatedMarker(output));

        // Composing a translation of the composed page must not duplicate lang/marker lines.
        var recomposed = PageDocument.Parse(output).Compose("t", "d", "b", "de");
        Assert.AreEqual(1, CountOccurrences(recomposed, "lang:"));
        Assert.AreEqual(1, CountOccurrences(recomposed, PageDocument.AutoTranslatedKey + ":"));
    }

    [TestMethod]
    public void Compose_escapes_quotes_in_translated_values()
    {
        var document = PageDocument.Parse(SamplePage);
        var output = document.Compose("Say \"hi\" – VpnHood!", null, "body", "fr");

        Assert.IsTrue(output.Contains("title: \"Say \\\"hi\\\" – VpnHood!\""));
    }

    [TestMethod]
    public void HasAutoTranslatedMarker_is_false_for_hand_written_pages()
    {
        Assert.IsFalse(PageDocument.HasAutoTranslatedMarker(SamplePage));
    }

    [TestMethod]
    public void HasDoNotTranslateFlag_detects_the_opt_out_forms_only()
    {
        Assert.IsTrue(PageDocument.HasDoNotTranslateFlag("---\ntranslate: false\n---\nx"));
        Assert.IsTrue(PageDocument.HasDoNotTranslateFlag("---\ntranslate: no\r\n---\r\nx"));
        Assert.IsFalse(PageDocument.HasDoNotTranslateFlag(SamplePage));
        Assert.IsFalse(PageDocument.HasDoNotTranslateFlag("---\ntranslate: true\n---\nx"));
    }

    [TestMethod]
    public void Block_scalar_values_are_treated_as_untranslatable()
    {
        var document = PageDocument.Parse("---\ntitle: >\n  Folded title text\n---\n<p>x</p>");

        Assert.IsNull(document.Title, "A block scalar cannot be rewritten line-based; it must pass through");

        var output = document.Compose(null, null, "<p>x</p>", "fr");
        Assert.IsTrue(output.Contains("title: >"), "The block header line must be carried over verbatim");
        Assert.IsTrue(output.Contains("  Folded title text"), "The block body line must be carried over verbatim");
    }

    [TestMethod]
    public void Compose_normalizes_model_newlines_to_the_source_style()
    {
        var document = PageDocument.Parse(SamplePage);

        var output = document.Compose("t – VpnHood!", "d", "<p>line1</p>\r\n<p>line2</p>", "fr");
        Assert.IsFalse(output.Contains('\r'), "LF source must stay pure LF even when the model emits CRLF");
    }

    private static int CountOccurrences(string text, string value)
    {
        return text.Split(value).Length - 1;
    }
}
