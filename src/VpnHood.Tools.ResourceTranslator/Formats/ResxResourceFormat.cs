using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace VpnHood.Tools.ResourceTranslator.Formats;

/// <summary>
/// Reads and writes Microsoft .resx resource files. String entries are translated;
/// non-string entries (images, files, typed objects) and metadata are preserved as-is.
/// Follows the standard naming convention: a neutral base file (e.g. Strings.resx) with
/// culture-specific siblings (e.g. Strings.fr.resx).
/// </summary>
public class ResxResourceFormat : IResourceFormat
{
    // Canonical resx preamble (schema + resheaders) used when creating a new file from scratch.
    private const string EmptyResxTemplate =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <root>
          <xsd:schema id="root" xmlns="" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:msdata="urn:schemas-microsoft-com:xml-msdata">
            <xsd:import namespace="http://www.w3.org/XML/1998/namespace" />
            <xsd:element name="root" msdata:IsDataSet="true">
              <xsd:complexType>
                <xsd:choice maxOccurs="unbounded">
                  <xsd:element name="metadata">
                    <xsd:complexType>
                      <xsd:sequence>
                        <xsd:element name="value" type="xsd:string" minOccurs="0" />
                      </xsd:sequence>
                      <xsd:attribute name="name" use="required" type="xsd:string" />
                      <xsd:attribute name="type" type="xsd:string" />
                      <xsd:attribute name="mimetype" type="xsd:string" />
                      <xsd:attribute ref="xml:space" />
                    </xsd:complexType>
                  </xsd:element>
                  <xsd:element name="assembly">
                    <xsd:complexType>
                      <xsd:attribute name="alias" type="xsd:string" />
                      <xsd:attribute name="name" type="xsd:string" />
                    </xsd:complexType>
                  </xsd:element>
                  <xsd:element name="data">
                    <xsd:complexType>
                      <xsd:sequence>
                        <xsd:element name="value" type="xsd:string" minOccurs="0" msdata:Ordinal="1" />
                        <xsd:element name="comment" type="xsd:string" minOccurs="0" msdata:Ordinal="2" />
                      </xsd:sequence>
                      <xsd:attribute name="name" type="xsd:string" use="required" msdata:Ordinal="1" />
                      <xsd:attribute name="type" type="xsd:string" msdata:Ordinal="3" />
                      <xsd:attribute name="mimetype" type="xsd:string" msdata:Ordinal="4" />
                      <xsd:attribute ref="xml:space" />
                    </xsd:complexType>
                  </xsd:element>
                  <xsd:element name="resheader">
                    <xsd:complexType>
                      <xsd:sequence>
                        <xsd:element name="value" type="xsd:string" minOccurs="0" msdata:Ordinal="1" />
                      </xsd:sequence>
                      <xsd:attribute name="name" type="xsd:string" use="required" />
                    </xsd:complexType>
                  </xsd:element>
                </xsd:choice>
              </xsd:complexType>
            </xsd:element>
          </xsd:schema>
          <resheader name="resmimetype">
            <value>text/microsoft-resx</value>
          </resheader>
          <resheader name="version">
            <value>2.0</value>
          </resheader>
          <resheader name="reader">
            <value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
          </resheader>
          <resheader name="writer">
            <value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
          </resheader>
        </root>
        """;

    public bool TryLoad(string path, [NotNullWhen(true)] out List<KeyValuePair<string, string>>? entries, out string? error)
    {
        try {
            var doc = XDocument.Load(path);
            entries = doc.Root!
                .Elements("data")
                .Where(IsStringData)
                .Select(d => new KeyValuePair<string, string>(
                    (string)d.Attribute("name")!,
                    d.Element("value")?.Value ?? string.Empty))
                .ToList();
            error = null;
            return true;
        }
        catch (Exception ex) {
            entries = null;
            error = ex.Message;
            return false;
        }
    }

    public async Task SaveAsync(string path, IReadOnlyList<string> orderedKeys, IReadOnlyDictionary<string, string> map)
    {
        // Load the existing document (to preserve schema, metadata, comments and non-string data)
        // or start from the canonical template.
        XDocument doc;
        if (File.Exists(path)) {
            try {
                doc = XDocument.Load(path);
            }
            catch {
                doc = XDocument.Parse(EmptyResxTemplate);
            }
        }
        else {
            doc = XDocument.Parse(EmptyResxTemplate);
        }

        var root = doc.Root!;

        // Index existing string <data> nodes by name so we can reuse comments and attributes.
        var existingStringData = root.Elements("data")
            .Where(IsStringData)
            .GroupBy(d => (string)d.Attribute("name")!)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        // Preserve everything that is not a string <data> node (schema, resheader, metadata,
        // assembly, and typed/binary data) in its original relative order.
        var preservedNodes = root.Elements()
            .Where(e => e.Name.LocalName != "data" || !IsStringData(e))
            .ToList();

        root.RemoveNodes();
        foreach (var node in preservedNodes)
            root.Add(node);

        foreach (var key in orderedKeys) {
            var value = map.GetValueOrDefault(key, string.Empty);
            if (existingStringData.TryGetValue(key, out var existing)) {
                existing.SetAttributeValue(XNamespace.Xml + "space", "preserve");
                var valueElement = existing.Element("value");
                if (valueElement == null) {
                    valueElement = new XElement("value");
                    existing.AddFirst(valueElement);
                }
                valueElement.Value = value;
                root.Add(existing);
            }
            else {
                root.Add(new XElement("data",
                    new XAttribute("name", key),
                    new XAttribute(XNamespace.Xml + "space", "preserve"),
                    new XElement("value", value)));
            }
        }

        var settings = new XmlWriterSettings {
            Indent = true,
            IndentChars = "  ",
            Encoding = new UTF8Encoding(false),
            Async = true
        };

        await using var fs = File.Create(path);
        await using var writer = XmlWriter.Create(fs, settings);
        await doc.SaveAsync(writer, CancellationToken.None);
    }

    public string GetLanguageCode(string path)
    {
        // Culture comes from the filename segment before .resx (e.g. Strings.fr.resx -> "fr").
        // A neutral file (e.g. Strings.resx) has no culture; treat it as the default source "en".
        return TryGetCulture(path, out var culture) ? culture : "en";
    }

    public IEnumerable<string> FindSiblingLocaleFiles(string basePath)
    {
        var fullBase = Path.GetFullPath(basePath);
        var dir = Path.GetDirectoryName(fullBase)!;
        var baseName = GetResourceBaseName(fullBase);
        var fullBaseName = Path.GetFileName(fullBase);

        return Directory.EnumerateFiles(dir, $"{baseName}*.resx", SearchOption.TopDirectoryOnly)
            .Where(p => GetResourceBaseName(p).Equals(baseName, StringComparison.OrdinalIgnoreCase))
            .Where(p => !Path.GetFileName(p).Equals(fullBaseName, StringComparison.OrdinalIgnoreCase));
    }

    public string GetLocaleFilePath(string basePath, string languageCode)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(basePath))!;
        var baseName = GetResourceBaseName(basePath);
        return Path.Combine(dir, $"{baseName}.{languageCode}.resx");
    }

    private static bool IsStringData(XElement data)
    {
        // String entries carry neither a 'type' nor a 'mimetype' attribute.
        return data.Attribute("type") == null && data.Attribute("mimetype") == null;
    }

    /// <summary>
    /// Returns the resource base name without culture or extension.
    /// "Strings.resx" -> "Strings"; "Strings.fr.resx" -> "Strings"; "My.App.de-DE.resx" -> "My.App".
    /// </summary>
    private static string GetResourceBaseName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path); // strips ".resx"
        var lastDot = name.LastIndexOf('.');
        if (lastDot > 0 && IsValidCulture(name[(lastDot + 1)..]))
            return name[..lastDot];
        return name;
    }

    private static bool TryGetCulture(string path, out string culture)
    {
        var name = Path.GetFileNameWithoutExtension(path); // strips ".resx"
        var lastDot = name.LastIndexOf('.');
        if (lastDot > 0) {
            var candidate = name[(lastDot + 1)..];
            if (IsValidCulture(candidate)) {
                culture = candidate;
                return true;
            }
        }
        culture = string.Empty;
        return false;
    }

    // Windows (NLS) throws for an unknown culture name, but Linux and macOS (ICU) happily
    // fabricate one, which would read "My.Resources.resx" as culture "Resources". Matching
    // against the platform's known cultures behaves identically everywhere.
    private static readonly HashSet<string> KnownCultureNames = CultureInfo
        .GetCultures(CultureTypes.AllCultures)
        .Select(culture => culture.Name)
        .Where(name => !string.IsNullOrEmpty(name))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool IsValidCulture(string candidate)
    {
        // Rejects e.g. "Resources" and "App"; accepts "en", "fr", "de-DE", "zh-Hans".
        return !string.IsNullOrEmpty(candidate) && KnownCultureNames.Contains(candidate);
    }
}
