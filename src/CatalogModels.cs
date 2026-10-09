using System;
using System.Collections.Generic;
using System.Globalization;

namespace DJLibrary
{
    public sealed class CatalogRelease
    {
        public long Id { get; set; }
        public string AlbumArtist { get; set; }
        public string Album { get; set; }
        public string ReleaseDate { get; set; }
        public string Genre { get; set; }
        public string Label { get; set; }
        public string Catalog { get; set; }
        public string Country { get; set; }
        public int TotalDiscs { get; set; }

        public CatalogRelease()
        {
            AlbumArtist = Album = ReleaseDate = Genre = Label = Catalog = Country = "";
            TotalDiscs = 1;
        }

        public string DisplayText
        {
            get { return (String.IsNullOrWhiteSpace(AlbumArtist) ? "—" : AlbumArtist) + " — " + (String.IsNullOrWhiteSpace(Album) ? "—" : Album); }
        }
    }

    public sealed class CatalogDisc
    {
        public long Id { get; set; }
        public long ReleaseId { get; set; }
        public long? LegacyAlbumId { get; set; }
        public int DiscNumber { get; set; }
        public string Medium { get; set; }
        public string Toc { get; set; }
        public bool TocComplete { get; set; }
        public bool CdTextPresent { get; set; }
        public string CdTextStatus { get; set; }
        public double DurationSeconds { get; set; }
        public string LegacyJson { get; set; }

        public CatalogDisc()
        {
            DiscNumber = 1;
            Medium = Toc = "";
            CdTextStatus = "manual_unknown";
            LegacyJson = "{}";
        }

        public string DisplayText
        {
            get { return "Disc " + DiscNumber.ToString() + (String.IsNullOrWhiteSpace(Medium) ? "" : " · " + Medium); }
        }
        public string DurationText { get { return UiHelpers.FormatDuration(DurationSeconds); } }
        public string CdTextStatusText
        {
            get
            {
                if (CdTextStatus == "drive_present") return "Vom Drive erkannt";
                if (CdTextStatus == "drive_absent") return "Vom Drive geprüft: nicht present";
                if (CdTextStatus == "drive_unavailable") return "Vom Drive cannot be checked reliably";
                if (CdTextStatus == "drive_read_error") return "Read Error beim CD-TEXT";
                if (CdTextStatus == "legacy_present") return "Im Legacy-Bestand als present markiert";
                if (CdTextStatus == "manual_present") return "Manual als verfügbar markiert";
                return "Im Legacy-Bestand nicht erfasst / not checked";
            }
        }
        public CdxCompatibilityState CdxCompatibilityState { get { return DiscPlaybackRule.Classify(Toc, TocComplete); } }
        public string CdxCompatibilityText { get { return DiscPlaybackRule.Text(CdxCompatibilityState); } }
        public string CdxCompatibilityCode { get { return DiscPlaybackRule.Code(CdxCompatibilityState, false); } }
        public int CdxCompatibilitySortKey { get { return CdxCompatibility.SortKey(CdxCompatibilityState); } }
        public string CdxCompatibilityToolTip { get { return DiscPlaybackRule.Explanation(CdxCompatibilityState); } }
    }

    public sealed class CatalogTrack
    {
        public string ParentDiscToc { get; set; }
        public bool ParentDiscTocComplete { get; set; }
        public CdxCompatibilityState CdxCompatibilityState { get { return DiscPlaybackRule.Classify(ParentDiscToc, ParentDiscTocComplete); } }
        public string CdxCompatibilityText { get { return DiscPlaybackRule.Text(CdxCompatibilityState); } }
        public string CdxCompatibilityCode { get { return DiscPlaybackRule.Code(CdxCompatibilityState, true); } }
        public int CdxCompatibilitySortKey { get { return CdxCompatibility.SortKey(CdxCompatibilityState); } }
        public string CdxCompatibilityToolTip { get { return DiscPlaybackRule.Explanation(CdxCompatibilityState); } }

        public long Id { get; set; }
        public long DiscId { get; set; }
        public int Position { get; set; }
        public string Artist { get; set; }
        public string Title { get; set; }
        public string Version { get; set; }
        public string ReleaseDate { get; set; }
        public string Genre { get; set; }
        public string LegacyGenre { get; set; }
        public double Bpm { get; set; }
        public double DurationSeconds { get; set; }
        public string LegacyArtistRaw { get; set; }
        public string LegacyJson { get; set; }

        public CatalogTrack()
        {
            Artist = Title = Version = ReleaseDate = Genre = LegacyGenre = LegacyArtistRaw = "";
            LegacyJson = "{}";
        }

