using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ReflectaBot.Models.AI
{
    public class AIQuizResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public QuizQuestion[] Questions { get; set; } = Array.Empty<QuizQuestion>();
        public int ProcessingTimeMs { get; set; }
        public int TokensUsed { get; set; }
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }
}