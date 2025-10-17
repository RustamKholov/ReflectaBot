using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ReflectaBot.Models.Content;

public class ScrapedContent
{
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Source { get; set; }
    public int WordCount { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
}
