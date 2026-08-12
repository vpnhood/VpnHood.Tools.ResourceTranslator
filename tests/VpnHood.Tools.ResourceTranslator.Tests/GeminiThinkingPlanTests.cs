using VpnHood.Tools.ResourceTranslator.Translation;

namespace VpnHood.Tools.ResourceTranslator.Tests;

[TestClass]
public sealed class GeminiThinkingPlanTests
{
    [TestMethod]
    [DataRow("gemini-2.5-flash-lite")]
    [DataRow("gemini-2.0-flash")]
    [DataRow("gemini-1.5-pro")]
    public void ForModel_PrefersTheBudgetFieldOnModelsOlderThanThree(string model)
    {
        // thinkingLevel is rejected outright by these: "Thinking level is not supported for this model"
        Assert.AreEqual(GeminiThinkingMode.Budget, GeminiThinkingPlan.ForModel(model)[0]);
    }

    [TestMethod]
    [DataRow("gemini-3-flash-preview")]
    [DataRow("gemini-3.5-flash-lite")]
    public void ForModel_PrefersTheLevelFieldFromGenerationThreeOn(string model)
    {
        Assert.AreEqual(GeminiThinkingMode.Level, GeminiThinkingPlan.ForModel(model)[0]);
    }

    [TestMethod]
    [DataRow("gemini-flash-lite-latest")]
    [DataRow("gemini-flash-latest")]
    public void ForModel_TreatsAnUndatedAliasAsCurrent(string model)
    {
        // The aliases only ever roll forward, and the rollover onto Gemini 3 is what broke this
        // tool: thinkingBudget started answering 400 with no hint of which field was at fault.
        Assert.AreEqual(GeminiThinkingMode.Level, GeminiThinkingPlan.ForModel(model)[0]);
    }

    [TestMethod]
    [DataRow("gemini-2.5-flash-lite")]
    [DataRow("gemini-flash-lite-latest")]
    public void ForModel_OffersEveryModeAndEndsOnTheOneNoModelRefuses(string model)
    {
        var plan = GeminiThinkingPlan.ForModel(model);

        CollectionAssert.AreEquivalent(Enum.GetValues<GeminiThinkingMode>(), plan.ToArray(),
            "a rejected field must always leave another candidate to try");
        Assert.AreEqual(GeminiThinkingMode.None, plan[^1],
            "sending no thinking config is the last resort: it costs more but every model accepts it");
    }
}
