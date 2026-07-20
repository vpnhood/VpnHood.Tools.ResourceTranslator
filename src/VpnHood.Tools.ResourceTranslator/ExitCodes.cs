namespace VpnHood.Tools.ResourceTranslator;

/// <summary>
/// Process exit codes. Values are part of the tool's contract with CI scripts, so they are
/// kept stable across releases.
/// </summary>
public static class ExitCodes
{
    public const int Success = 0;
    public const int InvalidArguments = 1;
    public const int FileNotFound = 2;
    public const int ParseError = 3;
    public const int MissingApiKey = 4;
    public const int TranslationFailed = 10;
}
