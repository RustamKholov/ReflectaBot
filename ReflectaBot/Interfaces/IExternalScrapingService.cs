using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ReflectaBot.Models.Content;

namespace ReflectaBot.Interfaces
{
    public interface IExternalScrapingService
    {
        Task<ScrapedContent> ScrapeUrlAsync(string url);
    }
}