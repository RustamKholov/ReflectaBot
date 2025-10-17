using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReflectaBot.Interfaces;
using ReflectaBot.Models.AI;
using ReflectaBot.Models.Configuration;
using ReflectaBot.Models.Content;

namespace ReflectaBot.Services
{
    public class AIContentService : IAIContentService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<AIContentService> _logger;
        private readonly LlmConfiguration _config;

        public AIContentService(
            HttpClient httpClient,
            ILogger<AIContentService> logger,
            IOptions<LlmConfiguration> config)
        {
            _httpClient = httpClient;
            _logger = logger;
            _config = config.Value;

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_config.ApiKey}");
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "ReflectaBot/1.0");
        }

        public async Task<AISummaryResult> GenerateSummaryAsync(ProcessedContent content, CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;

            try
            {
                _logger.LogInformation("Generating AI summary for content: {WordCount} words", content.WordCount);

                var optimizedContent = OptimizeContentForAI(content.Content, maxTokens: 2000);
                var estimatedTokens = EstimateTokenUsage(optimizedContent);

                var prompt = $"""
                    Generate a concise, 3-bullet point summary of the following text.
                    Focus on the main concepts, key insights, and actionable takeaways.
                    Keep each bullet point under 50 words.

                    Text:
                    {optimizedContent}
                    """;

                var response = await CallOpenAIAsync(prompt, maxTokens: 200, cancellationToken);

                if (response.Success)
                {
                    var processingTime = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;

                    return new AISummaryResult
                    {
                        Success = true,
                        Summary = FormatSummaryBullets(response.Content),
                        KeyPoints = ExtractKeyPoints(response.Content),
                        ProcessingTimeMs = processingTime,
                        TokensUsed = estimatedTokens + response.TokensUsed
                    };
                }

                return new AISummaryResult
                {
                    Success = false,
                    Error = response.Error ?? "Failed to generate summary"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating AI summary");
                return new AISummaryResult
                {
                    Success = false,
                    Error = $"AI service error: {ex.Message}"
                };
            }
        }

        public async Task<AIQuizResult> GenerateQuizAsync(ProcessedContent content, CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;

            try
            {
                _logger.LogInformation("Generating AI quiz for content: {WordCount} words", content.WordCount);

                var optimizedContent = OptimizeContentForAI(content.Content, maxTokens: 2500);
                var estimatedTokens = EstimateTokenUsage(optimizedContent);

                var jsonTemplate = """
                    {
                        "questions": [
                            {
                                "question": "Question text here?",
                                "options": ["Option A", "Option B", "Option C", "Option D"],
                                "correctAnswer": 0,
                                "explanation": "Why this answer is correct"
                            }
                        ]
                    }
                    """;

                var prompt = $"""
                    Analyze the following text and create exactly 3 multiple-choice questions to test understanding of the key concepts.
                    Each question should have 4 options (A, B, C, D) with only one correct answer.
                    Include a brief explanation for the correct answer.
                    
                    Format your response as JSON using this exact structure:
                    {jsonTemplate}

                    Text:
                    {optimizedContent}
                    """;

                var response = await CallOpenAIAsync(prompt, maxTokens: 800, cancellationToken);

                if (response.Success)
                {
                    var questions = ParseQuizFromJson(response.Content);
                    var processingTime = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;

                    return new AIQuizResult
                    {
                        Success = true,
                        Questions = questions,
                        ProcessingTimeMs = processingTime,
                        TokensUsed = estimatedTokens + response.TokensUsed
                    };
                }

                return new AIQuizResult
                {
                    Success = false,
                    Error = response.Error ?? "Failed to generate quiz"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating AI quiz");
                return new AIQuizResult
                {
                    Success = false,
                    Error = $"AI service error: {ex.Message}"
                };
            }
        }

        #region Private Helper Methods
        private async Task<OpenAIResponse> CallOpenAIAsync(string prompt, int maxTokens, CancellationToken cancellationToken)
        {
            try
            {
                var requestBody = new
                {
                    model = _config.ModelName,
                    messages = new[]
                    {
                        new { role = "user", content = prompt }
                    },
                    max_tokens = maxTokens,
                    temperature = 0.3,
                    top_p = 1.0,
                    frequency_penalty = 0.0,
                    presence_penalty = 0.0
                };

                var json = JsonSerializer.Serialize(requestBody);
                var httpContent = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(_config.BaseUrl, httpContent, cancellationToken);
                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var openAIResponse = JsonSerializer.Deserialize<OpenAIApiResponse>(responseContent);
                    var content = openAIResponse?.choices?.FirstOrDefault()?.message?.content ?? "";
                    var tokensUsed = openAIResponse?.usage?.total_tokens ?? 0;

                    return new OpenAIResponse
                    {
                        Success = true,
                        Content = content.Trim(),
                        TokensUsed = tokensUsed
                    };
                }

                _logger.LogWarning("OpenAI API error: {StatusCode} - {Response}", response.StatusCode, responseContent);
                return new OpenAIResponse
                {
                    Success = false,
                    Error = $"API error: {response.StatusCode}"
                };
            }
            catch (TaskCanceledException)
            {
                return new OpenAIResponse
                {
                    Success = false,
                    Error = "Request timeout"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OpenAI API call failed");
                return new OpenAIResponse
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }


        private static string OptimizeContentForAI(string content, int maxTokens)
        {
            var estimatedTokens = EstimateTokenUsage(content);

            if (estimatedTokens <= maxTokens)
                return content;

            var targetLength = maxTokens * 4;
            var halfLength = targetLength / 2;

            if (content.Length <= targetLength)
                return content;

            var firstPart = content.Substring(0, halfLength);
            var lastPart = content.Substring(content.Length - halfLength);

            return $"{firstPart}\n\n[... content truncated ...]\n\n{lastPart}";
        }

        private static string FormatSummaryBullets(string summary)
        {
            var lines = summary.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var bullets = new List<string>();

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                if (!trimmed.StartsWith("•") && !trimmed.StartsWith("-") && !trimmed.StartsWith("*"))
                {
                    trimmed = "• " + trimmed;
                }

                bullets.Add(trimmed);
            }

            return string.Join("\n", bullets);
        }


        private static string[] ExtractKeyPoints(string summary)
        {
            return summary.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                         .Select(line => line.Trim().TrimStart('•', '-', '*', ' '))
                         .Where(line => !string.IsNullOrEmpty(line))
                         .Take(3)
                         .ToArray();
        }

        private QuizQuestion[] ParseQuizFromJson(string jsonContent)
        {
            try
            {
                var cleanJson = jsonContent.Trim();
                if (cleanJson.StartsWith("```json"))
                {
                    cleanJson = cleanJson.Substring(7);
                }
                if (cleanJson.EndsWith("```"))
                {
                    cleanJson = cleanJson.Substring(0, cleanJson.Length - 3);
                }

                var quizData = JsonSerializer.Deserialize<QuizJsonResponse>(cleanJson);
                return quizData?.questions ?? Array.Empty<QuizQuestion>();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse quiz JSON, using fallback parsing");
                return CreateFallbackQuiz();
            }
        }
        private static QuizQuestion[] CreateFallbackQuiz()
        {
            return new[]
            {
                new QuizQuestion
                {
                    Question = "Based on the content, what was the main topic discussed?",
                    Options = new[] { "Technology", "Education", "Business", "Science" },
                    CorrectAnswer = 0,
                    Explanation = "The content focused primarily on the main topic area."
                }
            };
        }
        private static int EstimateTokenUsage(string text)
        {
            return (int)Math.Ceiling(text.Length / 4.0);
        }

        #endregion

    }
}