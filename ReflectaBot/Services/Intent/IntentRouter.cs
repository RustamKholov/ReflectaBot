using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using ReflectaBot.Models;
using ReflectaBot.Models.Configuration;
using ReflectaBot.Models.Intent;
using ReflectaBot.Services.Embedding;

namespace ReflectaBot.Services.Intent;

public record IntentExample(string Intent, string Text, float[] Embedding);

public class IntentRouter : IIntentRouter
{
    private readonly EmbeddingHelper _embeddingHelper;
    private readonly IntentEmbeddingService _embeddingService;
    private readonly ILlmClassifier _llmClassifier;
    private readonly ILogger<IntentRouter> _logger;
    private List<IntentEmbeddingData> _intentEmbeddings = new();
    private readonly Task _initializationTask;

    private const double HighThreshold = 0.78;
    private const double LowThreshold = 0.55;

    public IntentRouter(EmbeddingHelper embeddingHelper, IntentEmbeddingService embeddingService, ILlmClassifier llmClassifier, ILogger<IntentRouter> logger)
    {
        _embeddingHelper = embeddingHelper;
        _embeddingService = embeddingService;
        _logger = logger;
        _llmClassifier = llmClassifier;

        _initializationTask = LoadEmbeddingsAsync();

    }

    private async Task LoadEmbeddingsAsync()
    {
        var database = await _embeddingService.LoadIntentEmbeddingsAsync();

        if (database?.Intents != null)
        {
            _intentEmbeddings = database.Intents;
            _logger.LogInformation("Loaded {Count} intent embeddings", _intentEmbeddings.Count);
        }
        else
        {
            _logger.LogWarning(" No intent embeddings found. Generate them first!");
        }
    }
    public async Task<(string Intent, double Score)> RouteAsync(string userText, CancellationToken ct = default)
    {
        await _initializationTask;

        //fallback
        if (_intentEmbeddings.Count == 0)
        {
            _logger.LogDebug("No embeddings available, using LLM fallback");
            var llmIntent = await _llmClassifier.ClassifyAsync(userText,
                    new[] { "joke", "dice", "time", "fact", "coin", "greeting", "weather", "none" }, ct);

            if (!string.IsNullOrEmpty(llmIntent) && llmIntent != "none")
            {
                try
                {
                    var userEmbedding = await _embeddingHelper.GetEmbeddingAsync(userText, ct);
                    await _embeddingService.SaveFallbackEmbeddingAsync(llmIntent, userText, userEmbedding);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to save fallback embedding for: {Text}", userText);
                }
            }

            return (llmIntent ?? "none", 0.8);
        }

        try
        {
            var userEmbedding = await _embeddingHelper.GetEmbeddingAsync(userText, ct);

            var similarities = _intentEmbeddings
                .Select(example => new
                {
                    example.Intent,
                    example.Text,
                    Similarity = CosineSimilarity(userEmbedding, example.Embedding)
                })
                .OrderByDescending(x => x.Similarity)
                .Take(10)
                .ToList();

            var bestMatch = similarities.First();
            var bestIntent = bestMatch.Intent;
            var bestScore = bestMatch.Similarity;

            _logger.LogDebug("Best match: {Intent} ({Score:F3}) - '{Text}'",
                    bestIntent, bestScore, bestMatch.Text);

            if (bestScore >= HighThreshold)
            {
                await _embeddingService.SaveFallbackEmbeddingAsync(bestIntent, userText, userEmbedding);
                return (bestIntent, bestScore);
            }

            //fallback clarify with llm
            if (bestScore >= LowThreshold)
            {
                _logger.LogDebug("Medium confidence, using LLM verification");
                var llmIntent = await _llmClassifier.ClassifyAsync(userText,
                    new[] { "joke", "dice", "time", "fact", "coin", "greeting", "weather", "none" }, ct);

                if (!string.IsNullOrEmpty(llmIntent) && llmIntent != "none")
                {
                    await _embeddingService.SaveFallbackEmbeddingAsync(llmIntent, userText, userEmbedding);
                }

                return (llmIntent ?? bestIntent, bestScore);
            }

            await _embeddingService.SaveFallbackEmbeddingAsync("none", userText, userEmbedding);
            return ("none", bestScore);

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in embedding-based routing, falling back to LLM");
            var llmIntent = await _llmClassifier.ClassifyAsync(userText,
                new[] { "joke", "dice", "time", "fact", "coin", "greeting", "weather", "none" }, ct);

            //save fallback even in error cases
            if (!string.IsNullOrEmpty(llmIntent) && llmIntent != "none")
            {
                try
                {
                    var userEmbedding = await _embeddingHelper.GetEmbeddingAsync(userText, ct);
                    await _embeddingService.SaveFallbackEmbeddingAsync(llmIntent, userText, userEmbedding);
                }
                catch
                {
                    //ignore save errors in error fallback
                }
            }

            return (llmIntent ?? "none", 0.5);
        }
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
