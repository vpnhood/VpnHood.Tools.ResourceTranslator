using VpnHood.Tools.ResourceTranslator.Translation;
// ReSharper disable StringLiteralTypo

namespace VpnHood.Tools.ResourceTranslator.Tests;

[TestClass]
public sealed class TranslationPostProcessorTests
{
    [TestMethod]
    public void ExtractPlaceholders_FindsAllTokens()
    {
        var placeholders = TranslationPostProcessor.ExtractPlaceholders("Hello {username}, you have {count} items.");

        CollectionAssert.AreEqual(new[] { "{username}", "{count}" }, placeholders);
    }

    [TestMethod]
    public void ExtractPlaceholders_ReturnsEmptyForTextWithoutTokens()
    {
        Assert.AreEqual(0, TranslationPostProcessor.ExtractPlaceholders("No tokens here").Count);
        Assert.AreEqual(0, TranslationPostProcessor.ExtractPlaceholders(string.Empty).Count);
    }

    [TestMethod]
    public void ExtractPlaceholders_IgnoresUnclosedBraces()
    {
        var placeholders = TranslationPostProcessor.ExtractPlaceholders("Broken {token");

        Assert.AreEqual(0, placeholders.Count);
    }

    [TestMethod]
    public void PostProcess_TrimsAndRemovesWrappingQuotes()
    {
        Assert.AreEqual("Bonjour", TranslationPostProcessor.PostProcess("Hello", " \"Bonjour\" "));
        Assert.AreEqual("Bonjour", TranslationPostProcessor.PostProcess("Hello", "'Bonjour'"));
        Assert.AreEqual("Bonjour", TranslationPostProcessor.PostProcess("Hello", "`Bonjour`"));
    }

    [TestMethod]
    public void PostProcess_AppendsMissingPlaceholders()
    {
        var result = TranslationPostProcessor.PostProcess("Hello {username}!", "Bonjour !");

        Assert.AreEqual("Bonjour ! {username}", result);
    }

    [TestMethod]
    public void PostProcess_KeepsExistingPlaceholders()
    {
        var result = TranslationPostProcessor.PostProcess("Hello {username}!", "Bonjour {username} !");

        Assert.AreEqual("Bonjour {username} !", result);
    }

    [TestMethod]
    public void PostProcess_ReturnsEmptyForNull()
    {
        Assert.AreEqual(string.Empty, TranslationPostProcessor.PostProcess("Hello", null));
    }

    private static readonly string Lrm = ((char)0x200E).ToString(); // LEFT-TO-RIGHT MARK

    [TestMethod]
    public void PostProcess_KeepsLatinExclamationAttachedInRtlOutput()
    {
        // Without the mark, the bidi algorithm renders the brand's '!' on the wrong
        // side of "VpnHood" inside Persian text.
        var result = TranslationPostProcessor.PostProcess(
            "Get VpnHood! today", "VpnHood! را دریافت کنید", "fa");

        Assert.AreEqual("VpnHood!" + Lrm + " را دریافت کنید", result);
    }

    [TestMethod]
    public void PostProcess_AppendsMarkAtEndOfTextToo()
    {
        var result = TranslationPostProcessor.PostProcess("Try VpnHood!", "VpnHood! امتحان کنید VpnHood!", "fa");

        StringAssert.EndsWith(result, "VpnHood!" + Lrm);
    }

    [TestMethod]
    public void PostProcess_LeavesLtrTargetsAlone()
    {
        Assert.AreEqual("Essayez VpnHood! maintenant",
            TranslationPostProcessor.PostProcess("Try VpnHood! now", "Essayez VpnHood! maintenant", "fr"));
    }

    [TestMethod]
    public void PostProcess_SkipsMarkWhenNextWordStayedLatin()
    {
        // "VpnHood! CLIENT" is one Latin run — the '!' sits between two Latin words and the
        // bidi algorithm never moves it, so the mark would be noise.
        Assert.AreEqual("VpnHood! CLIENT را دانلود کنید",
            TranslationPostProcessor.PostProcess("Download VpnHood! CLIENT", "VpnHood! CLIENT را دانلود کنید", "fa"));
    }

