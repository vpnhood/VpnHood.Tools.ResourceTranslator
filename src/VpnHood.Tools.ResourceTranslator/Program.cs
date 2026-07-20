using System.CommandLine;
using System.Text;
using VpnHood.Tools.ResourceTranslator.Cli;

namespace VpnHood.Tools.ResourceTranslator;

internal static class Program
{
    private static Task<int> Main(string[] args)
    {
        EnableUnicodeOutput();
        return TranslatorCommand.Create().Parse(args).InvokeAsync();
    }

    /// <summary>
    /// Progress output uses symbols like '✓', which the default Windows console code page
    /// mangles. Failing to set this is cosmetic only, so a refusing console is ignored.
    /// </summary>
    private static void EnableUnicodeOutput()
    {
        try {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (IOException) {
            // Output is redirected to a handle that rejects the change; keep the default.
        }
    }
}
