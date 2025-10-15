using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ReflectaBot.Models.Intent;
using ReflectaBot.Services.Embedding;

namespace ReflectaBot.Services.Intent
{
    public class IntentEmbeddingService
    {
        private readonly EmbeddingHelper _embeddingHelper;
        private readonly ILlmClassifier _llmClassifier;
        private readonly ILogger<IntentEmbeddingService> _logger;
        private List<IntentEmbeddingData>? _intentEmbeddings;
        private readonly string _dataPath;

        public IntentEmbeddingService(
            EmbeddingHelper embeddingHelper,
            ILlmClassifier llmClassifier,
            ILogger<IntentEmbeddingService> logger,
            IWebHostEnvironment environment
        )
        {
            _embeddingHelper = embeddingHelper;
            _llmClassifier = llmClassifier;
            _logger = logger;
            _dataPath = Path.Combine(environment.ContentRootPath, "Data");
            Directory.CreateDirectory(_dataPath);
        }

        public async Task<bool> GenerateIntentEmbeddingAsync(List<IntentDefinition> definitions)
        {
            try
            {
                _logger.LogInformation("Starting intent ebedding generation for {Count} intents", definitions.Count);
                var allEmbeddings = new List<IntentEmbeddingData>();
                var totalExamples = 0;

                foreach (var definition in definitions)
                {
                    _logger.LogInformation("Processing intent: {Intent}", definition.Intent);
                    var generatedExamples = await GenerateAdditionalExamples(definition);
                    var allExamples = definition.Examples.Concat(generatedExamples).ToList();

                    _logger.LogInformation("Generated {Generated} examples for {Intent} (total: {Total})",
                        generatedExamples.Count, definition.Intent, allExamples.Count);

                    foreach (var example in allExamples)
                    {
                        try
                        {
                            var ebedding = await _embeddingHelper.GetEmbeddingAsync(example);
                            allEmbeddings.Add(new IntentEmbeddingData
                            {
                                Intent = definition.Intent,
                                Text = example,
                                Embedding = ebedding,
                                GeneratedAt = DateTime.UtcNow
                            });
                            totalExamples++;

                            await Task.Delay(100);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to generate embedding for: {Text}", example);
                        }
                    }
                }

                var database = new IntentEmbeddingDatabase
                {
                    Version = "1.0",
                    Model = "text-embedding-3-small",
                    CreatedAt = DateTime.UtcNow,
                    Intents = allEmbeddings
                };

                await SaveToJsonFile(database);

                _logger.LogInformation("Successfully generated {TotalExamples} embeddings for {IntentCount} intents",
                    totalExamples, definitions.Count);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate intent embeddings");
                return false;
            }
        }

        private async Task<List<string>> GenerateAdditionalExamples(IntentDefinition definition)
        {
            try
            {
                var prompt = $@"Generate 10 different ways a user might express the intent '{definition.Intent}' in a chat conversation.

                    Intent: {definition.Intent}
                    Description: {definition.Description}
                    Existing examples: {string.Join(", ", definition.Examples)}

                    Generate variations that include:
                    - Different formality levels (casual to formal)
                    - Different phrase lengths (short and long)
                    - Common typos or informal language
                    - Questions vs statements
                    - Different emotional tones

                    Return ONLY a JSON array of strings, nothing else:
                    [""example 1"", ""example 2"", ...]";
                var response = await _llmClassifier.GenerateTextAsync(prompt);
                var examples = JsonSerializer.Deserialize<List<string>>(response);
                return examples ?? new List<string>();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate examples for intent: {Intent}", definition.Intent);
                return new List<string>();
            }
        }
        public async Task SaveFallbackEmbeddingAsync(string intent, string text, float[] embedding)
        {
            try
            {
                var database = await LoadIntentEmbeddingsAsync() ?? new IntentEmbeddingDatabase
                {
                    Version = "1.0",
                    Model = "text-embedding-3-small",
                    CreatedAt = DateTime.UtcNow,
                    Intents = new List<IntentEmbeddingData>()
                };

                var exists = database.Intents.Any(x =>
                    x.Intent == intent &&
                    string.Equals(x.Text, text, StringComparison.OrdinalIgnoreCase));

                if (!exists)
                {
                    database.Intents.Add(new IntentEmbeddingData
                    {
                        Intent = intent,
                        Text = text,
                        Embedding = embedding,
                        GeneratedAt = DateTime.UtcNow
                    });

                    await SaveToJsonFile(database);

                    _intentEmbeddings?.Add(new IntentEmbeddingData
                    {
                        Intent = intent,
                        Text = text,
                        Embedding = embedding,
                        GeneratedAt = DateTime.UtcNow
                    });

                    _logger.LogInformation("Saved fallback embedding: {Intent} - '{Text}'", intent, text);
                }
                else
                {
                    _logger.LogDebug("Embedding already exists: {Intent} - '{Text}'", intent, text);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save fallback embedding: {Intent} - '{Text}'", intent, text);
            }
        }
        public async Task SaveMultipleFallbackEmbeddingsAsync(List<(string Intent, string Text, float[] Embedding)> embeddings)
        {
            try
            {
                var database = await LoadIntentEmbeddingsAsync() ?? new IntentEmbeddingDatabase
                {
                    Version = "1.0",
                    Model = "text-embedding-3-small",
                    CreatedAt = DateTime.UtcNow,
                    Intents = new List<IntentEmbeddingData>()
                };

                var addedCount = 0;
                var existingTexts = new HashSet<string>(
                    database.Intents.Select(x => $"{x.Intent}:{x.Text.ToLowerInvariant()}"));

                foreach (var (intent, text, embedding) in embeddings)
                {
                    var key = $"{intent}:{text.ToLowerInvariant()}";

                    if (!existingTexts.Contains(key))
                    {
                        database.Intents.Add(new IntentEmbeddingData
                        {
                            Intent = intent,
                            Text = text,
                            Embedding = embedding,
                            GeneratedAt = DateTime.UtcNow
                        });

                        existingTexts.Add(key);
                        addedCount++;
                    }
                }

                if (addedCount > 0)
                {
                    await SaveToJsonFile(database);
                    _logger.LogInformation("Saved {Count} new fallback embeddings", addedCount);
                }
                else
                {
                    _logger.LogDebug("No new embeddings to save");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save multiple fallback embeddings");
            }
        }
        private async Task SaveToJsonFile(IntentEmbeddingDatabase database)
        {
            var filePath = Path.Combine(_dataPath, "intent_embeddings.json");
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };
            var json = JsonSerializer.Serialize(database, options);
            await File.WriteAllTextAsync(filePath, json);


            _logger.LogInformation("Saved intent embeddings to: {FilePath}", filePath);


            var backupPath = Path.Combine(_dataPath, $"intent_embeddings_backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
            await File.WriteAllTextAsync(backupPath, json);

        }

        public async Task<IntentEmbeddingDatabase?> LoadIntentEmbeddingsAsync()
        {
            try
            {
                var filePath = Path.Combine(_dataPath, "intent_embeddings.json");

                if (!File.Exists(filePath))
                {
                    _logger.LogWarning("Intent embeddings file not found: {FilePath}", filePath);
                    return null;
                }

                var json = await File.ReadAllTextAsync(filePath);
                var database = JsonSerializer.Deserialize<IntentEmbeddingDatabase>(json);

                _intentEmbeddings = database?.Intents;

                _logger.LogInformation("Loaded {Count} intent embeddings from file",
                    database?.Intents.Count ?? 0);

                return database;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load intent embeddings");
                return null;
            }
        }
    }
}