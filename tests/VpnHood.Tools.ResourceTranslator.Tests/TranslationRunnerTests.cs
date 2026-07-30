using System.Text.Json;
using VpnHood.Tools.ResourceTranslator.Cli;
using VpnHood.Tools.ResourceTranslator.Configuration;
using VpnHood.Tools.ResourceTranslator.Translation;

namespace VpnHood.Tools.ResourceTranslator.Tests;

[TestClass]
public sealed class TranslationRunnerTests
{
    private const string BaseJson =
        """
        {
          "GREETING": "Hello",
          "FAREWELL": "Goodbye",
          "SETTINGS": "Settings"
        }
        """;

    [TestMethod]
    public async Task RunAsync_WithoutWatchFile_TranslatesEveryKey()
    {
        using var workspace = new TestWorkspace();
        var basePath = workspace.WriteFile("en.json", BaseJson);
        workspace.WriteFile("fr.json", """{ "GREETING": "Bonjour" }""");

        var translator = new FakeTranslator();
        await CreateRunner(basePath, translator).RunAsync();

        // Cold start: with no recorded baseline nothing can be assumed current, so every
        // entry is retranslated - including one that already had a translation.
        CollectionAssert.AreEquivalent(new[] { "GREETING", "FAREWELL", "SETTINGS" }, translator.TranslatedKeys);
        Assert.AreEqual("[fr] Hello", ReadJson(workspace.ReadFile("fr.json"))["GREETING"]);
    }

    [TestMethod]
    public async Task RunAsync_AfterWatchFileSeeded_TranslatesOnlyMissingEntries()
    {
        using var workspace = new TestWorkspace();
        var basePath = workspace.WriteFile("en.json", BaseJson);
        workspace.WriteFile("fr.json", """{ "GREETING": "Bonjour" }""");

        // Seeding the watch file is how a project adopts existing hand-made translations.
        await CreateRunner(basePath, new FakeTranslator()).RebuildWatchFileAsync();

        var translator = new FakeTranslator();
        await CreateRunner(basePath, translator).RunAsync();

        var french = ReadJson(workspace.ReadFile("fr.json"));
        CollectionAssert.AreEquivalent(new[] { "FAREWELL", "SETTINGS" }, translator.TranslatedKeys);
        Assert.AreEqual("Bonjour", french["GREETING"]);
        Assert.AreEqual("[fr] Goodbye", french["FAREWELL"]);
        Assert.AreEqual("[fr] Settings", french["SETTINGS"]);
    }

    [TestMethod]
    public async Task RunAsync_PreservesBaseKeyOrder()
    {
        using var workspace = new TestWorkspace();
        var basePath = workspace.WriteFile("en.json", BaseJson);
        workspace.WriteFile("fr.json", "{}");

        await CreateRunner(basePath, new FakeTranslator()).RunAsync();

        var keys = JsonDocument.Parse(workspace.ReadFile("fr.json"))
            .RootElement.EnumerateObject().Select(p => p.Name).ToArray();

        CollectionAssert.AreEqual(new[] { "GREETING", "FAREWELL", "SETTINGS" }, keys);
    }

    [TestMethod]
    public async Task RunAsync_SecondRunTranslatesNothingWhenSourceUnchanged()
    {
        using var workspace = new TestWorkspace();
        var basePath = workspace.WriteFile("en.json", BaseJson);
        workspace.WriteFile("fr.json", "{}");

        await CreateRunner(basePath, new FakeTranslator()).RunAsync();

        var secondTranslator = new FakeTranslator();
        await CreateRunner(basePath, secondTranslator).RunAsync();

        Assert.AreEqual(0, secondTranslator.CallCount);
    }

    [TestMethod]
    public async Task RunAsync_RetranslatesOnlyKeysWhoseSourceTextChanged()
    {
        using var workspace = new TestWorkspace();
        var basePath = workspace.WriteFile("en.json", BaseJson);
        workspace.WriteFile("fr.json", "{}");

        await CreateRunner(basePath, new FakeTranslator()).RunAsync();

        // Change one source string; the other two must stay untouched.
        workspace.WriteFile("en.json", """
            {
              "GREETING": "Hello there",
              "FAREWELL": "Goodbye",
              "SETTINGS": "Settings"
            }
            """);

        var translator = new FakeTranslator();
        await CreateRunner(basePath, translator).RunAsync();

        CollectionAssert.AreEqual(new[] { "GREETING" }, translator.TranslatedKeys);
        Assert.AreEqual("[fr] Hello there", ReadJson(workspace.ReadFile("fr.json"))["GREETING"]);
    }

