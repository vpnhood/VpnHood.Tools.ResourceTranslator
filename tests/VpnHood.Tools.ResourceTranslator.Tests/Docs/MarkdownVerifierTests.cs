using VpnHood.Tools.ResourceTranslator.Docs;

namespace VpnHood.Tools.ResourceTranslator.Tests.Docs;

[TestClass]
public class MarkdownVerifierTests
{
    private const string Source =
        """
        # Split tunneling

        Choose which apps use the VPN and which connect directly.

        - Works per app
        - Works per IP range

        Read the [guide](https://www.vpnhood.com/guide) for details.
        """;

    [TestMethod]
    public void A_faithful_translation_passes()
    {
        const string translated =
            """
            # Tunnel divisé

            Choisissez quelles applications utilisent le VPN et lesquelles se connectent directement.

            - Fonctionne par application
            - Fonctionne par plage IP

            Lisez le [guide](https://www.vpnhood.com/guide) pour les détails.
            """;

        Assert.AreEqual(0, MarkdownVerifier.Verify(Source, translated).Count);
    }

    [TestMethod]
    public void A_dropped_heading_fails()
    {
        var translated = Source.Replace("# Split tunneling", "Split tunneling");
        Assert.AreNotEqual(0, MarkdownVerifier.Verify(Source, translated).Count);
    }

    [TestMethod]
    public void A_changed_link_url_fails()
    {
        var translated = Source.Replace("https://www.vpnhood.com/guide", "https://evil.example.com/guide");
        Assert.AreNotEqual(0, MarkdownVerifier.Verify(Source, translated).Count);
    }

    [TestMethod]
    public void A_lost_list_item_fails()
    {
        var translated = Source.Replace("- Works per IP range\n", "");
        Assert.AreNotEqual(0, MarkdownVerifier.Verify(Source, translated).Count);
    }

    [TestMethod]
    public void A_truncated_translation_fails_the_length_ratio()
    {
        Assert.AreNotEqual(0, MarkdownVerifier.Verify(Source, "# Ok\n\nShort.\n\n- a\n- b\n\nx [g](https://www.vpnhood.com/guide).").Count);
    }

    [TestMethod]
    public void Translated_headings_do_not_trip_on_auto_identifiers()
    {
        // A translated heading changes the text an auto-identifier would be derived from; the
        // pipeline must not derive ids at all, or every correct translation would fail.
        const string source = "# Privacy matters";
        const string translated = "# La confidentialité compte";

        Assert.AreEqual(0, MarkdownVerifier.Verify(source, translated).Count);
    }
}
