using VpnHood.Tools.ResourceTranslator.Site;

namespace VpnHood.Tools.ResourceTranslator.Tests.Site;

[TestClass]
public class LiquidMaskerTests
{
    [TestMethod]
    public void Mask_replaces_liquid_tags_and_round_trips()
    {
        const string source = "{% raw %}\n<p>Hello {{ site.title }} world</p>\n{% endraw %}\n{% include faq.html items=site.data.faqs %}";

        var masker = LiquidMasker.Mask(source);

        Assert.AreEqual(4, masker.TokenCount);
        Assert.IsFalse(masker.Masked.Contains("{%"), "Liquid block tags must be masked");
        Assert.IsFalse(masker.Masked.Contains("{{"), "Liquid output tags must be masked");
        Assert.AreEqual(source, masker.Unmask(masker.Masked), "Unmasking the masked text must restore the original");
    }

    [TestMethod]
    public void Mask_leaves_plain_braces_alone()
    {
        const string source = "<script>var x = { a: 1 };</script>";
        var masker = LiquidMasker.Mask(source);

        Assert.AreEqual(0, masker.TokenCount);
        Assert.AreEqual(source, masker.Masked);
    }

    [TestMethod]
    public void Validate_flags_missing_and_duplicated_tokens()
    {
        var masker = LiquidMasker.Mask("{% include a.html %} and {% include b.html %}");

        Assert.AreEqual(0, masker.Validate(LiquidMasker.TokenName(0) + " x " + LiquidMasker.TokenName(1)).Count);

        var missing = masker.Validate(LiquidMasker.TokenName(0) + " only");
        Assert.AreEqual(1, missing.Count);
        Assert.IsTrue(missing[0].Contains(LiquidMasker.TokenName(1)));

        var duplicated = masker.Validate(LiquidMasker.TokenName(0) + LiquidMasker.TokenName(0) + LiquidMasker.TokenName(1));
        Assert.AreEqual(1, duplicated.Count);
        Assert.IsTrue(duplicated[0].Contains("2 times"));
    }
}
