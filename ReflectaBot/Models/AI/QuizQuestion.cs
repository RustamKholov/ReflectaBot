using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ReflectaBot.Models.AI
{
    public class QuizQuestion
    {
        public string Question { get; set; } = string.Empty;
        public string[] Options { get; set; } = Array.Empty<string>();
        public int CorrectAnswer { get; set; }
        public string Explanation { get; set; } = string.Empty;
        public string Difficulty { get; set; } = "Medium";
        public string[] Topics { get; set; } = Array.Empty<string>();
    }
}