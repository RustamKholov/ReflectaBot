using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using ReflectaBot.Interfaces;
using ReflectaBot.Models;

namespace ReflectaBot.Services
{
    public class ContentScrapingService : IContentScrapingService
    {
        private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

        private readonly HttpClient _httpClient;
        private readonly ILogger<ContentScrapingService> _logger;
        private readonly IUrlService _urlService;

        public ContentScrapingService(
            HttpClient httpClient,
            ILogger<ContentScrapingService> logger,
            IUrlService urlService)
        {
            _httpClient = httpClient;
            _logger = logger;
            _urlService = urlService;

            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "ReflectaBot/1.0");
            _httpClient.DefaultRequestHeaders.Add("Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
        }

        public async Task<ScrapedContent> ScrapeFromUrlAsync(string url)
        {
            var result = new ScrapedContent
            {
                Source = url,
                Success = false
            };

            try
            {
                _logger.LogInformation("Starting content scraping from {Url}", url);

                if (!_urlService.IsValidUrl(url))
                {
                    result.Error = "Invalid URL format";
                    _logger.LogWarning("Invalid URL format: {Url}", url);
                    return result;
                }

                var normalizedUrl = _urlService.NormalizeUrl(url);
                result.Source = normalizedUrl;

                var isAccessible = await _urlService.IsAccessibleAsync(normalizedUrl);
                if (!isAccessible)
                {
                    result.Error = "URL is not accessible";
                    _logger.LogWarning("URL not accessible: {Url}", normalizedUrl);
                    return result;
                }

                using var response = await _httpClient.GetAsync(normalizedUrl);
                response.EnsureSuccessStatusCode();

                var contentType = response.Content.Headers.ContentType?.MediaType;
                if (!string.IsNullOrEmpty(contentType) && !contentType.Contains("text/html"))
                {
                    result.Error = $"Unsupported content type: {contentType}";
                    _logger.LogWarning("Unsupported content type {ContentType} for {Url}", contentType, normalizedUrl);
                    return result;
                }

                var html = await response.Content.ReadAsStringAsync();

                if (string.IsNullOrWhiteSpace(html))
                {
                    result.Error = "Empty HTML content received";
                    _logger.LogWarning("Empty HTML content from {Url}", normalizedUrl);
                    return result;
                }

                var scrapedContent = ParseHtml(html, normalizedUrl);

                _logger.LogInformation("Successfully scraped content from {Url}: {WordCount} words, Title: '{Title}'",
                    normalizedUrl, scrapedContent.WordCount, scrapedContent.Title);

                return scrapedContent;
            }
            catch (HttpRequestException ex)
            {
                result.Error = $"Network error: {ex.Message}";
                _logger.LogError(ex, "Network error while scraping {Url}", url);
            }
            catch (TaskCanceledException ex)
            {
                result.Error = "Request timeout";
                _logger.LogError(ex, "Timeout while scraping {Url}", url);
            }
            catch (Exception ex)
            {
                result.Error = $"Unexpected error: {ex.Message}";
                _logger.LogError(ex, "Unexpected error while scraping {Url}", url);
            }

            return result;
        }

        public ScrapedContent ParseHtml(string html, string? sourceUrl = null)
        {
            var result = new ScrapedContent
            {
                Source = sourceUrl,
                Success = false
            };

            try
            {
                _logger.LogDebug("Parsing HTML content from {Source}", sourceUrl ?? "unknown source");

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                RemoveUnwantedElements(doc);

                result.Title = ExtractTitle(doc);

                result.Content = ExtractMainContent(doc);

                result.WordCount = CalculateWordCount(result.Content);

                if (string.IsNullOrWhiteSpace(result.Content) || result.WordCount < 10)
                {
                    result.Error = "No meaningful content found";
                    _logger.LogWarning("No meaningful content extracted from {Source}", sourceUrl);
                    return result;
                }

                result.Success = true;
                _logger.LogDebug("Successfully parsed HTML: {WordCount} words, Title: '{Title}'",
                    result.WordCount, result.Title);

                return result;
            }
            catch (Exception ex)
            {
                result.Error = $"HTML parsing error: {ex.Message}";
                _logger.LogError(ex, "Error parsing HTML from {Source}", sourceUrl);
                return result;
            }
        }

