namespace Moser.RagAi.Tests;

public class RetrievalTests
{
    [Fact]
    public void Cosine_handles_equal_opposite_and_empty_vectors()
    {
        Assert.Equal(1, VectorMath.Similarity([1f, 0f], [2f, 0f]), precision: 3);
        Assert.Equal(-1, VectorMath.Similarity([1f, 0f], [-1f, 0f]), precision: 3);
        Assert.Equal(0, VectorMath.Similarity([0f, 0f], [1f, 0f]));
    }

    [Fact]
    public async Task Search_returns_the_nearest_passage_first()
    {
        var index = new InMemoryDocumentIndex();
        await index.AddAsync(Draft("Leave", [1f, 0f], "20 days of paid leave."), CancellationToken.None);
        await index.AddAsync(Draft("Wifi", [0f, 1f], "The guest network name is printed on the router."), CancellationToken.None);

        var found = await index.SearchAsync([0.9f, 0.1f], limit: 2, CancellationToken.None);

        Assert.Equal("Leave", found[0].Title);
        Assert.True(found[0].Similarity > found[1].Similarity);
    }

    private static DocumentDraft Draft(string title, float[] embedding, string text) =>
        new(title, title + ".md", false, "staff", [new NewChunk(0, null, text, embedding)]);
}
