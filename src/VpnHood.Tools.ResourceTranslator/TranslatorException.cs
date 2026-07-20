namespace VpnHood.ResourceTranslator;

/// <summary>
/// An error that is meaningful to the user and maps onto a specific process exit code.
/// Anything else is a bug and is allowed to surface with its own stack trace.
/// </summary>
public sealed class TranslatorException(string message, int exitCode = ExitCodes.InvalidArguments)
    : Exception(message)
{
    public int ExitCode { get; } = exitCode;
}
