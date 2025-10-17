using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ReflectaBot.Models.Content
{
    public class ProcessingMetadata
    {
        public string? Language { get; set; }
        public string? Domain { get; set; }
        public TimeSpan ProcessingTime { get; set; }
        public string ProcessorUsed { get; set; } = string.Empty;
        public long? FileSizeBytes { get; set; }
        public string? OriginalFileName { get; set; }
    }
}