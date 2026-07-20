using VpnHood.Tools.ResourceTranslator.Translation;

namespace VpnHood.Tools.ResourceTranslator.Tests;

[TestClass]
public sealed class EngineModelSelectorTests
{
    [TestMethod]
    [DataRow("gemini-2.5-flash", TranslationEngine.Gemini)]
    [DataRow("gpt-4o-mini", TranslationEngine.Gpt)]
    [DataRow("grok-4-latest", TranslationEngine.Grok)]
    public void Select_AutoDetectsEngineFromModel(string model, TranslationEngine expected)
    {
        var selection = EngineModelSelector.Select(null, model);

        Assert.AreEqual(expected, selection.Engine);
        Assert.AreEqual(model, selection.Model);
    }

    [TestMethod]
    public void Select_ExplicitEngineOverridesDetection()
    {
        var selection = EngineModelSelector.Select("grok", "gpt-4");

        Assert.AreEqual(TranslationEngine.Grok, selection.Engine);
        Assert.AreEqual("gpt-4", selection.Model);
    }

    [TestMethod]
    public void Select_UsesDefaultEngineAndModelWhenNothingSpecified()
    {
        var selection = EngineModelSelector.Select(null, null);

        Assert.AreEqual(TranslationEngine.Gemini, selection.Engine);
        Assert.AreEqual("gemini-flash-lite-latest", selection.Model);
    }

    [TestMethod]
    [DataRow("grok", TranslationEngine.Grok, "grok-4-latest")]
    [DataRow("gpt", TranslationEngine.Gpt, "gpt-4o-mini")]
    [DataRow("gemini", TranslationEngine.Gemini, "gemini-flash-lite-latest")]
    public void Select_UsesEngineSpecificDefaultModels(string engine, TranslationEngine expectedEngine, string expectedModel)
    {
        var selection = EngineModelSelector.Select(engine, null);

        Assert.AreEqual(expectedEngine, selection.Engine);
        Assert.AreEqual(expectedModel, selection.Model);
    }

    [TestMethod]
    [DataRow("chatgpt", TranslationEngine.Gpt)]
    [DataRow("openai", TranslationEngine.Gpt)]
    [DataRow("OpenAI", TranslationEngine.Gpt)]
    [DataRow("grok-ai", TranslationEngine.Grok)]
    [DataRow("grokai", TranslationEngine.Grok)]
    [DataRow("x-ai", TranslationEngine.Grok)]
    [DataRow("xai", TranslationEngine.Grok)]
    [DataRow("google", TranslationEngine.Gemini)]
    public void ParseEngine_NormalizesAliases(string alias, TranslationEngine expected)
    {
        Assert.AreEqual(expected, EngineModelSelector.ParseEngine(alias));
    }

    [TestMethod]
    public void ParseEngine_ThrowsOnUnknownEngine()
    {
        var ex = Assert.ThrowsExactly<ArgumentException>(() => EngineModelSelector.ParseEngine("llama"));

        StringAssert.Contains(ex.Message, "llama");
        StringAssert.Contains(ex.Message, "gemini");
    }

    [TestMethod]
    [DataRow(TranslationEngine.Gemini, "GEMINI_API_KEY")]
    [DataRow(TranslationEngine.Gpt, "OPENAI_API_KEY")]
    [DataRow(TranslationEngine.Grok, "GROK_API_KEY")]
    public void GetApiKeyVariableName_ReturnsCorrectVariables(TranslationEngine engine, string expected)
    {
        Assert.AreEqual(expected, EngineModelSelector.GetApiKeyVariableName(engine));
    }
}
