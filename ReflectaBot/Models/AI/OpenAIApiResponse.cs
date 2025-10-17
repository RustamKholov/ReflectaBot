using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ReflectaBot.Models.AI
{
    public class OpenAIApiResponse
    {
        public Choice[]? choices { get; set; }
        public Usage? usage { get; set; }
    }

    public class Choice
    {
        public AIMessage? message { get; set; }
    }

    public class AIMessage
    {
        public string? content { get; set; }
    }

    public class Usage
    {
        public int total_tokens { get; set; }
    }

    public class QuizJsonResponse
    {
        public QuizQuestion[]? questions { get; set; }
    }

}