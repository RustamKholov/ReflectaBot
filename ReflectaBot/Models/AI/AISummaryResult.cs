using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ReflectaBot.Models.AI
{
    public class AISummaryResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string Summary { get; set; } = string.Empty;
        public string[] KeyPoints { get; set; } = Array.Empty<string>();
        public int ProcessingTimeMs { get; set; }
        public int TokensUsed { get; set; }
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }
}