        public ScrapedContent ParseText(string text, string? sourceTitle = null)
        {
            var result = new ScrapedContent
            {
                Source = sourceTitle,
                Success = false
            };

            try
            {
                _logger.LogDebug("Parsing plain text content");

                if (string.IsNullOrWhiteSpace(text))
                {
                    result.Error = "Empty text provided";
                    return result;
                }

                var cleanedText = CleanText(text);

                result.Title = sourceTitle ?? "Text Content";
                result.Content = cleanedText;
                result.WordCount = CalculateWordCount(cleanedText);
                result.Success = true;

                _logger.LogDebug("Successfully parsed text: {WordCount} words", result.WordCount);

                return result;
            }
            catch (Exception ex)
            {
                result.Error = $"Text parsing error: {ex.Message}";
                _logger.LogError(ex, "Error parsing text content");
                return result;
            }
        }

        #region Private Helper Methods

        private void RemoveUnwantedElements(HtmlDocument doc)
        {
            try
            {
                var unwantedSelectors = new[]
                {
                    // Scripts and styles
                    "script", "style", "noscript",
                    
                    // Navigation and UI elements
                    "nav", "header", "footer", "aside", "menu",
                    
                    // Interactive elements
                    "form", "button", "input", "select", "textarea",
                    
                    // Multimedia that doesn't help with text content
                    "iframe", "embed", "object", "audio", "video"
                };

                foreach (var selector in unwantedSelectors)
                {
                    var nodes = doc.DocumentNode.Descendants(selector).ToList();
                    foreach (var node in nodes)
                    {
                        node.Remove();
                    }
                }
                RemoveElementsByAttribute(doc, "class", new[] { "ad", "advertisement", "social", "share", "comment", "sidebar", "widget", "popup", "modal" });
                RemoveElementsByAttribute(doc, "id", new[] { "ad", "sidebar", "comments", "footer", "header" });

                _logger.LogDebug("Successfully removed unwanted elements from HTML document");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error removing unwanted elements from HTML, continuing with content extraction");
            }
        }

