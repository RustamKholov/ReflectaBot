using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ReflectaBot.Models.Content
{
    public class ContentCacheStats
    {
        public int TotalCachedItems { get; set; }
        public long TotalSizeBytes { get; set; }
        public DateTime LastCleanup { get; set; }
        public TimeSpan AverageProcessingTime { get; set; }
    }
}