    [TestMethod]
    public void IsolateLatinPunctuation_LooksPastMarkupForTheNextWord()
    {
        // A tag is not a word: what follows it decides.
        Assert.AreEqual("VpnHood!<span>ENGINE</span> است",
            TranslationPostProcessor.IsolateLatinPunctuation("VpnHood!<span>ENGINE</span> است"));
        Assert.AreEqual("VpnHood!" + Lrm + "<b>رایگان</b>",
            TranslationPostProcessor.IsolateLatinPunctuation("VpnHood!<b>رایگان</b>"));
    }

    [TestMethod]
    public void IsolateLatinPunctuation_LooksPastNeutralsForTheNextWord()
    {
        Assert.AreEqual("VpnHood!" + Lrm + " – یک VPN رایگان",
            TranslationPostProcessor.IsolateLatinPunctuation("VpnHood! – یک VPN رایگان"));
    }

    [TestMethod]
    public void PostProcess_NeverTouchesPunctuationInsideLatinRuns()
    {
        // '!' followed by another Latin character (URLs, code) must stay untouched.
        Assert.AreEqual("wow!yes دیدنی",
            TranslationPostProcessor.PostProcess("wow!yes nice", "wow!yes دیدنی", "fa"));
    }

    [TestMethod]
    public void IsolateLatinPunctuation_IsIdempotent()
    {
        var once = TranslationPostProcessor.IsolateLatinPunctuation("VpnHood! متن");
        Assert.AreEqual(once, TranslationPostProcessor.IsolateLatinPunctuation(once));
    }

    [TestMethod]
    public void IsRtlLanguage_MatchesBaseCodesAndRegionVariants()
    {
        Assert.IsTrue(TranslationPostProcessor.IsRtlLanguage("fa"));
        Assert.IsTrue(TranslationPostProcessor.IsRtlLanguage("fa-IR"));
        Assert.IsTrue(TranslationPostProcessor.IsRtlLanguage("AR"));
        Assert.IsFalse(TranslationPostProcessor.IsRtlLanguage("de"));
        Assert.IsFalse(TranslationPostProcessor.IsRtlLanguage(null));
    }
}

[TestClass]
public sealed class PromptBuilderTests
{
    [TestMethod]
    public void BuildPrompt_ContainsPromptAndItems()
    {
        var options = new PromptOptions {
            Prompt = "Translate the following items.",
            Items = [
                new TranslateItem {
                    SourceLanguage = "en",
                    TargetLanguage = "fr",
                    Key = "GREETING",
                    Text = "Hello"
                }
            ]
        };

        var prompt = PromptBuilder.BuildPrompt(options);

        StringAssert.Contains(prompt, "Translate the following items.");
        StringAssert.Contains(prompt, "GREETING");
        StringAssert.Contains(prompt, "Hello");
    }

    [TestMethod]
    public void BuildOptions_AppendsExtraPromptUnderGuidelinesHeading()
    {
        var options = PromptBuilder.BuildOptions([], "Base prompt.", "Keep VpnHood untranslated.");

        StringAssert.Contains(options.Prompt, "Base prompt.");
        StringAssert.Contains(options.Prompt, "Additional guidelines:");
        StringAssert.Contains(options.Prompt, "Keep VpnHood untranslated.");
    }

    [TestMethod]
    public void BuildOptions_OmitsGuidelinesWhenNoExtraPrompt()
    {
        var options = PromptBuilder.BuildOptions([], "Base prompt.", extraPrompt: null);

        Assert.IsFalse(options.Prompt.Contains("Additional guidelines:", StringComparison.Ordinal));
    }

    [TestMethod]
    public void BuildPrompt_SendsRealCharactersNotJsonEscapes()
    {
        // Escaped HTML in the prompt made Gemini reproduce (and corrupt) the escape
        // sequences around Persian output; the model must see the actual characters.
        var options = PromptBuilder.BuildOptions([
            new TranslateItem {
                SourceLanguage = "en",
                TargetLanguage = "fa",
                Key = "start_contact",
                Text = "<strong>Contact Us:</strong> reach us at <a href=\"mailto:x@y.z\">x@y.z</a> — толк"
            }
        ], basePrompt: "Translate.", extraPrompt: null);

        var prompt = PromptBuilder.BuildPrompt(options);

        StringAssert.Contains(prompt, "<strong>Contact Us:</strong>");
        StringAssert.Contains(prompt, "— толк");
        Assert.IsFalse(prompt.Contains("\\u003"), "HTML must not be JSON-escaped in the prompt");
        Assert.IsFalse(prompt.Contains("\\u2014"), "Non-ASCII must not be JSON-escaped in the prompt");
    }
}