        private void RemoveElementsByAttribute(HtmlDocument doc, string attributeName, string[] keywords)
        {
            try
            {
                var allElements = doc.DocumentNode.Descendants().ToList();

                foreach (var element in allElements)
                {
                    var attributeValue = element.GetAttributeValue(attributeName, string.Empty).ToLowerInvariant();

                    if (!string.IsNullOrEmpty(attributeValue))
                    {
                        foreach (var keyword in keywords)
                        {
                            if (attributeValue.Contains(keyword.ToLowerInvariant()))
                            {
                                element.Remove();
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error removing elements by attribute {AttributeName}", attributeName);
            }
        }

        private string ExtractTitle(HtmlDocument doc)
        {
            var titleStrategies = new (string description, Func<HtmlDocument, string> extractor)[]
            {
                // Open Graph title (most reliable for articles)
                ("Open Graph title", d => d.DocumentNode.SelectSingleNode("//meta[@property='og:title']")?.GetAttributeValue("content", "") ?? ""),
                
                // Twitter card title
                ("Twitter title", d => d.DocumentNode.SelectSingleNode("//meta[@name='twitter:title']")?.GetAttributeValue("content", "") ?? ""),
                
                // Main heading (usually the article title)
                ("H1 heading", d => d.DocumentNode.SelectSingleNode("//h1")?.InnerText ?? ""),
                
                // JSON-LD structured data title
                ("JSON-LD title", d => ExtractJsonLdTitle(d.DocumentNode.SelectSingleNode("//script[@type='application/ld+json']")?.InnerText ?? "")),
                
                // Page title as fallback
                ("Page title", d => d.DocumentNode.SelectSingleNode("//title")?.InnerText ?? "")
            };

            foreach (var (description, extractor) in titleStrategies)
            {
                try
                {
                    var title = extractor(doc);
                    title = CleanText(title);

                    if (!string.IsNullOrWhiteSpace(title) &&
                        title.Length > 3 &&
                        title.Length < 200 &&
                        !IsGenericTitle(title))
                    {
                        return title;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error extracting title using strategy: {Strategy}", description);
                    continue;
                }
            }

            return "Untitled Article";
        }

        private string ExtractMainContent(HtmlDocument doc)
        {
            var contentStrategies = new (string description, Func<HtmlDocument, IEnumerable<HtmlNode>> selector)[]
            {
                // Semantic HTML5 article content
                ("Article paragraphs", d => d.DocumentNode.SelectNodes("//article//p") ?? Enumerable.Empty<HtmlNode>()),
                
                // Common article content classes
                ("Post content paragraphs", d => GetElementsByClass(d, "post-content")?.SelectMany(n => n.Descendants("p")) ?? Enumerable.Empty<HtmlNode>()),
                ("Entry content paragraphs", d => GetElementsByClass(d, "entry-content")?.SelectMany(n => n.Descendants("p")) ?? Enumerable.Empty<HtmlNode>()),
                ("Article content paragraphs", d => GetElementsByClass(d, "article-content")?.SelectMany(n => n.Descendants("p")) ?? Enumerable.Empty<HtmlNode>()),
                ("Content body paragraphs", d => GetElementsByClass(d, "content-body")?.SelectMany(n => n.Descendants("p")) ?? Enumerable.Empty<HtmlNode>()),
                
                // Generic content containers
                ("Content ID paragraphs", d => d.DocumentNode.SelectSingleNode("//*[@id='content']")?.Descendants("p") ?? Enumerable.Empty<HtmlNode>()),
                ("Content class paragraphs", d => GetElementsByClass(d, "content")?.SelectMany(n => n.Descendants("p")) ?? Enumerable.Empty<HtmlNode>()),
                ("Main paragraphs", d => d.DocumentNode.SelectNodes("//main//p") ?? Enumerable.Empty<HtmlNode>()),
                
                // Fallback
                ("All substantial paragraphs", d => d.DocumentNode.Descendants("p").Where(p => p.InnerText?.Length > 50))
            };

            foreach (var (description, selector) in contentStrategies)
            {
                try
                {
                    var paragraphs = selector(doc).ToList();
                    if (paragraphs.Any())
                    {
                        var content = string.Join("\n\n",
                            paragraphs.Select(p => CleanText(p.InnerText))
                                     .Where(text => !string.IsNullOrWhiteSpace(text) && text.Length > 20)
                                     .Take(50));

                        if (!string.IsNullOrWhiteSpace(content) && content.Length > 100)
                        {
                            return content;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error extracting content using strategy: {Strategy}", description);
                    continue;
                }
            }

            //fallback
            try
            {
                var bodyText = doc.DocumentNode.SelectSingleNode("//body")?.InnerText;
                return bodyText != null ? CleanText(bodyText) : string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract any content from document");
                return string.Empty;
            }
        }

        private IEnumerable<HtmlNode>? GetElementsByClass(HtmlDocument doc, string className)
        {
            try
            {
                return doc.DocumentNode.Descendants()
                    .Where(node => node.GetAttributeValue("class", "")
                        .Contains(className, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return null;
            }
        }

        private string ExtractJsonLdTitle(string jsonContent)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(jsonContent))
                    return string.Empty;
                var titleMatch = Regex.Match(jsonContent, @"""(?:title|headline)""\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase);
                return titleMatch.Success ? titleMatch.Groups[1].Value : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private bool IsGenericTitle(string title)
        {
            var genericTitles = new[]
            {
                "home", "welcome", "untitled", "page", "article", "post", "blog",
                "loading", "error", "404", "not found", "default"
            };

            var lowerTitle = title.ToLowerInvariant();
            return genericTitles.Any(generic => lowerTitle.Contains(generic) && lowerTitle.Length < 20);
        }

        private string CleanText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            try
            {
                text = System.Net.WebUtility.HtmlDecode(text);

                text = WhitespaceRegex.Replace(text.Trim(), " ");

                var unwantedPatterns = new[]
                {
                    @"Cookie Policy.*",
                    @"Privacy Policy.*",
                    @"Terms of Service.*",
                    @"Subscribe.*newsletter.*",
                    @"Sign up.*updates.*",
                    @"Follow us on.*",
                    @"Share this.*",
                    @"Advertisement\s*"
                };

                foreach (var pattern in unwantedPatterns)
                {
                    text = Regex.Replace(text, pattern, "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                }

                return text.Trim();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error cleaning text, returning original");
                return text?.Trim() ?? string.Empty;
            }
        }

        private int CalculateWordCount(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return 0;

            try
            {
                return content.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
            }
            catch
            {
                return 0;
            }
        }
        #endregion
    }
}