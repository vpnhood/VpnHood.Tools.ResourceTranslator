using VpnHood.Tools.ResourceTranslator.Docs;

namespace VpnHood.Tools.ResourceTranslator.Tests.Docs;

[TestClass]
public class DocDocumentTests
{
    [TestMethod]
    public void Parse_reads_front_matter_and_body()
    {
        var document = DocDocument.Parse(
            """
            ---
            title: "What is split tunneling?"
            date: 2026-08-10
            translate_prompt: "User guide. Keep UI labels intact."
            ---

            First paragraph.
            """);

        Assert.AreEqual("What is split tunneling?", document.GetValue("title"));
        Assert.AreEqual("2026-08-10", document.GetValue("date"));
        Assert.AreEqual("User guide. Keep UI labels intact.", document.TranslatePrompt);
        Assert.IsTrue(document.Body.Contains("First paragraph."));
        Assert.IsFalse(document.IsAutoTranslated);
        Assert.IsFalse(document.DoNotTranslate);
    }

    [TestMethod]
    public void Parse_accepts_a_file_without_front_matter()
    {
        var document = DocDocument.Parse("Just a paragraph.\n\nAnother one.\n");

        Assert.AreEqual(0, document.FrontMatterLines.Count);
        Assert.IsTrue(document.Body.StartsWith("Just a paragraph."));
        Assert.IsNull(document.GetValue("title"));
    }

    [TestMethod]
    public void Parse_throws_on_unclosed_front_matter()
    {
        Assert.ThrowsExactly<TranslatorException>(() => DocDocument.Parse("---\ntitle: x\nno closing marker"));
    }

    [TestMethod]
    public void Marker_checks_are_scoped_to_the_front_matter()
    {
        // A document QUOTING the markers in its body — say, a post about this very tool —
        // must not be mistaken for a generated or opted-out file.
        var document = DocDocument.Parse(
            """
            Some prose.

            auto_translated: true
            translate: false
            """);

        Assert.IsFalse(document.IsAutoTranslated);
        Assert.IsFalse(document.DoNotTranslate);

        var generated = DocDocument.Parse("---\nauto_translated: true\n---\nBody.");
        Assert.IsTrue(generated.IsAutoTranslated);

        var optedOut = DocDocument.Parse("---\ntranslate: false\n---\nBody.");
        Assert.IsTrue(optedOut.DoNotTranslate);
    }

    [TestMethod]
    public void Compose_replaces_chosen_values_and_appends_the_marker()
    {
        var document = DocDocument.Parse(
            """
            ---
            title: Original title
            date: 2026-08-10
            tags: [privacy, android]
            ---
            Body text.
            """);

        var composed = document.Compose(
            new Dictionary<string, string> { ["title"] = "Titre traduit" }, "Texte du corps.", "fr");

        Assert.IsTrue(composed.Contains("title: \"Titre traduit\""));
        Assert.IsTrue(composed.Contains("date: 2026-08-10"), "untranslated keys are copied verbatim");
        Assert.IsTrue(composed.Contains("tags: [privacy, android]"));
        Assert.IsTrue(composed.Contains("lang: fr"));
        Assert.IsTrue(composed.Contains("auto_translated: true"));
        Assert.IsTrue(composed.TrimEnd().EndsWith("Texte du corps."));
    }

    [TestMethod]
    public void Compose_gives_a_front_matter_block_to_a_plain_file()
    {
        var document = DocDocument.Parse("Only a body.\n");

        var composed = document.Compose(new Dictionary<string, string>(), "Seulement un corps.\n", "fr");

        Assert.IsTrue(composed.StartsWith("---\nlang: fr\nauto_translated: true\n---\n"));
        Assert.IsTrue(composed.EndsWith("Seulement un corps.\n"));
    }

    [TestMethod]
    public void Compose_preserves_crlf_newlines()
    {
        var document = DocDocument.Parse("---\r\ntitle: Hi\r\n---\r\nBody line.\r\n");

        var composed = document.Compose(
            new Dictionary<string, string> { ["title"] = "Salut" }, "Ligne du corps.\n", "fr");

        Assert.IsTrue(composed.Contains("\r\n"));
        Assert.IsFalse(composed.Replace("\r\n", "").Contains('\r'), "no stray lone CR");
        Assert.IsTrue(composed.Contains("title: \"Salut\"\r\n"));
    }
}
