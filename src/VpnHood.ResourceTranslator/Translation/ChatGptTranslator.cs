using OpenAI;
using OpenAI.Chat;
using VpnHood.ResourceTranslator.Models;

namespace VpnHood.ResourceTranslator.Translators;

internal sealed class ChatGptTranslator(
    string apiKey,
    string model)
    : ITranslator
{
    private readonly OpenAIClient _client = new(apiKey);

    public async Task<TranslateResult[]> TranslateAsync(PromptOptions promptOptions, CancellationToken cancellationToken)
    {
        var prompt = TranslateUtils.BuildPrompt(promptOptions);
        var chatClient = _client.GetChatClient(model);

        var messages = new List<ChatMessage> {
            ChatMessage.CreateSystemMessage(TranslateUtils.BuildSystemPrompt()),
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
