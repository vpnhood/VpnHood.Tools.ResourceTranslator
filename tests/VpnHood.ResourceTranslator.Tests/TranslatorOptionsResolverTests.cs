using VpnHood.ResourceTranslator.Cli;
using VpnHood.ResourceTranslator.Configuration;
using VpnHood.ResourceTranslator.Translation;

namespace VpnHood.ResourceTranslator.Tests;

[TestClass]
public sealed class TranslatorOptionsResolverTests
{
    [TestMethod]
    public void Resolve_UsesConfigFileDiscoveredFromBaseFileFolder()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("vhtranslator.json", """
            { "engine": "grok", "batch": 5, "languages": ["fr", "de"] }
            """);
        var basePath = workspace.WriteFile("locales/en.json", "{}");

        var options = TranslatorOptionsResolver.Resolve(new CommandLineOptions { BasePath = basePath });

        // Config lives one folder above the base file and is still found.
        Assert.AreEqual(TranslationEngine.Grok, options.Engine);
        Assert.AreEqual("grok-4-latest", options.Model);
        Assert.AreEqual(5, options.BatchSize);
        CollectionAssert.AreEqual(new[] { "fr", "de" }, options.Languages.ToArray());
    }

    [TestMethod]
    public void Resolve_CommandLineOverridesConfigFile()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("vhtranslator.json", """
            { "engine": "grok", "model": "grok-4-latest", "batch": 5 }
            """);
        var basePath = workspace.WriteFile("en.json", "{}");

        var options = TranslatorOptionsResolver.Resolve(new CommandLineOptions {
            BasePath = basePath,
            Engine = "gemini",
            Model = "gemini-2.5-flash",
            BatchSize = 50
        });

        Assert.AreEqual(TranslationEngine.Gemini, options.Engine);
        Assert.AreEqual("gemini-2.5-flash", options.Model);
        Assert.AreEqual(50, options.BatchSize);
    }

    [TestMethod]
    public void Resolve_TakesBasePathFromConfigWhenNotOnCommandLine()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("locales/en.json", "{}");
        var configPath = workspace.WriteFile("vhtranslator.json", """
            { "base": "locales/en.json" }
            """);

        var options = TranslatorOptionsResolver.Resolve(new CommandLineOptions { ConfigPath = configPath });

        // The config-relative path is resolved against the config's own folder.
        Assert.AreEqual(Path.Combine(workspace.Path, "locales", "en.json"), options.BasePath);
    }

    [TestMethod]
    public void Resolve_ThrowsWhenNoBasePathAnywhere()
    {
        using var workspace = new TestWorkspace();
        var configPath = workspace.WriteFile("vhtranslator.json", "{}");

        var ex = Assert.ThrowsExactly<TranslatorException>(
            () => TranslatorOptionsResolver.Resolve(new CommandLineOptions { ConfigPath = configPath }));

        Assert.AreEqual(ExitCodes.InvalidArguments, ex.ExitCode);
    }

    [TestMethod]
    public void Resolve_ThrowsWhenBaseFileMissing()
    {
        using var workspace = new TestWorkspace();
        var missing = Path.Combine(workspace.Path, "nope.json");

        var ex = Assert.ThrowsExactly<TranslatorException>(
            () => TranslatorOptionsResolver.Resolve(new CommandLineOptions { BasePath = missing }));

        Assert.AreEqual(ExitCodes.FileNotFound, ex.ExitCode);
    }

    [TestMethod]
    public void Resolve_ThrowsOnUnsupportedFileType()
    {
        using var workspace = new TestWorkspace();
        var basePath = workspace.WriteFile("strings.xml", "<root/>");

        var ex = Assert.ThrowsExactly<TranslatorException>(
            () => TranslatorOptionsResolver.Resolve(new CommandLineOptions { BasePath = basePath }));

        StringAssert.Contains(ex.Message, ".json");
    }

    [TestMethod]
    public void Resolve_ThrowsOnUnknownEngine()
    {
        using var workspace = new TestWorkspace();
        var basePath = workspace.WriteFile("en.json", "{}");

        var ex = Assert.ThrowsExactly<TranslatorException>(
            () => TranslatorOptionsResolver.Resolve(new CommandLineOptions { BasePath = basePath, Engine = "llama" }));

        StringAssert.Contains(ex.Message, "llama");
    }

    [TestMethod]
    public void Resolve_PicksUpConventionalCustomPromptFile()
    {
        using var workspace = new TestWorkspace();
        var basePath = workspace.WriteFile("en.json", "{}");
        var promptPath = workspace.WriteFile(Path.Combine("vh_translator", "custom_prompt.txt"), "Keep brand names.");

        var options = TranslatorOptionsResolver.Resolve(new CommandLineOptions { BasePath = basePath });

        Assert.AreEqual(promptPath, options.ExtraPromptPath);
    }

    [TestMethod]
    public void Resolve_ThrowsWhenExplicitExtraPromptMissing()
    {
        using var workspace = new TestWorkspace();
        var basePath = workspace.WriteFile("en.json", "{}");

        var ex = Assert.ThrowsExactly<TranslatorException>(
            () => TranslatorOptionsResolver.Resolve(new CommandLineOptions {
                BasePath = basePath,
                ExtraPromptPath = Path.Combine(workspace.Path, "missing.txt")
            }));

        Assert.AreEqual(ExitCodes.FileNotFound, ex.ExitCode);
    }

    [TestMethod]
    public void GetRequiredApiKey_ThrowsWithEngineSpecificVariableName()
    {
        var options = new TranslatorOptions {
            BasePath = "en.json",
            Engine = TranslationEngine.Grok,
            Model = "grok-4-latest",
            BatchSize = 20,
            ApiKey = null
        };

        var ex = Assert.ThrowsExactly<TranslatorException>(() => options.GetRequiredApiKey());

        Assert.AreEqual(ExitCodes.MissingApiKey, ex.ExitCode);
        StringAssert.Contains(ex.Message, "GROK_API_KEY");
    }
}
