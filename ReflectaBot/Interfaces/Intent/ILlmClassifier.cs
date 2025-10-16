using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ReflectaBot.Interfaces.Intent
{
    public interface ILlmClassifier
    {
        Task<string?> ClassifyAsync(string text, string[] labels, CancellationToken ct = default);
        Task<string> GenerateTextAsync(string prompt, CancellationToken ct = default);
    }
}