using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ReflectaBot.Models
{
    public class LlmConfiguration
    {
        public string ApiKey { get; set; } = string.Empty;
        public string ModelName { get; set; } = "gpt-4o-mini";
        public string BaseUrl { get; set; } = "https://api.openai.com/";
    }
}