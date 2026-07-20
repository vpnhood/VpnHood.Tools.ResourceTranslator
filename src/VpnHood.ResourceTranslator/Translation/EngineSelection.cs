namespace VpnHood.ResourceTranslator.Translation;

/// <summary>
/// The engine/model pair resolved from user input and defaults.
/// </summary>
public sealed record EngineSelection(TranslationEngine Engine, string Model);
