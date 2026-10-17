using System.Text;
using Microsoft.Extensions.Logging;

namespace Moser.RagAi;

public sealed class GeminiAnswerGenerator : IAnswerGenerator
{
    public const string DefaultModel = "gemini-3.8-flash";

    private readonly HttpClient _http;
    private readonly string _model;
    private readonly string? _apiKey;
    private readonly ILogger<GeminiAnswerGenerator> _logger;

    public GeminiAnswerGenerator(HttpClient http, string model, string? apiKey, ILogger<GeminiAnswerGenerator>? logger = null)
    {
        _http = http;
        _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<GeminiAnswerGenerator>.Instance;
    }

    public async Task<Generation> GenerateAsync(string question, IReadOnlyList<Passage> passages, CancellationToken cancellationToken)
    {
        if (_apiKey is null)
            return new Generation(GenerationStatus.NotConfigured, null);

        var body = GeminiPayload.Create(GroundedPrompt.System, GroundedPrompt.User(question, passages));
        using var request = new HttpRequestMessage(HttpMethod.Post, $"v1beta/models/{Uri.EscapeDataString(_model)}:generateContent");
        request.Headers.TryAddWithoutValidation("x-goog-api-key", _apiKey);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "The model request failed.");
            return new Generation(GenerationStatus.Failed, null);
        }

        using (response)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                _logger.LogWarning("The model returned 429.");
                return new Generation(GenerationStatus.RateLimited, null);
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("The model returned {StatusCode}.", (int)response.StatusCode);
                return new Generation(GenerationStatus.Failed, null);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var text = GeminiPayload.ReadText(json);
            if (string.IsNullOrWhiteSpace(text))
                return new Generation(GenerationStatus.Failed, null);

            return new Generation(GenerationStatus.Answered, text);
        }
    }
}
