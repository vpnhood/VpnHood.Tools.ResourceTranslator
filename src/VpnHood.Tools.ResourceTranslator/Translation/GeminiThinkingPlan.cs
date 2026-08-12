using System.Text.RegularExpressions;

namespace VpnHood.Tools.ResourceTranslator.Translation;

/// <summary>
/// Decides, in order of preference, which thinking knobs to try for a Gemini model.
///
/// Translation is a mechanical mapping, not a reasoning task, and thinking tokens bill at the
/// OUTPUT rate — the expensive side — so every request asks for the least thinking the model
/// allows. WHICH field carries that request depends on the generation: 2.x takes
/// thinkingBudget=0, 3.x removed it in favour of thinkingLevel, and each rejects the other's
/// field with HTTP 400. Alias names ("gemini-flash-lite-latest") carry no generation at all and
/// roll silently from one to the next — that rollover once broke every release of this tool —
/// so the name only chooses the FIRST attempt. The caller walks the rest of the list when the
/// API refuses, which makes the next rollover a wasted request instead of an outage.
/// </summary>
internal static partial class GeminiThinkingPlan
{
    /// <summary>The candidates to try, best first. Always ends with <see cref="GeminiThinkingMode.None" />,
    /// which every model accepts, so a future field rename degrades the bill rather than the run.</summary>
    public static IReadOnlyList<GeminiThinkingMode> ForModel(string model)
    {
        return IsPreThinkingLevelGeneration(model)
            ? [GeminiThinkingMode.Budget, GeminiThinkingMode.Level, GeminiThinkingMode.None]
            : [GeminiThinkingMode.Level, GeminiThinkingMode.Budget, GeminiThinkingMode.None];
    }

    /// <summary>
    /// True when the name states a generation older than 3. Anything else — a newer generation or
    /// an undated alias — is assumed current, because that is the direction the aliases move.
    /// </summary>
    private static bool IsPreThinkingLevelGeneration(string model)
    {
        var match = GenerationRegex().Match(model);
        return match.Success && int.Parse(match.Groups[1].Value) < 3;
    }

    [GeneratedRegex(@"gemini[-_]?(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex GenerationRegex();
}
