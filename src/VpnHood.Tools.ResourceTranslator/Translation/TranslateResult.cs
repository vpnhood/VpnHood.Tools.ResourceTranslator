namespace VpnHood.Tools.ResourceTranslator.Translation;

public class TranslateResult
{
    /// <summary>Identifies the item this translates; the only field used to match a result back.</summary>
    public required string Key { get; set; }

    public required string TranslatedText { get; set; }

    // Optional, and never read. Earlier prompt versions asked the model to echo these back,
    // which cost roughly 40% of every response in output tokens — the expensive side of the
    // bill — for values the caller already knows from the request. They stay on the model so a
    // response that still includes them (an older prompt, a chattier model) parses cleanly.
    public string? SourceText { get; set; }
    public string? SourceLanguage { get; set; }
    public string? TargetLanguage { get; set; }
}