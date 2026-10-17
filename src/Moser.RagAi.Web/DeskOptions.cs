namespace Moser.RagAi.Web;

public sealed class DeskOptions
{
    public int MaxUploadBytes { get; set; } = DeskLimits.DefaultMaxUploadBytes;
    public float MinimumSimilarity { get; set; } = 0.32f;
    public int TopK { get; set; } = 4;
    public List<ConfiguredUser> Users { get; set; } = [];
}

public sealed class ConfiguredUser
{
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";

    public bool IsUsable => !string.IsNullOrWhiteSpace(UserName) && Password.Length > 0;
}

public sealed class GeminiOptions
{
    public string Model { get; set; } = GeminiAnswerGenerator.DefaultModel;
    public string Endpoint { get; set; } = "https://generativelanguage.googleapis.com/";
}

public sealed class EmbeddingLocation
{
    public EmbeddingLocation(string path) => Path = path;

    public string Path { get; }
}
