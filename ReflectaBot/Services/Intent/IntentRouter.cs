using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using ReflectaBot.Interfaces.Intent;
using ReflectaBot.Models;
using ReflectaBot.Models.Configuration;
using ReflectaBot.Models.Intent;
using ReflectaBot.Services.Embedding;

namespace ReflectaBot.Services.Intent;

public class IntentRouter : IIntentRouter
{
    private readonly EmbeddingHelper _embeddingHelper;
    private readonly IntentEmbeddingService _embeddingService;
    private readonly ILlmClassifier _llmClassifier;
    private readonly ILogger<IntentRouter> _logger;
    private readonly IntentConfiguration _config;

    private List<IntentEmbeddingData> _intentEmbeddings = new();
    private readonly Task _initializationTask;

    private const double HighConfidenceThreshold = 0.78;
    private const double MediumConfidenceThreshold = 0.55;
    private const double MinimumConfidenceThreshold = 0.30;

    public IntentRouter(
        EmbeddingHelper embeddingHelper,
        IntentEmbeddingService embeddingService,
        ILlmClassifier llmClassifier,
        ILogger<IntentRouter> logger,
        IOptions<IntentConfiguration> config)
    {
        _embeddingHelper = embeddingHelper;
        _embeddingService = embeddingService;
        _llmClassifier = llmClassifier;
        _logger = logger;
        _config = config.Value;

        _initializationTask = LoadEmbeddingsAsync();
    }

    private async Task LoadEmbeddingsAsync()
    {
        try
        {
            var database = await _embeddingService.LoadIntentEmbeddingsAsync();

            if (database?.Intents != null && database.Intents.Count > 0)
            {
                _intentEmbeddings = database.Intents;
                _logger.LogInformation("Loaded {Count} intent embeddings covering {IntentTypes} intent types",
                    _intentEmbeddings.Count,
                    _intentEmbeddings.Select(x => x.Intent).Distinct().Count());

                var intentCounts = _intentEmbeddings.GroupBy(x => x.Intent)
                    .ToDictionary(g => g.Key, g => g.Count());
                _logger.LogDebug("Intent distribution: {IntentCounts}",
                    string.Join(", ", intentCounts.Select(kv => $"{kv.Key}:{kv.Value}")));
            }
            else
            {
                _logger.LogWarning("No intent embeddings found. Initialize embeddings first!");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load intent embeddings, -> LLM fallback");
        }
    }

    public async Task<(string Intent, double Score)> RouteAsync(string userText, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userText))
        {
            _logger.LogWarning("Empty or null user text provided");
            return (ContentIntents.None, 0.0);
        }

        await _initializationTask;

        if (_intentEmbeddings.Count == 0)
        {
            _logger.LogDebug("No embeddings available, using LLM-only classification");
            return await ClassifyWithLlmFallback(userText, ct);
        }

