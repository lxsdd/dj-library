using System;

namespace DJLibrary
{
    public sealed class CdMetadataFetchOptions
    {
        public bool UseCatalog { get; set; }
        public bool UseFoobar { get; set; }
        public bool UseMusicBrainz { get; set; }
        public bool UseDiscogs { get; set; }
        public bool UseTitleAnalysis { get; set; }

        // Per-lookup search anchors. They are intentionally not persisted as global defaults.
        public string SearchAlbumArtist { get; set; }
        public string SearchAlbum { get; set; }
        public string SearchYear { get; set; }
        public string SearchLabel { get; set; }
        public string SearchCatalog { get; set; }
        public string SearchCountry { get; set; }

        public CdMetadataFetchOptions()
        {
            UseCatalog = true;
            UseFoobar = true;
            UseMusicBrainz = true;
            UseDiscogs = true;
            UseTitleAnalysis = true;
            SearchAlbumArtist = SearchAlbum = SearchYear = SearchLabel = SearchCatalog = SearchCountry = "";
        }

        public CdMetadataFetchOptions Clone()
        {
            return new CdMetadataFetchOptions
            {
                UseCatalog = UseCatalog,
                UseFoobar = UseFoobar,
                UseMusicBrainz = UseMusicBrainz,
                UseDiscogs = UseDiscogs,
                UseTitleAnalysis = UseTitleAnalysis,
                SearchAlbumArtist = SearchAlbumArtist,
                SearchAlbum = SearchAlbum,
                SearchYear = SearchYear,
                SearchLabel = SearchLabel,
                SearchCatalog = SearchCatalog,
                SearchCountry = SearchCountry
            };
        }

        public string Summary
        {
            get
            {
                System.Collections.Generic.List<string> values = new System.Collections.Generic.List<string>();
                if (UseCatalog) values.Add("Catalog");
                if (UseFoobar) values.Add("foobar");
                if (UseMusicBrainz) values.Add("MusicBrainz");
                if (UseDiscogs) values.Add("Discogs");
                if (UseTitleAnalysis) values.Add("Title Analysis");
                return values.Count == 0 ? "no additional sources" : String.Join(" · ", values.ToArray());
            }
        }
    }
}
