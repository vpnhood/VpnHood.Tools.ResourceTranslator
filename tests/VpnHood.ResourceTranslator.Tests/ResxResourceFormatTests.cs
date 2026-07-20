using System.Xml.Linq;
using VpnHood.ResourceTranslator.Formats;

namespace VpnHood.ResourceTranslator.Tests;

[TestClass]
public sealed class ResxResourceFormatTests
{
    private const string SampleResx =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <root>
          <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>
          <resheader name="version"><value>2.0</value></resheader>
          <data name="Welcome" xml:space="preserve">
            <value>Welcome, {name}!</value>
            <comment>shown on home</comment>
          </data>
          <data name="Save" xml:space="preserve">
            <value>Save</value>
          </data>
          <data name="Logo" type="System.Resources.ResXFileRef, System.Windows.Forms">
            <value>logo.png;System.Drawing.Bitmap</value>
          </data>
        </root>
        """;

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vhresx_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [TestMethod]
    public void TryLoad_ReturnsOnlyStringEntriesInOrder()
    {
        var dir = CreateTempDir();
        var path = Path.Combine(dir, "Strings.resx");
        File.WriteAllText(path, SampleResx);

        var format = new ResxResourceFormat();
        var ok = format.TryLoad(path, out var entries, out var error);

        Assert.IsTrue(ok, error);
        Assert.IsNotNull(entries);
        // The typed "Logo" file-ref entry must be excluded.
        CollectionAssert.AreEqual(new[] { "Welcome", "Save" }, entries.Select(e => e.Key).ToArray());
        Assert.AreEqual("Welcome, {name}!", entries[0].Value);
        Assert.AreEqual("Save", entries[1].Value);
    }

    [TestMethod]
    public void GetLanguageCode_NeutralFileDefaultsToEn_CultureFileReturnsCulture()
    {
        var format = new ResxResourceFormat();
        Assert.AreEqual("en", format.GetLanguageCode("/x/Strings.resx"));
        Assert.AreEqual("fr", format.GetLanguageCode("/x/Strings.fr.resx"));
        Assert.AreEqual("de-DE", format.GetLanguageCode("/x/My.App.de-DE.resx"));
        // A dotted resource name whose last segment is not a culture stays neutral.
        Assert.AreEqual("en", format.GetLanguageCode("/x/My.Resources.resx"));
    }

    [TestMethod]
    public void GetLocaleFilePath_BuildsCultureSpecificName()
    {
        var format = new ResxResourceFormat();
        var basePath = Path.Combine(CreateTempDir(), "Strings.resx");
        var result = format.GetLocaleFilePath(basePath, "es");
        Assert.AreEqual("Strings.es.resx", Path.GetFileName(result));
    }

    [TestMethod]
    public void FindSiblingLocaleFiles_FindsCulturesAndExcludesBase()
    {
        var dir = CreateTempDir();
        File.WriteAllText(Path.Combine(dir, "Strings.resx"), SampleResx);
        File.WriteAllText(Path.Combine(dir, "Strings.fr.resx"), SampleResx);
        File.WriteAllText(Path.Combine(dir, "Strings.de.resx"), SampleResx);
        // Different resource base; must be ignored.
        File.WriteAllText(Path.Combine(dir, "Other.fr.resx"), SampleResx);

        var format = new ResxResourceFormat();
        var siblings = format.FindSiblingLocaleFiles(Path.Combine(dir, "Strings.resx"))
            .Select(Path.GetFileName)
            .OrderBy(x => x)
            .ToArray();

        CollectionAssert.AreEqual(new[] { "Strings.de.resx", "Strings.fr.resx" }, siblings);
    }

    [TestMethod]
    public async Task SaveAsync_RoundTripsPreservesNonStringDataAndComments()
    {
        var dir = CreateTempDir();
        var basePath = Path.Combine(dir, "Strings.resx");
        File.WriteAllText(basePath, SampleResx);

        var format = new ResxResourceFormat();
        var targetPath = Path.Combine(dir, "Strings.fr.resx");

        // Seed the target with the sample (so the file-ref and comment exist) then translate values.
        File.WriteAllText(targetPath, SampleResx);
        var orderedKeys = new List<string> { "Welcome", "Save" };
        var map = new Dictionary<string, string> {
            ["Welcome"] = "Bienvenue, {name} !",
            ["Save"] = "Enregistrer"
        };

        await format.SaveAsync(targetPath, orderedKeys, map);

        // Reload via the format: string values updated.
        Assert.IsTrue(format.TryLoad(targetPath, out var entries, out var err), err);
        var loaded = entries!.ToDictionary(e => e.Key, e => e.Value);
        Assert.AreEqual("Bienvenue, {name} !", loaded["Welcome"]);
        Assert.AreEqual("Enregistrer", loaded["Save"]);

        // Inspect the raw XML: non-string data preserved, comment preserved, valid resx headers intact.
        var doc = XDocument.Load(targetPath);
        var logo = doc.Root!.Elements("data").FirstOrDefault(d => (string?)d.Attribute("name") == "Logo");
        Assert.IsNotNull(logo, "Typed file-ref entry should be preserved.");
        Assert.AreEqual("System.Resources.ResXFileRef, System.Windows.Forms", (string?)logo!.Attribute("type"));

        var welcome = doc.Root!.Elements("data").First(d => (string?)d.Attribute("name") == "Welcome");
        Assert.AreEqual("shown on home", welcome.Element("comment")?.Value);

        Assert.IsTrue(doc.Root!.Elements("resheader").Any(r => (string?)r.Attribute("name") == "resmimetype"));
    }

    [TestMethod]
    public async Task SaveAsync_CreatesValidResxWhenTargetMissing()
    {
        var dir = CreateTempDir();
        var format = new ResxResourceFormat();
        var targetPath = Path.Combine(dir, "Strings.it.resx");

        await format.SaveAsync(targetPath,
            new List<string> { "Save" },
            new Dictionary<string, string> { ["Save"] = "Salva" });

        Assert.IsTrue(File.Exists(targetPath));
        Assert.IsTrue(format.TryLoad(targetPath, out var entries, out var err), err);
        Assert.AreEqual("Salva", entries!.Single(e => e.Key == "Save").Value);

        // Canonical headers are written so the file is consumable by ResX tooling.
        var doc = XDocument.Load(targetPath);
        Assert.IsTrue(doc.Root!.Elements("resheader").Any(r => (string?)r.Attribute("name") == "resmimetype"));
        Assert.IsTrue(doc.Root!.Elements().Any(e => e.Name.LocalName == "schema"));
    }
}
