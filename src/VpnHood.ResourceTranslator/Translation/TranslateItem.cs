namespace VpnHood.ResourceTranslator.Models;

public class TranslateItem
{
    public required string SourceLanguage { get; set; }
    public required string TargetLanguage { get; set; }
    public required string Key { get; set; }
    public required string Text { get; set; }
}