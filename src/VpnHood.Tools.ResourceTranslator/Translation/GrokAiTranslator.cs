using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace VpnHood.Tools.ResourceTranslator.Translation;

internal sealed class GrokAiTranslator(
    string apiKey,
    string model)
    : ITranslator
{
    private const string ApiUrl = "https://api.x.ai/v1/chat/completions";
    private static readonly HttpClient HttpClient = new();

    public async Task<TranslateResult[]> TranslateAsync(PromptOptions promptOptions, CancellationToken cancellationToken)
    {
        var prompt = PromptBuilder.BuildPrompt(promptOptions);

        var requestBody = new {
            model,
            messages = new[] {
                new { role = "system", content = PromptBuilder.BuildSystemPrompt() },
                new { role = "user", content = prompt }
            },
            temperature = 0.1,
            stream = false
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
        request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Grok AI API error ({response.StatusCode}): {responseContent}");

        using var document = JsonDocument.Parse(responseContent);

        var choices = document.RootElement.GetProperty("choices");
        if (choices.GetArrayLength() == 0)
            throw new Exception("Grok AI response contains no choices.");

        var content = choices[0].GetProperty("message").GetProperty("content").GetString();
        if (string.IsNullOrWhiteSpace(content))
            throw new Exception("Grok AI result content is null or empty.");

        return AiResponseParser.ParseResponse(content);
    }
}
