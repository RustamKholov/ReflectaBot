using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ReflectaBot.Interfaces;

namespace ReflectaBot.Services
{
    public class UrlService : IUrlService
    {

        private readonly HttpClient _httpClient;
        private readonly ILogger<UrlService> _logger;
        private static readonly Regex UrlRegex = new(
            @"https?://[^\s]+",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public UrlService(HttpClient httpClient, ILogger<UrlService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;

            _httpClient.Timeout = TimeSpan.FromSeconds(10);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "ReflectaBot/1.0");
        }
        public List<string> ExtractUrls(string text)
        {
            var matches = UrlRegex.Matches(text);
            var urls = matches.Cast<Match>()
                .Select(m => m.Value.TrimEnd('.', ',', '!', '?', ')'))
                .Where(IsValidUrl)
                .Select(NormalizeUrl)
                .Distinct()
                .ToList();

            _logger.LogInformation("Extracted {Count} valid URLs from text", urls.Count);
            return urls;
        }

        public async Task<bool> IsAccessibleAsync(string url)
        {
            try
            {
                _logger.LogDebug("Checking accessibility of {Url}", url);

                using var response = await _httpClient.SendAsync(
                    new HttpRequestMessage(HttpMethod.Head, url));

                var isAccessible = response.IsSuccessStatusCode;

                _logger.LogDebug("URL {Url} accessibility: {IsAccessible}", url, isAccessible);

                return isAccessible;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check accessibility of {Url}", url);

                return false;
            }
        }

        public bool IsValidUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                   (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        public string NormalizeUrl(string url)
        {
            try
            {
                var uri = new Uri(url.Trim());
                return uri.ToString();
            }
            catch
            {
                return url;
            }
        }
    }
}