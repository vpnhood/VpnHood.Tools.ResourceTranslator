using VpnHood.Tools.ResourceTranslator.Translation;

namespace VpnHood.Tools.ResourceTranslator.Tests;

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

    [TestMethod]
    public void ParseResponse_AcceptsMinimalObject_WithoutEchoedSourceOrLanguages()
    {
        // The prompt asks for Key + TranslatedText only; echoing the source back was ~40% of
        // every response in output tokens, for values the caller already has.
        var results = AiResponseParser.ParseResponse(
            """
            [{ "Key": "GREETING", "TranslatedText": "Bonjour" }]
            """);

        Assert.AreEqual(1, results.Length);
        Assert.AreEqual("GREETING", results[0].Key);
        Assert.AreEqual("Bonjour", results[0].TranslatedText);
        Assert.IsNull(results[0].SourceText);
        Assert.IsNull(results[0].SourceLanguage);
    }

    [TestMethod]
    public void ParseResponse_StillAcceptsEchoedFields_FromAChattierModel()
    {
        // A model that ignores the instruction and echoes everything must not break the run.
        var results = AiResponseParser.ParseResponse(ArrayJson);

        Assert.AreEqual("Bonjour", results[0].TranslatedText);
        Assert.AreEqual("Hello", results[0].SourceText);
    }

    [TestMethod]
    public void ParseResponse_AcceptsTextForTranslatedText_WhenTheProperNameIsAbsent()
    {
        // gemini-flash-lite answered one item of a two-item batch with "Text", five attempts in a
        // row, while the other item was fine. That name counts as the translation when the proper
        // one is absent.
        var results = AiResponseParser.ParseResponse(
            """
            [
              { "Key": "ADD_OR_REMOVE_SERVERS", "TranslatedText": "Server hinzufügen oder entfernen" },
              { "Key": "REMOTE_ACCESS_HINT_SERVERS", "Text": "Öffne auf deinem Smartphone den Bereich Server." }
            ]
            """);

        Assert.AreEqual(2, results.Length);
        Assert.AreEqual("Server hinzufügen oder entfernen", results[0].TranslatedText);
        Assert.AreEqual("Öffne auf deinem Smartphone den Bereich Server.", results[1].TranslatedText);
    }

    [TestMethod]
    public void ParseResponse_KeepsTranslatedText_WhenTextIsAlsoPresent()
    {
        var results = AiResponseParser.ParseResponse(
            """
            [{ "Key": "GREETING", "Text": "Hello", "TranslatedText": "Bonjour" }]
            """);

        Assert.AreEqual(1, results.Length);
        Assert.AreEqual("Bonjour", results[0].TranslatedText);
    }
}
