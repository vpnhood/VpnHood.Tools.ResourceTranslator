using System.Text.Json.Serialization;

namespace VpnHood.ResourceTranslator.Watch;

internal class WatchFile
{
    public const int CurrentVersion = 1;

    [JsonPropertyName("version")]
    public int Version { get; init; } = CurrentVersion;

    [JsonPropertyName("items")]
    public Dictionary<string, string> Items { get; init; } = new(StringComparer.Ordinal);
}
