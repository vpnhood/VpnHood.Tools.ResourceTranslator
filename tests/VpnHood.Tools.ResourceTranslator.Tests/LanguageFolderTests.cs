using System.Text.Json;
using VpnHood.Tools.ResourceTranslator.Configuration;
using VpnHood.Tools.ResourceTranslator.Formats;
using VpnHood.Tools.ResourceTranslator.Translation;

namespace VpnHood.Tools.ResourceTranslator.Tests;

/// <summary>
/// The folder-per-language convention: a base folder named after its language (i18n/en)
/// whose files are translated to sibling language folders (i18n/fa).
/// </summary>
[TestClass]
public sealed class LanguageFolderTests
{
    [TestMethod]
    public void Format_MapsPathsBetweenSiblingLanguageFolders()
    {
        using var workspace = new TestWorkspace();
        var basePath = workspace.WriteFile("i18n/en/home.json", "{}");
        workspace.WriteFile("i18n/fr/home.json", "{}");
        workspace.WriteFile("i18n/fr/other.json", "{}");
        workspace.WriteFile("i18n/vh_translator/home.json", "{}");

        var format = new LanguageFolderResourceFormat(new JsonResourceFormat());

        Assert.AreEqual("en", format.GetLanguageCode(basePath));
        Assert.AreEqual(
            Path.Combine(workspace.Path, "i18n", "fa", "home.json"),
            format.GetLocaleFilePath(basePath, "fa"));

        // Siblings are the same file name in other language folders; bookkeeping folders don't count.
        CollectionAssert.AreEqual(
            new[] { Path.Combine(workspace.Path, "i18n", "fr", "home.json") },
            format.FindSiblingLocaleFiles(basePath).ToArray());
    }

    [TestMethod]
    public async Task RunAsync_FolderBase_TranslatesEveryFileToEveryLanguage()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("i18n/en/home.json", """{ "TITLE": "Welcome" }""");
        workspace.WriteFile("i18n/en/about.json", """{ "HEADING": "About us" }""");
        workspace.WriteFile("i18n/en/notes.txt", "not a resource file");

        var translator = new FakeTranslator();
        await CreateFolderRunner(workspace, translator, languages: ["fa", "de"]).RunAsync();

