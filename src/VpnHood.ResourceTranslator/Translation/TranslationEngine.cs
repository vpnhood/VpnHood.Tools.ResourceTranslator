namespace VpnHood.ResourceTranslator.Translation;

/// <summary>
/// The AI backends the translator can drive. Engine names accepted on the command line
/// (and their aliases) are mapped onto these values by <see cref="EngineModelSelector" />.
/// </summary>
public enum TranslationEngine
{
    Gemini,
    Gpt,
    Grok
}
