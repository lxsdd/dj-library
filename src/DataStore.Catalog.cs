using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;

namespace DJLibrary
{
    public sealed partial class DataStore
    {
        private List<DigitalItem> _activeLiveItems;
        private readonly JavaScriptSerializer _catalogJson = new JavaScriptSerializer();

        public void LoadFromCatalog(CatalogService catalog, string referenceMatchesPath)
        {
            if (catalog == null) throw new ArgumentNullException("catalog");
            bool pristine = !catalog.HasUserChanges;
            Tracks.Clear(); Cds.Clear(); TracksByDisc.Clear(); MatchesByTrack.Clear();
            DigitalItemCount = 0; ProjectedTrackGenreCount = 0; ProjectedCdGenreCount = 0;
            DigitalSourceDescription = pristine ? "Qualifizierter Referenzindex" : "Catalog · Digital Index wartet auf Live-Bridge";

            List<CatalogNativeDisc> discs = catalog.GetNativeDiscs();
            List<CatalogNativeTrack> tracks = catalog.GetNativeTracks();
            Dictionary<long,int> trackCounts = new Dictionary<long,int>();
            foreach (CatalogNativeTrack source in tracks)
            {
                int count; if (!trackCounts.TryGetValue(source.DiscId,out count)) count=0; trackCounts[source.DiscId]=count+1;
            }

            foreach (CatalogNativeDisc source in discs)
            {
                Dictionary<string,object> legacy = ParseLegacy(source.LegacyJson);
                CdRow c = new CdRow();
                c.DiscId=CheckedInt(source.Id,"Disc-ID"); c.ReleaseId=CheckedInt(source.ReleaseId,"Release-ID"); c.LegacyAlbumId=CheckedInt(source.LegacyAlbumId,"LegacyAlbumId");
                c.AlbumArtist=source.AlbumArtist; c.Album=source.Album; c.DiscNumber=source.DiscNumber; c.TotalDiscs=Math.Max(1,source.TotalDiscs);
                c.Tracks=source.TrackCount; c.DurationSeconds=source.DurationSeconds; c.DurationSource=J(legacy,"DurationSource","catalog");
                c.Date=source.ReleaseDate; c.Genre=source.Genre; c.LegacyGenre=J(legacy,"LegacyGenre",source.Genre); c.Label=source.Label; c.Catalog=source.Catalog; c.Country=source.Country;
                c.Medium=source.Medium; c.Toc=source.Toc; c.TocComplete=source.TocComplete; c.CdTextPresent=source.CdTextPresent;
                c.LegacySerial=J(legacy,"LegacySerial",""); c.LegacyPackaging=J(legacy,"LegacyPackaging",""); c.LegacyReleaseType=J(legacy,"LegacyReleaseType","");
                c.LayoutKind=J(legacy,"LayoutKind",J(legacy,"LayoutKind","")); c.PhysicalTrackCount=JI(legacy,"PhysicalTrackCount",source.TrackCount); c.LogicalTrackCount=JI(legacy,"LogicalTrackCount",source.TrackCount);
                c.IssueCode=J(legacy,"IssueCode",""); c.IssueDetail=J(legacy,"IssueDetail","");
                if (pristine)
                {
                    c.StrongTracks=JI(legacy,"StrongTracks",0); c.LikelyTracks=JI(legacy,"LikelyTracks",0); c.CandidateTracks=JI(legacy,"CandidateTracks",0); c.NoMatchTracks=JI(legacy,"NoMatchTracks",Math.Max(0,source.TrackCount));
                    c.GenreSource=J(legacy,"GenreSource","legacy"); c.DigitalGenres=J(legacy,"DigitalGenres",""); c.GenreCoverage=JD(legacy,"GenreCoverage",0); c.GenreDominance=JD(legacy,"GenreDominance",0); c.ProjectedGenreTrackCount=JI(legacy,"ProjectedGenreTrackCount",0);
                }
                else
                {
                    c.StrongTracks=0; c.LikelyTracks=0; c.CandidateTracks=0; c.NoMatchTracks=source.TrackCount; c.GenreSource="catalog"; c.DigitalGenres=""; c.GenreCoverage=0; c.GenreDominance=0; c.ProjectedGenreTrackCount=0;
                }
                if(c.GenreProjected) ProjectedCdGenreCount++;
                c.SearchText=LowerJoin(c.AlbumArtist,c.Album,c.Date,c.Genre,c.LegacyGenre,c.DigitalGenres,c.Label,c.Catalog,c.Country,c.Medium);
                Cds.Add(c);
            }

            foreach (CatalogNativeTrack source in tracks)
            {
                Dictionary<string,object> legacy = ParseLegacy(source.LegacyJson);
                TrackRow t = new TrackRow();
                t.TrackId=CheckedInt(source.Id,"Track-ID"); t.DiscId=CheckedInt(source.DiscId,"Disc-ID"); t.Artist=source.Artist; t.Title=source.Title; t.Version=source.Version;
                t.DisplayTitle=J(legacy,"DisplayTitle",String.IsNullOrWhiteSpace(source.Version)?source.Title:source.Title+" ("+source.Version+")");
                t.Date=source.ReleaseDate; t.Genre=source.Genre; t.LegacyGenre=source.LegacyGenre; t.Style=J(legacy,"Style",""); t.Bpm=source.Bpm; t.DurationSeconds=source.DurationSeconds;
                t.TrackNumber=source.Position; t.LegacyTrackNumber=J(legacy,"LegacyTrackNumber",source.Position.ToString(CultureInfo.InvariantCulture)); t.LegacyArtistRaw=source.LegacyArtistRaw;
                t.Album=source.Album; t.AlbumArtist=source.AlbumArtist; t.Label=source.Label; t.Catalog=source.Catalog; t.Medium=source.Medium; t.DiscNumber=source.DiscNumber; t.TotalDiscs=Math.Max(1,source.TotalDiscs);
                int discCount; if(!trackCounts.TryGetValue(source.DiscId,out discCount)) discCount=0; t.DiscTrackCount=discCount; t.DiscDurationSeconds=source.DiscDurationSeconds;
                t.DiscDurationSource=J(legacy,"DiscDurationSource","catalog"); t.DiscLayout=J(legacy,"DiscLayout",""); t.IssueCode=J(legacy,"IssueCode",""); t.IssueDetail=J(legacy,"IssueDetail","");
                if(pristine)
                {
                    t.DigitalLevel=J(legacy,"DigitalLevel","none"); t.DigitalCount=JI(legacy,"DigitalCount",0); t.StrongCount=JI(legacy,"StrongCount",0); t.LikelyCount=JI(legacy,"LikelyCount",0); t.CandidateCount=JI(legacy,"CandidateCount",0);
                    t.GenreSource=J(legacy,"GenreSource","legacy"); t.GenreConfidence=JD(legacy,"GenreConfidence",0); t.GenreEvidenceCount=JI(legacy,"GenreEvidenceCount",0);
                }
                else
                {
                    t.DigitalLevel="none"; t.DigitalCount=t.StrongCount=t.LikelyCount=t.CandidateCount=0; t.GenreSource="catalog"; t.GenreConfidence=0; t.GenreEvidenceCount=0;
                }
                if(t.GenreProjected) ProjectedTrackGenreCount++;
                t.SearchText=LowerJoin(t.Artist,t.Title,t.Version,t.DisplayTitle,t.Album,t.AlbumArtist,t.Genre,t.LegacyGenre,t.Style,t.Label,t.Catalog,t.Date,t.Medium);
                Tracks.Add(t);
                List<TrackRow> list; if(!TracksByDisc.TryGetValue(t.DiscId,out list)) { list=new List<TrackRow>(); TracksByDisc[t.DiscId]=list; } list.Add(t);
            }

            BindTracksToPhysicalDiscs();
            if(pristine && !String.IsNullOrWhiteSpace(referenceMatchesPath) && File.Exists(referenceMatchesPath)) LoadMatches(referenceMatchesPath);
            if(_activeLiveItems!=null) ApplyLiveDigitalItems(new List<DigitalItem>(_activeLiveItems));
        }

