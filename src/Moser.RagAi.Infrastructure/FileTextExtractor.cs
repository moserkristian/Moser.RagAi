using UglyToad.PdfPig;

namespace Moser.RagAi;

public sealed class FileTextExtractor : IDocumentTextExtractor
{
    public const int MaxPdfPages = 40;

    public ExtractedDocument Extract(string fileName, ReadOnlyMemory<byte> content)
    {
        var extension = Path.GetExtension(fileName);
        if (extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) || extension.Equals(".md", StringComparison.OrdinalIgnoreCase))
            return PlainText.Extract(content);
        if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            return ReadPdf(content);

        throw new DeskInputException("Upload a PDF, text, or Markdown file.");
    }

    private static ExtractedDocument ReadPdf(ReadOnlyMemory<byte> content)
    {
        using var stream = new MemoryStream(content.ToArray(), writable: false);
        using var document = PdfDocument.Open(stream);
        if (document.NumberOfPages > MaxPdfPages)
            throw new DeskInputException($"That PDF has more than {MaxPdfPages} pages.");

        var pages = new List<PageText>();
        foreach (var page in document.GetPages())
        {
            var text = page.Text?.Trim();
            if (!string.IsNullOrEmpty(text))
                pages.Add(new PageText(page.Number, text));
        }

        if (pages.Count == 0)
            throw new DeskInputException("That PDF has no extractable text.");

        return new ExtractedDocument(pages);
    }
}
