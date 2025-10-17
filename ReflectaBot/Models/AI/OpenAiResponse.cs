using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ReflectaBot.Models.AI
{
    public class OpenAIResponse
    {
        public bool Success { get; set; }
        public string Content { get; set; } = string.Empty;
        public string? Error { get; set; }
        public int TokensUsed { get; set; }
    }
}