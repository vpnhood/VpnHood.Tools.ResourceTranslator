using VpnHood.Tools.ResourceTranslator.Cli;
using VpnHood.Tools.ResourceTranslator.Configuration;
using VpnHood.Tools.ResourceTranslator.Translation;

namespace VpnHood.Tools.ResourceTranslator.Tests;

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
    public void Resolve_PicksUpConventionalPromptFile()
    {
        using var workspace = new TestWorkspace();
        var basePath = workspace.WriteFile("en.json", "{}");
        var promptPath = workspace.WriteFile(Path.Combine("vh_translator", "prompt.txt"), "Keep brand names.");

        var options = TranslatorOptionsResolver.Resolve(new CommandLineOptions { BasePath = basePath });

        Assert.AreEqual(promptPath, options.ExtraPrompt.SharedPromptPath);
    }

    [TestMethod]
    public void Resolve_StillHonorsLegacyCustomPromptFile()
    {
        using var workspace = new TestWorkspace();
        var basePath = workspace.WriteFile("en.json", "{}");
        var promptPath = workspace.WriteFile(Path.Combine("vh_translator", "custom_prompt.txt"), "Keep brand names.");

        var options = TranslatorOptionsResolver.Resolve(new CommandLineOptions { BasePath = basePath });

        Assert.AreEqual(promptPath, options.ExtraPrompt.SharedPromptPath);
    }

    [TestMethod]
    public void Resolve_PrefersPromptFileOverLegacyName()
    {
        using var workspace = new TestWorkspace();
        var basePath = workspace.WriteFile("en.json", "{}");
        var promptPath = workspace.WriteFile(Path.Combine("vh_translator", "prompt.txt"), "Current.");
        workspace.WriteFile(Path.Combine("vh_translator", "custom_prompt.txt"), "Legacy.");

        var options = TranslatorOptionsResolver.Resolve(new CommandLineOptions { BasePath = basePath });

        Assert.AreEqual(promptPath, options.ExtraPrompt.SharedPromptPath);
    }

    [TestMethod]
    public async Task ExtraPrompt_AppendsPerLanguagePromptAfterSharedOne()
    {
        using var workspace = new TestWorkspace();
        var basePath = workspace.WriteFile("en.json", "{}");
        workspace.WriteFile(Path.Combine("vh_translator", "prompt.txt"), "Shared rules.");
        workspace.WriteFile(Path.Combine("vh_translator", "prompts", "fr.prompt.txt"), "French rules.");

        var options = TranslatorOptionsResolver.Resolve(new CommandLineOptions { BasePath = basePath });

        Assert.AreEqual("Shared rules.\n\nFrench rules.", await options.ExtraPrompt.LoadAsync("fr", CancellationToken.None));
        Assert.AreEqual("Shared rules.", await options.ExtraPrompt.LoadAsync("de", CancellationToken.None));
    }

    [TestMethod]
    public async Task ExtraPrompt_PerLanguagePromptWorksWithoutSharedOne()
    {
        using var workspace = new TestWorkspace();
        var basePath = workspace.WriteFile("en.json", "{}");
        workspace.WriteFile(Path.Combine("vh_translator", "prompts", "fa.prompt.txt"), "Persian rules.");

        var options = TranslatorOptionsResolver.Resolve(new CommandLineOptions { BasePath = basePath });

        Assert.IsNull(options.ExtraPrompt.SharedPromptPath);
        Assert.AreEqual("Persian rules.", await options.ExtraPrompt.LoadAsync("fa", CancellationToken.None));
        Assert.IsNull(await options.ExtraPrompt.LoadAsync("de", CancellationToken.None));
    }

    [TestMethod]
    public async Task ExtraPrompt_FindsPerLanguagePromptInConfigAdjacentFolder()
    {
        using var workspace = new TestWorkspace();
        var basePath = workspace.WriteFile("src/locales/en.json", "{}");
        workspace.WriteFile(Path.Combine("vh_translator", "vhtranslator.json"), "{}");
        workspace.WriteFile(Path.Combine("vh_translator", "prompts", "fr.prompt.txt"), "French rules.");

        var options = TranslatorOptionsResolver.Resolve(new CommandLineOptions { BasePath = basePath });

        Assert.AreEqual("French rules.", await options.ExtraPrompt.LoadAsync("fr", CancellationToken.None));
    }

    [TestMethod]
    public void ExtraPrompt_ListsEveryPromptFileForStartupLogging()
    {
        using var workspace = new TestWorkspace();
        var basePath = workspace.WriteFile("en.json", "{}");
        var sharedPath = workspace.WriteFile(Path.Combine("vh_translator", "prompt.txt"), "Shared.");
        var frPath = workspace.WriteFile(Path.Combine("vh_translator", "prompts", "fr.prompt.txt"), "French.");
        var faPath = workspace.WriteFile(Path.Combine("vh_translator", "prompts", "fa.prompt.txt"), "Persian.");

        var options = TranslatorOptionsResolver.Resolve(new CommandLineOptions { BasePath = basePath });

        // Shared first, then the per-language files in stable (alphabetical) order.
        CollectionAssert.AreEqual(new[] { sharedPath, faPath, frPath },
            options.ExtraPrompt.GetPromptFilePaths().ToArray());
    }

    [TestMethod]
    public void Resolve_ThrowsWhenExplicitExtraPromptMissing()
    {
        using var workspace = new TestWorkspace();
        var basePath = workspace.WriteFile("en.json", "{}");

        var ex = Assert.ThrowsExactly<TranslatorException>(
            () => TranslatorOptionsResolver.Resolve(new CommandLineOptions {
                BasePath = basePath,
                // ReSharper disable once AccessToDisposedClosure
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
