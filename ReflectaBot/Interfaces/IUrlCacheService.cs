using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ReflectaBot.Interfaces
{
    public interface IUrlCacheService
    {
        /// <summary>
        /// Store URL and return a short identifier
        /// </summary>
        Task<string> CacheUrlAsync(string url);

        /// <summary>
        /// Retrieve URL by short identifier
        /// </summary>
        Task<string?> GetUrlAsync(string identifier);

        /// <summary>
        /// Check if identifier exists
        /// </summary>
        Task<bool> ExistsAsync(string identifier);
    }
}