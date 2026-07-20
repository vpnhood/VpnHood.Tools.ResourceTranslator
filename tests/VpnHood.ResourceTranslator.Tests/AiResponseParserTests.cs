using VpnHood.ResourceTranslator.Translation;

namespace VpnHood.ResourceTranslator.Tests;

[TestClass]
public sealed class AiResponseParserTests
{
    private const string SingleItemJson =
        """
        {
            "SourceText": "Hello",
            "TranslatedText": "Bonjour",
            "SourceLanguage": "en",
            "TargetLanguage": "fr",
            "Key": "GREETING"
        }
        """;

    private static string ArrayJson => $"[{SingleItemJson}]";

    [TestMethod]
    public void ParseResponse_ParsesDirectArray()
    {
        var results = AiResponseParser.ParseResponse(ArrayJson);

        Assert.AreEqual(1, results.Length);
        Assert.AreEqual("GREETING", results[0].Key);
        Assert.AreEqual("Bonjour", results[0].TranslatedText);
    }

    [TestMethod]
    public void ParseResponse_ParsesSingleObject()
    {
        var results = AiResponseParser.ParseResponse(SingleItemJson);

        Assert.AreEqual(1, results.Length);
        Assert.AreEqual("GREETING", results[0].Key);
    }

    [TestMethod]
    public void ParseResponse_StripsJsonMarkdownFence()
    {
        var results = AiResponseParser.ParseResponse($"```json\n{ArrayJson}\n```");

        Assert.AreEqual(1, results.Length);
        Assert.AreEqual("Bonjour", results[0].TranslatedText);
    }

    [TestMethod]
    public void ParseResponse_StripsPlainMarkdownFence()
    {
        var results = AiResponseParser.ParseResponse($"```\n{ArrayJson}\n```");

        Assert.AreEqual(1, results.Length);
        Assert.AreEqual("Bonjour", results[0].TranslatedText);
    }

    [TestMethod]
    public void ParseResponse_UnwrapsCommonWrapperObjects()
    {
        foreach (var wrapper in new[] { "result", "results", "translations", "data" }) {
            var results = AiResponseParser.ParseResponse($"{{ \"{wrapper}\": {ArrayJson} }}");

            Assert.AreEqual(1, results.Length, $"Failed for wrapper '{wrapper}'");
            Assert.AreEqual("GREETING", results[0].Key, $"Failed for wrapper '{wrapper}'");
        }
    }

    [TestMethod]
    public void ParseResponse_IsCaseInsensitiveForPropertyNames()
    {
        const string camelCaseJson =
            """
            [{
                "sourceText": "Hello",
                "translatedText": "Bonjour",
                "sourceLanguage": "en",
                "targetLanguage": "fr",
                "key": "GREETING"
            }]
            """;

        var results = AiResponseParser.ParseResponse(camelCaseJson);

        Assert.AreEqual(1, results.Length);
        Assert.AreEqual("Bonjour", results[0].TranslatedText);
    }

    [TestMethod]
    public void ParseResponse_ThrowsOnInvalidJson()
    {
        Assert.ThrowsExactly<Exception>(() => AiResponseParser.ParseResponse("this is not json"));
    }

    [TestMethod]
    public void ParseResponse_ThrowsOnEmptyArray()
    {
        Assert.ThrowsExactly<Exception>(() => AiResponseParser.ParseResponse("[]"));
    }

    [TestMethod]
    public void ParseResponse_ThrowsOnUnknownStructure()
    {
        Assert.ThrowsExactly<Exception>(() => AiResponseParser.ParseResponse("123"));
    }
}
