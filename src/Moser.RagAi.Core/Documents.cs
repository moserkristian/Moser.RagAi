namespace Moser.RagAi;

public sealed class DeskInputException : Exception
{
    public DeskInputException(string message) : base(message)
    {
    }
}

public sealed record DeskLimits(int MaxUploadBytes, float MinimumSimilarity, int TopK)
{
    public const int DefaultMaxUploadBytes = 8 * 1024 * 1024;

    public static DeskLimits Default { get; } = new(DefaultMaxUploadBytes, 0.32f, 4);

    public int MaxUploadMegabytes => Math.Max(1, MaxUploadBytes / (1024 * 1024));
}

public sealed record PageText(int? PageNumber, string Text);

public sealed record ExtractedDocument(IReadOnlyList<PageText> Pages);

public interface IDocumentTextExtractor
{
    ExtractedDocument Extract(string fileName, ReadOnlyMemory<byte> content);
}

public sealed record StoredDocument(
    Guid Id,
    string Title,
    string FileName,
    bool IsSample,
    string UploadedBy,
    DateTimeOffset UploadedAt);

public sealed record NewChunk(int Ordinal, int? PageNumber, string Text, float[] Embedding);

public sealed record DocumentDraft(
    string Title,
    string FileName,
    bool IsSample,
    string UploadedBy,
    IReadOnlyList<NewChunk> Chunks);

public sealed record Passage(
    Guid ChunkId,
    Guid DocumentId,
    string Title,
    string FileName,
    bool IsSample,
    int? PageNumber,
    int Ordinal,
    string Text,
    float Similarity);

public static class DocumentNaming
{
    public static bool IsSample(string fileName, string text)
    {
        var name = Path.GetFileName(fileName);
        if (name.StartsWith("sample-", StringComparison.OrdinalIgnoreCase))
            return true;

        var head = text.Length <= 500 ? text : text[..500];
        return head.Contains("SAMPLE DOCUMENT", StringComparison.OrdinalIgnoreCase);
    }

    public static string Title(string fileName, string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("# ", StringComparison.Ordinal))
                continue;

            var title = line[2..].Trim();
            if (title.Length > 0)
                return title;
        }

        var name = Path.GetFileNameWithoutExtension(fileName)
            .Replace('-', ' ')
            .Replace('_', ' ')
            .Trim();
        return name.Length == 0 ? "Untitled" : name;
    }
}

public static class PlainText
{
    public static ExtractedDocument Extract(ReadOnlyMemory<byte> content)
    {
        var text = System.Text.Encoding.UTF8.GetString(content.Span).Trim().TrimStart('\uFEFF');
        if (text.Length == 0)
            throw new DeskInputException("That file has no text to store.");

        return new ExtractedDocument([new PageText(null, text)]);
    }
}
