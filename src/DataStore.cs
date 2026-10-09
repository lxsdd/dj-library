using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace DJLibrary
{
    public sealed partial class DataStore
    {
        public List<TrackRow> Tracks { get; private set; }
        public List<CdRow> Cds { get; private set; }
        public Dictionary<int, List<TrackRow>> TracksByDisc { get; private set; }
        public Dictionary<int, List<MatchRow>> MatchesByTrack { get; private set; }
        public int DigitalItemCount { get; private set; }
        public int ProjectedTrackGenreCount { get; private set; }
        public int ProjectedCdGenreCount { get; private set; }
        public string DigitalSourceDescription { get; private set; }

        public DataStore()
        {
            Tracks = new List<TrackRow>();
            Cds = new List<CdRow>();
            TracksByDisc = new Dictionary<int, List<TrackRow>>();
            MatchesByTrack = new Dictionary<int, List<MatchRow>>();
            DigitalSourceDescription = "Qualifizierter Testindex";
        }

        public void Load(string dataDirectory)
        {
            LoadTracks(Path.Combine(dataDirectory, "tracks.tsv.gz"));
            LoadCds(Path.Combine(dataDirectory, "cds.tsv.gz"));
            BindTracksToPhysicalDiscs();
            LoadMatches(Path.Combine(dataDirectory, "matches.tsv.gz"));
        }

        // One read-only association per import; no duplicate compatibility data
        // is stored in SQLite or physical MP3 tags.
        private void BindTracksToPhysicalDiscs()
        {
            Dictionary<int, CdRow> discs = new Dictionary<int, CdRow>();
            foreach (CdRow cd in Cds) discs[cd.DiscId] = cd;
            foreach (TrackRow track in Tracks)
            {
                CdRow disc;
                track.DiscToc = discs.TryGetValue(track.DiscId, out disc) ? disc.Toc : "";
                track.DiscTocComplete = disc != null && disc.TocComplete;
            }
        }

        public void SetDigitalSourceDescription(string description)
        {
            if (!String.IsNullOrWhiteSpace(description)) DigitalSourceDescription = description;
        }

        public void GetDigitalMatchCounts(out int strong, out int likely, out int candidate, out int none)
        {
            strong = likely = candidate = none = 0;
            foreach (TrackRow t in Tracks)
            {
                if (String.Equals(t.DigitalLevel, "strong", StringComparison.OrdinalIgnoreCase)) strong++;
                else if (String.Equals(t.DigitalLevel, "likely", StringComparison.OrdinalIgnoreCase)) likely++;
                else if (String.Equals(t.DigitalLevel, "candidate", StringComparison.OrdinalIgnoreCase)) candidate++;
                else none++;
            }
        }

        private static StreamReader OpenGzipText(string path)
        {
            FileStream fs = File.OpenRead(path);
            GZipStream gz = new GZipStream(fs, CompressionMode.Decompress);
            return new StreamReader(gz, new UTF8Encoding(false), true, 65536);
        }

        private void LoadTracks(string path)
        {
            using (StreamReader sr = OpenGzipText(path))
            {
                string line = sr.ReadLine();
                int lineNo = 1;
                while ((line = sr.ReadLine()) != null)
                {
                    lineNo++;
                    string[] p = line.Split('\t');
                    if (p.Length != 36) throw new InvalidDataException("tracks.tsv.gz: Zeile " + lineNo + " hat " + p.Length + " statt 36 Spalten.");
                    TrackRow t = new TrackRow();
                    t.TrackId = I(p, 0);
                    t.DiscId = I(p, 1);
                    t.Artist = S(p, 2);
                    t.Title = S(p, 3);
                    t.Version = S(p, 4);
                    t.DisplayTitle = S(p, 5);
                    t.Date = S(p, 6);
                    t.Genre = S(p, 7);
                    t.Style = S(p, 8);
                    t.Bpm = D(p, 9);
                    t.DurationSeconds = D(p, 10);
                    t.TrackNumber = I(p, 11);
                    t.LegacyTrackNumber = S(p, 12);
                    t.LegacyArtistRaw = S(p, 13);
                    t.Album = S(p, 14);
                    t.AlbumArtist = S(p, 15);
                    t.Label = S(p, 16);
                    t.Catalog = S(p, 17);
                    t.Medium = S(p, 18);
                    t.DiscNumber = I(p, 19);
                    t.TotalDiscs = I(p, 20);
                    t.DiscTrackCount = I(p, 21);
                    t.DiscDurationSeconds = D(p, 22);
                    t.DiscDurationSource = S(p, 23);
                    t.DiscLayout = S(p, 24);
                    t.IssueCode = S(p, 25);
                    t.IssueDetail = S(p, 26);
                    t.DigitalLevel = S(p, 27);
                    t.DigitalCount = I(p, 28);
                    t.StrongCount = I(p, 29);
                    t.LikelyCount = I(p, 30);
                    t.CandidateCount = I(p, 31);
                    t.LegacyGenre = S(p, 32);
                    t.GenreSource = S(p, 33);
                    t.GenreConfidence = D(p, 34);
                    t.GenreEvidenceCount = I(p, 35);
                    if (t.GenreProjected) ProjectedTrackGenreCount++;
                    t.SearchText = LowerJoin(t.Artist, t.Title, t.Version, t.DisplayTitle, t.Album, t.AlbumArtist, t.Genre, t.LegacyGenre, t.Style, t.Label, t.Catalog, t.Date, t.Medium);

                    Tracks.Add(t);

                    List<TrackRow> list;
                    if (!TracksByDisc.TryGetValue(t.DiscId, out list))
                    {
                        list = new List<TrackRow>();
                        TracksByDisc.Add(t.DiscId, list);
                    }
                    list.Add(t);
                }
            }
        }

        private void LoadCds(string path)
        {
            using (StreamReader sr = OpenGzipText(path))
            {
                string line = sr.ReadLine();
                int lineNo = 1;
                while ((line = sr.ReadLine()) != null)
                {
                    lineNo++;
                    string[] p = line.Split('\t');
                    if (p.Length != 37) throw new InvalidDataException("cds.tsv.gz: Zeile " + lineNo + " hat " + p.Length + " statt 37 Spalten.");
                    CdRow c = new CdRow();
                    c.DiscId = I(p, 0);
                    c.ReleaseId = I(p, 1);
                    c.LegacyAlbumId = I(p, 2);
                    c.AlbumArtist = S(p, 3);
                    c.Album = S(p, 4);
                    c.DiscNumber = I(p, 5);
                    c.TotalDiscs = I(p, 6);
                    c.Tracks = I(p, 7);
                    c.DurationSeconds = D(p, 8);
                    c.DurationSource = S(p, 9);
                    c.Date = S(p, 10);
                    c.Genre = S(p, 11);
                    c.Label = S(p, 12);
                    c.Catalog = S(p, 13);
                    c.Country = S(p, 14);
                    c.Medium = S(p, 15);
                    c.Toc = S(p, 16);
                    c.TocComplete = B(p, 17);
                    c.CdTextPresent = B(p, 18);
                    c.LegacySerial = S(p, 19);
                    c.LegacyPackaging = S(p, 20);
                    c.LegacyReleaseType = S(p, 21);
                    c.LayoutKind = S(p, 22);
                    c.PhysicalTrackCount = I(p, 23);
                    c.LogicalTrackCount = I(p, 24);
                    c.IssueCode = S(p, 25);
                    c.IssueDetail = S(p, 26);
                    c.StrongTracks = I(p, 27);
                    c.LikelyTracks = I(p, 28);
                    c.CandidateTracks = I(p, 29);
                    c.NoMatchTracks = I(p, 30);
                    c.LegacyGenre = S(p, 31);
                    c.GenreSource = S(p, 32);
                    c.DigitalGenres = S(p, 33);
                    c.GenreCoverage = D(p, 34);
                    c.GenreDominance = D(p, 35);
                    c.ProjectedGenreTrackCount = I(p, 36);
                    if (c.GenreProjected) ProjectedCdGenreCount++;
                    c.SearchText = LowerJoin(c.AlbumArtist, c.Album, c.Date, c.Genre, c.LegacyGenre, c.DigitalGenres, c.Label, c.Catalog, c.Country, c.Medium);
                    Cds.Add(c);
                }
            }
        }

        private void LoadMatches(string path)
        {
            HashSet<int> digitalIds = new HashSet<int>();
            using (StreamReader sr = OpenGzipText(path))
            {
                string line = sr.ReadLine();
                int lineNo = 1;
                while ((line = sr.ReadLine()) != null)
                {
                    lineNo++;
                    string[] p = line.Split('\t');
                    if (p.Length != 21) throw new InvalidDataException("matches.tsv.gz: Zeile " + lineNo + " hat " + p.Length + " statt 21 Spalten.");
                    MatchRow m = new MatchRow();
                    m.TrackId = I(p, 0);
                    m.DigitalItemId = I(p, 1);
                    if (m.DigitalItemId > 0) digitalIds.Add(m.DigitalItemId);
                    m.Level = S(p, 2);
                    m.Method = S(p, 3);
                    m.Confidence = D(p, 4);
                    m.Artist = S(p, 5);
                    m.Title = S(p, 6);
                    m.OriginalTitle = S(p, 7);
                    m.RemixedBy = S(p, 8);
                    m.Album = S(p, 9);
                    m.AlbumArtist = S(p, 10);
                    m.DurationSeconds = D(p, 11);
                    m.Bpm = D(p, 12);
                    m.Label = S(p, 13);
                    m.Catalog = S(p, 14);
                    m.Codec = S(p, 15);
                    m.Bitrate = S(p, 16);
                    m.Path = S(p, 17);
                    m.Subsong = I(p, 18);
                    m.Genre = S(p, 19);
                    m.Style = S(p, 20);

                    List<MatchRow> list;
                    if (!MatchesByTrack.TryGetValue(m.TrackId, out list))
                    {
                        list = new List<MatchRow>();
                        MatchesByTrack.Add(m.TrackId, list);
                    }
                    list.Add(m);
                }
            }
            DigitalItemCount = digitalIds.Count;
        }

        public List<TrackRow> GetTracksForDisc(int discId)
        {
            List<TrackRow> list;
            if (TracksByDisc.TryGetValue(discId, out list)) return list;
            return new List<TrackRow>();
        }

        public List<MatchRow> GetMatchesForTrack(int trackId)
        {
            List<MatchRow> list;
            if (MatchesByTrack.TryGetValue(trackId, out list)) return list;
            return new List<MatchRow>();
        }

        public DigitalItem GetActiveDigitalItem(int itemId)
        {
            if (itemId <= 0 || _activeLiveItems == null) return null;
            foreach (DigitalItem item in _activeLiveItems)
            {
                if (item.ItemId == itemId) return item;
            }
            return null;
        }

        public string ApplyLiveDigitalItems(List<DigitalItem> items)
        {
            if (items == null) throw new ArgumentNullException("items");
            _activeLiveItems = new List<DigitalItem>(items);
            // A completed bridge generation is authoritative. Rebuild derived match/genre state
            // from preserved legacy values so repeated generations never accumulate stale projections.
            MatchesByTrack.Clear(); DigitalItemCount = items.Count; ProjectedTrackGenreCount = 0; ProjectedCdGenreCount = 0;
            DigitalSourceDescription = "Live-foobar-Bridge";

            // Live matching intentionally exposes four confidence states again:
            // strong      = exact displayed-title OR exact structured base-title+version, with no known >3 s duration conflict
            // likely      = exact base title plus useful supporting evidence (matching duration or exact identity with duration conflict)
            // candidate   = exact normalized artist + base title only
            // none        = no exact artist + base-title/display-title candidate
            //
            // Only STRONG matches are ever allowed to project GENRE. Likely/candidate rows are
            // visibility for the user, not permission to modify derived metadata.
            Dictionary<string, List<DigitalItem>> byArtistDisplay = new Dictionary<string, List<DigitalItem>>(StringComparer.Ordinal);
            Dictionary<string, List<DigitalItem>> byArtistBaseVersion = new Dictionary<string, List<DigitalItem>>(StringComparer.Ordinal);
            Dictionary<string, List<DigitalItem>> byArtistBase = new Dictionary<string, List<DigitalItem>>(StringComparer.Ordinal);
            foreach (DigitalItem d in items)
            {
                AddDigitalIndex(byArtistDisplay, Norm(d.Artist) + "\n" + Norm(d.Title), d);
                string digitalBase = !String.IsNullOrWhiteSpace(d.OriginalTitle) ? d.OriginalTitle : d.Title;
                AddDigitalIndex(byArtistBase, Norm(d.Artist) + "\n" + Norm(digitalBase), d);
                AddDigitalIndex(byArtistBaseVersion, Norm(d.Artist) + "\n" + Norm(digitalBase) + "\n" + Norm(d.RemixedBy), d);
            }

            foreach (TrackRow t in Tracks)
            {
                t.Genre = t.LegacyGenre; t.GenreSource = "legacy"; t.GenreConfidence = 0; t.GenreEvidenceCount = 0;
                t.DigitalLevel = "none"; t.DigitalCount = t.StrongCount = t.LikelyCount = t.CandidateCount = 0;

                string displayTitle = String.IsNullOrWhiteSpace(t.DisplayTitle) ? t.Title : t.DisplayTitle;
                List<DigitalItem> exactDisplay = null;
                byArtistDisplay.TryGetValue(Norm(t.Artist) + "\n" + Norm(displayTitle), out exactDisplay);

                List<DigitalItem> exactStructured = null;
                if (!String.IsNullOrWhiteSpace(t.Version))
                    byArtistBaseVersion.TryGetValue(Norm(t.Artist) + "\n" + Norm(t.Title) + "\n" + Norm(t.Version), out exactStructured);

                List<DigitalItem> exactBase = null;
                byArtistBase.TryGetValue(Norm(t.Artist) + "\n" + Norm(t.Title), out exactBase);

                List<DigitalItem> identityCandidates = MergeUniqueDigitalItems(exactDisplay, exactStructured);
                List<DigitalItem> candidates = MergeUniqueDigitalItems(exactBase, identityCandidates);
                if (candidates.Count > 0)
                {
                    List<DigitalItem> strong = new List<DigitalItem>();
                    List<DigitalItem> likely = new List<DigitalItem>();
                    List<DigitalItem> simple = new List<DigitalItem>();
                    Dictionary<int, string> levels = new Dictionary<int, string>();
                    Dictionary<int, string> methods = new Dictionary<int, string>();
                    Dictionary<int, double> confidences = new Dictionary<int, double>();

                    foreach (DigitalItem d in candidates)
                    {
                        string digitalBase = !String.IsNullOrWhiteSpace(d.OriginalTitle) ? d.OriginalTitle : d.Title;
                        bool structured = !String.IsNullOrWhiteSpace(t.Version) &&
                            Norm(digitalBase) == Norm(t.Title) && Norm(d.RemixedBy) == Norm(t.Version);
                        bool displayExact = Norm(d.Title) == Norm(displayTitle);
                        bool identityExact = structured || displayExact;
                        bool durationKnown = t.DurationSeconds > 0 && d.DurationSeconds > 0;
                        double durationDiff = durationKnown ? Math.Abs(t.DurationSeconds - d.DurationSeconds) : 0;
                        bool durationClose = durationKnown && durationDiff <= 3.0;
                        bool durationConflict = durationKnown && durationDiff > 3.0;

                        if (identityExact && !durationConflict)
                        {
                            strong.Add(d); levels[d.ItemId] = "strong";
                            methods[d.ItemId] = structured ? "live_exact_artist_title_version_duration" : "live_exact_display_title_duration";
                            confidences[d.ItemId] = structured ? 1.0 : (String.IsNullOrWhiteSpace(t.Version) ? 1.0 : 0.99);
                        }
                        else if (identityExact || durationClose)
                        {
                            likely.Add(d); levels[d.ItemId] = "likely";
                            if (identityExact && durationConflict) methods[d.ItemId] = "live_exact_identity_duration_conflict";
                            else if (durationClose) methods[d.ItemId] = "live_exact_artist_base_title_duration";
                            else methods[d.ItemId] = "live_partial_identity";
                            confidences[d.ItemId] = durationClose ? 0.85 : 0.78;
                        }
                        else
                        {
                            simple.Add(d); levels[d.ItemId] = "candidate";
                            methods[d.ItemId] = "live_exact_artist_base_title";
                            confidences[d.ItemId] = 0.60;
                        }
                    }

                    t.DigitalCount = candidates.Count;
                    t.StrongCount = strong.Count;
                    t.LikelyCount = likely.Count;
                    t.CandidateCount = simple.Count;
                    if (strong.Count > 0) t.DigitalLevel = "strong";
                    else if (likely.Count > 0) t.DigitalLevel = "likely";
                    else t.DigitalLevel = "candidate";

                    List<MatchRow> matches = new List<MatchRow>();
                    foreach (DigitalItem d in candidates)
                    {
                        MatchRow m = new MatchRow();
                        m.TrackId=t.TrackId; m.DigitalItemId=d.ItemId; m.Level=levels[d.ItemId]; m.Method=methods[d.ItemId]; m.Confidence=confidences[d.ItemId];
                        m.Artist=d.Artist; m.Title=d.Title; m.OriginalTitle=d.OriginalTitle; m.RemixedBy=d.RemixedBy; m.Album=d.Album; m.AlbumArtist=d.AlbumArtist; m.DurationSeconds=d.DurationSeconds; m.Bpm=d.Bpm; m.Label=d.Label; m.Catalog=d.Catalog; m.Genre=d.Genre; m.Style=d.Style; m.Codec=d.Codec; m.Bitrate=d.Bitrate; m.Path=d.Path; m.Subsong=d.Subsong;
                        matches.Add(m);
                    }
                    MatchesByTrack[t.TrackId] = matches;

                    // Ambiguity firewall: only equally STRONG candidates participate in genre
                    // projection, and all non-empty strong GENRE values must agree. Likely/candidate
                    // matches are deliberately excluded from projection regardless of their tags.
                    Dictionary<string,int> genres = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
                    foreach (DigitalItem d in strong)
                    {
                        if (!String.IsNullOrWhiteSpace(d.Genre))
                        {
                            int n; genres.TryGetValue(d.Genre.Trim(), out n); genres[d.Genre.Trim()] = n + 1;
                        }
                    }
                    if (strong.Count > 0 && genres.Count == 1)
                    {
                        foreach (KeyValuePair<string,int> g in genres)
                        {
                            t.Genre=g.Key;
                            t.GenreSource=String.IsNullOrWhiteSpace(t.Version) ? "digital_live_exact_title_duration" : "digital_live_exact_version_duration";
                            t.GenreConfidence=1.0; t.GenreEvidenceCount=g.Value; ProjectedTrackGenreCount++;
                        }
                    }
                }

                // Always rebuild SearchText after a live generation. RC8 skipped this for no-match
                // rows, allowing a stale projected genre from the embedded fallback index to remain
                // searchable even though the visible genre had already reverted to LegacyGenre.
                t.SearchText=LowerJoin(t.Artist,t.Title,t.Version,t.DisplayTitle,t.Album,t.AlbumArtist,t.Genre,t.LegacyGenre,t.Style,t.Label,t.Catalog,t.Date,t.Medium);
            }

            // Conservative CD projection: >=50% of tracks have projected genres and >=80% agree.
            foreach (CdRow c in Cds)
            {
                c.Genre=c.LegacyGenre; c.GenreSource="legacy"; c.DigitalGenres=""; c.GenreCoverage=0; c.GenreDominance=0; c.ProjectedGenreTrackCount=0;
                c.StrongTracks=0; c.LikelyTracks=0; c.CandidateTracks=0; c.NoMatchTracks=0;
                List<TrackRow> ts;
                if (TracksByDisc.TryGetValue(c.DiscId,out ts) && ts.Count>0)
                {
                    Dictionary<string,int> gs=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase); int projected=0;
                    foreach(TrackRow t in ts)
                    {
                        if (t.DigitalLevel == "strong") c.StrongTracks++;
                        else if (t.DigitalLevel == "likely") c.LikelyTracks++;
                        else if (t.DigitalLevel == "candidate") c.CandidateTracks++;
                        else c.NoMatchTracks++;
                        if(t.GenreProjected) { projected++; int n; gs.TryGetValue(t.Genre,out n); gs[t.Genre]=n+1; }
                    }
                    c.ProjectedGenreTrackCount=projected; c.GenreCoverage=(double)projected/ts.Count;
                    string best=""; int bestN=0; foreach(KeyValuePair<string,int> g in gs) if(g.Value>bestN){best=g.Key;bestN=g.Value;}
                    c.GenreDominance=projected>0?(double)bestN/projected:0; c.DigitalGenres=JoinGenres(gs);
                    if(c.GenreCoverage>=0.5 && c.GenreDominance>=0.8 && !String.IsNullOrEmpty(best)){c.Genre=best;c.GenreSource="digital_consensus";ProjectedCdGenreCount++;}
                }
                c.SearchText=LowerJoin(c.AlbumArtist,c.Album,c.Date,c.Genre,c.LegacyGenre,c.DigitalGenres,c.Label,c.Catalog,c.Country,c.Medium);
            }
            return String.Format(CultureInfo.InvariantCulture,"Live: {0} Items -> {1} Track- und {2} CD-Genreprojektionen",items.Count,ProjectedTrackGenreCount,ProjectedCdGenreCount);
        }

        private static void AddDigitalIndex(Dictionary<string, List<DigitalItem>> index, string key, DigitalItem item)
        {
            if (String.IsNullOrEmpty(key)) return;
            List<DigitalItem> list;
            if (!index.TryGetValue(key, out list)) { list = new List<DigitalItem>(); index.Add(key, list); }
            list.Add(item);
        }

        private static List<DigitalItem> MergeUniqueDigitalItems(List<DigitalItem> a, List<DigitalItem> b)
        {
            List<DigitalItem> result = new List<DigitalItem>(); HashSet<int> ids = new HashSet<int>();
            if (a != null) foreach (DigitalItem d in a) if (ids.Add(d.ItemId)) result.Add(d);
            if (b != null) foreach (DigitalItem d in b) if (ids.Add(d.ItemId)) result.Add(d);
            return result;
        }

        private static string Norm(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return "";
            StringBuilder b=new StringBuilder(); foreach(char ch in value.ToLowerInvariant()) if(Char.IsLetterOrDigit(ch)) b.Append(ch);
            return b.ToString();
        }
        private static string JoinGenres(Dictionary<string,int> genres)
        {
            List<string> parts=new List<string>(); foreach(KeyValuePair<string,int> g in genres) parts.Add(g.Key+" ("+g.Value.ToString(CultureInfo.InvariantCulture)+")"); parts.Sort(StringComparer.OrdinalIgnoreCase); return String.Join("; ",parts.ToArray());
        }

        public string Validate()
        {
            HashSet<int> trackIds = new HashSet<int>();
            HashSet<int> discIds = new HashSet<int>();
            int projectedFromOriginallyEmptyLegacy = 0;
            foreach (CdRow c in Cds)
            {
                if (c.DiscId <= 0 || !discIds.Add(c.DiscId)) throw new InvalidDataException("Ungültige/doppelte Disc-ID: " + c.DiscId);
                if (c.GenreProjected)
                {
                    if (String.IsNullOrWhiteSpace(c.Genre)) throw new InvalidDataException("CD " + c.DiscId + " besitzt eine Genre-Projektion ohne projiziertes Genre.");
                    if (String.IsNullOrEmpty(c.LegacyGenre)) projectedFromOriginallyEmptyLegacy++;
                }
            }
            foreach (TrackRow t in Tracks)
            {
                if (t.TrackId <= 0 || !trackIds.Add(t.TrackId)) throw new InvalidDataException("Ungültige/doppelte Track-ID: " + t.TrackId);
                if (!discIds.Contains(t.DiscId)) throw new InvalidDataException("Track " + t.TrackId + " verweist auf fehlende Disc " + t.DiscId);
                if (t.GenreProjected)
                {
                    if (String.IsNullOrWhiteSpace(t.Genre)) throw new InvalidDataException("Track " + t.TrackId + " besitzt eine Genre-Projektion ohne projiziertes Genre.");
                    if (String.IsNullOrEmpty(t.LegacyGenre)) projectedFromOriginallyEmptyLegacy++;
                }
            }
            foreach (KeyValuePair<int, List<MatchRow>> pair in MatchesByTrack)
                if (!trackIds.Contains(pair.Key)) throw new InvalidDataException("Digital-Match verweist auf fehlenden Track " + pair.Key);
            return String.Format(CultureInfo.InvariantCulture, "PASS: {0} Tracks, {1} CDs, {2} digitale Items, {3} Track- und {4} CD-Genreprojektionen", Tracks.Count, Cds.Count, DigitalItemCount, ProjectedTrackGenreCount, ProjectedCdGenreCount);
        }

        private static string S(string[] p, int i)
        {
            if (p == null || i < 0 || i >= p.Length) return "";
            return p[i] ?? "";
        }

        private static int I(string[] p, int i)
        {
            int v;
            if (Int32.TryParse(S(p, i), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
            return 0;
        }

        private static double D(string[] p, int i)
        {
            double v;
            if (Double.TryParse(S(p, i), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            return 0;
        }

        private static bool B(string[] p, int i)
        {
            string x = S(p, i);
            return x == "1" || String.Equals(x, "true", StringComparison.OrdinalIgnoreCase) || String.Equals(x, "yes", StringComparison.OrdinalIgnoreCase);
        }

        private static string LowerJoin(params string[] values)
        {
            StringBuilder sb = new StringBuilder();
            int i;
            for (i = 0; i < values.Length; i++)
            {
                if (!String.IsNullOrEmpty(values[i]))
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(values[i]);
                }
            }
            return sb.ToString().ToLowerInvariant();
        }
    }
}
