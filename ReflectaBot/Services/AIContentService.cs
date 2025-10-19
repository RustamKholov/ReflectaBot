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

                var estimatedTokens = EstimateTokenUsage(content.Content);

                var prompt = $"""
                    Generate a concise, 3-bullet point summary of the following text.
                    Focus on the main concepts, key insights, and actionable takeaways.
                    Keep each bullet point under 50 words.

                    Text:
                    {content.Content}
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

                var estimatedTokens = EstimateTokenUsage(content.Content);

                var prompt = """
                    Analyze the following text and create exactly 3 multiple-choice questions to test understanding of the key concepts.
                    Each question should have 4 options with only one correct answer.
                    Include a brief explanation for the correct answer.
                    
                    IMPORTANT: Respond ONLY with valid JSON in this exact format (no additional text, no markdown formatting):
                    {
                        "questions": [
                            {
                                "question": "Question text here?",
                                "options": ["Option A", "Option B", "Option C", "Option D"],
                                "correctAnswer": 0,
                                "explanation": "Why this answer is correct"
                            },
                            {
                                "question": "Second question text?",
                                "options": ["Option A", "Option B", "Option C", "Option D"],
                                "correctAnswer": 1,
                                "explanation": "Why this answer is correct"
                            },
                            {
                                "question": "Third question text?",
                                "options": ["Option A", "Option B", "Option C", "Option D"],
                                "correctAnswer": 2,
                                "explanation": "Why this answer is correct"
                            }
                        ]
                    }

                    Text to analyze:
                    """ + content.Content;

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
                _logger.LogDebug("Raw AI response: {JsonContent}", jsonContent);

                var cleanJson = jsonContent.Trim();
                if (cleanJson.StartsWith("```json"))
                {
                    cleanJson = cleanJson.Substring(7);
                }
                if (cleanJson.EndsWith("```"))
                {
                    cleanJson = cleanJson.Substring(0, cleanJson.Length - 3);
                }
                cleanJson = cleanJson.Trim();

                _logger.LogDebug("Cleaned JSON: {CleanJson}", cleanJson);

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var quizData = JsonSerializer.Deserialize<QuizJsonResponse>(cleanJson, options);

                if (quizData?.questions == null || !quizData.questions.Any())
                {
                    _logger.LogWarning("No questions found in parsed JSON, attempting manual parsing");
                    return ParseQuizManually(cleanJson);
                }

                _logger.LogInformation("Successfully parsed {QuestionCount} questions", quizData.questions.Length);
                return quizData.questions;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse quiz JSON: {JsonContent}", jsonContent);
                return ParseQuizManually(jsonContent);
            }
        }

        private QuizQuestion[] ParseQuizManually(string content)
        {
            try
            {
                _logger.LogInformation("Attempting manual quiz parsing");


                var questions = new List<QuizQuestion>();


                var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                QuizQuestion? currentQuestion = null;
                var options = new List<string>();

                foreach (var line in lines)
                {
                    var trimmed = line.Trim();


                    if (trimmed.Contains("?") && !trimmed.StartsWith("[") && !trimmed.StartsWith("A.") &&
                        !trimmed.StartsWith("B.") && !trimmed.StartsWith("C.") && !trimmed.StartsWith("D."))
                    {

                        if (currentQuestion != null && options.Count >= 4)
                        {
                            currentQuestion.Options = options.Take(4).ToArray();
                            questions.Add(currentQuestion);
                        }

                        currentQuestion = new QuizQuestion
                        {
                            Question = trimmed,
                            CorrectAnswer = 0,
                            Explanation = "Explanation not provided"
                        };
                        options.Clear();
                    }

                    else if (trimmed.StartsWith("A.") || trimmed.StartsWith("B.") ||
                             trimmed.StartsWith("C.") || trimmed.StartsWith("D.") ||
                             (options.Count < 4 && trimmed.Length > 3))
                    {
                        var option = trimmed.StartsWith("A.") || trimmed.StartsWith("B.") ||
                                   trimmed.StartsWith("C.") || trimmed.StartsWith("D.")
                            ? trimmed.Substring(2).Trim()
                            : trimmed;
                        options.Add(option);
                    }
                }

                if (currentQuestion != null && options.Count >= 4)
                {
                    currentQuestion.Options = options.Take(4).ToArray();
                    questions.Add(currentQuestion);
                }

                if (questions.Any())
                {
                    _logger.LogInformation("Manual parsing extracted {QuestionCount} questions", questions.Count);
                    return questions.ToArray();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Manual parsing failed");
            }

            return CreateFallbackQuiz();
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