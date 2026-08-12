namespace VpnHood.Tools.ResourceTranslator.Translation;

/// <summary>
/// How a Gemini model is told to keep its internal reasoning cheap. The knob changed between
/// generations and each model accepts exactly one of them, so this is what
/// <see cref="GeminiThinkingPlan" /> picks between.
/// </summary>
internal enum GeminiThinkingMode
{
    /// <summary>thinkingLevel=MINIMAL — Gemini 3 and later; earlier models reject the field.</summary>
    Level,

    /// <summary>thinkingBudget=0 — Gemini 2.x; version 3 removed the field and rejects it.</summary>
    Budget,

    /// <summary>Send no thinking config at all: accepted everywhere, but pays for the model's default.</summary>
    None
}
