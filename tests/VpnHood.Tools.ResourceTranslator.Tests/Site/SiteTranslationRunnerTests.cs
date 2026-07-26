using VpnHood.Tools.ResourceTranslator.Site;
using VpnHood.Tools.ResourceTranslator.Translation;
// ReSharper disable StringLiteralTypo

namespace VpnHood.Tools.ResourceTranslator.Tests.Site;

[TestClass]
public class SiteTranslationRunnerTests
{
    private const string HomePage =
        """
        ---
        layout: none
        title: Free & Open Source VPN – VpnHood!
        description: A free VPN for everyone.
        nav_active: home
        ---
        {% include header.html %}
        {% raw %}
        <section id="sp-main-body">
          <h1>Free VPN for everyone who cares about privacy</h1>
          <p>Get <a class="vh-btn" href="/free-vpn/download/">VpnHood!</a> free today, no account needed.</p>
        </section>
        {% endraw %}
        {% include footer.html %}
        """;

    private const string DownloadPage =
        """
        ---
        layout: none
        title: Download – VpnHood!
        description: Download the app.
        nav_active: free-vpn
        ---
        {% include header.html %}
        <section id="sp-main-body">
          <h2>Download VpnHood for Windows and Android right now</h2>
        </section>
        {% include footer.html %}
        """;

    private static SiteOptions CreateOptions(TestWorkspace workspace, params string[] dataFiles)
    {
        return CreateOptionsWithConfig(workspace, configPath: null, dataFiles: dataFiles);
    }

    private static SiteOptions CreateOptionsWithConfig(
        TestWorkspace workspace,
        string? configPath,
        PageBodyMode pageBody = PageBodyMode.Translate,
        params string[] dataFiles)
    {
        return new SiteOptions {
            RootPath = workspace.Path,
            PagePatterns = ["**/index.html"],
            ExcludePatterns = ["_site/**", "vh_translator/**", "fr/**", "de/**"],
            Languages = ["fr", "de"],
            OutputPattern = "{lang}/{path}",
            DataFiles = dataFiles.Select(file => Path.Combine(workspace.Path, file)).ToList(),
            SourceLanguage = "en",
            Engine = TranslationEngine.Gemini,
            Model = "test-model",
            BatchSize = 20,
            TitleMustContain = "VpnHood!",
            ApiKey = "unused",
            ConfigPath = configPath,
            PageBodyMode = pageBody
        };
    }

    private static SiteTranslationRunner CreateRunner(SiteOptions options, ITranslator translator)
    {
        return new SiteTranslationRunner(options, translatorFactory: () => translator) {
            PacingDelay = TimeSpan.Zero
        };
    }

    [TestMethod]
    public async Task Run_creates_verified_locale_pages()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("index.html", HomePage);
        workspace.WriteFile("free-vpn/download/index.html", DownloadPage);

        var translator = new FakeTranslator();
        var runner = CreateRunner(CreateOptions(workspace), translator);

        var exitCode = await runner.RunAsync();

        Assert.AreEqual(ExitCodes.Success, exitCode);
        foreach (var path in new[] { "fr/index.html", "de/index.html", "fr/free-vpn/download/index.html", "de/free-vpn/download/index.html" })
            Assert.IsTrue(workspace.Exists(path), $"{path} must be generated");

