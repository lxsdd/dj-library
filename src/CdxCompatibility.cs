using System;
using System.Globalization;

namespace DJLibrary
{
    public enum CdxCompatibilityState
    {
        Unknown = 0,
        Compatible = 1,
        Incompatible = 2
    }

    public static class CdxCompatibility
    {
        public const int FramesPerSecond = 75;
        public const int IncompatibleFromFrames = 359999; // physical duration 79:59:74

        // Stored TOCs contain absolute frame addresses: first track start ... lead-out.
        // Physical play length is lead-out minus first-track start (normally the 150-frame pregap offset).
        public static bool TryGetDurationFrames(string toc, bool tocComplete, out int durationFrames)
        {
            durationFrames = 0;
            if (!tocComplete || String.IsNullOrWhiteSpace(toc)) return false;

            string[] parts = toc.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return false;

            int first = -1;
            int previous = -1;
            for (int i = 0; i < parts.Length; i++)
            {
                int value;
                if (!Int32.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return false;
                if (value < 0 || (previous >= 0 && value <= previous)) return false;
                if (first < 0) first = value;
                previous = value;
            }
            if (first < 0 || previous <= first) return false;
            durationFrames = previous - first;
            return durationFrames > 0;
        }

        public static CdxCompatibilityState Classify(string toc, bool tocComplete)
        {
            int durationFrames;
            if (!TryGetDurationFrames(toc, tocComplete, out durationFrames)) return CdxCompatibilityState.Unknown;
            return durationFrames >= IncompatibleFromFrames ? CdxCompatibilityState.Incompatible : CdxCompatibilityState.Compatible;
        }

        // Legacy browser rows already expose the physical TOC lead-out duration in seconds.
        // This fallback is display-only; writable-catalog classification always uses the stored TOC itself.
        public static CdxCompatibilityState ClassifyLegacyDuration(string durationSource, double durationSeconds)
        {
            if (!String.Equals(durationSource, "toc_leadout", StringComparison.OrdinalIgnoreCase) || durationSeconds <= 0)
                return CdxCompatibilityState.Unknown;
            int frames = (int)Math.Round(durationSeconds * FramesPerSecond, MidpointRounding.AwayFromZero);
            return frames >= IncompatibleFromFrames ? CdxCompatibilityState.Incompatible : CdxCompatibilityState.Compatible;
        }

        public static string Text(CdxCompatibilityState state)
        {
            if (state == CdxCompatibilityState.Compatible) return "Yes";
            if (state == CdxCompatibilityState.Incompatible) return "No";
            return "Unknown";
        }

        public static string Code(CdxCompatibilityState state)
        {
            if (state == CdxCompatibilityState.Compatible) return "yes";
            if (state == CdxCompatibilityState.Incompatible) return "no";
            return "unknown";
        }

        public static int SortKey(CdxCompatibilityState state)
        {
            if (state == CdxCompatibilityState.Compatible) return 0;
            if (state == CdxCompatibilityState.Incompatible) return 1;
            return 2;
        }

        public static string ToolTip(CdxCompatibilityState state)
        {
            if (state == CdxCompatibilityState.Incompatible)
                return "Numark CDX: durations of 79:59:74 or longer are not playable.";
            if (state == CdxCompatibilityState.Unknown)
                return "CDX compatibility is unknown because no complete physical TOC is available.";
            return "Numark CDX: physical TOC duration is below 79:59:74.";
        }

        public static string FormatMsf(int frames)
        {
            if (frames < 0) frames = 0;
            int minutes = frames / (60 * FramesPerSecond);
            int remainder = frames % (60 * FramesPerSecond);
            int seconds = remainder / FramesPerSecond;
            int frame = remainder % FramesPerSecond;
            return minutes.ToString(CultureInfo.InvariantCulture) + ":" + seconds.ToString("00", CultureInfo.InvariantCulture) + ":" + frame.ToString("00", CultureInfo.InvariantCulture);
        }

        public static bool HasHistoricalMarker(string album)
        {
            if (String.IsNullOrWhiteSpace(album)) return false;
            return album.TrimEnd().EndsWith("°", StringComparison.Ordinal);
        }

        public static string RemoveHistoricalMarker(string album)
        {
            if (!HasHistoricalMarker(album)) return album ?? "";
            string trimmed = album.TrimEnd();
            return trimmed.Substring(0, trimmed.Length - 1).TrimEnd();
        }

        public static string NormalizeLegacyAlbumDisplay(string album, string toc, bool tocComplete)
        {
            return HasHistoricalMarker(album) && Classify(toc, tocComplete) == CdxCompatibilityState.Incompatible
                ? RemoveHistoricalMarker(album)
                : (album ?? "");
        }

        public static string NormalizeLegacyAlbumDisplay(string album, string durationSource, double durationSeconds)
        {
            return HasHistoricalMarker(album) && ClassifyLegacyDuration(durationSource, durationSeconds) == CdxCompatibilityState.Incompatible
                ? RemoveHistoricalMarker(album)
                : (album ?? "");
        }
    }
}
