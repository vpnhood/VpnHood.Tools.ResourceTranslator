namespace VpnHood.ResourceTranslator.Cli;

/// <summary>Writes progress to stdout and warnings to stderr, so CI logs separate cleanly.</summary>
public sealed class ConsoleTranslationReporter : ITranslationReporter
{
    public void Info(string message)
    {
        Console.WriteLine(message);
    }

    public void Warn(string message)
    {
        Console.Error.WriteLine(message);
    }

    public void Progress(string scope, int completed, int total)
    {
        if (total <= 0)
            return;

        Console.WriteLine($"  Progress: {completed}/{total} ({completed * 100 / total}%)");
    }
}
