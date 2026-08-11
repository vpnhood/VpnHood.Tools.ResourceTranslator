using Mscc.GenerativeAI;
using Mscc.GenerativeAI.Types;

namespace VpnHood.Tools.ResourceTranslator.Translation;

internal sealed class GeminiTranslator(
    string apiKey,
    string model)
    : ITranslator
{
    private readonly GoogleAI _googleAi = new(apiKey);

    public async Task<TranslateResult[]> TranslateAsync(PromptOptions promptOptions, CancellationToken cancellationToken)
    {
        var prompt = PromptBuilder.BuildPrompt(promptOptions);

        var geminiModel = _googleAi.GenerativeModel(model: model);
        var response = await geminiModel.GenerateContent(prompt, new GenerationConfig {
            ResponseMimeType = "application/json",

            // Translation is a mechanical mapping, not a reasoning task, but the Flash models
            // think by default — and thinking tokens bill at the OUTPUT rate, the expensive
            // side. Nothing observed in the output justified paying for it.
            ThinkingConfig = new ThinkingConfig { ThinkingBudget = 0 },

            // The same source string is translated in separate batches that cannot see one
            // another, so sampling variance surfaces as one label rendered two ways across the
            // site. Deterministic decoding removes that; the glossary handles the rest.
            Temperature = 0
        }, cancellationToken: cancellationToken);

        if (string.IsNullOrWhiteSpace(response.Text))
            throw new Exception("AI result content is null or empty.");

        return AiResponseParser.ParseResponse(response.Text);
    }
}
