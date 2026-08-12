using System.Collections.Concurrent;
using System.Net;
using Mscc.GenerativeAI;
using Mscc.GenerativeAI.Types;

namespace VpnHood.Tools.ResourceTranslator.Translation;

internal sealed class GeminiTranslator(
    string apiKey,
    string model)
    : ITranslator
{
    private readonly GoogleAI _googleAi = new(apiKey);

    /// <summary>
    /// The thinking knob each model turned out to accept. A translator instance lives for a single
    /// file, so without this every file would re-pay the probe; the answer is a property of the
    /// model, so one run learns it once.
    /// </summary>
    private static readonly ConcurrentDictionary<string, GeminiThinkingMode> AcceptedThinkingModes =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<TranslateResult[]> TranslateAsync(PromptOptions promptOptions, CancellationToken cancellationToken)
    {
        var prompt = PromptBuilder.BuildPrompt(promptOptions);

        var geminiModel = _googleAi.GenerativeModel(model: model);
        var response = await GenerateContent(geminiModel, prompt, cancellationToken);

        if (string.IsNullOrWhiteSpace(response.Text))
            throw new Exception("AI result content is null or empty.");

        return AiResponseParser.ParseResponse(response.Text);
    }

    /// <summary>
    /// Sends the prompt, walking the thinking candidates until one is not refused. Only the
    /// thinking config varies between attempts, so a 400 that survives the whole list is about
    /// something else — and that first, most faithful error is the one raised.
    /// </summary>
    private async Task<GenerateContentResponse> GenerateContent(GenerativeModel geminiModel, string prompt,
        CancellationToken cancellationToken)
    {
        var candidates = AcceptedThinkingModes.TryGetValue(model, out var known)
            ? [known, .. GeminiThinkingPlan.ForModel(model).Where(mode => mode != known)]
            : GeminiThinkingPlan.ForModel(model);

        var firstRejection = default(GeminiApiException);
        foreach (var mode in candidates) {
            try {
                var response = await geminiModel.GenerateContent(prompt, BuildGenerationConfig(mode),
                    cancellationToken: cancellationToken);
                AcceptedThinkingModes[model] = mode;
                return response;
            }
            catch (GeminiApiException ex) when (IsBadRequest(ex)) {
                firstRejection ??= ex;
            }
        }

        throw firstRejection ?? new Exception($"Gemini refused every thinking configuration for model '{model}'.");
    }

    private static GenerationConfig BuildGenerationConfig(GeminiThinkingMode thinkingMode)
    {
        return new GenerationConfig {
            ResponseMimeType = "application/json",

            ThinkingConfig = thinkingMode switch {
                GeminiThinkingMode.Level => new ThinkingConfig { ThinkingLevel = ThinkingLevel.Minimal },
                GeminiThinkingMode.Budget => new ThinkingConfig { ThinkingBudget = 0 },
                _ => null
            },

            // The same source string is translated in separate batches that cannot see one
            // another, so sampling variance surfaces as one label rendered two ways across the
            // site. Deterministic decoding removes that; the glossary handles the rest.
            Temperature = 0
        };
    }

    /// <summary>
    /// A rejected thinking field comes back as a plain 400 — for thinkingBudget on Gemini 3 the
    /// body says only "Request contains an invalid argument", naming no field — so the status is
    /// all there is to match on.
    /// </summary>
    private static bool IsBadRequest(GeminiApiException exception)
    {
        return exception.Response?.StatusCode == HttpStatusCode.BadRequest;
    }
}
