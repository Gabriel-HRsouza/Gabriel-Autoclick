using System;
using System.IO;
using System.Media;

namespace GabrielAutoclick.Services
{
    /// <summary>
    /// Soft, low-volume chime sounds synthesized in memory (no external files).
    /// Each sound is a short bell-like tone: sine fundamental with gentle harmonics,
    /// a smooth attack and a long exponential fade so it never sounds harsh.
    /// </summary>
    public static class SoundService
    {
        private const int SampleRate = 44100;

        public static bool StartSoundEnabled { get; set; } = true;
        public static bool StopSoundEnabled { get; set; } = true;

        /// <summary>Kept for compatibility: toggles both start and stop sounds.</summary>
        public static bool IsEnabled
        {
            get => StartSoundEnabled || StopSoundEnabled;
            set { StartSoundEnabled = value; StopSoundEnabled = value; }
        }

        private static readonly Lazy<SoundPlayer?> StartPlayer = new(() => CreatePlayer(new[]
        {
            new Note(523.25, 0.00, 0.55, 1.00), // C5
            new Note(783.99, 0.11, 0.70, 0.85), // G5
        }, 0.95));

        private static readonly Lazy<SoundPlayer?> StopPlayer = new(() => CreatePlayer(new[]
        {
            new Note(783.99, 0.00, 0.50, 0.85), // G5
            new Note(523.25, 0.11, 0.75, 1.00), // C5
        }, 1.00));

        private static readonly Lazy<SoundPlayer?> PanicPlayer = new(() => CreatePlayer(new[]
        {
            new Note(659.25, 0.00, 0.45, 0.9),  // E5
            new Note(493.88, 0.10, 0.50, 0.9),  // B4
            new Note(392.00, 0.20, 0.80, 1.0),  // G4
        }, 1.10));

        private static readonly Lazy<SoundPlayer?> TickPlayer = new(() => CreatePlayer(new[]
        {
            new Note(880.00, 0.00, 0.18, 0.55), // A5, short and quiet
        }, 0.30));

        public static void Preload()
        {
            _ = StartPlayer.Value;
            _ = StopPlayer.Value;
            _ = PanicPlayer.Value;
            _ = TickPlayer.Value;
        }

        public static void PlayStartSound()
        {
            if (StartSoundEnabled) Play(StartPlayer);
        }

        public static void PlayStopSound()
        {
            if (StopSoundEnabled) Play(StopPlayer);
        }

        public static void PlayPanicSound()
        {
            if (StopSoundEnabled) Play(PanicPlayer);
        }

        public static void PlayCountdownTick()
        {
            if (StartSoundEnabled) Play(TickPlayer);
        }

        private static void Play(Lazy<SoundPlayer?> player)
        {
            try
            {
                player.Value?.Play(); // asynchronous, never blocks the caller
            }
            catch { }
        }

        private readonly record struct Note(double Frequency, double StartSec, double DecaySec, double Gain);

        private static SoundPlayer? CreatePlayer(Note[] notes, double totalSec)
        {
            try
            {
                var player = new SoundPlayer(new MemoryStream(BuildWav(notes, totalSec)));
                player.Load();
                return player;
            }
            catch
            {
                return null;
            }
        }

        private static byte[] BuildWav(Note[] notes, double totalSec)
        {
            int sampleCount = (int)(SampleRate * totalSec);
            var buffer = new double[sampleCount];

            foreach (var note in notes)
            {
                int start = (int)(note.StartSec * SampleRate);
                for (int i = start; i < sampleCount; i++)
                {
                    double t = (double)(i - start) / SampleRate;

                    // 12 ms smooth attack, then exponential decay
                    double attack = Math.Min(1.0, t / 0.012);
                    attack = attack * attack * (3 - 2 * attack);
                    double env = attack * Math.Exp(-t / (note.DecaySec / 3.0));
                    if (env < 0.0005 && t > 0.05) break;

                    double w = 2 * Math.PI * note.Frequency * t;
                    double s = Math.Sin(w)
                             + 0.18 * Math.Sin(2 * w) * Math.Exp(-t * 6)   // short shimmer
                             + 0.05 * Math.Sin(3 * w) * Math.Exp(-t * 10);

                    buffer[i] += s * env * note.Gain;
                }
            }

            // Fade the tail and normalize to a gentle volume
            double peak = 0;
            foreach (var v in buffer) peak = Math.Max(peak, Math.Abs(v));
            double scale = peak > 0 ? 0.22 / peak : 0;
            int fadeSamples = Math.Min(sampleCount, SampleRate / 20);
            for (int i = 0; i < fadeSamples; i++)
            {
                buffer[sampleCount - 1 - i] *= (double)i / fadeSamples;
            }

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            int dataBytes = sampleCount * 2;

            bw.Write("RIFF"u8.ToArray());
            bw.Write(36 + dataBytes);
            bw.Write("WAVE"u8.ToArray());
            bw.Write("fmt "u8.ToArray());
            bw.Write(16);
            bw.Write((short)1);            // PCM
            bw.Write((short)1);            // mono
            bw.Write(SampleRate);
            bw.Write(SampleRate * 2);      // byte rate
            bw.Write((short)2);            // block align
            bw.Write((short)16);           // bits per sample
            bw.Write("data"u8.ToArray());
            bw.Write(dataBytes);

            foreach (var v in buffer)
            {
                bw.Write((short)Math.Clamp(v * scale * short.MaxValue, short.MinValue, short.MaxValue));
            }

            bw.Flush();
            return ms.ToArray();
        }
    }
}
