using System.Text.RegularExpressions;
using VpnHood.Tools.ResourceTranslator.Configuration;
using VpnHood.Tools.ResourceTranslator.Docs;
using VpnHood.Tools.ResourceTranslator.Translation;
// ReSharper disable StringLiteralTypo

namespace VpnHood.Tools.ResourceTranslator.Tests.Docs;

[TestClass]
public class DocsTranslationRunnerTests
{
    /// <summary>Blog-shaped fixture: full front matter, fenced code with a blank line inside,
    /// inline code, a link, and a runtime placeholder.</summary>
    private const string BlogPost =
        """
        ---
        title: "What is split tunneling?"
        description: "Split tunneling and your privacy."
        date: 2026-08-10
        tags: [privacy, android]
        image: /assets/images/blog/split.webp
        image_alt: "A phone splitting its traffic"
        ---

        Split tunneling gives you privacy control in {appName}.

        ```csharp
        var tunnel = new Tunnel();

        tunnel.Start();
        ```

        Read the [guide](https://www.vpnhood.com/guide) or run `vhtranslator docs` for privacy.
        """;

    /// <summary>Structure-preserving fake translation: rewrites one word instead of prefixing,
    /// so markdown structure survives and the structural verifier stays honest.</summary>
    private static string Translate(TranslateItem item)
    {
        return item.Key.StartsWith("body", StringComparison.Ordinal)
            ? item.Text.Replace("privacy", $"privacy_{item.TargetLanguage}", StringComparison.Ordinal)
            : $"[{item.TargetLanguage}] {item.Text}";
    }

    private static DocsOptions CreateOptions(TestWorkspace workspace, params string[] languages)
    {
        return new DocsOptions {
            RootPath = workspace.Path,
            SourceRoot = Path.Combine(workspace.Path, "posts", "en"),
            FilePatterns = ["**/*.md"],
            ExcludePatterns = [],
            Languages = languages.Length > 0 ? languages : ["fr", "de"],
            OutputPattern = "posts/{lang}/{path}",
            SourceLanguage = "en",
            FrontMatterKeys = ["title", "description", "image_alt"],
            ChunkChars = 10_000,
            MaskPatterns = [new Regex(@"\{[A-Za-z0-9_]+\}")],
            Engine = TranslationEngine.Gemini,
            Model = "test-model",
            ApiKey = "unused",
            ExtraPrompt = ExtraPromptStore.Empty
        };
    }

    private static DocsTranslationRunner CreateRunner(DocsOptions options, ITranslator translator)
    {
        return new DocsTranslationRunner(options, translatorFactory: () => translator) {
            PacingDelay = TimeSpan.Zero
        };
    }

    [TestMethod]
    public async Task Run_creates_verified_language_folders()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("posts/en/split-tunneling.md", BlogPost);

        var translator = new FakeTranslator(Translate);
        var runner = CreateRunner(CreateOptions(workspace), translator);

        Assert.AreEqual(ExitCodes.Success, await runner.RunAsync());

        foreach (var path in new[] { "posts/fr/split-tunneling.md", "posts/de/split-tunneling.md" })
            Assert.IsTrue(workspace.Exists(path), $"{path} must be generated");

        // The fixture's own line endings follow this source file's checkout; normalize so the
        // newline-sensitive assertions below hold on both LF and CRLF checkouts.
        var french = workspace.ReadFile("posts/fr/split-tunneling.md").Replace("\r\n", "\n");
        Assert.IsTrue(french.Contains("title: \"[fr] What is split tunneling?\""));
        Assert.IsTrue(french.Contains("image_alt: \"[fr] A phone splitting its traffic\""));
        Assert.IsTrue(french.Contains("date: 2026-08-10"), "untranslated front matter is copied verbatim");
        Assert.IsTrue(french.Contains("tags: [privacy, android]"), "tags stay untouched even when a word matches");
        Assert.IsTrue(french.Contains("lang: fr"));
        Assert.IsTrue(french.Contains("auto_translated: true"));

