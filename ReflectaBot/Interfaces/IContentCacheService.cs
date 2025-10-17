using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ReflectaBot.Models.Content;

namespace ReflectaBot.Interfaces;

public interface IContentCacheService
{
    Task<string> CacheContentAsync(ProcessedContent content);
    Task<ProcessedContent?> GetContentAsync(string identifier);
    Task<bool> ExistsAsync(string identifier);
    Task<bool> RemoveAsync(string identifier);
    Task<ContentCacheStats> GetStatsAsync();
}
