namespace Moser.RagAi.Tests;

public class TextChunkerTests
{
    [Fact]
    public void Empty_text_yields_no_chunks()
    {
        Assert.Empty(TextChunker.Chunk(" \r\n\t ", pageNumber: 2));
    }

    [Fact]
    public void Short_paragraphs_stay_in_one_chunk()
    {
        var chunks = TextChunker.Chunk("One.\n\nTwo.", pageNumber: null, maxCharacters: 100, overlapCharacters: 10);

        var chunk = Assert.Single(chunks);
        Assert.Null(chunk.PageNumber);
        Assert.Contains("One.", chunk.Text);
        Assert.Contains("Two.", chunk.Text);
    }

    [Fact]
    public void Packed_chunks_keep_a_tail_and_the_page_number()
    {
        var first = new string('a', 30);
        var second = new string('b', 20);
        var chunks = TextChunker.Chunk($"{first}\n\n{second}", pageNumber: 4, maxCharacters: 40, overlapCharacters: 8);

        Assert.Equal(2, chunks.Count);
        Assert.Equal(4, chunks[0].PageNumber);
        Assert.Equal(4, chunks[1].PageNumber);
        Assert.StartsWith(new string('a', 8), chunks[1].Text);
        Assert.Contains(second, chunks[1].Text);
    }

    [Fact]
    public void A_long_paragraph_is_split_inside_the_limit()
    {
        var chunks = TextChunker.Chunk(new string('x', 100), pageNumber: 1, maxCharacters: 40, overlapCharacters: 10);

        Assert.True(chunks.Count >= 3);
        Assert.All(chunks, chunk => Assert.InRange(chunk.Text.Length, 1, 40));
    }
}
