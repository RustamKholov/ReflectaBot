using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using ReflectaBot.Models;
using ReflectaBot.Services.Embedding;

namespace ReflectaBot.Services.Intent;

public record IntentExample(string Intent, string Text, float[] Embedding);

public class IntentRouter : IIntentRouter
{
    private readonly IOptions<IntentConfiguration> _config;
    private readonly ILlmClassifier _llmClassifier;
    private readonly ILogger<IntentRouter> _logger;
    private readonly string[] _supportedIntents =
        {
            "joke", "dice", "time", "fact", "coin", "greeting", "weather", "none"
        };


    private const double HighThreshold = 0.78;
    private const double LowThreshold = 0.55;

    public IntentRouter(IOptions<IntentConfiguration> config, ILlmClassifier llmClassifier, ILogger<IntentRouter> logger)
    {
        _logger = logger;
        _config = config;
        _llmClassifier = llmClassifier;

    }
    public async Task<(string Intent, double Score)> RouteAsync(string userText, CancellationToken ct = default)
    {
        try
        {
            _logger.LogDebug("Classifying intent for text: {Text}", userText);

            var intent = await _llmClassifier.ClassifyAsync(userText, _supportedIntents, ct);

            if (string.IsNullOrEmpty(intent) || intent == "none")
            {
                _logger.LogDebug("No clear intent detected for: {Text}", userText);
                return ("none", 0.1);
            }

            _logger.LogInformation("Intent classified: {Intent} for text: {Text}", intent, userText);
            return (intent, 0.9);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error classifying intent for text: {Text}", userText);
            return ("none", 0.0);
        }
    }

    private static List<IntentExample> LoadExamples(string path)
    {
        var raw = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(raw);
        var list = new List<IntentExample>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var intent = el.GetProperty("intent").GetString()!;
            var text = el.GetProperty("text").GetString()!;
            if (el.TryGetProperty("embedding", out var embEl) && embEl.ValueKind == JsonValueKind.Array)
            {
                var emb = embEl.EnumerateArray().Select(x => x.GetSingle()).ToArray();
                list.Add(new IntentExample(intent, text, emb));
            }
            else
            {
                list.Add(new IntentExample(intent, text, null!));
            }
        }
        return list;
    }
    public static double CosineSimilarity(float[] vectorA, float[] vectorB)
    {
        if (vectorA == null || vectorB == null)
        {
            throw new ArgumentNullException("Vectors cannot be null");
        }

        if (vectorA.Length == 0 || vectorB.Length == 0)
        {
            throw new ArgumentException("Vectors cannot be empty");
        }

        if (vectorA.Length != vectorB.Length)
        {
            throw new ArgumentException($"Vector dimensions must match. VectorA: {vectorA.Length}, VectorB: {vectorB.Length}");
        }

        double dot = 0.0;
        double normA = 0.0;
        double normB = 0.0;

        for (int i = 0; i < vectorA.Length; i++)
        {
            dot += vectorA[i] * vectorB[i];
            normA += vectorA[i] * vectorA[i];
            normB += vectorB[i] * vectorB[i];
        }

        if (normA == 0.0 || normB == 0.0)
        {
            return 0.0;
        }

        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
