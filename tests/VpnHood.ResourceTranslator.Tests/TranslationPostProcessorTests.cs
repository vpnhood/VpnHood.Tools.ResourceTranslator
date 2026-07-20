using VpnHood.ResourceTranslator.Models;

namespace VpnHood.ResourceTranslator.Test;

[TestClass]
public sealed class TranslateUtilsTests
{
    [TestMethod]
    public void ExtractPlaceholders_FindsAllTokens()
    {
        var placeholders = TranslateUtils.ExtractPlaceholders("Hello {username}, you have {count} items.");

        CollectionAssert.AreEqual(new[] { "{username}", "{count}" }, placeholders);
    }

    [TestMethod]
    public void ExtractPlaceholders_ReturnsEmptyForTextWithoutTokens()
    {
        Assert.AreEqual(0, TranslateUtils.ExtractPlaceholders("No tokens here").Count);
        Assert.AreEqual(0, TranslateUtils.ExtractPlaceholders(string.Empty).Count);
    }

    [TestMethod]
    public void ExtractPlaceholders_IgnoresUnclosedBraces()
    {
        var placeholders = TranslateUtils.ExtractPlaceholders("Broken {token");

        Assert.AreEqual(0, placeholders.Count);
    }

    [TestMethod]
    public void PostProcessTranslation_TrimsAndRemovesWrappingQuotes()
    {
        Assert.AreEqual("Bonjour", TranslateUtils.PostProcessTranslation("Hello", " \"Bonjour\" "));
        Assert.AreEqual("Bonjour", TranslateUtils.PostProcessTranslation("Hello", "'Bonjour'"));
        Assert.AreEqual("Bonjour", TranslateUtils.PostProcessTranslation("Hello", "`Bonjour`"));
    }

    [TestMethod]
    public void PostProcessTranslation_AppendsMissingPlaceholders()
    {
        var result = TranslateUtils.PostProcessTranslation("Hello {username}!", "Bonjour !");

        Assert.AreEqual("Bonjour ! {username}", result);
    }

    [TestMethod]
    public void PostProcessTranslation_KeepsExistingPlaceholders()
    {
        var result = TranslateUtils.PostProcessTranslation("Hello {username}!", "Bonjour {username} !");

        Assert.AreEqual("Bonjour {username} !", result);
    }

    [TestMethod]
    public void PostProcessTranslation_ReturnsEmptyForNull()
    {
        Assert.AreEqual(string.Empty, TranslateUtils.PostProcessTranslation("Hello", null));
    }

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

        var prompt = TranslateUtils.BuildPrompt(options);

        StringAssert.Contains(prompt, "Translate the following items.");
        StringAssert.Contains(prompt, "GREETING");
        StringAssert.Contains(prompt, "Hello");
    }
}