        var french = workspace.ReadFile("fr/index.html");
        Assert.IsTrue(french.Contains("lang: fr"));
        Assert.IsTrue(french.Contains("auto_translated: true"));
        Assert.IsTrue(french.Contains("permalink: /fr/"), "The served URL must be pinned explicitly");
        Assert.IsTrue(french.Contains("title: \"[fr] Free & Open Source VPN – VpnHood!\""));
        Assert.IsTrue(french.Contains("{% include header.html %}"), "Liquid tags must survive round-trip");
        Assert.IsTrue(french.Contains("{% raw %}"));
        Assert.IsTrue(french.Contains("href=\"/free-vpn/download/\""), "Links must be untouched");
        Assert.IsTrue(french.Contains("[fr]"), "Body must be translated");
        Assert.IsTrue(workspace.Exists("vh_translator/watches/pages/site_watch.json"), "Watch file must be recorded");
    }

    [TestMethod]
    public async Task Run_is_incremental_and_idempotent()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("index.html", HomePage);

        var translator = new FakeTranslator();
        var options = CreateOptions(workspace);
        var runner = CreateRunner(options, translator);

        await runner.RunAsync();
        var callsAfterFirstRun = translator.CallCount;
        Assert.AreEqual(2, callsAfterFirstRun, "One call per language");

        // Nothing changed: the second run must not contact the AI at all.
        await runner.RunAsync();
        Assert.AreEqual(callsAfterFirstRun, translator.CallCount);

        // Deleting one target retranslates exactly that page/language pair.
        File.Delete(Path.Combine(workspace.Path, "fr", "index.html"));
        await runner.RunAsync();
        Assert.AreEqual(callsAfterFirstRun + 1, translator.CallCount);
        Assert.IsTrue(workspace.Exists("fr/index.html"));

        // Editing the source retranslates it for every language.
        workspace.WriteFile("index.html", HomePage.Replace("no account needed", "no signup needed"));
        await runner.RunAsync();
        Assert.AreEqual(callsAfterFirstRun + 3, translator.CallCount);
        Assert.IsTrue(workspace.ReadFile("de/index.html").Contains("no signup needed"));
    }

    [TestMethod]
    public async Task Run_never_overwrites_hand_authored_pages()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("index.html", HomePage);
        workspace.WriteFile("fr/index.html", HomePage.Replace("VPN for everyone", "hand-written French page"));

        var runner = CreateRunner(CreateOptions(workspace), new FakeTranslator());
        var exitCode = await runner.RunAsync();

        Assert.AreEqual(ExitCodes.Success, exitCode);
        Assert.IsTrue(workspace.ReadFile("fr/index.html").Contains("hand-written French page"),
            "A page without the auto_translated marker must never be overwritten");
        Assert.IsTrue(workspace.ReadFile("de/index.html").Contains("auto_translated: true"),
            "Other languages must still be generated");
    }

    [TestMethod]
    public async Task Run_rejects_structurally_broken_output_and_retries_next_run()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("index.html", HomePage);

        // A translator that silently drops the <a> element from the body.
        var breaking = new FakeTranslator(item => item.Key == "body"
            ? item.Text.Replace("<a class=\"vh-btn\" href=\"/free-vpn/download/\">VpnHood!</a>", "VpnHood!")
            : $"[{item.TargetLanguage}] {item.Text}");

        var runner = CreateRunner(CreateOptions(workspace), breaking);
        var exitCode = await runner.RunAsync();

        Assert.AreEqual(ExitCodes.VerificationFailed, exitCode);
        Assert.IsFalse(workspace.Exists("fr/index.html"), "Broken output must never be written");
        Assert.IsFalse(workspace.Exists("de/index.html"));

        // The failure must not be recorded as done: a later run with a good translator recovers.
        var good = new FakeTranslator();
        var recovered = CreateRunner(CreateOptions(workspace), good);
        Assert.AreEqual(ExitCodes.Success, await recovered.RunAsync());
        Assert.IsTrue(workspace.Exists("fr/index.html"));
    }

    [TestMethod]
    public async Task Run_rejects_lost_liquid_placeholders()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("index.html", HomePage);

        // A translator that "translates" the protected include placeholder away.
        var breaking = new FakeTranslator(item => item.Key == "body"
            ? item.Text.Replace(LiquidMasker.TokenName(0), "")
            : item.Text);

        var runner = CreateRunner(CreateOptions(workspace), breaking);

        Assert.AreEqual(ExitCodes.VerificationFailed, await runner.RunAsync());
        Assert.IsFalse(workspace.Exists("fr/index.html"));
    }

    [TestMethod]
    public async Task Run_rejects_titles_that_lose_the_brand()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("index.html", HomePage);

        var breaking = new FakeTranslator(item => item.Key == "title"
            ? "VPN gratuit et open source"
            : item.Text);

        var runner = CreateRunner(CreateOptions(workspace), breaking);

        Assert.AreEqual(ExitCodes.VerificationFailed, await runner.RunAsync());
        Assert.IsFalse(workspace.Exists("fr/index.html"));
    }

    [TestMethod]
    public async Task Run_skips_the_title_rule_when_the_source_itself_violates_it()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("index.html", HomePage.Replace(
            "title: Free & Open Source VPN – VpnHood!", "title: Free VPN download"));

        // The translation cannot contain the brand because the source does not either;
        // that is a source defect to warn about, not a translation failure.
        var runner = CreateRunner(CreateOptions(workspace), new FakeTranslator());

        Assert.AreEqual(ExitCodes.Success, await runner.RunAsync());
        Assert.IsTrue(workspace.Exists("fr/index.html"));
    }

    [TestMethod]
    public async Task Run_translates_data_files_with_the_classic_pipeline()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("index.html", HomePage);
        workspace.WriteFile("_data/i18n/en.json", """{ "nav_home": "Home", "nav_faq": "FAQ" }""");

        var translator = new FakeTranslator();
        var runner = CreateRunner(CreateOptions(workspace, "_data/i18n/en.json"), translator);

        Assert.AreEqual(ExitCodes.Success, await runner.RunAsync());
        Assert.IsTrue(workspace.Exists("_data/i18n/fr.json"), "Data siblings must be created");
        Assert.IsTrue(workspace.ReadFile("_data/i18n/fr.json").Contains("[fr] Home"));
        Assert.IsTrue(workspace.ReadFile("_data/i18n/de.json").Contains("[de] FAQ"));
    }

    [TestMethod]
    public async Task Run_translates_a_data_language_folder_to_sibling_folders()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("index.html", HomePage);
        workspace.WriteFile("_data/i18n/en/home.json", """{ "hero_title": "Free VPN" }""");
        workspace.WriteFile("_data/i18n/en/about.json", """{ "heading": "About us" }""");
        var configPath = workspace.WriteFile("vh_translator/vhtranslator.json", "{}");

        var runner = CreateRunner(
            CreateOptionsWithConfig(workspace, configPath, dataFiles: "_data/i18n/en"), new FakeTranslator());

        Assert.AreEqual(ExitCodes.Success, await runner.RunAsync());
        Assert.IsTrue(workspace.ReadFile("_data/i18n/fr/home.json").Contains("[fr] Free VPN"));
        Assert.IsTrue(workspace.ReadFile("_data/i18n/de/home.json").Contains("[de] Free VPN"));
        Assert.IsTrue(workspace.ReadFile("_data/i18n/fr/about.json").Contains("[fr] About us"));
        Assert.IsTrue(workspace.Exists(Path.Combine("vh_translator", "watches", "i18n", "home_watch.json")),
            "Data bookkeeping must live next to the config");
        Assert.IsFalse(Directory.Exists(Path.Combine(workspace.Path, "_data", "i18n", "vh_translator")),
            "Bookkeeping must not land inside the Jekyll data tree");
    }

    [TestMethod]
    public async Task Run_survives_a_page_without_front_matter()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("index.html", HomePage);
        workspace.WriteFile("broken/index.html", "<p>No front matter here.</p>");

        var runner = CreateRunner(CreateOptions(workspace), new FakeTranslator());
        var exitCode = await runner.RunAsync();

        Assert.AreEqual(ExitCodes.VerificationFailed, exitCode, "The malformed page must be reported");
        Assert.IsTrue(workspace.Exists("fr/index.html"), "Healthy pages must still be translated");
        Assert.IsFalse(workspace.Exists("fr/broken/index.html"));
    }

    [TestMethod]
    public async Task RebuildLanguage_forces_data_files_too()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("index.html", HomePage);
        workspace.WriteFile("_data/i18n/en/home.json", """{ "hero_title": "Free VPN" }""");
        var configPath = workspace.WriteFile("vh_translator/vhtranslator.json", "{}");

        var runner = CreateRunner(
            CreateOptionsWithConfig(workspace, configPath, dataFiles: "_data/i18n/en"), new FakeTranslator());
        Assert.AreEqual(ExitCodes.Success, await runner.RunAsync());

        // Simulate a bad shipped translation: the watch says up to date, but the fr data is wrong.
        workspace.WriteFile("_data/i18n/fr/home.json", """{ "hero_title": "stale" }""");
        Assert.AreEqual(ExitCodes.Success, await runner.RebuildLanguageAsync("fr"));

        Assert.IsTrue(workspace.ReadFile("_data/i18n/fr/home.json").Contains("[fr] Free VPN"),
            "A language rebuild must retranslate the data files, not just the pages");
        Assert.IsTrue(workspace.ReadFile("_data/i18n/de/home.json").Contains("[de] Free VPN"),
            "Other languages must keep their existing data");
    }

    [TestMethod]
    public async Task RebuildLanguage_rejects_unlisted_languages()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("index.html", HomePage);

        var runner = CreateRunner(CreateOptions(workspace), new FakeTranslator());

        await Assert.ThrowsExactlyAsync<TranslatorException>(() => runner.RebuildLanguageAsync("it"));
    }

    [TestMethod]
    public async Task Run_skips_generated_pages_discovered_as_source()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("index.html", HomePage);
        // Simulates a bad exclude glob letting a generated page into discovery.
        workspace.WriteFile("es/index.html", HomePage.Replace("nav_active: home", "nav_active: home\nauto_translated: true"));

        var translator = new FakeTranslator();
        var runner = CreateRunner(CreateOptions(workspace), translator);

        Assert.AreEqual(ExitCodes.Success, await runner.RunAsync());
        Assert.IsFalse(workspace.Exists("fr/es/index.html"), "A generated page must never become a source");
        Assert.IsFalse(workspace.Exists("de/es/index.html"));
    }

    [TestMethod]
    public async Task Run_prunes_orphaned_generated_pages_but_never_hand_authored_ones()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("index.html", HomePage);

        var runner = CreateRunner(CreateOptions(workspace), new FakeTranslator());
        Assert.AreEqual(ExitCodes.Success, await runner.RunAsync());

        // A leftover generated page whose source is gone, and a hand-authored page.
        workspace.WriteFile("fr/removed-page/index.html", "---\nlang: fr\nauto_translated: true\n---\n<p>stale</p>");
        workspace.WriteFile("fr/hand-page/index.html", "---\nlang: fr\n---\n<p>hand-written</p>");

        Assert.AreEqual(ExitCodes.Success, await runner.RunAsync());
        Assert.IsFalse(workspace.Exists("fr/removed-page/index.html"), "Orphaned generated pages must be pruned");
        Assert.IsTrue(workspace.Exists("fr/hand-page/index.html"), "Hand-authored pages must never be deleted");
    }

    [TestMethod]
    public async Task Run_honors_the_translate_false_front_matter_flag()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("index.html", HomePage);
        workspace.WriteFile("internal/index.html", HomePage.Replace("nav_active: home", "nav_active: home\ntranslate: false"));

        var runner = CreateRunner(CreateOptions(workspace), new FakeTranslator());
        Assert.AreEqual(ExitCodes.Success, await runner.RunAsync());

        Assert.IsTrue(workspace.Exists("fr/index.html"));
        Assert.IsFalse(workspace.Exists("fr/internal/index.html"), "An opted-out page must not be translated");

        // Opting out an already-translated page prunes its generated copies on the next run.
        workspace.WriteFile("fr/internal/index.html", "---\nlang: fr\nauto_translated: true\n---\n<p>old</p>");
        Assert.AreEqual(ExitCodes.Success, await runner.RunAsync());
        Assert.IsFalse(workspace.Exists("fr/internal/index.html"),
            "Generated copies of an opted-out page must be pruned");
    }

    [TestMethod]
    public async Task Run_fails_when_the_response_misses_a_metadata_item()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("index.html", HomePage);

        var runner = CreateRunner(CreateOptions(workspace), new DroppingTranslator("title"));

        Assert.AreEqual(ExitCodes.VerificationFailed, await runner.RunAsync());
        Assert.IsFalse(workspace.Exists("fr/index.html"),
            "A page whose title was never translated must not be written");
    }

    /// <summary>Delegates to <see cref="FakeTranslator" /> but drops one key from every response.</summary>
    private sealed class DroppingTranslator(string keyToDrop) : ITranslator
    {
        private readonly FakeTranslator _inner = new();

        public async Task<TranslateResult[]> TranslateAsync(PromptOptions promptOptions, CancellationToken cancellationToken)
        {
            var results = await _inner.TranslateAsync(promptOptions, cancellationToken);
            return results.Where(result => result.Key != keyToDrop).ToArray();
        }
    }

    [TestMethod]
    public async Task Run_copy_mode_keeps_the_body_byte_identical_and_translates_only_metadata()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("index.html", HomePage);

        var translator = new FakeTranslator();
        var options = CreateOptionsWithConfig(workspace, configPath: null, PageBodyMode.Copy);
        var runner = CreateRunner(options, translator);

        Assert.AreEqual(ExitCodes.Success, await runner.RunAsync());
        Assert.IsFalse(translator.TranslatedKeys.Contains("body"), "The model must never see the body");

        var french = workspace.ReadFile("fr/index.html");
        Assert.IsTrue(french.Contains("title: \"[fr] Free & Open Source VPN – VpnHood!\""));
        Assert.IsTrue(french.Contains("lang: fr"));
        Assert.IsTrue(french.Contains("auto_translated: true"));
        Assert.IsTrue(french.Contains("<h1>Free VPN for everyone who cares about privacy</h1>"),
            "The body must be the untouched source body (it self-localizes via page.lang)");
        Assert.IsTrue(french.Contains("{% raw %}"), "Liquid must survive untouched");
    }

    [TestMethod]
    public async Task Run_copy_mode_still_rejects_titles_that_lose_the_brand()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("index.html", HomePage);

        var breaking = new FakeTranslator(item => item.Key == "title" ? "VPN gratuit" : $"[fr] {item.Text}");
        var options = CreateOptionsWithConfig(workspace, configPath: null, PageBodyMode.Copy);
        var runner = CreateRunner(options, breaking);

        Assert.AreEqual(ExitCodes.VerificationFailed, await runner.RunAsync());
        Assert.IsFalse(workspace.Exists("fr/index.html"));
    }

    [TestMethod]
    public async Task Run_copy_mode_writes_pages_without_metadata_without_calling_the_ai()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("index.html", "---\nlayout: none\n---\n<p>No title, no description.</p>");

        var translator = new FakeTranslator();
        var options = CreateOptionsWithConfig(workspace, configPath: null, PageBodyMode.Copy);

        Assert.AreEqual(ExitCodes.Success, await CreateRunner(options, translator).RunAsync());
        Assert.AreEqual(0, translator.CallCount);
        Assert.IsTrue(workspace.ReadFile("fr/index.html").Contains("<p>No title, no description.</p>"));
        Assert.IsTrue(workspace.ReadFile("fr/index.html").Contains("lang: fr"));
    }

    [TestMethod]
    public async Task Run_supports_a_collection_folder_output_pattern()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("index.html", HomePage);
        workspace.WriteFile("free-vpn/download/index.html", DownloadPage);

        var options = new SiteOptions {
            RootPath = workspace.Path,
            PagePatterns = ["**/index.html"],
            ExcludePatterns = ["_site/**", "vh_translator/**", "_langs/fr/**", "_langs/de/**"],
            Languages = ["fr", "de"],
            OutputPattern = "_langs/{lang}/{path}",
            DataFiles = [],
            SourceLanguage = "en",
            Engine = TranslationEngine.Gemini,
            Model = "test-model",
            BatchSize = 20,
            TitleMustContain = "VpnHood!",
            ApiKey = "unused",
            PageBodyMode = PageBodyMode.Copy
        };
        var runner = CreateRunner(options, new FakeTranslator());

        Assert.AreEqual(ExitCodes.Success, await runner.RunAsync());

        var page = workspace.ReadFile("_langs/fr/free-vpn/download/index.html");
        Assert.IsTrue(page.Contains("permalink: /fr/free-vpn/download/"),
            "The permalink must point at the root URL, not the _langs folder");
        Assert.IsTrue(page.Contains("lang: fr"));

        // Orphan pruning must follow the pattern into the collection folder.
        workspace.WriteFile("_langs/fr/removed/index.html", "---\nlang: fr\nauto_translated: true\n---\n<p>stale</p>");
        Assert.AreEqual(ExitCodes.Success, await runner.RunAsync());
        Assert.IsFalse(workspace.Exists("_langs/fr/removed/index.html"));
    }

    [TestMethod]
    public void Resolver_rejects_unknown_pageBody_values()
    {
        using var workspace = new TestWorkspace();
        var configPath = workspace.WriteFile("vhtranslator.json",
            """{ "site": { "languages": ["fr"], "pageBody": "verbatim" } }""");

        Assert.ThrowsExactly<TranslatorException>(() =>
            SiteOptionsResolver.Resolve(new Cli.CommandLineOptions { ConfigPath = configPath }));
    }

    [TestMethod]
    public async Task ShowChanges_reports_stale_pages_without_calling_the_ai()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("index.html", HomePage);

        var translator = new FakeTranslator();
        var runner = CreateRunner(CreateOptions(workspace), translator);

        Assert.AreEqual(ExitCodes.Success, await runner.ShowChangesAsync());
        Assert.AreEqual(0, translator.CallCount);
    }

    [TestMethod]
    public async Task RebuildWatchFile_marks_everything_current_without_calling_the_ai()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("index.html", HomePage);

        var translator = new FakeTranslator();
        var options = CreateOptions(workspace);
        var runner = CreateRunner(options, translator);

        Assert.AreEqual(ExitCodes.Success, await runner.RebuildWatchFileAsync());
        Assert.AreEqual(0, translator.CallCount);

        // After adopting, a run only fills in the still-missing locale files.
        workspace.WriteFile("fr/index.html", "---\nauto_translated: true\n---\nexisting");
        await runner.RunAsync();
        Assert.IsTrue(workspace.ReadFile("fr/index.html").Contains("existing"),
            "Marked-current pages with an existing target must not be retranslated");
        Assert.IsTrue(workspace.Exists("de/index.html"), "Missing targets are still created");
    }
}
