
namespace VpnHood.Tools.ResourceTranslator.Translation;

public interface ITranslator
{
    Task<TranslateResult[]> TranslateAsync(PromptOptions promptOptions, CancellationToken cancellationToken);
}