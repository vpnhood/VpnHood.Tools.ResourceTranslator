using VpnHood.Tools.ResourceTranslator.Cli;
using VpnHood.Tools.ResourceTranslator.Docs;

namespace VpnHood.Tools.ResourceTranslator.Tests.Docs;

[TestClass]
public class DocsOptionsResolverTests
{
    private static string WriteConfig(TestWorkspace workspace, string docsJson)
    {
        return workspace.WriteFile("vhtranslator.json", $$"""{ "docs": {{docsJson}} }""");
    }

    [TestMethod]
    public void Defaults_derive_from_the_source_folder()
    {
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "posts", "en"));
        var configPath = WriteConfig(workspace, """{ "source": "posts/en", "languages": ["fr", "de"] }""");

        var options = DocsOptionsResolver.Resolve(new CommandLineOptions { ConfigPath = configPath });

        Assert.AreEqual(Path.Combine(workspace.Path, "posts", "en"), options.SourceRoot);
        Assert.AreEqual("posts/{lang}/{path}", options.OutputPattern);
        Assert.AreEqual("en", options.SourceLanguage, "the source folder's leaf name is the language");
        CollectionAssert.AreEqual(new[] { "**/*.md" }, options.FilePatterns.ToArray());
        CollectionAssert.AreEqual(new[] { "title", "description", "image_alt" }, options.FrontMatterKeys.ToArray());
        Assert.AreEqual(10_000, options.ChunkChars);
        Assert.AreEqual(1, options.MaskPatterns.Count);
        Assert.AreEqual(Path.Combine(workspace.Path, "posts", "fr", "hello.md"),
            options.GetTargetFullPath("hello.md", "fr"));
    }

    [TestMethod]
    public void A_language_that_would_overwrite_the_source_is_refused()
    {
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "posts", "en"));
        var configPath = WriteConfig(workspace, """{ "source": "posts/en", "languages": ["fr", "en"] }""");

        var ex = Assert.ThrowsExactly<TranslatorException>(() =>
            DocsOptionsResolver.Resolve(new CommandLineOptions { ConfigPath = configPath }));
        Assert.IsTrue(ex.Message.Contains("source folder itself"));
    }

    [TestMethod]
    public void A_missing_source_folder_is_refused()
    {
        using var workspace = new TestWorkspace();
        var configPath = WriteConfig(workspace, """{ "source": "posts/en", "languages": ["fr"] }""");

        Assert.ThrowsExactly<TranslatorException>(() =>
            DocsOptionsResolver.Resolve(new CommandLineOptions { ConfigPath = configPath }));
    }

    [TestMethod]
    public void An_invalid_mask_pattern_is_refused()
    {
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "posts", "en"));
        var configPath = WriteConfig(workspace,
            """{ "source": "posts/en", "languages": ["fr"], "maskPatterns": ["("] }""");

        var ex = Assert.ThrowsExactly<TranslatorException>(() =>
            DocsOptionsResolver.Resolve(new CommandLineOptions { ConfigPath = configPath }));
        Assert.IsTrue(ex.Message.Contains("maskPatterns"));
    }

    [TestMethod]
    public void A_config_without_a_docs_section_is_refused()
    {
        using var workspace = new TestWorkspace();
        var configPath = workspace.WriteFile("vhtranslator.json", """{ "base": "en.json" }""");

        var ex = Assert.ThrowsExactly<TranslatorException>(() =>
            DocsOptionsResolver.Resolve(new CommandLineOptions { ConfigPath = configPath }));
        Assert.IsTrue(ex.Message.Contains("docs"));
    }
}
