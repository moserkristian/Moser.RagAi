using Microsoft.Extensions.Logging;

namespace Moser.RagAi;

public sealed record AskResult(DeskAnswer Answer, IReadOnlyList<Passage> Matches);

public sealed class DeskService
{
    public const int MaxQuestionLength = 1000;
    public const int MaxStoredCharacters = 200_000;
    public const int MaxChunks = 200;

    private readonly IDocumentIndex _index;
    private readonly IEmbeddingGenerator _embeddings;
    private readonly IAnswerGenerator _answers;
    private readonly IDocumentTextExtractor _extractor;
    private readonly DeskLimits _limits;
    private readonly ILogger<DeskService> _logger;

    public DeskService(
        IDocumentIndex index,
        IEmbeddingGenerator embeddings,
        IAnswerGenerator answers,
        IDocumentTextExtractor extractor,
        DeskLimits limits,
        ILogger<DeskService> logger)
    {
        _index = index;
        _embeddings = embeddings;
        _answers = answers;
        _extractor = extractor;
        _limits = limits;
        _logger = logger;
    }

    public Task<IReadOnlyList<StoredDocument>> ListAsync(CancellationToken cancellationToken) =>
        _index.ListAsync(cancellationToken);

    public async Task DeleteAsync(Guid documentId, CancellationToken cancellationToken)
    {
        await _index.DeleteAsync(documentId, cancellationToken);
        _logger.LogInformation("Removed document {DocumentId}.", documentId);
    }

    public async Task<StoredDocument> IngestAsync(string fileName, Stream content, string uploadedBy, CancellationToken cancellationToken)
    {
        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName) || safeName.Length > 180)
            throw new DeskInputException("Use a file name of 180 characters or fewer.");

        var bytes = await ReadLimitedAsync(content, cancellationToken);
        var extracted = _extractor.Extract(safeName, bytes);
        var fullText = string.Join("\n\n", extracted.Pages.Select(page => page.Text));
        if (fullText.Length > MaxStoredCharacters)
            throw new DeskInputException("That document is too long to store. Split it and upload the parts.");

        var chunks = new List<NewChunk>();
        foreach (var page in extracted.Pages)
        {
            foreach (var chunk in TextChunker.Chunk(page.Text, page.PageNumber))
            {
                if (chunks.Count >= MaxChunks)
                    throw new DeskInputException("That document is too long to store. Split it and upload the parts.");

                var embedding = await _embeddings.EmbedAsync(chunk.Text, cancellationToken);
                chunks.Add(new NewChunk(chunks.Count, chunk.PageNumber, chunk.Text, embedding));
            }
        }

        if (chunks.Count == 0)
            throw new DeskInputException("That file has no text to store.");

        var draft = new DocumentDraft(
            DocumentNaming.Title(safeName, fullText),
            safeName,
            DocumentNaming.IsSample(safeName, fullText),
            string.IsNullOrWhiteSpace(uploadedBy) ? "staff" : uploadedBy,
            chunks);
        var stored = await _index.AddAsync(draft, cancellationToken);
        _logger.LogInformation("Stored document {DocumentId} with {ChunkCount} chunks.", stored.Id, chunks.Count);
        return stored;
    }

    public async Task<AskResult> AskAsync(string question, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(question))
            throw new DeskInputException("Enter a question.");
        if (question.Length > MaxQuestionLength)
            throw new DeskInputException("Keep the question under 1000 characters.");

        var vector = await _embeddings.EmbedAsync(question, cancellationToken);
        var found = await _index.SearchAsync(vector, _limits.TopK, cancellationToken);
        var useful = found.Where(passage => passage.Similarity >= _limits.MinimumSimilarity).ToList();
        _logger.LogInformation(
            "Question matched {MatchCount} passages. Best similarity {BestSimilarity}.",
            useful.Count,
            found.Count == 0 ? 0 : found[0].Similarity);

        if (useful.Count == 0)
            return new AskResult(AnswerComposer.Refuse(), []);

        var generation = await _answers.GenerateAsync(question, useful, cancellationToken);
        return new AskResult(AnswerComposer.FromGeneration(useful, generation), useful);
    }

    private async Task<byte[]> ReadLimitedAsync(Stream content, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var rented = new byte[81_920];
        long total = 0;
        int read;
        while ((read = await content.ReadAsync(rented, cancellationToken)) > 0)
        {
            total += read;
            if (total > _limits.MaxUploadBytes)
                throw new DeskInputException($"That file is larger than {_limits.MaxUploadMegabytes} MB.");
            buffer.Write(rented, 0, read);
        }

        return buffer.ToArray();
    }
}
