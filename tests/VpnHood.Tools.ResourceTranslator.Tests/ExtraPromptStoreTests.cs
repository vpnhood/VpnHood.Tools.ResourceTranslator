using VpnHood.Tools.ResourceTranslator.Configuration;

namespace VpnHood.Tools.ResourceTranslator.Tests;

[TestClass]
public class ExtraPromptStoreTests
{
    [TestMethod]
    public async Task LoadAsync_composes_the_shared_and_per_language_prompts()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile($"vh_translator/{ExtraPromptStore.SharedPromptFileName}", "Shared rule.");
        workspace.WriteFile($"vh_translator/{ExtraPromptStore.PromptsFolderName}/fa.prompt.txt", "Persian rule.");

        var store = ExtraPromptStore.Resolve(null, [Path.Combine(workspace.Path, "vh_translator")]);

        Assert.AreEqual("Shared rule.\n\nPersian rule.", await store.LoadAsync("fa", CancellationToken.None));
        Assert.AreEqual("Shared rule.", await store.LoadAsync("de", CancellationToken.None));
    }

    [TestMethod]
    public async Task LoadAsync_is_safe_to_call_concurrently()
    {
        // One resolved store is shared by every runner of a folder or site run, and Empty is a
        // process-wide singleton that TranslatorOptions hands out by default, so the cache must
        // survive concurrent entry. With a plain Dictionary a CI run died inside LoadAsync with
        // "Operations that change non-concurrent collections must have exclusive access".
        // A data race cannot be provoked on demand — this hammers the invariant rather than
        // reproducing that failure, and would not have caught it on every machine.
        using var workspace = new TestWorkspace();
        workspace.WriteFile($"vh_translator/{ExtraPromptStore.SharedPromptFileName}", "Shared rule.");
        var store = ExtraPromptStore.Resolve(null, [Path.Combine(workspace.Path, "vh_translator")]);

        var languages = Enumerable.Range(0, 200).Select(i => $"l{i}").ToArray();
        var loads = languages.Select(language => Task.Run(() => store.LoadAsync(language, CancellationToken.None)));
        var results = await Task.WhenAll(loads);

        Assert.IsTrue(results.All(result => result == "Shared rule."));

        // The same hammering against the shared singleton, which owns no files at all.
        var emptyLoads = languages.Select(language => Task.Run(() => ExtraPromptStore.Empty.LoadAsync(language, CancellationToken.None)));
        Assert.IsTrue((await Task.WhenAll(emptyLoads)).All(result => result == null));
    }

    [TestMethod]
    public void Resolve_fails_loudly_when_an_explicit_prompt_file_is_missing()
    {
        using var workspace = new TestWorkspace();
        var missing = Path.Combine(workspace.Path, "nope.txt");

        Assert.ThrowsExactly<TranslatorException>(() => ExtraPromptStore.Resolve(missing, []));
    }
}
