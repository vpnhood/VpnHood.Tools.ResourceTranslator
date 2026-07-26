namespace VpnHood.Tools.ResourceTranslator.Cli;

/// <summary>Writes progress to stdout and warnings to stderr, so CI logs separate cleanly.</summary>
public sealed class ConsoleTranslationReporter : ITranslationReporter
{
    // Redirected stdout (CI pipes, log files) is block-buffered by the runtime, so lines
    // can surface only when a buffer fills — on GitHub Actions the translate step then
    // looks silent for minutes while working. Flush per line to keep live logs live;
    // an interactive console flushes on its own.
    private static readonly bool FlushEachLine = Console.IsOutputRedirected;

    public void Info(string message)
    {
        Console.WriteLine(message);
        if (FlushEachLine)
            Console.Out.Flush();
    }

    public void Warn(string message)
    {
        Console.Error.WriteLine(message);
        if (Console.IsErrorRedirected)
            Console.Error.Flush();
    }

    public void Progress(string scope, int completed, int total)
    {
        if (total <= 0)
            return;

        Console.WriteLine($"  Progress: {completed}/{total} ({completed * 100 / total}%)");
        if (FlushEachLine)
            Console.Out.Flush();
    }
}
