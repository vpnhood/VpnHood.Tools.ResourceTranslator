using VpnHood.Tools.ResourceTranslator.Docs;

namespace VpnHood.Tools.ResourceTranslator.Tests.Docs;

[TestClass]
public class MarkdownChunkerTests
{
    [TestMethod]
    public void A_short_body_stays_one_untouched_chunk()
    {
        const string body = "One.\n\n\nTwo with an odd separator.";
        var chunks = MarkdownChunker.Split(body, 1000);

        Assert.AreEqual(1, chunks.Count);
        Assert.AreEqual(body, chunks[0]);
    }

    [TestMethod]
    public void A_long_body_splits_at_paragraph_boundaries()
    {
        var paragraphs = Enumerable.Range(0, 6).Select(i => $"Paragraph {i} " + new string('x', 400));
        var body = string.Join("\n\n", paragraphs);

        var chunks = MarkdownChunker.Split(body, 1000);

        Assert.IsTrue(chunks.Count >= 3, $"expected several chunks, got {chunks.Count}");
        Assert.IsTrue(chunks.All(chunk => chunk.Length <= 1000));
        foreach (var i in Enumerable.Range(0, 6))
            Assert.IsTrue(chunks.Any(chunk => chunk.Contains($"Paragraph {i} ")), $"paragraph {i} must survive");
    }

    [TestMethod]
    public void An_oversized_single_block_stays_whole()
    {
        var block = "One giant paragraph " + new string('y', 5000);
        var chunks = MarkdownChunker.Split("Small.\n\n" + block, 1000);

        Assert.AreEqual(2, chunks.Count);
        Assert.AreEqual(block, chunks[1]);
    }

    [TestMethod]
    public void Split_then_join_renders_identically_to_the_source()
    {
        // The round-trip guarantee that makes chunking safe at all: whatever the splitter does,
        // the reassembled document must render to the same element tree as the source —
        // including odd blank-line runs and loose lists.
        const string body =
            """
            # Title

            Paragraph one.


            Paragraph after a double blank run.

            - item one

            - item two (loose list)

            Final paragraph.
            """;

        var rejoined = MarkdownChunker.Join(MarkdownChunker.Split(body, 30));
        Assert.AreEqual(0, MarkdownVerifier.Verify(body, rejoined).Count);
    }
}
