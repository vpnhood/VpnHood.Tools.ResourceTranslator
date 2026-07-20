using System.Text.Json;
using VpnHood.ResourceTranslator.Configuration;
using VpnHood.ResourceTranslator.Translation;

namespace VpnHood.ResourceTranslator.Tests;

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
        Assert.IsTrue(workspace.Exists(Path.Combine("vh_translator", "en_watch.json")));
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
