namespace Moser.RagAi;

public static class EmbeddingFiles
{
    public const string ModelFileName = "model.onnx";
    public const string VocabFileName = "vocab.txt";

    public static readonly Uri ModelUri = new("https://huggingface.co/Xenova/all-MiniLM-L6-v2/resolve/main/onnx/model_quantized.onnx");
    public static readonly Uri VocabUri = new("https://huggingface.co/Xenova/all-MiniLM-L6-v2/resolve/main/vocab.txt");

    public static async Task EnsureAsync(string directory, HttpClient http, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        await DownloadAsync(http, ModelUri, Path.Combine(directory, ModelFileName), cancellationToken);
        await DownloadAsync(http, VocabUri, Path.Combine(directory, VocabFileName), cancellationToken);
    }

    private static async Task DownloadAsync(HttpClient http, Uri uri, string path, CancellationToken cancellationToken)
    {
        if (File.Exists(path) && new FileInfo(path).Length > 0)
            return;

        var temporary = path + ".partial";
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var destination = File.Create(temporary))
        {
            await source.CopyToAsync(destination, cancellationToken);
        }

        File.Move(temporary, path, overwrite: true);
    }
}
