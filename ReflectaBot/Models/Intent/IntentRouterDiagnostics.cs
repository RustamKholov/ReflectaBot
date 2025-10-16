using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ReflectaBot.Models.Intent;

public class IntentRouterDiagnostics
{
    public int TotalEmbeddings { get; set; }
    public int UniqueIntents { get; set; }
    public Dictionary<string, int> IntentDistribution { get; set; } = new();
    public bool IsInitialized { get; set; }
    public double HighConfidenceThreshold { get; set; }
    public double MediumConfidenceThreshold { get; set; }
    public double MinimumConfidenceThreshold { get; set; }
}

