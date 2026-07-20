namespace VpnHood.ResourceTranslator.Models;

public class PromptOptions
{
    public required string Prompt { get; set; }
    public required TranslateItem[] Items { get; set; }
}