namespace VpnHood.ResourceTranslator.Cli;

/// <summary>
/// Raw command-line input. Every value is optional here; defaults and the config file are
/// applied by <see cref="Configuration.TranslatorOptionsResolver" />.
/// </summary>
public sealed class CommandLineOptions
{
    public string? BasePath { get; init; }
    public string? ConfigPath { get; init; }
    public string? ExtraPromptPath { get; init; }
    public string? ApiKey { get; init; }
    public string? Model { get; init; }
    public string? Engine { get; init; }
    public int? BatchSize { get; init; }

    // Actions (mutually exclusive with a normal translation run).
    public bool ShowChanges { get; init; }
    public string? RebuildLang { get; init; }
    public bool RebuildWatch { get; init; }
}
