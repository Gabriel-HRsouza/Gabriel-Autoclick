using System;

namespace GabrielAutoclick.Models
{
    public class AppStatistics
    {
        public long TotalClicksLifetime { get; set; } = 0;
        public long TotalActiveSecondsLifetime { get; set; } = 0;
        public double PeakCpsLifetime { get; set; } = 0;
        public int MacrosExecutedLifetime { get; set; } = 0;
        public DateTime? LastResetDate { get; set; } = DateTime.UtcNow;

        // Session (runtime only)
        [System.Text.Json.Serialization.JsonIgnore]
        public long SessionClicks { get; set; } = 0;

        [System.Text.Json.Serialization.JsonIgnore]
        public double SessionActiveSeconds { get; set; } = 0;

        [System.Text.Json.Serialization.JsonIgnore]
        public double SessionCurrentCps { get; set; } = 0;

        [System.Text.Json.Serialization.JsonIgnore]
        public double SessionPeakCps { get; set; } = 0;

        [System.Text.Json.Serialization.JsonIgnore]
        public int SessionMacrosExecuted { get; set; } = 0;
    }
}
