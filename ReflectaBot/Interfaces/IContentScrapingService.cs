using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ReflectaBot.Models;

namespace ReflectaBot.Interfaces
{
    public interface IContentScrapingService
    {
        /// <summary>
        /// Scrape content from a URL
        /// </summary>
        Task<ScrapedContent> ScrapeFromUrlAsync(string url);

        /// <summary>
        /// Parse content from raw HTML
        /// </summary>
        ScrapedContent ParseHtml(string html, string? sourceUrl = null);

        /// <summary>
        /// Extract content from plain text (for future non-URL sources)
        /// </summary>
        ScrapedContent ParseText(string text, string? sourceTitle = null);
    }
}