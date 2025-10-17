using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ReflectaBot.Interfaces;
using ReflectaBot.Models.Content;

namespace ReflectaBot.Services
{
    public class ContentCacheService : IContentCacheService
    {
        private readonly ILogger<ContentCacheService> _logger;
        private readonly ConcurrentDictionary<string, CachedContentItem> _contentCache = new();
        private readonly ConcurrentDictionary<string, string> _contentHashLookup = new();

        public ContentCacheService(ILogger<ContentCacheService> logger)
        {
            _logger = logger;
        }

        public Task<string> CacheContentAsync(ProcessedContent content)
        {
            try
            {
                var contentHash = GenerateContentHash(content.Content);

                if (_contentHashLookup.TryGetValue(contentHash, out var existingId))
                {
                    _logger.LogDebug("Content already cached with identifier: {Identifier}", existingId);
                    return Task.FromResult(existingId);
                }

                var identifier = GenerateShortId();
                var cacheItem = new CachedContentItem
                {
                    Content = content,
                    CachedAt = DateTime.UtcNow,
                    LastAccessed = DateTime.UtcNow,
                    AccessCount = 0,
                    ContentHash = contentHash
                };

                _contentCache[identifier] = cacheItem;
                _contentHashLookup[contentHash] = identifier;

                _logger.LogDebug("Cached content with identifier: {Identifier}, WordCount: {WordCount}",
                    identifier, content.WordCount);

                return Task.FromResult(identifier);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error caching content");
                throw;
            }
        }

        public Task<ProcessedContent?> GetContentAsync(string identifier)
        {
            try
            {
                if (_contentCache.TryGetValue(identifier, out var cacheItem))
                {
                    cacheItem.LastAccessed = DateTime.UtcNow;
                    cacheItem.AccessCount++;

                    _logger.LogDebug("Retrieved cached content: {Identifier}, AccessCount: {AccessCount}",
                        identifier, cacheItem.AccessCount);

                    return Task.FromResult<ProcessedContent?>(cacheItem.Content);
                }

                _logger.LogDebug("Content not found in cache: {Identifier}", identifier);
                return Task.FromResult<ProcessedContent?>(null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving cached content: {Identifier}", identifier);
                return Task.FromResult<ProcessedContent?>(null);
            }
        }

        public Task<bool> ExistsAsync(string identifier)
        {
            var exists = _contentCache.ContainsKey(identifier);
            _logger.LogDebug("Content exists check: {Identifier} = {Exists}", identifier, exists);
            return Task.FromResult(exists);
        }

        public Task<bool> RemoveAsync(string identifier)
        {
            try
            {
                if (_contentCache.TryRemove(identifier, out var cacheItem))
                {
                    _contentHashLookup.TryRemove(cacheItem.ContentHash, out _);

                    _logger.LogDebug("Removed cached content: {Identifier}", identifier);
                    return Task.FromResult(true);
                }

                return Task.FromResult(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing cached content: {Identifier}", identifier);
                return Task.FromResult(false);
            }
        }

        public Task<ContentCacheStats> GetStatsAsync()
        {
            try
            {
                var items = _contentCache.Values.ToList();
                var stats = new ContentCacheStats
                {
                    TotalCachedItems = items.Count,
                    TotalSizeBytes = items.Sum(item => EstimateItemSize(item)),
                    LastCleanup = DateTime.UtcNow,
                    AverageProcessingTime = items.Any()
                        ? TimeSpan.FromMilliseconds(items.Average(item => item.Content.Metadata.ProcessingTime.TotalMilliseconds))
                        : TimeSpan.Zero
                };

                return Task.FromResult(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating cache stats");
                return Task.FromResult(new ContentCacheStats());
            }
        }

        #region Private Helper Methods

        private static string GenerateShortId()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, 8)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        private static string GenerateContentHash(string content)
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(content));
            return Convert.ToBase64String(hash)[..16];
        }

        private static long EstimateItemSize(CachedContentItem item)
        {
            var contentSize = Encoding.UTF8.GetByteCount(item.Content.Content);
            var titleSize = Encoding.UTF8.GetByteCount(item.Content.Title);
            var metadataSize = 200; 

            return contentSize + titleSize + metadataSize;
        }

        #endregion

        #region Private Data Models

        private class CachedContentItem
        {
            public ProcessedContent Content { get; set; } = null!;
            public DateTime CachedAt { get; set; }
            public DateTime LastAccessed { get; set; }
            public int AccessCount { get; set; }
            public string ContentHash { get; set; } = string.Empty;
        }

        #endregion
    }
}