        public string DisplayTitle
        {
            get { return String.IsNullOrWhiteSpace(Version) ? Title : Title + " (" + Version + ")"; }
        }
        public string DurationText { get { return UiHelpers.FormatDuration(DurationSeconds); } }
    }

    public sealed class CatalogCounts
    {
        public long Releases { get; set; }
        public long Discs { get; set; }
        public long Tracks { get; set; }
    }

    public sealed class CatalogHistoryEntry
    {
        public long Id { get; set; }
        public string ChangedUtc { get; set; }
        public string TransactionGroup { get; set; }
        public string Entity { get; set; }
        public long EntityId { get; set; }
        public string Operation { get; set; }
        public bool Undone { get; set; }
        public bool Redoable { get; set; }

        public string DisplayText
        {
            get
            {
                string operation = Operation == "create" ? "Created" :
                                   Operation == "update" ? "Edited" :
                                   Operation == "delete" ? "Deleted" :
                                   Operation == "reorder" ? "Reordered" : Operation;
                string entity = Entity == "release" ? "Release" :
                                Entity == "disc" ? "Disc" :
                                Entity == "track" ? "Track" : Entity;
                string timestamp = ChangedUtc;
                DateTime utc;
                if (DateTime.TryParseExact(ChangedUtc, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out utc))
                    timestamp = utc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture);
                string state = Undone ? (Redoable ? " · undone" : " · discarded") : "";
                return timestamp + " · " + operation + " · " + entity + " #" + EntityId.ToString() + state;
            }
        }
    }

    public sealed class CatalogSearchDocument
    {
        public long ReleaseId { get; set; }
        public long DiscId { get; set; }
        public long TrackId { get; set; }
        public string SearchText { get; set; }

        public CatalogSearchDocument()
        {
            SearchText = "";
        }
    }

    public sealed class CatalogSearchResult
    {
        public HashSet<long> ReleaseIds { get; private set; }
        public HashSet<long> DiscIds { get; private set; }
        public HashSet<long> TrackIds { get; private set; }

        public CatalogSearchResult()
        {
            ReleaseIds = new HashSet<long>();
            DiscIds = new HashSet<long>();
            TrackIds = new HashSet<long>();
        }
    }

    public sealed class CdTrackCapture
    {
        public int Position { get; set; }
        public double DurationSeconds { get; set; }
        public string Title { get; set; }
        public string RawTitle { get; set; }
        public string Artist { get; set; }
        public string Version { get; set; }
        public string Genre { get; set; }
        public string MetadataSource { get; set; }
        public string MetadataDetail { get; set; }
        public double MetadataConfidence { get; set; }

        public string DurationText { get { return UiHelpers.FormatDuration(DurationSeconds); } }

        public CdTrackCapture()
        {
            Title = RawTitle = Artist = Version = Genre = MetadataSource = MetadataDetail = "";
        }
    }

    public sealed class CdSnapshot
    {
        public string DriveId { get; set; }
        public string Toc { get; set; }
        public bool CdTextPresent { get; set; }
        public string CdTextStatus { get; set; }
        public string CdTextSource { get; set; }
        public string CdTextError { get; set; }
        public string Album { get; set; }
        public string AlbumArtist { get; set; }
        public string ReleaseDate { get; set; }
        public string Genre { get; set; }
        public string Label { get; set; }
        public string Catalog { get; set; }
        public string Country { get; set; }
        public string MusicBrainzDiscId { get; set; }
        public string MusicBrainzReleaseId { get; set; }
        public string DiscogsReleaseId { get; set; }
        public List<MetadataEvidence> MetadataEvidence { get; set; }
        public List<MetadataSourceStatus> MetadataSources { get; set; }
        public List<CdTrackCapture> Tracks { get; set; }

        public CdSnapshot()
        {
            DriveId = Toc = Album = AlbumArtist = ReleaseDate = Genre = Label = Catalog = Country = "";
            CdTextSource = CdTextError = MusicBrainzDiscId = MusicBrainzReleaseId = DiscogsReleaseId = "";
            CdTextStatus = "drive_unavailable";
            MetadataEvidence = new List<MetadataEvidence>();
            MetadataSources = new List<MetadataSourceStatus>();
            Tracks = new List<CdTrackCapture>();
        }
    }

    public interface ICdDrive
    {
        string DisplayName { get; }
        CdSnapshot Capture();
    }

    public sealed class SimulatedCdDrive : ICdDrive
    {
        private readonly CdSnapshot _snapshot;
        public SimulatedCdDrive(CdSnapshot snapshot) { _snapshot = snapshot; }
        public string DisplayName { get { return "Simulated"; } }
        public CdSnapshot Capture() { return _snapshot; }
    }
}
