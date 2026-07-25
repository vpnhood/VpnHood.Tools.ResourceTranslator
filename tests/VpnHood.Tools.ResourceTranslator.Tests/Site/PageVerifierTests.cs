using VpnHood.Tools.ResourceTranslator.Site;

namespace VpnHood.Tools.ResourceTranslator.Tests.Site;

[TestClass]
public class PageVerifierTests
{
    private const string SourceBody =
        """
        <section id="sp-main-body">
          <div class="section-start-text" data-aos="fade-up">
            <h1 class="section-title">Free VPN for everyone who needs privacy online</h1>
            <p class="section-desc">Get <a class="vh-btn vh-btn-primary" href="/free-vpn/download/">VpnHood!</a> free today.</p>
            <img src="/assets/images/hero.png" alt="VpnHood app screenshot">
          </div>
        </section>
        """;

    [TestMethod]
    public void Accepts_a_faithful_translation()
    {
        var translated = SourceBody
            .Replace("Free VPN for everyone who needs privacy online", "VPN gratuit pour tous ceux qui veulent rester privés en ligne")
            .Replace("free today", "gratuitement aujourd'hui")
            .Replace("VpnHood app screenshot", "Capture d'écran de l'application VpnHood");

        var errors = PageVerifier.Verify(SourceBody, translated);
        Assert.AreEqual(0, errors.Count, string.Join("\n", errors));
    }

    [TestMethod]
    public void Rejects_a_dropped_element()
    {
        var translated = SourceBody.Replace(
            "<img src=\"/assets/images/hero.png\" alt=\"VpnHood app screenshot\">", "");

        var errors = PageVerifier.Verify(SourceBody, translated);
        Assert.AreNotEqual(0, errors.Count);
        Assert.IsTrue(errors[0].Contains("child elements"));
    }

    [TestMethod]
    public void Rejects_a_changed_link()
    {
        var translated = SourceBody.Replace("/free-vpn/download/", "/fr/free-vpn/download/");

        var errors = PageVerifier.Verify(SourceBody, translated);
        Assert.AreNotEqual(0, errors.Count);
        Assert.IsTrue(errors[0].Contains("href"));
    }

    [TestMethod]
    public void Rejects_a_changed_class()
    {
        var translated = SourceBody.Replace("vh-btn vh-btn-primary", "vh-btn vh-btn-secondary");

        var errors = PageVerifier.Verify(SourceBody, translated);
        Assert.AreNotEqual(0, errors.Count);
        Assert.IsTrue(errors[0].Contains("class"));
    }

    [TestMethod]
    public void Rejects_a_removed_attribute()
    {
        var translated = SourceBody.Replace(" data-aos=\"fade-up\"", "");

        var errors = PageVerifier.Verify(SourceBody, translated);
        Assert.AreNotEqual(0, errors.Count);
        Assert.IsTrue(errors[0].Contains("data-aos"));
    }

    [TestMethod]
    public void Rejects_truncated_output()
    {
        const string translated =
            """
            <section id="sp-main-body">
              <div class="section-start-text" data-aos="fade-up">
                <h1 class="section-title">V</h1>
                <p class="section-desc">G <a class="vh-btn vh-btn-primary" href="/free-vpn/download/">V</a> g.</p>
                <img src="/assets/images/hero.png" alt="V">
              </div>
            </section>
            """;

        var errors = PageVerifier.Verify(SourceBody, translated);
        Assert.AreNotEqual(0, errors.Count);
        Assert.IsTrue(errors.Any(error => error.Contains("length")));
    }

    [TestMethod]
    public void Rejects_modified_script_content()
    {
        const string source = "<div><script>var a = 1;</script><p>Hello world, this is a long enough text to check.</p></div>";
        var translated = source
            .Replace("var a = 1;", "var a = 2;")
            .Replace("Hello world, this is a long enough text to check.", "Bonjour le monde, ceci est un texte assez long pour vérifier.");

        var errors = PageVerifier.Verify(source, translated);
        Assert.AreNotEqual(0, errors.Count);
        Assert.IsTrue(errors[0].Contains("script"));
    }
}
