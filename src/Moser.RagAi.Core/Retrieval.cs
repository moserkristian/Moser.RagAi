namespace Moser.RagAi;

public interface IEmbeddingGenerator
{
    int Dimension { get; }

    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken);
}

public interface IDocumentIndex
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task<StoredDocument> AddAsync(DocumentDraft draft, CancellationToken cancellationToken);

    Task<IReadOnlyList<StoredDocument>> ListAsync(CancellationToken cancellationToken);

    Task DeleteAsync(Guid documentId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Passage>> SearchAsync(float[] query, int limit, CancellationToken cancellationToken);
}

public static class VectorMath
{
    public static float Similarity(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        if (left.Length != right.Length)
            throw new ArgumentException("Vectors must have the same length.");

        double dot = 0;
        double leftNorm = 0;
        double rightNorm = 0;
        for (var i = 0; i < left.Length; i++)
        {
            dot += left[i] * right[i];
            leftNorm += left[i] * left[i];
            rightNorm += right[i] * right[i];
        }

        if (leftNorm == 0 || rightNorm == 0)
            return 0;

        return (float)(dot / (Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm)));
    }

    public static void Normalize(Span<float> vector)
    {
        double norm = 0;
        for (var i = 0; i < vector.Length; i++)
            norm += vector[i] * vector[i];
        if (norm == 0)
            return;

        var scale = (float)(1 / Math.Sqrt(norm));
        for (var i = 0; i < vector.Length; i++)
            vector[i] *= scale;
    }
}

public sealed class InMemoryDocumentIndex : IDocumentIndex
{
    private readonly List<Passage> _passages = [];
    private readonly Dictionary<Guid, float[]> _vectors = [];
    private readonly Dictionary<Guid, StoredDocument> _documents = [];
    private readonly Lock _gate = new();

    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<StoredDocument> AddAsync(DocumentDraft draft, CancellationToken cancellationToken)
    {
        var document = new StoredDocument(Guid.NewGuid(), draft.Title, draft.FileName, draft.IsSample, draft.UploadedBy, DateTimeOffset.UtcNow);
        lock (_gate)
        {
            _documents[document.Id] = document;
            foreach (var chunk in draft.Chunks)
            {
                var passage = new Passage(
                    Guid.NewGuid(),
                    document.Id,
                    document.Title,
                    document.FileName,
                    document.IsSample,
                    chunk.PageNumber,
                    chunk.Ordinal,
                    chunk.Text,
                    0);
                _passages.Add(passage);
                _vectors[passage.ChunkId] = chunk.Embedding;
            }
        }

        return Task.FromResult(document);
    }

    public Task<IReadOnlyList<StoredDocument>> ListAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<StoredDocument> documents = _documents.Values
                .OrderByDescending(document => document.UploadedAt)
                .ToList();
            return Task.FromResult(documents);
        }
    }

    public Task DeleteAsync(Guid documentId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _documents.Remove(documentId);
            var gone = _passages.Where(passage => passage.DocumentId == documentId).Select(passage => passage.ChunkId).ToList();
            _passages.RemoveAll(passage => passage.DocumentId == documentId);
            foreach (var id in gone)
                _vectors.Remove(id);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Passage>> SearchAsync(float[] query, int limit, CancellationToken cancellationToken)
    {
        if (limit <= 0)
            return Task.FromResult<IReadOnlyList<Passage>>([]);

        lock (_gate)
        {
            IReadOnlyList<Passage> ranked = _passages
                .Select(passage => passage with { Similarity = VectorMath.Similarity(query, _vectors[passage.ChunkId]) })
                .OrderByDescending(passage => passage.Similarity)
                .ThenBy(passage => passage.Ordinal)
                .Take(limit)
                .ToList();
            return Task.FromResult(ranked);
        }
    }
}
