using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ReflectaBot.Interfaces;
using ReflectaBot.Interfaces.Enums;
using ReflectaBot.Models.Content;

namespace ReflectaBot.Services
{
    public class TextAnalysisService : ITextAnalysisService
    {
        private readonly ILogger<TextAnalysisService> _logger;
        private static readonly Regex SentenceRegex = new(@"[.!?]+", RegexOptions.Compiled);
        private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

        public TextAnalysisService(ILogger<TextAnalysisService> logger)
        {
            _logger = logger;
        }

        public async Task<ProcessedContent> ProcessTextAsync(string text)
        {
            try
            {
                _logger.LogDebug("Processing text content of length: {Length}", text.Length);

                if (string.IsNullOrWhiteSpace(text))
                {
                    return new ProcessedContent
                    {
                        Success = false,
                        Error = "Empty text provided"
                    };
                }

                var cleanedText = CleanAndNormalizeText(text);
                var title = ExtractTitle(cleanedText);
                var content = ExtractMainContent(cleanedText);
                var wordCount = CountWords(content);

                if (wordCount < 50)
                {
                    return new ProcessedContent
                    {
                        Success = false,
                        Error = "Text too short for meaningful analysis (minimum 50 words)"
                    };
                }

                var language = DetectLanguage(content);

                return new ProcessedContent
                {
                    Title = title,
                    Content = content,
                    WordCount = wordCount,
                    Success = true,
                    SourceType = ContentSourceType.PlainText,
                    Metadata = {
                        Language = language,
                        ProcessorUsed = "TextAnalysis"
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing text content");
                return new ProcessedContent
                {
                    Success = false,
                    Error = $"Text processing error: {ex.Message}"
                };
            }
        }

        private static string CleanAndNormalizeText(string text)
        {
            text = WhitespaceRegex.Replace(text.Trim(), " ");

            text = text.Replace("\r\n", "\n")
                      .Replace("\r", "\n")
                      .Replace("\t", " ");

            text = Regex.Replace(text, @"https?://[^\s]+", "", RegexOptions.IgnoreCase);

            return text.Trim();
        }

        private static string ExtractTitle(string text)
        {
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            if (lines.Length > 0)
            {
                var firstLine = lines[0].Trim();

                if (firstLine.Length > 5 && firstLine.Length < 100 &&
                    !firstLine.EndsWith('.') && !firstLine.EndsWith('!') && !firstLine.EndsWith('?'))
                {
                    return firstLine;
                }
            }

            var sentences = SentenceRegex.Split(text);
            if (sentences.Length > 0)
            {
                var firstSentence = sentences[0].Trim();
                if (firstSentence.Length > 10 && firstSentence.Length < 150)
                {
                    return firstSentence;
                }
            }

            return "Text Content";
        }

        private static string ExtractMainContent(string text)
        {
            return text;
        }

        private static int CountWords(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            return text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
        }

        private static string DetectLanguage(string text)
        {
            var commonEnglishWords = new[] { "the", "and", "or", "but", "in", "on", "at", "to", "for", "of", "with", "by" };
            var words = text.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var englishWordCount = words.Count(word => commonEnglishWords.Contains(word));
            var englishRatio = (double)englishWordCount / Math.Min(words.Length, 100);

            return englishRatio > 0.1 ? "en" : "unknown";
        }
    }
}