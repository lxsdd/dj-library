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

    // User-defined compatibility display rule. The original CdxCompatibility
    // historical marker migration intentionally remains fixed and independent.
    public static class DiscPlaybackRule
    {
        private static DiscPlaybackRuleSettings _current = new DiscPlaybackRuleSettings();

        public static DiscPlaybackRuleSettings Current
        {
            get { return _current; }
        }

        public static void Configure(DiscPlaybackRuleSettings options)
        {
            DiscPlaybackRuleSettings normalized = options == null ? new DiscPlaybackRuleSettings() : options.Clone();
            if (normalized.LimitFrames < 1 || normalized.LimitFrames > 999 * 60 * 75 + 59 * 75 + 74)
                normalized.LimitFrames = CdxCompatibility.IncompatibleFromFrames;
            if (String.IsNullOrWhiteSpace(normalized.DeviceLabel)) normalized.DeviceLabel = "Disc player";
            if (normalized.WarningColor != "Orange") normalized.WarningColor = "Red";
            _current = normalized;
        }

        public static bool TryParseMsf(string value, out int frames)
        {
            frames = 0;
            if (String.IsNullOrWhiteSpace(value)) return false;
            string[] parts = value.Trim().Split(':');
            if (parts.Length != 3) return false;
            int mm, ss, ff;
            if (!Int32.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out mm) ||
                !Int32.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out ss) ||
                !Int32.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out ff) ||
                mm < 0 || mm > 999 || ss < 0 || ss > 59 || ff < 0 || ff > 74)
                return false;
            frames = (mm * 60 + ss) * 75 + ff;
            return frames > 0;
        }

        public static CdxCompatibilityState Classify(string toc, bool complete)
        {
            int duration;
            if (!CdxCompatibility.TryGetDurationFrames(toc, complete, out duration))
                return CdxCompatibilityState.Unknown;
            DiscPlaybackRuleSettings rule = Current;
            bool exceeds = rule.Inclusive ? duration >= rule.LimitFrames : duration > rule.LimitFrames;
            return exceeds ? CdxCompatibilityState.Incompatible : CdxCompatibilityState.Compatible;
        }

        public static string Text(CdxCompatibilityState state)
        {
            return Current.Enabled ? CdxCompatibility.Text(state) : "";
        }

        public static string Code(CdxCompatibilityState state, bool track)
        {
            if (!Current.Enabled || (track ? !Current.MarkTracks : !Current.MarkCds)) return "";
            return CdxCompatibility.Code(state);
        }

        public static string Explanation(CdxCompatibilityState state)
        {
            if (!Current.Enabled) return "Physical-disc playback rule is disabled.";
            string rule = Current.DeviceLabel + " · " +
                (Current.Inclusive ? "≥ " : "> ") + CdxCompatibility.FormatMsf(Current.LimitFrames);
            if (state == CdxCompatibilityState.Unknown)
                return rule + ": unknown, because complete valid physical TOC information is unavailable.";
            return rule + (state == CdxCompatibilityState.Incompatible ?
                ": the containing physical disc exceeds the configured player limit." :
                ": the containing physical disc remains under the configured player limit.");
        }

        public static string ValidateContract()
        {
            DiscPlaybackRuleSettings saved = Current.Clone();
            try
            {
                DiscPlaybackRuleSettings opt = new DiscPlaybackRuleSettings();
                int frames;
                if (!TryParseMsf("79:59:74", out frames) || frames != 359999 ||
                    TryParseMsf("79:60:00", out frames) || TryParseMsf("79:59:75", out frames))
                    throw new InvalidOperationException("MM:SS:FF playback threshold parsing failed.");
                opt.LimitFrames = 359999;
                Configure(opt);
                if (Classify("150 360149", true) != CdxCompatibilityState.Compatible ||
                    Classify("150 360149", false) != CdxCompatibilityState.Unknown ||
                    Classify("150 360149 300", true) != CdxCompatibilityState.Unknown ||
                    Classify("150 360149", true) != CdxCompatibilityState.Compatible ||
                    Classify("150 360150", true) != CdxCompatibilityState.Incompatible)
                    throw new InvalidOperationException("Physical TOC inclusivity threshold failed.");
                opt.Inclusive = false;
                Configure(opt);
                if (Classify("150 360149", true) != CdxCompatibilityState.Compatible ||
                    Classify("150 360150", true) != CdxCompatibilityState.Compatible ||
                    Classify("150 360151", true) != CdxCompatibilityState.Incompatible)
                    throw new InvalidOperationException("Strictly-greater-than custom threshold failed.");
                opt.Enabled = false;
                Configure(opt);
                if (Text(Classify("150 360151", true)) != "" ||
                    Code(CdxCompatibilityState.Incompatible, true) != "")
                    throw new InvalidOperationException("Disabled playback rule is not visually silent.");
                return "configurable physical TOC limit + MM:SS:FF parser + inclusive/strict boundary + disabled markings";
            }
            finally { Configure(saved); }
        }
    }

}
