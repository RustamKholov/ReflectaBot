using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ReflectaBot.Models.AI;
using ReflectaBot.Models.Content;

namespace ReflectaBot.Interfaces
{
    public interface IAIContentService
    {
        Task<AISummaryResult> GenerateSummaryAsync(ProcessedContent content, CancellationToken cancellationToken = default);
        Task<AIQuizResult> GenerateQuizAsync(ProcessedContent content, CancellationToken cancellationToken = default);
    }
}