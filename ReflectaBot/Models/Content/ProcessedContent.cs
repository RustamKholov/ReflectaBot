using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ReflectaBot.Interfaces.Enums;

namespace ReflectaBot.Models.Content
{
    public class ProcessedContent
    {
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string? SourceUrl { get; set; }
        public ContentSourceType SourceType { get; set; }
        public int WordCount { get; set; }
        public bool Success { get; set; }
        public string? Error { get; set; }
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
        public ProcessingMetadata Metadata { get; set; } = new();
    }
}