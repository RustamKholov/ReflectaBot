using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ReflectaBot.Models.Intent;

public class IntentDefinition
{
    public string Intent { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Examples { get; set; } = new();
}
