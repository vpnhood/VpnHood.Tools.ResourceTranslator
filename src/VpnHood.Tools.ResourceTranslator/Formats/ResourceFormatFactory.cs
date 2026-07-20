namespace VpnHood.Tools.ResourceTranslator.Formats;

/// <summary>
/// Chooses the resource format from the file extension. New formats are added here and by
/// implementing <see cref="IResourceFormat" />; nothing else in the pipeline needs to change.
/// </summary>
public static class ResourceFormatFactory
{
    /// <summary>Extensions understood by the tool, for help text and error messages.</summary>
    public static IReadOnlyCollection<string> SupportedExtensions { get; } = [".json", ".resx"];

    public static IResourceFormat? TryCreate(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch {
            ".json" => new JsonResourceFormat(),
            ".resx" => new ResxResourceFormat(),
            _ => null
        };
    }

    public static IResourceFormat Create(string path)
    {
        return TryCreate(path) ?? throw new TranslatorException(
            $"Unsupported file type '{Path.GetExtension(path)}'. " +
            $"Supported formats: {string.Join(", ", SupportedExtensions)}",
            ExitCodes.FileNotFound);
    }
}
