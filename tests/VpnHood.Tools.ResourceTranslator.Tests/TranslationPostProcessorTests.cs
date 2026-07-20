using VpnHood.Tools.ResourceTranslator.Translation;

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
}
