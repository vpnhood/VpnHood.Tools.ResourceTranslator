using VpnHood.Tools.ResourceTranslator.Configuration;

namespace VpnHood.Tools.ResourceTranslator.Tests;

[TestClass]
public class ConfigDiscoveryTests
{
    [TestMethod]
    public void Discover_finds_a_config_at_the_root()
    {
        using var workspace = new TestWorkspace();
        var path = workspace.WriteFile("vhtranslator.json", """{ "model": "m" }""");

        var config = TranslatorConfig.Discover(workspace.Path);

        Assert.AreEqual(path, config.SourcePath);
        Assert.AreEqual(workspace.Path, config.BaseDirectory);
    }

    [TestMethod]
    public void Discover_finds_a_config_inside_the_vh_translator_folder()
    {
        using var workspace = new TestWorkspace();
        var path = workspace.WriteFile("vh_translator/vhtranslator.json", """{ "model": "m" }""");

        var config = TranslatorConfig.Discover(workspace.Path);

        Assert.AreEqual(Path.GetFullPath(path), Path.GetFullPath(config.SourcePath!));
        Assert.AreEqual(workspace.Path, config.BaseDirectory,
            "Paths must resolve against the site root, not the vh_translator folder");
    }

    [TestMethod]
    public void Discover_prefers_the_root_config_over_the_nested_one()
    {
        using var workspace = new TestWorkspace();
        var rootConfig = workspace.WriteFile("vhtranslator.json", """{ "model": "root" }""");
        workspace.WriteFile("vh_translator/vhtranslator.json", """{ "model": "nested" }""");

        var config = TranslatorConfig.Discover(workspace.Path);

        Assert.AreEqual(rootConfig, config.SourcePath);
        Assert.AreEqual("root", config.Model);
    }

    [TestMethod]
    public void Load_remaps_the_base_directory_for_nested_configs_too()
    {
        using var workspace = new TestWorkspace();
        var path = workspace.WriteFile("vh_translator/vhtranslator.json", """{ "base": "locales/en.json" }""");

        var config = TranslatorConfig.Load(path);

        Assert.AreEqual(Path.Combine(workspace.Path, "locales", "en.json"), config.ResolvePath(config.Base));
    }
}