    [TestMethod]
    public async Task RunAsync_SkipMarkerKeepsExistingTranslation()
    {
        using var workspace = new TestWorkspace();
        var basePath = workspace.WriteFile("en.json", """{ "BRAND": "VpnHood", "GREETING": "Hello" }""");
        workspace.WriteFile("fr.json", "{}");

        // "*" is the model's way of declining an entry; the source text should survive.
        var translator = new FakeTranslator(item => item.Key == "BRAND" ? "*" : $"[fr] {item.Text}");
        await CreateRunner(basePath, translator).RunAsync();

        var french = ReadJson(workspace.ReadFile("fr.json"));
        Assert.AreEqual("VpnHood", french["BRAND"]);
        Assert.AreEqual("[fr] Hello", french["GREETING"]);
    }

    [TestMethod]
    public async Task RunAsync_CreatesFilesForConfiguredLanguages()
    {
        using var workspace = new TestWorkspace();
        var basePath = workspace.WriteFile("en.json", BaseJson);

        var options = CreateOptions(basePath, languages: ["fr", "de"]);
        await new TranslationRunner(options, translatorFactory: () => new FakeTranslator()).RunAsync();

        Assert.IsTrue(workspace.Exists("fr.json"));
        Assert.IsTrue(workspace.Exists("de.json"));
        Assert.AreEqual("[de] Hello", ReadJson(workspace.ReadFile("de.json"))["GREETING"]);
    }

    [TestMethod]
    public async Task RebuildLanguageAsync_RetranslatesEverythingIncludingExistingEntries()
    {
        using var workspace = new TestWorkspace();
        var basePath = workspace.WriteFile("en.json", BaseJson);
        workspace.WriteFile("fr.json", """{ "GREETING": "Bonjour" }""");

        var translator = new FakeTranslator();
        await CreateRunner(basePath, translator).RebuildLanguageAsync("fr");

        CollectionAssert.AreEquivalent(new[] { "GREETING", "FAREWELL", "SETTINGS" }, translator.TranslatedKeys);
        Assert.AreEqual("[fr] Hello", ReadJson(workspace.ReadFile("fr.json"))["GREETING"]);
    }

    [TestMethod]
    public async Task RebuildWatchFileAsync_MarksEverythingCurrentWithoutTranslating()
    {
        using var workspace = new TestWorkspace();
        var basePath = workspace.WriteFile("en.json", BaseJson);
        workspace.WriteFile("fr.json", "{}");

        await CreateRunner(basePath, new FakeTranslator()).RebuildWatchFileAsync();

        // Nothing was translated, but a later run should now see no changes.
        var translator = new FakeTranslator();
        await CreateRunner(basePath, translator).RunAsync();

        // fr.json is still empty, so entries count as missing and are filled — but none as "changed".
        Assert.AreEqual(3, translator.TranslatedKeys.Count);
        Assert.IsTrue(workspace.Exists(Path.Combine("vh_translator", "watches", "en_watch.json")));
    }

    [TestMethod]
    public async Task RunAsync_MigratesLegacyWatchFileIntoWatchesSubfolder()
    {
        using var workspace = new TestWorkspace();
        var basePath = workspace.WriteFile("en.json", BaseJson);
        workspace.WriteFile("fr.json", """{ "GREETING": "Bonjour" }""");

        // A watch file from an older tool version, directly inside vh_translator/.
        workspace.WriteFile("vh_translator/en_watch.json",
            """{ "version": 1, "items": { "GREETING": "Hello", "FAREWELL": "Goodbye", "SETTINGS": "Settings" } }""");

        var translator = new FakeTranslator();
        await CreateRunner(basePath, translator).RunAsync();

        // The legacy snapshot was honored (nothing counted as changed, only missing filled)...
        CollectionAssert.AreEquivalent(new[] { "FAREWELL", "SETTINGS" }, translator.TranslatedKeys);

        // ...and the file now lives in watches/, with the legacy copy gone.
        Assert.IsTrue(workspace.Exists(Path.Combine("vh_translator", "watches", "en_watch.json")));
        Assert.IsFalse(workspace.Exists(Path.Combine("vh_translator", "en_watch.json")));
    }