        public void ReloadFromCatalog(CatalogService catalog, string referenceMatchesPath)
        {
            LoadFromCatalog(catalog,referenceMatchesPath);
        }

        internal static string ValidateCatalogSourceContract(CatalogService catalog, string referenceMatchesPath)
        {
            DataStore probe=new DataStore(); probe.LoadFromCatalog(catalog,referenceMatchesPath); probe.Validate();
            CatalogCounts counts=catalog.GetCounts();
            if(probe.Cds.Count!=counts.Discs || probe.Tracks.Count!=counts.Tracks) throw new InvalidDataException("Native-Ansicht verwendet nicht vollständig den SQLite-Catalog.");
            return "Native physical source = writable SQLite catalog; TSV physical fixtures are not runtime authority";
        }

        private Dictionary<string,object> ParseLegacy(string json)
        {
            if(String.IsNullOrWhiteSpace(json)) return new Dictionary<string,object>(StringComparer.OrdinalIgnoreCase);
            try
            {
                Dictionary<string,object> raw=_catalogJson.DeserializeObject(json) as Dictionary<string,object>;
                return raw ?? new Dictionary<string,object>(StringComparer.OrdinalIgnoreCase);
            }
            catch { return new Dictionary<string,object>(StringComparer.OrdinalIgnoreCase); }
        }

        private static string J(Dictionary<string,object> values,string key,string fallback)
        {
            object value; if(values!=null && values.TryGetValue(key,out value) && value!=null) { string s=Convert.ToString(value,CultureInfo.InvariantCulture); if(!String.IsNullOrWhiteSpace(s)) return s; }
            return fallback ?? "";
        }
        private static int JI(Dictionary<string,object> values,string key,int fallback)
        {
            string value=J(values,key,""); int result; return Int32.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out result)?result:fallback;
        }
        private static double JD(Dictionary<string,object> values,string key,double fallback)
        {
            string value=J(values,key,""); double result; return Double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out result)?result:fallback;
        }
        private static int CheckedInt(long value,string label)
        {
            if(value>Int32.MaxValue || value<Int32.MinValue) throw new InvalidDataException(label+" außerhalb des Native-ID-Bereichs: "+value.ToString(CultureInfo.InvariantCulture));
            return (int)value;
        }
    }
}
