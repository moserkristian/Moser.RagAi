using System.Text;

namespace Moser.RagAi.Tests;

public class PdfTextTests
{
    [Fact]
    public void A_text_pdf_keeps_the_page_number()
    {
        var extracted = new FileTextExtractor().Extract("leave.pdf", SamplePdf("Paid leave is 20 days."));

        var page = Assert.Single(extracted.Pages);
        Assert.Equal(1, page.PageNumber);
        Assert.Contains("Paid leave is 20 days.", page.Text);
    }

    private static byte[] SamplePdf(string sentence)
    {
        var stream = Encoding.ASCII.GetBytes($"BT /F1 18 Tf 36 120 Td ({sentence}) Tj ET");
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>"u8.ToArray(),
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"u8.ToArray(),
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 200] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>"u8.ToArray(),
            Encoding.ASCII.GetBytes($"<< /Length {stream.Length} >>\nstream\n")
                .Concat(stream)
                .Concat("\nendstream"u8.ToArray())
                .ToArray(),
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"u8.ToArray()
        };

        using var output = new MemoryStream();
        output.Write("%PDF-1.4\n"u8);
        var offsets = new List<long> { 0 };
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(output.Position);
            var header = Encoding.ASCII.GetBytes($"{i + 1} 0 obj\n");
            output.Write(header);
            output.Write(objects[i]);
            output.Write("\nendobj\n"u8);
        }

        var xref = output.Position;
        var xrefHeader = Encoding.ASCII.GetBytes($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        output.Write(xrefHeader);
        for (var i = 1; i < offsets.Count; i++)
            output.Write(Encoding.ASCII.GetBytes($"{offsets[i]:0000000000} 00000 n \n"));

        output.Write(Encoding.ASCII.GetBytes($"trailer << /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF"));
        return output.ToArray();
    }
}
