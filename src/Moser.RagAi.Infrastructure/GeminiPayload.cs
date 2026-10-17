using System.Text.Json;

namespace Moser.RagAi;

public static class GeminiPayload
{
    public static string Create(string system, string user)
    {
        var payload = new
        {
            systemInstruction = new
            {
                parts = new[] { new { text = system } }
            },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = user } }
                }
            },
            generationConfig = new
            {
                temperature = 0,
                thinkingConfig = new
                {
                    thinkingLevel = "low"
                }
            }
        };

        return JsonSerializer.Serialize(payload);
    }

    public static string? ReadText(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            return null;

        if (!candidates[0].TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts))
            return null;

        var builder = new System.Text.StringBuilder();
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True)
                continue;
            if (part.TryGetProperty("text", out var text))
                builder.Append(text.GetString());
        }

        var value = builder.ToString().Trim();
        return value.Length == 0 ? null : value;
    }
}
