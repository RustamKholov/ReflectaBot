using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using ReflectaBot.Models;
using ReflectaBot.Models.Configuration;

namespace ReflectaBot.Services.Embedding;

public class EmbeddingHelper
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _endpoint;
    private readonly string _embeddingModel;
    public EmbeddingHelper(IOptions<EmbeddingConfiguration> config, HttpClient client)
    {
        _httpClient = client;
        _apiKey = config.Value.ApiKey;
        _endpoint = config.Value.BaseUrl;
        _embeddingModel = config.Value.ModelName;
    }

    public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default)
    {
        var payload = new { input = text.Trim(), model = _embeddingModel, encoding_format = "float" };
        using var req = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        using var res = await _httpClient.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();

        using var stream = await res.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var data = doc.RootElement.GetProperty("data")[0].GetProperty("embedding");
        var list = new List<float>(data.GetArrayLength());
        foreach (var el in data.EnumerateArray())
            list.Add(el.GetSingle());
        return list.ToArray();
    }
}
