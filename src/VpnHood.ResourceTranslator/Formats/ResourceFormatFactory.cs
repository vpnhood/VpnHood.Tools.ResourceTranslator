namespace VpnHood.ResourceTranslator.Formats;

public static class ResourceFormatFactory
{
    public static IResourceFormat? TryCreate(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch {
            ".json" => new JsonResourceFormat(),
            ".resx" => new ResxResourceFormat(),
            _ => null
        };
    }
}
