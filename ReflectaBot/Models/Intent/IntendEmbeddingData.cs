using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace ReflectaBot.Models.Intent;

public class IntentEmbeddingData
{
    [JsonPropertyName("intent")]
    public string Intent { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("embedding")]
    public float[] Embedding { get; set; } = Array.Empty<float>();

    [JsonPropertyName("generated_at")]
    public DateTime GeneratedAt { get; set; }
}
