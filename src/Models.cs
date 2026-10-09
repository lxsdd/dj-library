using System;
using System.Collections.Generic;

namespace DJLibrary
{
    public sealed class TrackRow
    {
        private string _album;
        private string _medium;

        public int TrackId { get; set; }
        public int DiscId { get; set; }
        public string Artist { get; set; }
        public string Title { get; set; }
        public string Version { get; set; }
        public string DisplayTitle { get; set; }
        public string Date { get; set; }
        public string Genre { get; set; }
        public string LegacyGenre { get; set; }
        public string GenreSource { get; set; }
        public double GenreConfidence { get; set; }
        public int GenreEvidenceCount { get; set; }
        public string Style { get; set; }
        public double Bpm { get; set; }
        public double DurationSeconds { get; set; }
        public int TrackNumber { get; set; }
        public string LegacyTrackNumber { get; set; }
        public string LegacyArtistRaw { get; set; }
        public string Album
        {
            get { return CdxCompatibility.NormalizeLegacyAlbumDisplay(_album, DiscDurationSource, DiscDurationSeconds); }
            set { _album = value ?? ""; }
        }
        public string LegacyAlbum { get { return _album ?? ""; } }
        public string DisplayAlbum { get { return Album; } }
        public string AlbumArtist { get; set; }
        public string Label { get; set; }
        public string Catalog { get; set; }
        public string Medium
        {
            get { return CatalogMultiDiscCompatibility.NormalizeMediumDisplay(_medium, TotalDiscs); }
            set { _medium = value ?? ""; }
        }
        public string LegacyMedium { get { return _medium ?? ""; } }
        public int DiscNumber { get; set; }
        public int TotalDiscs { get; set; }
        public int DiscTrackCount { get; set; }
        public double DiscDurationSeconds { get; set; }
        public string DiscDurationSource { get; set; }
        // Inherited from the actual containing disc, never estimated from the track.
        public string DiscToc { get; set; }
        public bool DiscTocComplete { get; set; }
        public string DiscLayout { get; set; }
        public string IssueCode { get; set; }
        public string IssueDetail { get; set; }
        public string DigitalLevel { get; set; }
        public int DigitalCount { get; set; }
        public int StrongCount { get; set; }
        public int LikelyCount { get; set; }
        public int CandidateCount { get; set; }
        public string SearchText { get; set; }

        public CdxCompatibilityState CdxCompatibilityState { get { return DiscPlaybackRule.Classify(DiscToc, DiscTocComplete); } }
        public string CdxCompatibilityText { get { return DiscPlaybackRule.Text(CdxCompatibilityState); } }
        public string CdxCompatibilityCode { get { return DiscPlaybackRule.Code(CdxCompatibilityState, true); } }
        public int CdxCompatibilitySortKey { get { return CdxCompatibility.SortKey(CdxCompatibilityState); } }
        public string CdxCompatibilityToolTip { get { return DiscPlaybackRule.Explanation(CdxCompatibilityState); } }

        public string DurationText { get { return UiHelpers.FormatDuration(DurationSeconds); } }
        public string BpmText { get { return Bpm > 0 ? Bpm.ToString("0.##") : ""; } }
        public string DiscText
        {
            get
            {
                if (TotalDiscs > 1) return DiscNumber.ToString() + "/" + TotalDiscs.ToString();
                return DiscNumber > 0 ? DiscNumber.ToString() : "";
            }
        }
        public string DigitalText
        {
            get { return UiHelpers.DigitalText(DigitalLevel, StrongCount, LikelyCount, CandidateCount); }
        }
        public string IssueText { get { return String.IsNullOrEmpty(IssueCode) ? "" : "Auffällig"; } }
        public string SpecialText { get { return UiHelpers.LayoutText(DiscLayout); } }
        public string GenreSourceText
        {
            get
            {
                if (GenreSource == "digital_live_exact_version_duration") return "Digital live: Artist + Title/Mix + Duration";
                if (GenreSource == "digital_live_exact_title_duration") return "Digital live: Artist + Title + Duration";
                if (GenreSource == "digital_exact_version_duration") return "Digital: Artist + Title + Mix + Duration";
                if (GenreSource == "digital_exact_version") return "Digital: Artist + Title + Mix";
                return "Legacy";
            }
        }
        public bool GenreProjected { get { return GenreSource != null && GenreSource.StartsWith("digital_", StringComparison.Ordinal); } }
    }

    public sealed class CdRow
    {
        private string _album;
        private string _medium;

