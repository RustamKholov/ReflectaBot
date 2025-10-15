using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ReflectaBot.Models.Configuration
{
    public class EmbeddingConfiguration
    {
        public string ApiKey { get; set; } = string.Empty;
        public string ModelName { get; set; } = "text-embedding-3-small";
        public string BaseUrl { get; set; } = "https://api.openai.com/";
    }
}