        Assert.IsTrue(french.Contains("privacy_fr control"), "body text is translated");
        Assert.IsTrue(french.Contains("var tunnel = new Tunnel();\n\ntunnel.Start();"),
            "fenced code survives byte-identically, blank line included");
        Assert.IsTrue(french.Contains("{appName}"), "the masked placeholder is restored, not translated");
        Assert.IsTrue(french.Contains("`vhtranslator docs`"), "inline code survives");
        Assert.IsTrue(french.Contains("https://www.vpnhood.com/guide"), "link urls survive");
    }

    [TestMethod]
    public async Task Second_run_translates_nothing()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("posts/en/split-tunneling.md", BlogPost);

        var translator = new FakeTranslator(Translate);
        await CreateRunner(CreateOptions(workspace), translator).RunAsync();
        var callsAfterFirstRun = translator.CallCount;

        await CreateRunner(CreateOptions(workspace), translator).RunAsync();

        Assert.AreEqual(callsAfterFirstRun, translator.CallCount, "an unchanged document must not be retranslated");
    }

    [TestMethod]
    public async Task A_changed_source_or_missing_target_is_retranslated()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("posts/en/split-tunneling.md", BlogPost);
        workspace.WriteFile("posts/en/other.md", "---\ntitle: Other\n---\nSome privacy text.\n");

        var translator = new FakeTranslator(Translate);
        await CreateRunner(CreateOptions(workspace), translator).RunAsync();

        // A deleted target file must be refilled even though the source hash is current.
        File.Delete(Path.Combine(workspace.Path, "posts/fr/other.md"));
        // A changed source must retranslate everywhere.
        workspace.WriteFile("posts/en/split-tunneling.md", BlogPost.Replace("gives you", "hands you"));

        var secondTranslator = new FakeTranslator(Translate);
        await CreateRunner(CreateOptions(workspace), secondTranslator).RunAsync();

        Assert.IsTrue(workspace.Exists("posts/fr/other.md"), "missing target must be refilled");
        Assert.IsTrue(workspace.ReadFile("posts/fr/split-tunneling.md").Contains("hands you"));
        // other.md needed fr only; split-tunneling.md needed fr and de.
        Assert.AreEqual(3, secondTranslator.CallCount);
    }

    [TestMethod]
    public async Task Opt_out_and_hand_authored_targets_are_respected()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("posts/en/split-tunneling.md", BlogPost);
        workspace.WriteFile("posts/en/draft.md", "---\ntranslate: false\n---\nNot ready.\n");
        workspace.WriteFile("posts/fr/split-tunneling.md", "A human wrote this French page by hand.\n");

        var translator = new FakeTranslator(Translate);
        Assert.AreEqual(ExitCodes.Success, await CreateRunner(CreateOptions(workspace), translator).RunAsync());

        Assert.IsFalse(workspace.Exists("posts/fr/draft.md"), "translate: false must be honored");
        Assert.AreEqual("A human wrote this French page by hand.\n", workspace.ReadFile("posts/fr/split-tunneling.md"),
            "a target without the marker is hand-authored and must never be clobbered");
        Assert.IsTrue(workspace.Exists("posts/de/split-tunneling.md"), "other languages still translate");
    }

    [TestMethod]
    public async Task A_deleted_source_prunes_its_generated_targets()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("posts/en/split-tunneling.md", BlogPost);
        workspace.WriteFile("posts/en/old.md", "---\ntitle: Old\n---\nOld privacy content.\n");

        var translator = new FakeTranslator(Translate);
        await CreateRunner(CreateOptions(workspace), translator).RunAsync();
        Assert.IsTrue(workspace.Exists("posts/fr/old.md"));

        File.Delete(Path.Combine(workspace.Path, "posts/en/old.md"));
        await CreateRunner(CreateOptions(workspace), new FakeTranslator(Translate)).RunAsync();

        Assert.IsFalse(workspace.Exists("posts/fr/old.md"), "generated copies of a deleted source must be pruned");
        Assert.IsFalse(workspace.Exists("posts/de/old.md"));
        Assert.IsTrue(workspace.Exists("posts/fr/split-tunneling.md"), "other documents stay");
    }

    [TestMethod]
    public async Task A_structure_breaking_translation_is_never_written()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("posts/en/split-tunneling.md", BlogPost);

        // Drops the heading structure and the tokens: verification must reject every attempt.
        var translator = new FakeTranslator(item =>
            item.Key.StartsWith("body", StringComparison.Ordinal) ? "Mangled." : item.Text);
        var runner = CreateRunner(CreateOptions(workspace, "fr"), translator);

        Assert.AreEqual(ExitCodes.VerificationFailed, await runner.RunAsync());
        Assert.IsFalse(workspace.Exists("posts/fr/split-tunneling.md"), "a failed translation must not be written");

        // The watch must NOT record success: a later run with a healthy translator recovers.
        var recovered = new FakeTranslator(Translate);
        Assert.AreEqual(ExitCodes.Success, await CreateRunner(CreateOptions(workspace, "fr"), recovered).RunAsync());
        Assert.IsTrue(workspace.Exists("posts/fr/split-tunneling.md"));
    }

    [TestMethod]
    public async Task A_long_body_is_chunked_and_reassembled()
    {
        using var workspace = new TestWorkspace();
        var paragraphs = Enumerable.Range(0, 8).Select(i => $"Paragraph {i} about privacy " + new string('x', 400));
        workspace.WriteFile("posts/en/long.md", "---\ntitle: Long\n---\n" + string.Join("\n\n", paragraphs) + "\n");

        var translator = new FakeTranslator(Translate);
        var options = CreateOptions(workspace, "fr");
        var chunkedOptions = new DocsOptions {
            RootPath = options.RootPath,
            SourceRoot = options.SourceRoot,
            FilePatterns = options.FilePatterns,
            ExcludePatterns = options.ExcludePatterns,
            Languages = options.Languages,
            OutputPattern = options.OutputPattern,
            SourceLanguage = options.SourceLanguage,
            FrontMatterKeys = options.FrontMatterKeys,
            ChunkChars = 1000,
            MaskPatterns = options.MaskPatterns,
            Engine = options.Engine,
            Model = options.Model,
            ApiKey = options.ApiKey,
            ExtraPrompt = options.ExtraPrompt
        };

        Assert.AreEqual(ExitCodes.Success, await CreateRunner(chunkedOptions, translator).RunAsync());

        Assert.IsTrue(translator.TranslatedKeys.Count(key => key.StartsWith("body[")) >= 3,
            "a long body must be split into several parts");

        var french = workspace.ReadFile("posts/fr/long.md");
        foreach (var i in Enumerable.Range(0, 8))
            Assert.IsTrue(french.Contains($"Paragraph {i} about privacy_fr"), $"paragraph {i} must survive chunking");
    }

    [TestMethod]
    public async Task The_per_file_prompt_reaches_the_model()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("posts/en/legal.md",
            """
            ---
            title: Privacy Policy
            translate_prompt: "Legal disclosure. Translate literally and completely; never shorten."
            ---
            We keep privacy logs for 30 days.
            """);

        var translator = new FakeTranslator(Translate);
        await CreateRunner(CreateOptions(workspace, "fr"), translator).RunAsync();

        Assert.IsTrue(translator.PromptsByLanguage["fr"].Contains("Legal disclosure. Translate literally"),
            "the file's own translate_prompt must be appended to the prompt");
    }

    [TestMethod]
    public async Task WebUi_layout_maps_into_the_content_tree()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("src/content/en/privacy-consent.md", "---\ntitle: Privacy\n---\nWe respect privacy.\n");

        var options = new DocsOptions {
            RootPath = workspace.Path,
            SourceRoot = Path.Combine(workspace.Path, "src", "content", "en"),
            FilePatterns = ["**/*.md"],
            ExcludePatterns = [],
            Languages = ["fa"],
            OutputPattern = "src/content/{lang}/{path}",
            SourceLanguage = "en",
            FrontMatterKeys = ["title"],
            ChunkChars = 10_000,
            MaskPatterns = [],
            Engine = TranslationEngine.Gemini,
            Model = "test-model",
            ApiKey = "unused",
            ExtraPrompt = ExtraPromptStore.Empty
        };

        Assert.AreEqual(ExitCodes.Success, await CreateRunner(options, new FakeTranslator(Translate)).RunAsync());
        var persian = workspace.ReadFile("src/content/fa/privacy-consent.md");
        Assert.IsTrue(persian.Contains("privacy_fa"));
        Assert.IsTrue(persian.Contains("lang: fa"));
    }

    [TestMethod]
    public async Task Generated_files_are_never_rediscovered_as_sources()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("posts/en/split-tunneling.md", BlogPost);

        var translator = new FakeTranslator(Translate);
        await CreateRunner(CreateOptions(workspace), translator).RunAsync();

        // Point a second run's SOURCE at a generated tree: everything must be skipped.
        var options = CreateOptions(workspace, "de");
        var wrongOptions = new DocsOptions {
            RootPath = options.RootPath,
            SourceRoot = Path.Combine(workspace.Path, "posts", "fr"),
            FilePatterns = options.FilePatterns,
            ExcludePatterns = options.ExcludePatterns,
            Languages = options.Languages,
            OutputPattern = options.OutputPattern,
            SourceLanguage = "fr",
            FrontMatterKeys = options.FrontMatterKeys,
            ChunkChars = options.ChunkChars,
            MaskPatterns = options.MaskPatterns,
            Engine = options.Engine,
            Model = options.Model,
            ApiKey = options.ApiKey,
            ExtraPrompt = options.ExtraPrompt
        };

        var secondTranslator = new FakeTranslator(Translate);
        await CreateRunner(wrongOptions, secondTranslator).RunAsync();
        Assert.AreEqual(0, secondTranslator.CallCount, "auto_translated files must never be treated as sources");
    }
}
