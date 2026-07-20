namespace VpnHood.ResourceTranslator;

/// <summary>
/// Sink for progress and diagnostics. Keeping this out of the runner lets the translation
/// pipeline be driven from a test or another host without writing to the console.
/// </summary>
public interface ITranslationReporter
{
    void Info(string message);
    void Warn(string message);
    void Progress(string scope, int completed, int total);
}

/// <summary>Discards everything; the default when a host does not care about progress.</summary>
public sealed class NullTranslationReporter : ITranslationReporter
{
    public static NullTranslationReporter Instance { get; } = new();

    public void Info(string message) { }
    public void Warn(string message) { }
    public void Progress(string scope, int completed, int total) { }
}
