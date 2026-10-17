using System.Text;

namespace Moser.RagAi;

public sealed record TextChunk(int? PageNumber, string Text);

public static class TextChunker
{
    public static IReadOnlyList<TextChunk> Chunk(
        string? text,
        int? pageNumber,
        int maxCharacters = 700,
        int overlapCharacters = 100)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        if (maxCharacters < 40)
            throw new ArgumentOutOfRangeException(nameof(maxCharacters));
        if (overlapCharacters < 0 || overlapCharacters >= maxCharacters)
            throw new ArgumentOutOfRangeException(nameof(overlapCharacters));

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        var pieces = Split(normalized, maxCharacters, overlapCharacters);
        if (pieces.Count == 0)
            return [];

        var packed = new List<string>();
        var buffer = new StringBuilder();
        foreach (var piece in pieces)
        {
            if (buffer.Length == 0)
            {
                buffer.Append(piece);
                continue;
            }

            if (buffer.Length + 2 + piece.Length <= maxCharacters)
            {
                buffer.Append("\n\n");
                buffer.Append(piece);
                continue;
            }

            packed.Add(buffer.ToString());
            buffer.Clear();
            var carry = Tail(packed[^1], overlapCharacters);
            if (carry.Length > 0 && carry.Length + 2 + piece.Length <= maxCharacters)
            {
                buffer.Append(carry);
                buffer.Append("\n\n");
            }

            buffer.Append(piece);
        }

        if (buffer.Length > 0)
            packed.Add(buffer.ToString());

        var chunks = new List<TextChunk>(packed.Count);
        foreach (var item in packed)
            chunks.Add(new TextChunk(pageNumber, item));
        return chunks;
    }

    private static List<string> Split(string text, int maxCharacters, int overlapCharacters)
    {
        var pieces = new List<string>();
        var step = Math.Max(1, maxCharacters - overlapCharacters);
        foreach (var paragraph in text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (paragraph.Length <= maxCharacters)
            {
                pieces.Add(paragraph);
                continue;
            }

            for (var start = 0; start < paragraph.Length; start += step)
            {
                var length = Math.Min(maxCharacters, paragraph.Length - start);
                var window = paragraph.Substring(start, length).Trim();
                if (window.Length > 0)
                    pieces.Add(window);
                if (start + length >= paragraph.Length)
                    break;
            }
        }

        return pieces;
    }

    private static string Tail(string text, int count)
    {
        if (count == 0 || text.Length <= count)
            return "";

        return text[^count..].Trim();
    }
}