    [TestMethod]
    public async Task RunAsync_FailsLoudlyWhenBaseFileIsNotValidJson()
    {
        using var workspace = new TestWorkspace();
        var basePath = workspace.WriteFile("en.json", "{ not json");

        var ex = await Assert.ThrowsExactlyAsync<TranslatorException>(
            () => CreateRunner(basePath, new FakeTranslator()).RunAsync());

        Assert.AreEqual(ExitCodes.ParseError, ex.ExitCode);
    }

    [TestMethod]
    public async Task RunAsync_SingleFileWithConfig_PutsWatchFileNextToConfig()
    {
        using var workspace = new TestWorkspace();
        var basePath = workspace.WriteFile("src/locales/en.json", BaseJson);
        workspace.WriteFile("src/locales/fr.json", """{ "GREETING": "Bonjour" }""");
        var configPath = workspace.WriteFile("vh_translator/vhtranslator.json", "{}");

        // A watch file from an older, config-less integration, beside the base file.
        workspace.WriteFile("src/locales/vh_translator/en_watch.json",
            """{ "version": 1, "items": { "GREETING": "Hello", "FAREWELL": "Goodbye", "SETTINGS": "Settings" } }""");

        var options = new TranslatorOptions {
            BasePath = basePath,
            Engine = TranslationEngine.Gemini,
            Model = "test-model",
            BatchSize = 20,
            ApiKey = "test-key",
            Languages = [],
            ConfigPath = configPath
        };
        var translator = new FakeTranslator();
        await new TranslationRunner(options, translatorFactory: () => translator).RunAsync();

        // The old snapshot was honored (only missing entries filled, nothing "changed")...
        CollectionAssert.AreEquivalent(new[] { "FAREWELL", "SETTINGS" }, translator.TranslatedKeys);

        // ...and the bookkeeping now lives next to the config, out of the resource tree.
        Assert.IsTrue(workspace.Exists(Path.Combine("vh_translator", "watches", "locales", "en_watch.json")));
        Assert.IsFalse(workspace.Exists(Path.Combine("src", "locales", "vh_translator", "en_watch.json")),
            "The base-adjacent copy must be removed after migration");
    }

    [TestMethod]
    public async Task RunAsync_SendsPerLanguagePromptOnlyToItsOwnLanguage()
    {
        using var workspace = new TestWorkspace();
        var basePath = workspace.WriteFile("en.json", BaseJson);
        workspace.WriteFile("fr.json", "{}");
        workspace.WriteFile("de.json", "{}");
        workspace.WriteFile(Path.Combine("vh_translator", "prompt.txt"), "SHARED-RULES");
        workspace.WriteFile(Path.Combine("vh_translator", "prompts", "fr_prompt.txt"), "FRENCH-RULES");

        // Resolved (not hand-built) options, so the conventional prompt files are discovered.
        var options = TranslatorOptionsResolver.Resolve(new CommandLineOptions { BasePath = basePath });
        var translator = new FakeTranslator();
        await new TranslationRunner(options, translatorFactory: () => translator).RunAsync();

        StringAssert.Contains(translator.PromptsByLanguage["fr"], "SHARED-RULES");
        StringAssert.Contains(translator.PromptsByLanguage["fr"], "FRENCH-RULES");
        Assert.IsTrue(translator.PromptsByLanguage["fr"].IndexOf("SHARED-RULES", StringComparison.Ordinal) <
                      translator.PromptsByLanguage["fr"].IndexOf("FRENCH-RULES", StringComparison.Ordinal),
            "The shared prompt must come before the per-language one");
        StringAssert.Contains(translator.PromptsByLanguage["de"], "SHARED-RULES");
        Assert.IsFalse(translator.PromptsByLanguage["de"].Contains("FRENCH-RULES"),
            "Another language's prompt must not leak into this one");
    }

    private static TranslationRunner CreateRunner(string basePath, ITranslator translator)
    {
        return new TranslationRunner(CreateOptions(basePath), translatorFactory: () => translator);
    }

    private static TranslatorOptions CreateOptions(string basePath, IReadOnlyList<string>? languages = null)
    {
        return new TranslatorOptions {
            BasePath = basePath,
            Engine = TranslationEngine.Gemini,
            Model = "test-model",
            BatchSize = 20,
            ApiKey = "test-key",
            Languages = languages ?? []
        };
    }

    private static Dictionary<string, string> ReadJson(string json)
    {
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
    }
}
