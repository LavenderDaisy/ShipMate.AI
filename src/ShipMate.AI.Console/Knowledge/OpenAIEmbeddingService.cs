using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ShipMate.AI.Console.Knowledge;

/// <summary>
/// Embedding service backed by an OpenAI-compatible <c>/embeddings</c> endpoint. Works with
/// OpenAI (text-embedding-3-small), Zhipu (embedding-3), and other compatible providers.
/// Produces semantically rich vectors. Falls back is handled by the factory: if this can't
/// be configured or fails, the app uses <see cref="HashingEmbeddingService"/> instead.
/// </summary>
public sealed class OpenAIEmbeddingService : IEmbeddingService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _model;
    private readonly string _endpoint;

    public OpenAIEmbeddingService(
        string apiKey, string model, string baseUrl, int dimensions, HttpClient? httpClient = null)
    {
        _model = model;
        _endpoint = baseUrl.TrimEnd('/') + "/embeddings";
        Dimensions = dimensions;

        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public int Dimensions { get; }

    public float[] Embed(string text) => EmbedAsync(text).GetAwaiter().GetResult();

    private async Task<float[]> EmbedAsync(string text)
    {
        var payload = new { model = _model, input = text };
        using var content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await _httpClient.PostAsync(_endpoint, content);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            var snippet = body.Length <= 300 ? body : body[..300] + "...";
            throw new InvalidOperationException(
                $"Embedding request failed ({(int)response.StatusCode}): {snippet}");
        }

        using var doc = JsonDocument.Parse(body);
        var array = doc.RootElement.GetProperty("data")[0].GetProperty("embedding");

        var vector = new float[array.GetArrayLength()];
        var i = 0;
        foreach (var el in array.EnumerateArray())
        {
            vector[i++] = el.GetSingle();
        }
        return vector;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