        try
        {
            var userEmbedding = await _embeddingHelper.GetEmbeddingAsync(userText, ct);

            var similarities = CalculateIntentSimilarities(userEmbedding);

            var bestMatch = similarities.First();
            var bestIntent = bestMatch.Intent;
            var bestScore = bestMatch.Similarity;

            _logger.LogDebug("Intent classification - Best match: {Intent} ({Score:F3}) from text: '{ExampleText}'",
                bestIntent, bestScore, bestMatch.ExampleText);

            if (bestScore >= HighConfidenceThreshold)
            {
                await SaveSuccessfulClassification(bestIntent, userText, userEmbedding);
                return (bestIntent, bestScore);
            }

            if (bestScore >= MediumConfidenceThreshold)
            {
                _logger.LogDebug("Medium confidence ({Score:F3}), verifying with LLM", bestScore);

                var verifiedIntent = await VerifyWithLlm(userText, bestIntent, similarities.Take(3).ToList(), ct);

                if (!string.IsNullOrEmpty(verifiedIntent) && verifiedIntent != ContentIntents.None)
                {
                    await SaveSuccessfulClassification(verifiedIntent, userText, userEmbedding);
                    return (verifiedIntent, bestScore);
                }
            }

            if (bestScore >= MinimumConfidenceThreshold)
            {
                _logger.LogDebug("Low confidence ({Score:F3}), using full LLM classification", bestScore);
                return await ClassifyWithLlmFallback(userText, ct, userEmbedding);
            }

            _logger.LogDebug("Very low confidence ({Score:F3}), classifying as unknown", bestScore);
            await SaveUnknownClassification(userText, userEmbedding);
            return (ContentIntents.None, bestScore);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in embedding-based classification, falling back to LLM");
            return await ClassifyWithLlmFallback(userText, ct);
        }
    }

    private List<(string Intent, double Similarity, string ExampleText)> CalculateIntentSimilarities(
        float[] userEmbedding)
    {
        var similarities = _intentEmbeddings
            .Select(example => new
            {
                example.Intent,
                example.Text,
                Similarity = CosineSimilarity(userEmbedding, example.Embedding)
            })
            .Where(x => x.Similarity > 0.1)
            .OrderByDescending(x => x.Similarity)
            .Take(20)
            .Select(x => (x.Intent, x.Similarity, x.Text))
            .ToList();

        var topMatches = similarities.Take(5);
        _logger.LogDebug("Top intent matches: {Matches}",
            string.Join(", ", topMatches.Select(m => $"{m.Intent}:{m.Similarity:F3}")));

        return similarities;
    }

    private async Task<string> VerifyWithLlm(
        string userText,
        string suggestedIntent,
        List<(string Intent, double Similarity, string ExampleText)> topMatches,
        CancellationToken ct)
    {
        try
        {
            var candidateIntents = topMatches
                .Select(m => m.Intent)
                .Distinct()
                .Take(8)
                .Append(ContentIntents.None)
                .ToArray();

            _logger.LogDebug("LLM verification with candidates: {Candidates}", string.Join(", ", candidateIntents));

            var llmResult = await _llmClassifier.ClassifyAsync(userText, candidateIntents, ct);

            _logger.LogDebug("LLM verification result: {LlmResult} (suggested: {SuggestedIntent})",
                llmResult, suggestedIntent);

            return llmResult ?? ContentIntents.None;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM verification failed, using embedding suggestion: {Intent}", suggestedIntent);
            return suggestedIntent;
        }
    }

    private async Task<(string Intent, double Score)> ClassifyWithLlmFallback(
        string userText,
        CancellationToken ct,
        float[]? userEmbedding = null)
    {
        try
        {
            var allIntents = GetAllAvailableIntents();

            _logger.LogDebug("LLM fallback classification with {IntentCount} intents", allIntents.Length);

            var llmIntent = await _llmClassifier.ClassifyAsync(userText, allIntents, ct);

            if (!string.IsNullOrEmpty(llmIntent) && llmIntent != ContentIntents.None)
            {
                if (userEmbedding != null)
                {
                    await SaveSuccessfulClassification(llmIntent, userText, userEmbedding);
                }
                else
                {
                    try
                    {
                        var embedding = await _embeddingHelper.GetEmbeddingAsync(userText, ct);
                        await SaveSuccessfulClassification(llmIntent, userText, embedding);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to save LLM classification for future training");
                    }
                }

                return (llmIntent, 0.75);
            }

            return (ContentIntents.None, 0.3);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM fallback classification failed");
            return (ContentIntents.None, 0.0);
        }
    }

    private static string[] GetAllAvailableIntents()
    {
        var contentIntents = ContentIntents.AllIntents;
        var legacyIntents = new[] { "joke", "humor", "dice", "roll", "random", "fact", "trivia", "coin", "flip", "time", "clock", "weather" };

        return contentIntents.Concat(legacyIntents).Distinct().ToArray();
    }

    private async Task SaveSuccessfulClassification(string intent, string userText, float[] embedding)
    {
        try
        {
            await _embeddingService.SaveFallbackEmbeddingAsync(intent, userText, embedding);
            _logger.LogDebug("Saved successful classification: {Intent} for text: '{Text}'", intent, userText);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save successful classification for intent: {Intent}", intent);
        }
    }

    private async Task SaveUnknownClassification(string userText, float[] embedding)
    {
        try
        {
            await _embeddingService.SaveFallbackEmbeddingAsync(ContentIntents.None, userText, embedding);
            _logger.LogDebug("Saved unknown classification for text: '{Text}'", userText);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save unknown classification");
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
            var a = vectorA[i];
            var b = vectorB[i];

            dot += a * b;
            normA += a * a;
            normB += b * b;
        }

        if (normA == 0.0 || normB == 0.0)
        {
            return 0.0;
        }

        var similarity = dot / (Math.Sqrt(normA) * Math.Sqrt(normB));

        return Math.Max(-1.0, Math.Min(1.0, similarity));
    }

    public async Task<IntentRouterDiagnostics> GetDiagnosticsAsync()
    {
        await _initializationTask;

        var intentCounts = _intentEmbeddings
            .GroupBy(x => x.Intent)
            .ToDictionary(g => g.Key, g => g.Count());

        return new IntentRouterDiagnostics
        {
            TotalEmbeddings = _intentEmbeddings.Count,
            UniqueIntents = intentCounts.Keys.Count,
            IntentDistribution = intentCounts,
            IsInitialized = _intentEmbeddings.Count > 0,
            HighConfidenceThreshold = HighConfidenceThreshold,
            MediumConfidenceThreshold = MediumConfidenceThreshold,
            MinimumConfidenceThreshold = MinimumConfidenceThreshold
        };
    }
}
