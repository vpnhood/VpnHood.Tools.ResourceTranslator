using VpnHood.ResourceTranslator.Models;

namespace VpnHood.ResourceTranslator.Test;

[TestClass]
public sealed class EngineModelSelectorTests
{
    [TestMethod]
    public void SelectEngineAndModel_AutoDetectsGemini()
    {
        var (engine, model) = EngineModelSelector.SelectEngineAndModel(null, "gemini-2.5-flash");

        Assert.AreEqual("gemini", engine);
        Assert.AreEqual("gemini-2.5-flash", model);
    }

    [TestMethod]
    public void SelectEngineAndModel_AutoDetectsGpt()
    {
        var (engine, model) = EngineModelSelector.SelectEngineAndModel(null, "gpt-4o-mini");

        Assert.AreEqual("gpt", engine);
        Assert.AreEqual("gpt-4o-mini", model);
    }

    [TestMethod]
    public void SelectEngineAndModel_AutoDetectsGrok()
    {
        var (engine, model) = EngineModelSelector.SelectEngineAndModel(null, "grok-4-latest");

        Assert.AreEqual("grok", engine);
        Assert.AreEqual("grok-4-latest", model);
    }

    [TestMethod]
    public void SelectEngineAndModel_ExplicitEngineOverridesDetection()
    {
        var (engine, model) = EngineModelSelector.SelectEngineAndModel("grok", "gpt-4");

        Assert.AreEqual("grok", engine);
        Assert.AreEqual("gpt-4", model);
    }

    [TestMethod]
    public void SelectEngineAndModel_UsesDefaultEngineAndModelWhenNothingSpecified()
    {
        var (engine, model) = EngineModelSelector.SelectEngineAndModel(null, null);

        Assert.AreEqual("gemini", engine);
        Assert.AreEqual("gemini-flash-lite-latest", model);
    }

    [TestMethod]
    public void SelectEngineAndModel_UsesEngineSpecificDefaultModels()
    {
        // Test Grok engine default
        var (grokEngine, grokModel) = EngineModelSelector.SelectEngineAndModel("grok", null);
        Assert.AreEqual("grok", grokEngine);
        Assert.AreEqual("grok-4-latest", grokModel);

        // Test GPT engine default
        var (gptEngine, gptModel) = EngineModelSelector.SelectEngineAndModel("gpt", null);
        Assert.AreEqual("gpt", gptEngine);
        Assert.AreEqual("gpt-4o-mini", gptModel);

        // Test Gemini engine default
        var (geminiEngine, geminiModel) = EngineModelSelector.SelectEngineAndModel("gemini", null);
        Assert.AreEqual("gemini", geminiEngine);
        Assert.AreEqual("gemini-flash-lite-latest", geminiModel);
    }

    [TestMethod]
    public void GetEnvironmentVariableName_ReturnsCorrectVariables()
    {
        Assert.AreEqual("GEMINI_API_KEY", EngineModelSelector.GetEnvironmentVariableName("gemini"));
        Assert.AreEqual("OPENAI_API_KEY", EngineModelSelector.GetEnvironmentVariableName("gpt"));
        Assert.AreEqual("GROK_API_KEY", EngineModelSelector.GetEnvironmentVariableName("grok"));
    }

    [TestMethod]
    public void SelectEngineAndModel_NormalizesEngineNames()
    {
        Assert.AreEqual("gpt", EngineModelSelector.SelectEngineAndModel("chatgpt", "test-model").engine);
        Assert.AreEqual("gpt", EngineModelSelector.SelectEngineAndModel("openai", "test-model").engine);
        Assert.AreEqual("grok", EngineModelSelector.SelectEngineAndModel("grok-ai", "test-model").engine);
        Assert.AreEqual("grok", EngineModelSelector.SelectEngineAndModel("grokai", "test-model").engine);
        Assert.AreEqual("grok", EngineModelSelector.SelectEngineAndModel("x-ai", "test-model").engine);
        Assert.AreEqual("grok", EngineModelSelector.SelectEngineAndModel("xai", "test-model").engine);
    }
}