        public int DiscId { get; set; }
        public int ReleaseId { get; set; }
        public int LegacyAlbumId { get; set; }
        public string AlbumArtist { get; set; }
        public string Album
        {
            get { return CdxCompatibility.NormalizeLegacyAlbumDisplay(_album, Toc, TocComplete); }
            set { _album = value ?? ""; }
        }
        public string LegacyAlbum { get { return _album ?? ""; } }
        public string DisplayAlbum { get { return Album; } }
        public int DiscNumber { get; set; }
        public int TotalDiscs { get; set; }
        public int Tracks { get; set; }
        public double DurationSeconds { get; set; }
        public string DurationSource { get; set; }
        public string Date { get; set; }
        public string Genre { get; set; }
        public string LegacyGenre { get; set; }
        public string GenreSource { get; set; }
        public string DigitalGenres { get; set; }
        public double GenreCoverage { get; set; }
        public double GenreDominance { get; set; }
        public int ProjectedGenreTrackCount { get; set; }
        public string Label { get; set; }
        public string Catalog { get; set; }
        public string Country { get; set; }
        public string Medium
        {
            get { return CatalogMultiDiscCompatibility.NormalizeMediumDisplay(_medium, TotalDiscs); }
            set { _medium = value ?? ""; }
        }
        public string LegacyMedium { get { return _medium ?? ""; } }
        public string Toc { get; set; }
        public bool TocComplete { get; set; }
        public bool CdTextPresent { get; set; }
        public string LegacySerial { get; set; }
        public string LegacyPackaging { get; set; }
        public string LegacyReleaseType { get; set; }
        public string LayoutKind { get; set; }
        public int PhysicalTrackCount { get; set; }
        public int LogicalTrackCount { get; set; }
        public string IssueCode { get; set; }
        public string IssueDetail { get; set; }
        public int StrongTracks { get; set; }
        public int LikelyTracks { get; set; }
        public int CandidateTracks { get; set; }
        public int NoMatchTracks { get; set; }
        public string SearchText { get; set; }

        public string DiscText
        {
            get
            {
                if (TotalDiscs > 1) return DiscNumber.ToString() + "/" + TotalDiscs.ToString();
                return DiscNumber > 0 ? DiscNumber.ToString() : "";
            }
        }
        public string DurationText { get { return UiHelpers.FormatDuration(DurationSeconds); } }
        public string DigitalText { get { return StrongTracks.ToString() + "/" + Tracks.ToString(); } }
        public string SpecialText { get { return UiHelpers.LayoutText(LayoutKind); } }
        public string IssueText { get { return String.IsNullOrEmpty(IssueCode) ? "" : "Auffällig"; } }
        public string GenreSourceText { get { return GenreSource == "digital_consensus" ? "Digitaler Track-Konsens" : "Legacy"; } }
        public bool GenreProjected { get { return GenreSource == "digital_consensus"; } }
        public CdxCompatibilityState CdxCompatibilityState { get { return DiscPlaybackRule.Classify(Toc, TocComplete); } }
        public string CdxCompatibilityText { get { return DiscPlaybackRule.Text(CdxCompatibilityState); } }
        public string CdxCompatibilityCode { get { return DiscPlaybackRule.Code(CdxCompatibilityState, false); } }
        public int CdxCompatibilitySortKey { get { return CdxCompatibility.SortKey(CdxCompatibilityState); } }
        public string CdxCompatibilityToolTip { get { return DiscPlaybackRule.Explanation(CdxCompatibilityState); } }
    }

    public sealed class MatchRow
    {
        public int TrackId { get; set; }
        public int DigitalItemId { get; set; }
        public string Level { get; set; }
        public string Method { get; set; }
        public double Confidence { get; set; }
        public string Artist { get; set; }
        public string Title { get; set; }
        public string OriginalTitle { get; set; }
        public string RemixedBy { get; set; }
        public string Album { get; set; }
        public string AlbumArtist { get; set; }
        public double DurationSeconds { get; set; }
        public double Bpm { get; set; }
        public string Label { get; set; }
        public string Catalog { get; set; }
        public string Genre { get; set; }
        public string Style { get; set; }
        public string Codec { get; set; }
        public string Bitrate { get; set; }
        public string Path { get; set; }
        public int Subsong { get; set; }

        public string DurationText { get { return UiHelpers.FormatDuration(DurationSeconds); } }
        public string BpmText { get { return Bpm > 0 ? Bpm.ToString("0.##") : ""; } }
        public string LevelText { get { return UiHelpers.DigitalLevelLabel(Level); } }
        public string ConfidenceText { get { return Confidence > 0 ? Confidence.ToString("0.00") : ""; } }
        public string MethodText { get { return GridRuntimeSupport.ReadableMatchMethod(Method); } }
    }

    public sealed class DigitalItem
    {
        public int ItemId { get; set; }
        public string Path { get; set; }
        public int Subsong { get; set; }
        public string Artist { get; set; }
        public string Title { get; set; }
        public string OriginalTitle { get; set; }
        public string RemixedBy { get; set; }
        public string Album { get; set; }
        public string AlbumArtist { get; set; }
        public string Genre { get; set; }
        public string Style { get; set; }
        public double Bpm { get; set; }
        public string Label { get; set; }
        public string Catalog { get; set; }
        public double DurationSeconds { get; set; }
        public string Codec { get; set; }
        public string Bitrate { get; set; }
        public string Fingerprint { get; set; }
        // Exact Bridge schema-v3 vector payload. Kept separate from the legacy projection
        // so normalization always sees the lossless ordered metadata document.
        public string MetadataVectorsJson { get; set; }
    }

    public sealed class FilterState
    {
        public string Search = "";
        public string Genre = "";
        public string Digital = "";
        public string Year = "";
        public string Medium = "";
        public string Mix = "";
        public string Label = "";
        public string Issues = "";
        public string Cdx = "";
    }

    public sealed class ColumnSpec
    {
        public string Key;
        public string Header;
        public string ToolTip;
        public string BindingPath;
        public double Width;
        public bool Visible;

        public ColumnSpec(string key, string header, string toolTip, string bindingPath, double width, bool visible)
        {
            Key = key;
            Header = header;
            ToolTip = toolTip;
            BindingPath = bindingPath;
            Width = width;
            Visible = visible;
        }
    }
}