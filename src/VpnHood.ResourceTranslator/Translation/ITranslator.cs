
namespace VpnHood.ResourceTranslator.Translation;

public interface ITranslator
{
    Task<TranslateResult[]> TranslateAsync(PromptOptions promptOptions, CancellationToken cancellationToken);
}