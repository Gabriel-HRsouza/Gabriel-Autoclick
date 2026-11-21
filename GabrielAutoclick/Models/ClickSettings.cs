using System;
using System.Text.Json.Serialization;

namespace GabrielAutoclick.Models
{
    public enum MouseButtonType
    {
        Left = 0,
        Right = 1,
        Middle = 2
    }

    public enum ClickActionType
    {
        Single = 0,
        Double = 1,
        HoldDown = 2,
        HoldUp = 3
    }

    public enum RepeatMode
    {
        UntilStopped = 0,
        Count = 1,
        Duration = 2
    }

    public enum ClickPositionMode
    {
        CurrentPosition = 0,
        FixedPosition = 1,
        FollowCursor = 2
    }

    public class ClickSettings
    {
        // Interval values
        public int Hours { get; set; } = 0;
        public int Minutes { get; set; } = 0;
        public int Seconds { get; set; } = 0;
        public int Milliseconds { get; set; } = 100; // 10 CPS default

        // Mouse configuration
        public MouseButtonType MouseButton { get; set; } = MouseButtonType.Left;
        public ClickActionType ClickAction { get; set; } = ClickActionType.Single;

        // Repeat configuration
        public RepeatMode RepeatMode { get; set; } = RepeatMode.UntilStopped;
        public int RepeatCount { get; set; } = 100;
        public int RepeatDurationSeconds { get; set; } = 10;

        // Position configuration
        public ClickPositionMode PositionMode { get; set; } = ClickPositionMode.FollowCursor;
        public int FixedX { get; set; } = 0;
        public int FixedY { get; set; } = 0;

        // Burst Mode
        public bool BurstModeEnabled { get; set; } = false;
        public int BurstClicksCount { get; set; } = 5;
        public int BurstIntervalMs { get; set; } = 50;
        public int BurstCooldownMs { get; set; } = 1000;

        // Randomization
        public bool RandomIntervalEnabled { get; set; } = false;
        public int MinRandomIntervalMs { get; set; } = 80;
        public int MaxRandomIntervalMs { get; set; } = 150;

        public bool RandomPositionEnabled { get; set; } = false;
        public int RandomRadiusPixels { get; set; } = 3;

        // Start Delay
        public int StartDelaySeconds { get; set; } = 0; // 0 = Instant

        [JsonIgnore]
        public int TotalIntervalMs
        {
            get
            {
                long total = ((long)Hours * 3600000) + ((long)Minutes * 60000) + ((long)Seconds * 1000) + Milliseconds;
                if (total < 1) total = 1;
                if (total > int.MaxValue) total = int.MaxValue;
                return (int)total;
            }
        }

        [JsonIgnore]
        public double ComputedCps
        {
            get
            {
                int totalMs = TotalIntervalMs;
                return totalMs <= 0 ? 0 : Math.Round(1000.0 / totalMs, 2);
            }
        }

        public void SetFromCps(double cps)
        {
            if (cps <= 0) return;
            double ms = 1000.0 / cps;
            int totalMs = (int)Math.Round(ms);
            if (totalMs < 1) totalMs = 1;

            Hours = totalMs / 3600000;
            totalMs %= 3600000;

            Minutes = totalMs / 60000;
            totalMs %= 60000;

            Seconds = totalMs / 1000;
            Milliseconds = totalMs % 1000;
        }

        public ClickSettings Clone()
        {
            return (ClickSettings)this.MemberwiseClone();
        }
    }
}
