using VpnHood.ResourceTranslator.Models;

namespace VpnHood.ResourceTranslator.Translators;

internal interface ITranslator
{
    Task<TranslateResult[]> TranslateAsync(PromptOptions promptOptions, CancellationToken cancellationToken);
}