using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using ReflectaBot.Models;

namespace ReflectaBot.Services.Intent
{
    public class LlmClassifier : ILlmClassifier
    {
        IOptions<LlmConfiguration> _config;
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _endpoint;
        private readonly string _model;
        public LlmClassifier(IOptions<LlmConfiguration> config, HttpClient httpClient)
        {
            _config = config;
            _httpClient = httpClient;
            _apiKey = _config.Value.ApiKey;
            _endpoint = _config.Value.BaseUrl;
            _model = _config.Value.ModelName;
        }
        public async Task<string?> ClassifyAsync(string text, string[] labels, CancellationToken ct = default)
        {
            var labelList = string.Join(", ", labels);
            var prompt = $@"You are a classifier. Possible labels: {labelList}, or 'none'.
                            Read the user text and return exactly one label (no explanation), only the label string.

                            User text:
                            ""{text}""
                            Return label:";
            var payload = new
            {
                model = _model,
                messages = new[] {
                new { role = "system", content = "You are a concise label classifier." },
                new { role = "user", content = prompt }
            },
                temperature = 0.0,
                max_tokens = 16
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            using var res = await _httpClient.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
            {
                return null;
            }
            using var stream = await res.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            try
            {
                var content = doc.RootElement
                            .GetProperty("choices")[0]
                            .GetProperty("message")
                            .GetProperty("content")
                            .GetString()!
                            .Trim();
                var normalized = labels.FirstOrDefault(l => string.Equals(l, content, StringComparison.OrdinalIgnoreCase));
                if (normalized != null) return normalized;

                //fallback
                var firstToken = content.Split(new[] { '\n', ' ', '\r' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (firstToken != null && labels.Any(l => string.Equals(l, firstToken, StringComparison.OrdinalIgnoreCase)))
                    return labels.First(l => string.Equals(l, firstToken, StringComparison.OrdinalIgnoreCase));

                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}