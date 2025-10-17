using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ReflectaBot.Interfaces;
using ReflectaBot.Interfaces.Enums;
using ReflectaBot.Models.Content;

namespace ReflectaBot.Services
{
    public class UniversalContentProcessor : IContentProcessor
    {
        private readonly ILogger<UniversalContentProcessor> _logger;
        private readonly IContentScrapingService _webScraper;
        private readonly ITextAnalysisService _textAnalyzer;
        private readonly IDocumentProcessor _documentProcessor;
        private readonly IExternalScrapingService? _externalScraper;

        public UniversalContentProcessor(
            ILogger<UniversalContentProcessor> logger,
            IContentScrapingService webScraper,
            ITextAnalysisService textAnalyzer,
            IDocumentProcessor documentProcessor,
            IExternalScrapingService? externalScraper = null)
        {
            _logger = logger;
            _webScraper = webScraper;
            _textAnalyzer = textAnalyzer;
            _documentProcessor = documentProcessor;
            _externalScraper = externalScraper;
        }

        public async Task<ProcessedContent> ProcessAsync(string input, ContentSourceType sourceType)
        {
            var startTime = DateTime.UtcNow;

            try
            {
                var result = sourceType switch
                {
                    ContentSourceType.Url => await ProcessUrlWithFallback(input),
                    ContentSourceType.PlainText => await _textAnalyzer.ProcessTextAsync(input),
                    ContentSourceType.Document => await _documentProcessor.ProcessDocumentAsync(input),
                    ContentSourceType.PastedContent => await _textAnalyzer.ProcessTextAsync(input),
                    _ => CreateErrorResult("Unsupported content type", sourceType)
                };

                result.Metadata.ProcessingTime = DateTime.UtcNow - startTime;
                result.SourceType = sourceType;

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing content of type {ContentType}", sourceType);
                return CreateErrorResult($"Processing error: {ex.Message}", sourceType);
            }
        }

        public async Task<bool> CanProcessAsync(string input, ContentSourceType sourceType)
        {
            return sourceType switch
            {
                ContentSourceType.PlainText => !string.IsNullOrWhiteSpace(input) && input.Length > 50,
                ContentSourceType.Url => Uri.TryCreate(input, UriKind.Absolute, out _),
                ContentSourceType.Document => !string.IsNullOrWhiteSpace(input),
                ContentSourceType.PastedContent => !string.IsNullOrWhiteSpace(input) && input.Length > 100,
                _ => false
            };
        }

        private async Task<ProcessedContent> ProcessUrlWithFallback(string url)
        {
            _logger.LogInformation("Starting URL processing with fallback strategy for: {Url}", url);

            try
            {
                var internalResult = await _webScraper.ScrapeFromUrlAsync(url);
                if (internalResult.Success && internalResult.WordCount > 100)
                {
                    _logger.LogInformation("Internal scraping successful for {Url}: {WordCount} words",
                        url, internalResult.WordCount);

                    return new ProcessedContent
                    {
                        Title = internalResult.Title,
                        Content = internalResult.Content,
                        SourceUrl = url,
                        WordCount = internalResult.WordCount,
                        Success = true,
                        Metadata = { ProcessorUsed = "Internal", Domain = GetDomain(url) }
                    };
                }

                _logger.LogWarning("Internal scraping failed or insufficient content for {Url}", url);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Internal scraping error for {Url}", url);
            }

            if (_externalScraper != null)
            {
                try
                {
                    var externalResult = await _externalScraper.ScrapeUrlAsync(url);
                    if (externalResult.Success && externalResult.WordCount > 100)
                    {
                        _logger.LogInformation("External scraping successful for {Url}: {WordCount} words",
                            url, externalResult.WordCount);

                        return new ProcessedContent
                        {
                            Title = externalResult.Title,
                            Content = externalResult.Content,
                            SourceUrl = url,
                            WordCount = externalResult.WordCount,
                            Success = true,
                            Metadata = { ProcessorUsed = "External", Domain = GetDomain(url) }
                        };
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "External scraping error for {Url}", url);
                }
            }

            return new ProcessedContent
            {
                Success = false,
                SourceUrl = url,
                Error = "Unable to extract content automatically",
                Metadata = {
                    ProcessorUsed = "None",
                    Domain = GetDomain(url)
                }
            };
        }

        private static ProcessedContent CreateErrorResult(string error, ContentSourceType sourceType)
        {
            return new ProcessedContent
            {
                Success = false,
                Error = error,
                SourceType = sourceType
            };
        }

        private static string GetDomain(string url)
        {
            try
            {
                return new Uri(url).Host;
            }
            catch
            {
                return "unknown";
            }
        }
    }
}