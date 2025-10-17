using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ReflectaBot.Interfaces.Enums;
using ReflectaBot.Models.Content;

namespace ReflectaBot.Interfaces
{
    public interface IContentProcessor
    {
        Task<ProcessedContent> ProcessAsync(string input, ContentSourceType sourceType);
        Task<bool> CanProcessAsync(string input, ContentSourceType sourceType);
    }
}