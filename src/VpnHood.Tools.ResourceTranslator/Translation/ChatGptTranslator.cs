using OpenAI;
using OpenAI.Chat;

namespace VpnHood.ResourceTranslator.Translation;

internal sealed class ChatGptTranslator(
    string apiKey,
    string model)
    : ITranslator
{
    private readonly OpenAIClient _client = new(apiKey);

    public async Task<TranslateResult[]> TranslateAsync(PromptOptions promptOptions, CancellationToken cancellationToken)
    {
        var prompt = PromptBuilder.BuildPrompt(promptOptions);
        var chatClient = _client.GetChatClient(model);

        var messages = new List<ChatMessage> {
            ChatMessage.CreateSystemMessage(PromptBuilder.BuildSystemPrompt()),
            ChatMessage.CreateUserMessage(prompt)
        };

        var response = await chatClient.CompleteChatAsync(messages, options: null, cancellationToken);

        if (response?.Value?.Content == null || response.Value.Content.Count == 0)
            throw new Exception("AI result is null or empty.");

        var content = response.Value.Content[0].Text;
        if (string.IsNullOrWhiteSpace(content))
            throw new Exception("AI result content is null or empty.");

        return AiResponseParser.ParseResponse(content);
    }
}
