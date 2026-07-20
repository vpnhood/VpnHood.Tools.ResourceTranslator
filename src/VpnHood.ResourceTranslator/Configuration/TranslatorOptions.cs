namespace VpnHood.ResourceTranslator;

internal class TranslatorOptions
{
    public string? BasePath { get; init; }
    public string? ExtraPromptPath { get; init; }
    public string? ApiKey { get; init; }
    public string? Model { get; init; }
    public string? Engine { get; init; }
    public bool ShowChanges { get; init; }
    public string? RebuildLang { get; init; }
    public bool RebuildWatch { get; init; }
    public int BatchSize { get; init; }
}
