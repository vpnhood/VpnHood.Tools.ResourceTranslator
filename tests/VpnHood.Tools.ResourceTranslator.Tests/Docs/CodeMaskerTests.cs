using System.Text.RegularExpressions;
using VpnHood.Tools.ResourceTranslator.Docs;

namespace VpnHood.Tools.ResourceTranslator.Tests.Docs;

[TestClass]
public class CodeMaskerTests
{
    private static readonly Regex[] PlaceholderPattern = [new(@"\{[A-Za-z0-9_]+\}")];

    [TestMethod]
    public void Fenced_block_is_one_token_including_its_blank_lines()
    {
        const string text = "Intro.\n\n```csharp\nvar x = 1;\n\nvar y = 2;\n```\n\nOutro.";
        var masker = CodeMasker.Mask(text, []);

        Assert.AreEqual(1, masker.TokenCount);
        Assert.IsFalse(masker.Masked.Contains("var x"), "code must not be visible to the model");
        // The fence collapsed into a single token, so the only blank lines left are the
        // paragraph boundaries — which is exactly what makes chunking safe.
        Assert.IsTrue(masker.Masked.Contains($"Intro.\n\n{CodeMasker.TokenName(0)}\n\nOutro."));
        Assert.AreEqual(text, masker.Unmask(masker.Masked));
    }

    [TestMethod]
    public void Tilde_fences_longer_closers_and_unclosed_fences_are_handled()
    {
        const string tilde = "~~~\ncode\n~~~\nAfter.";
        Assert.AreEqual(tilde, CodeMasker.Mask(tilde, []).Unmask(CodeMasker.Mask(tilde, []).Masked));

        const string longClose = "```\ncode\n`````\nAfter.";
        var masker = CodeMasker.Mask(longClose, []);
        Assert.IsTrue(masker.Masked.Contains("After."), "a longer closing run still closes the fence");
        Assert.IsFalse(masker.Masked.Contains("code"));

        const string unclosed = "Before.\n\n```\ncode to the end";
        var unclosedMasker = CodeMasker.Mask(unclosed, []);
        Assert.IsFalse(unclosedMasker.Masked.Contains("code to the end"));
        Assert.AreEqual(unclosed, unclosedMasker.Unmask(unclosedMasker.Masked));
    }

    [TestMethod]
    public void Inline_code_and_placeholders_are_masked()
    {
        const string text = "Run `vhtranslator docs` to translate {count} files.";
        var masker = CodeMasker.Mask(text, PlaceholderPattern);

        Assert.AreEqual(2, masker.TokenCount);
        Assert.IsFalse(masker.Masked.Contains("vhtranslator"));
        Assert.IsFalse(masker.Masked.Contains("{count}"));
        Assert.AreEqual(text, masker.Unmask(masker.Masked));
    }

    [TestMethod]
    public void Validate_reports_lost_and_duplicated_tokens()
    {
        var masker = CodeMasker.Mask("Use `a` and `b`.", []);
        Assert.AreEqual(0, masker.Validate(masker.Masked).Count);

        var lost = masker.Masked.Replace(CodeMasker.TokenName(0), "");
        Assert.AreEqual(1, masker.Validate(lost).Count);

        var duplicated = masker.Masked + CodeMasker.TokenName(1);
        Assert.AreEqual(1, masker.Validate(duplicated).Count);
    }
}