        Assert.AreEqual("[fa] Welcome", ReadJson(workspace.ReadFile("i18n/fa/home.json"))["TITLE"]);
        Assert.AreEqual("[de] Welcome", ReadJson(workspace.ReadFile("i18n/de/home.json"))["TITLE"]);
        Assert.AreEqual("[fa] About us", ReadJson(workspace.ReadFile("i18n/fa/about.json"))["HEADING"]);
        Assert.AreEqual("[de] About us", ReadJson(workspace.ReadFile("i18n/de/about.json"))["HEADING"]);
        Assert.IsFalse(workspace.Exists("i18n/fa/notes.txt"), "Unsupported files must be ignored");
    }

    [TestMethod]
    public async Task RunAsync_FolderBase_IsIncrementalAcrossRuns()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("i18n/en/home.json", """{ "TITLE": "Welcome" }""");
        workspace.WriteFile("i18n/en/about.json", """{ "HEADING": "About us" }""");

        await CreateFolderRunner(workspace, new FakeTranslator(), languages: ["fa"]).RunAsync();

        // Without a config the watch files land beside the language folders.
        Assert.IsTrue(workspace.Exists(Path.Combine("i18n", "vh_translator", "watches", "i18n", "home.watch.json")));

        var secondTranslator = new FakeTranslator();
        await CreateFolderRunner(workspace, secondTranslator, languages: ["fa"]).RunAsync();
        Assert.AreEqual(0, secondTranslator.CallCount, "Unchanged folder must not contact the AI");

        // Changing one file retranslates only that file's keys.
        workspace.WriteFile("i18n/en/home.json", """{ "TITLE": "Welcome back" }""");
        var thirdTranslator = new FakeTranslator();
        await CreateFolderRunner(workspace, thirdTranslator, languages: ["fa"]).RunAsync();

        CollectionAssert.AreEqual(new[] { "TITLE" }, thirdTranslator.TranslatedKeys);
        Assert.AreEqual("[fa] Welcome back", ReadJson(workspace.ReadFile("i18n/fa/home.json"))["TITLE"]);
    }

    [TestMethod]
    public async Task RunAsync_FolderBase_WithConfigPath_PutsWatchFilesNextToConfig()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("i18n/en/home.json", """{ "TITLE": "Welcome" }""");
        var configPath = workspace.WriteFile("vh_translator/vhtranslator.json", "{}");

        var options = CreateOptions(workspace, languages: ["fa"], configPath: configPath);
        await new TranslationRunner(options, translatorFactory: () => new FakeTranslator()).RunAsync();

        Assert.IsTrue(workspace.Exists(Path.Combine("vh_translator", "watches", "i18n", "home.watch.json")),
            "Bookkeeping must live next to the config, not inside the data tree");
        Assert.IsFalse(Directory.Exists(Path.Combine(workspace.Path, "i18n", "vh_translator")));
    }

    [TestMethod]
    public async Task RunAsync_FolderBase_MigratesFlatWatchFileIntoNamespaceSubfolder()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("i18n/en/home.json", """{ "TITLE": "Welcome" }""");
        workspace.WriteFile("i18n/fa/home.json", """{ "TITLE": "خوش آمدید" }""");
        var configPath = workspace.WriteFile("vh_translator/vhtranslator.json", "{}");

        // A watch file from the previous layout: prefixed, flat inside watches/.
        workspace.WriteFile("vh_translator/watches/i18n_home_watch.json",
            """{ "version": 1, "items": { "TITLE": "Welcome" } }""");

        var translator = new FakeTranslator();
        var options = CreateOptions(workspace, languages: ["fa"], configPath: configPath);
        await new TranslationRunner(options, translatorFactory: () => translator).RunAsync();

        Assert.AreEqual(0, translator.CallCount, "The flat watch file must be honored (nothing changed)");
        Assert.IsTrue(workspace.Exists(Path.Combine("vh_translator", "watches", "i18n", "home.watch.json")));
        Assert.IsFalse(workspace.Exists(Path.Combine("vh_translator", "watches", "i18n_home_watch.json")),
            "The flat copy must be removed after migration");
    }

    [TestMethod]
    public async Task RunAsync_FolderBase_WithoutLanguages_DiscoversSiblingFolders()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("i18n/en/home.json", """{ "TITLE": "Welcome" }""");
        workspace.WriteFile("i18n/fr/home.json", "{}");

        var translator = new FakeTranslator();
        await CreateFolderRunner(workspace, translator, languages: []).RunAsync();

        Assert.AreEqual("[fr] Welcome", ReadJson(workspace.ReadFile("i18n/fr/home.json"))["TITLE"]);
        Assert.IsFalse(workspace.Exists("i18n/de/home.json"), "Discovery must only fill existing languages");
    }

    [TestMethod]
    public async Task RebuildLanguageAsync_FolderBase_CreatesTheLanguageFolder()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("i18n/en/home.json", """{ "TITLE": "Welcome" }""");
        workspace.WriteFile("i18n/en/about.json", """{ "HEADING": "About us" }""");

        await CreateFolderRunner(workspace, new FakeTranslator(), languages: ["fa"]).RebuildLanguageAsync("fa");

        Assert.AreEqual("[fa] Welcome", ReadJson(workspace.ReadFile("i18n/fa/home.json"))["TITLE"]);
        Assert.AreEqual("[fa] About us", ReadJson(workspace.ReadFile("i18n/fa/about.json"))["HEADING"]);
    }

    [TestMethod]
    public void Constructor_FolderBase_WithoutSupportedFiles_FailsLoudly()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("i18n/en/readme.txt", "nothing translatable here");

        var options = CreateOptions(workspace, languages: ["fa"]);
        var ex = Assert.ThrowsExactly<TranslatorException>(
            () => new TranslationRunner(options, translatorFactory: FakeTranslatorFactory));

        Assert.AreEqual(ExitCodes.FileNotFound, ex.ExitCode);
    }

    [TestMethod]
    public void Resolver_AcceptsLanguageFolderAsBase()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("i18n/en/home.json", "{}");

        var options = TranslatorOptionsResolver.Resolve(new Cli.CommandLineOptions {
            BasePath = Path.Combine(workspace.Path, "i18n", "en")
        });

        Assert.AreEqual(Path.Combine(workspace.Path, "i18n", "en"), options.BasePath);
    }

    private static ITranslator FakeTranslatorFactory() => new FakeTranslator();

    private static TranslationRunner CreateFolderRunner(
        TestWorkspace workspace, ITranslator translator, IReadOnlyList<string> languages)
    {
        return new TranslationRunner(CreateOptions(workspace, languages), translatorFactory: () => translator);
    }

    private static TranslatorOptions CreateOptions(
        TestWorkspace workspace, IReadOnlyList<string> languages, string? configPath = null)
    {
        return new TranslatorOptions {
            BasePath = Path.Combine(workspace.Path, "i18n", "en"),
            Engine = TranslationEngine.Gemini,
            Model = "test-model",
            BatchSize = 20,
            ApiKey = "test-key",
            Languages = languages,
            ConfigPath = configPath
        };
    }

    private static Dictionary<string, string> ReadJson(string json)
    {
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
    }
}
