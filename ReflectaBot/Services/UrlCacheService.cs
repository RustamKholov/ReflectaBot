using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ReflectaBot.Interfaces;

namespace ReflectaBot.Services
{
    public class UrlCacheService : IUrlCacheService
    {
        private readonly ILogger<UrlCacheService> _logger;

        private readonly ConcurrentDictionary<string, string> _urlCache = new();
        private readonly ConcurrentDictionary<string, string> _identifierCache = new();

        public UrlCacheService(ILogger<UrlCacheService> logger)
        {
            _logger = logger;
        }

        public Task<string> CacheUrlAsync(string url)
        {
            try
            {
                if (_identifierCache.TryGetValue(url, out var existingId))
                {
                    _logger.LogDebug("URL already cached with identifier: {Identifier}", existingId);
                    return Task.FromResult(existingId);
                }

                var identifier = GenerateShortId(url);

                var counter = 0;
                var originalId = identifier;
                while (_urlCache.ContainsKey(identifier) && counter < 100)
                {
                    identifier = $"{originalId}{counter:X}";
                    counter++;
                }

                if (counter >= 100)
                {
                    throw new InvalidOperationException("Unable to generate unique identifier for URL");
                }

                _urlCache[identifier] = url;
                _identifierCache[url] = identifier;

                _logger.LogDebug("Cached URL {Url} with identifier: {Identifier}", url, identifier);
                return Task.FromResult(identifier);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error caching URL: {Url}", url);
                throw;
            }
        }

        public Task<string?> GetUrlAsync(string identifier)
        {
            try
            {
                var found = _urlCache.TryGetValue(identifier, out var url);
                _logger.LogDebug("Retrieved URL for identifier {Identifier}: {Found}", identifier, found);
                return Task.FromResult(url);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving URL for identifier: {Identifier}", identifier);
                return Task.FromResult<string?>(null);
            }
        }

        public Task<bool> ExistsAsync(string identifier)
        {
            var exists = _urlCache.ContainsKey(identifier);
            _logger.LogDebug("Identifier {Identifier} exists: {Exists}", identifier, exists);
            return Task.FromResult(exists);
        }

        private static string GenerateShortId(string url)
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(url));

            var chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var result = new StringBuilder(8);

            for (var i = 0; i < 6 && i < hash.Length; i++)
            {
                result.Append(chars[hash[i] % chars.Length]);
            }

            return result.ToString();
        }
    }
}