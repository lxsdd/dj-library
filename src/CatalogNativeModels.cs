namespace DJLibrary
{
    public sealed class CatalogNativeDisc
    {
        public long Id;
        public long ReleaseId;
        public long LegacyAlbumId;
        public string AlbumArtist = "";
        public string Album = "";
        public int DiscNumber;
        public int TotalDiscs;
        public int TrackCount;
        public double DurationSeconds;
        public string ReleaseDate = "";
        public string Genre = "";
        public string Label = "";
        public string Catalog = "";
        public string Country = "";
        public string Medium = "";
        public string Toc = "";
        public bool TocComplete;
        public bool CdTextPresent;
        public string LegacyJson = "{}";
    }

    public sealed class CatalogNativeTrack
    {
        public long Id;
        public long DiscId;
        public int Position;
        public string Artist = "";
        public string Title = "";
        public string Version = "";
        public string ReleaseDate = "";
        public string Genre = "";
        public string LegacyGenre = "";
        public double Bpm;
        public double DurationSeconds;
        public string LegacyArtistRaw = "";
        public string LegacyJson = "{}";
        public string Album = "";
        public string AlbumArtist = "";
        public string Label = "";
        public string Catalog = "";
        public string Medium = "";
        public int DiscNumber;
        public int TotalDiscs;
        public double DiscDurationSeconds;
        public string DiscToc = "";
        public bool DiscTocComplete;
    }
}
