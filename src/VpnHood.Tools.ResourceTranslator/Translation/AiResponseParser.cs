using System.Text.Json;
using System.Text.Json.Nodes;

namespace VpnHood.Tools.ResourceTranslator.Translation;

public static class AiResponseParser
{
    private static readonly string[] WrapperPropertyNames = ["result", "results", "translations", "data"];

    private static readonly JsonSerializerOptions SerializerOptions = new() {
        PropertyNameCaseInsensitive = true
    };

    public static TranslateResult[] ParseResponse(string content)
    {
        try {
            content = StripMarkdownFences(content.Trim());
            var jsonNode = JsonNode.Parse(content);

            switch (jsonNode) {
                // Direct array - this is what we want
                case JsonArray jsonArray:
                    return DeserializeArray(jsonArray);

                // Check for common wrapper patterns like { "results": [...] }
                case JsonObject jsonObject: {
                    foreach (var propertyName in WrapperPropertyNames) {
                        if (jsonObject[propertyName] is JsonArray wrappedArray)
                            return DeserializeArray(wrappedArray);
                    }

                    // Single object - wrap it in an array
                    var singleResult = jsonObject.Deserialize<TranslateResult>(SerializerOptions);
                    if (singleResult != null)
                        return [singleResult];
                    break;
                }
            }

            throw new Exception("Unable to parse AI response structure.");
        }
        catch (JsonException ex) {
            throw new Exception($"AI result JSON parsing failed: {ex.Message}. Content: {content}");
        }
        catch (Exception ex) {
            throw new Exception($"AI result processing failed: {ex.Message}. Content: {content}");
        }
    }

    private static string StripMarkdownFences(string content)
    {
        if (content.StartsWith("```json") && content.EndsWith("```"))
            return content[7..^3].Trim();

        if (content.StartsWith("```") && content.EndsWith("```"))
            return content[3..^3].Trim();

        return content;
    }

    private static TranslateResult[] DeserializeArray(JsonArray jsonArray)
    {
        var results = jsonArray
            .Select(item => item?.Deserialize<TranslateResult>(SerializerOptions))
            .Where(result => result != null)
            .Select(result => result!)
            .ToArray();

        if (results.Length == 0)
            throw new Exception("No valid translation results found in array.");

        return results;
    }
}
