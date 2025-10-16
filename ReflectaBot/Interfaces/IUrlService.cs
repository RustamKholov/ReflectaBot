using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ReflectaBot.Interfaces
{
    public interface IUrlService
    {
        /// <summary>
        /// Extract URLs from text input
        /// </summary>
        List<string> ExtractUrls(string text);

        /// <summary>
        /// Quick validation - is this a valid HTTP/HTTPS URL?
        /// </summary>
        bool IsValidUrl(string url);

        /// <summary>
        /// Normalize URL for consistent processing
        /// </summary>
        string NormalizeUrl(string url);

        /// <summary>
        /// Check if URL is accessible (returns HTTP 200)
        /// </summary>
        Task<bool> IsAccessibleAsync(string url);
    }
}