using System.Text;
using System.Text.Json;

namespace VpnHood.ResourceTranslator.Translation;

/// <summary>
/// Builds the system and user prompts sent to the AI engines. The models are asked for a bare
/// JSON array; the shape is demonstrated by example because that survives model drift better
/// than a prose description.
/// </summary>
public static class PromptBuilder
{
    private static readonly JsonSerializerOptions IndentedSerializerOptions = new() {
        WriteIndented = true
    };

    private static readonly TranslateResult[] SampleResults = [
        new() {
            SourceLanguage = "en",
            TargetLanguage = "fr",
            Key = "Key1",
            SourceText = "SourceText1",
            TranslatedText = "TranslatedText1"
        },
        new() {
            SourceLanguage = "en",
            TargetLanguage = "it",
            Key = "Key2",
            SourceText = "SourceText2",
            TranslatedText = "TranslatedText2"
        }
    ];

    public static string BuildSystemPrompt()
    {
        return
            "You are a professional localization engine. " +
            "Return ONLY a valid JSON array of translation objects. " +
            "Do not wrap the array in any additional objects or properties. " +
            "Do not include any commentary, explanations, or markdown formatting. " +
            "The response must start with '[' and end with ']'.";
    }

    public static string BuildPrompt(PromptOptions options)
    {
        var sb = new StringBuilder();
        sb.AppendLine(options.Prompt);
        sb.AppendLine();
        sb.AppendLine("IMPORTANT: Return ONLY a JSON array (starting with '[' and ending with ']'). Do not wrap it in any other object.");
        sb.AppendLine("Expected output format:");
        sb.AppendLine(JsonSerializer.Serialize(SampleResults, IndentedSerializerOptions));
        sb.AppendLine();
        sb.AppendLine("Items to translate:");
        sb.AppendLine(JsonSerializer.Serialize(options.Items, IndentedSerializerOptions));

        return sb.ToString();
    }

    /// <summary>
    /// Combines the built-in prompt template with optional project-specific guidelines.
    /// </summary>
    public static PromptOptions BuildOptions(TranslateItem[] items, string basePrompt, string? extraPrompt)
    {
        var promptBuilder = new StringBuilder(basePrompt);

        if (!string.IsNullOrWhiteSpace(extraPrompt)) {
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("Additional guidelines:");
            promptBuilder.AppendLine(extraPrompt);
        }

        return new PromptOptions {
            Prompt = promptBuilder.ToString(),
            Items = items
        };
    }
}
