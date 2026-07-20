using System.Text;
using System.Text.Json;
using VpnHood.ResourceTranslator.Translators;

namespace VpnHood.ResourceTranslator.Models;

public static class TranslateUtils
{
    private static readonly JsonSerializerOptions IndentedSerializerOptions = new() {
        WriteIndented = true
    };

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
        var sample = new TranslateResult[] {
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
        };

        var sb = new StringBuilder();
        sb.AppendLine(options.Prompt);
        sb.AppendLine();
        sb.AppendLine("IMPORTANT: Return ONLY a JSON array (starting with '[' and ending with ']'). Do not wrap it in any other object.");
        sb.AppendLine("Expected output format:");
        sb.AppendLine(JsonSerializer.Serialize(sample, IndentedSerializerOptions));
        sb.AppendLine();
        sb.AppendLine("Items to translate:");
        sb.AppendLine(JsonSerializer.Serialize(options.Items, IndentedSerializerOptions));

        return sb.ToString();
    }

    public static string PostProcessTranslation(string source, string? translated)
    {
        if (translated == null)
            return string.Empty;

        translated = translated.Trim();

        // Remove wrapping quotes/backticks if present
        if (translated.Length >= 2 &&
            ((translated.StartsWith('"') && translated.EndsWith('"')) ||
             (translated.StartsWith('\'') && translated.EndsWith('\'')) ||
             (translated.StartsWith('`') && translated.EndsWith('`')))) {
            translated = translated[1..^1];
        }

        // Ensure placeholders like {x} remain present; append missing ones to avoid breaking runtime formatting
        foreach (var token in ExtractPlaceholders(source)) {
            if (!translated.Contains(token, StringComparison.Ordinal))
                translated = translated + (translated.EndsWith(' ') ? string.Empty : " ") + token;
        }

        return translated;
    }

    public static List<string> ExtractPlaceholders(string s)
    {
        var list = new List<string>();
        if (string.IsNullOrEmpty(s))
            return list;

        for (var i = 0; i < s.Length; i++) {
            if (s[i] != '{')
                continue;

            var j = s.IndexOf('}', i + 1);
            if (j > i) {
                list.Add(s.Substring(i, j - i + 1));
                i = j;
            }
        }

        return list;
    }
}
