using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace DJLibrary
{
    internal sealed class DiscogsSeedTrack
    {
        public string Title = "";
        public string Artist = "";
        public double DurationSeconds;
    }

    internal sealed class DiscogsSearchSeed
    {
        public string Album = "";
        public string AlbumArtist = "";
        public string ReleaseDate = "";
        public string Label = "";
        public string Catalog = "";
        public string Country = "";
        public List<DiscogsSeedTrack> Tracks = new List<DiscogsSeedTrack>();
    }

    internal sealed class DiscogsCandidate
    {
        public string ReleaseId = "";
        public int Score;
        public int MatchedTitles;
        public int MatchedDurations;
        public int TrackStart;
        public Dictionary<string, object> Release;
    }

    internal static class DiscogsIndependentMetadataSource
    {
        private const int MaxDetailedCandidates = 6;

        public static DiscogsSearchSeed CaptureSeed(CdSnapshot snapshot)
        {
            DiscogsSearchSeed seed = new DiscogsSearchSeed();
            if (snapshot == null) return seed;
            seed.Album = snapshot.Album ?? "";
            seed.AlbumArtist = snapshot.AlbumArtist ?? "";
            foreach (CdTrackCapture track in snapshot.Tracks)
            {
                seed.Tracks.Add(new DiscogsSeedTrack
                {
                    Title = String.IsNullOrWhiteSpace(track.RawTitle) ? (track.Title ?? "") : track.RawTitle,
                    Artist = track.Artist ?? "",
                    DurationSeconds = track.DurationSeconds
                });
            }
            return seed;
        }

        public static DiscogsSearchSeed CaptureSeed(CdSnapshot snapshot, CdMetadataFetchOptions options)
        {
            DiscogsSearchSeed seed = CaptureSeed(snapshot);
            if (options == null) return seed;
            if (!String.IsNullOrWhiteSpace(options.SearchAlbum)) seed.Album = options.SearchAlbum.Trim();
            if (!String.IsNullOrWhiteSpace(options.SearchAlbumArtist)) seed.AlbumArtist = options.SearchAlbumArtist.Trim();
            if (!String.IsNullOrWhiteSpace(options.SearchYear)) seed.ReleaseDate = options.SearchYear.Trim();
            if (!String.IsNullOrWhiteSpace(options.SearchLabel)) seed.Label = options.SearchLabel.Trim();
            if (!String.IsNullOrWhiteSpace(options.SearchCatalog)) seed.Catalog = options.SearchCatalog.Trim();
            if (!String.IsNullOrWhiteSpace(options.SearchCountry)) seed.Country = options.SearchCountry.Trim();
            return seed;
        }

        public static void Apply(CdSnapshot snapshot, DiscogsSearchSeed seed, string musicBrainzLinkedReleaseId, CdMetadataReport report)
        {
            string token = DiscogsCredentials.LoadToken();
            if (String.IsNullOrWhiteSpace(token))
            {
                if (!String.IsNullOrWhiteSpace(musicBrainzLinkedReleaseId))
                {
                    DiscogsMetadataSource.Apply(snapshot, musicBrainzLinkedReleaseId, report);
                    report.Status("Discogs Search", "independent search inactive · personal API token missing", "https://www.discogs.com/settings/developers");
                }
                else report.Status("Discogs", "Data provided by Discogs · independent search inactive · configure an API token under Tools → Discogs Access", "https://www.discogs.com/settings/developers");
                return;
            }

            if (!HasSearchBasis(seed))
            {
                if (!String.IsNullOrWhiteSpace(musicBrainzLinkedReleaseId)) DiscogsMetadataSource.Apply(snapshot, musicBrainzLinkedReleaseId, report);
                report.Status("Discogs", "Data provided by Discogs · insufficient search anchors for an independent search", "https://www.discogs.com/");
                return;
            }

            List<DiscogsCandidate> candidates = Search(seed, token);
            string reason;
            DiscogsCandidate chosen = SelectCandidate(seed, candidates, out reason);
            if (chosen == null)
            {
                if (!String.IsNullOrWhiteSpace(musicBrainzLinkedReleaseId)) DiscogsMetadataSource.Apply(snapshot, musicBrainzLinkedReleaseId, report);
                report.Status("Discogs", "Data provided by Discogs · independent search: " + reason, "https://www.discogs.com/");
                return;
            }

            double confidence = Math.Min(0.99, 0.82 + Math.Min(0.16, chosen.MatchedTitles * 0.02));
            string detail = "independently matched from search anchors / track order / durations · score=" + chosen.Score.ToString(CultureInfo.InvariantCulture) +
                " · Title=" + chosen.MatchedTitles.ToString(CultureInfo.InvariantCulture) + "/" + seed.Tracks.Count.ToString(CultureInfo.InvariantCulture);
            ApplyResolvedRelease(snapshot, chosen.ReleaseId, chosen.Release, report, detail, confidence);

            if (!String.IsNullOrWhiteSpace(musicBrainzLinkedReleaseId) && !String.Equals(musicBrainzLinkedReleaseId, chosen.ReleaseId, StringComparison.OrdinalIgnoreCase))
                report.Status("Discogs Cross-check", "independent Discogs match " + chosen.ReleaseId + " differs from MusicBrainz-linked release " + musicBrainzLinkedReleaseId + "", ReleasePage(chosen.ReleaseId));
        }

        private static bool HasSearchBasis(DiscogsSearchSeed seed)
        {
            if (seed == null) return false;
            if (!String.IsNullOrWhiteSpace(seed.Album)) return true;
            if (!String.IsNullOrWhiteSpace(seed.Catalog)) return true;
            if (!String.IsNullOrWhiteSpace(seed.Label)) return true;
            return seed.Tracks.Any(x => !String.IsNullOrWhiteSpace(x.Title));
        }

        internal static string BuildSearchUrl(DiscogsSearchSeed seed)
        {
            List<string> query = new List<string>();
            query.Add("type=release");
            query.Add("format=CD");
            query.Add("per_page=20");
            if (!String.IsNullOrWhiteSpace(seed.Album)) query.Add("release_title=" + Uri.EscapeDataString(seed.Album.Trim()));
            if (!String.IsNullOrWhiteSpace(seed.AlbumArtist) && !IsVarious(seed.AlbumArtist)) query.Add("artist=" + Uri.EscapeDataString(seed.AlbumArtist.Trim()));
            if (!String.IsNullOrWhiteSpace(seed.ReleaseDate)) query.Add("year=" + Uri.EscapeDataString(seed.ReleaseDate.Trim()));
            if (!String.IsNullOrWhiteSpace(seed.Label)) query.Add("label=" + Uri.EscapeDataString(seed.Label.Trim()));
            if (!String.IsNullOrWhiteSpace(seed.Catalog)) query.Add("catno=" + Uri.EscapeDataString(seed.Catalog.Trim()));
            if (!String.IsNullOrWhiteSpace(seed.Country)) query.Add("country=" + Uri.EscapeDataString(seed.Country.Trim()));
            if (String.IsNullOrWhiteSpace(seed.Album) && seed.Tracks.Count > 0 && !String.IsNullOrWhiteSpace(seed.Tracks[0].Title)) query.Add("track=" + Uri.EscapeDataString(seed.Tracks[0].Title.Trim()));
            return "https://api.discogs.com/database/search?" + String.Join("&", query.ToArray());
        }

        private static List<DiscogsCandidate> Search(DiscogsSearchSeed seed, string token)
        {
            Dictionary<string, object> root = MusicBrainzMetadataSource.GetJson(BuildSearchUrl(seed), false, token);
            List<Dictionary<string, object>> results = Json.List(root, "results");
            List<string> ids = new List<string>();
            foreach (Dictionary<string, object> result in results)
            {
                string type = Json.String(result, "type");
                if (!String.IsNullOrWhiteSpace(type) && !String.Equals(type, "release", StringComparison.OrdinalIgnoreCase)) continue;
                string id = Json.String(result, "id");
                long numeric;
                if (!Int64.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out numeric) || numeric <= 0) continue;
                if (!ids.Contains(id)) ids.Add(id);
                if (ids.Count >= MaxDetailedCandidates) break;
            }

            List<DiscogsCandidate> candidates = new List<DiscogsCandidate>();
            foreach (string id in ids)
            {
                try
                {
                    Dictionary<string, object> release = MusicBrainzMetadataSource.GetJson("https://api.discogs.com/releases/" + Uri.EscapeDataString(id), false, token);
                    if (release != null) candidates.Add(ScoreRelease(seed, id, release));
                }
                catch { }
            }
            return candidates;
        }

        internal static DiscogsCandidate ScoreRelease(DiscogsSearchSeed seed, string releaseId, Dictionary<string, object> release)
        {
            DiscogsCandidate candidate = new DiscogsCandidate { ReleaseId = releaseId ?? "", Release = release, TrackStart = 0 };
            if (seed == null || release == null) return candidate;
            string title = Json.String(release, "title");
            string artist = ReleaseArtists(release);
            if (!String.IsNullOrWhiteSpace(seed.Album) && Same(title, seed.Album)) candidate.Score += 35;
            if (!String.IsNullOrWhiteSpace(seed.AlbumArtist) && SameArtist(artist, seed.AlbumArtist)) candidate.Score += 20;
            if (HasCdFormat(release)) candidate.Score += 15;
            int releaseYear = Json.Int(release, "year");
            if (!String.IsNullOrWhiteSpace(seed.ReleaseDate) && releaseYear > 0 && Same(releaseYear.ToString(CultureInfo.InvariantCulture), seed.ReleaseDate)) candidate.Score += 8;
            string releaseCountry = Json.String(release, "country");
            if (!String.IsNullOrWhiteSpace(seed.Country) && Same(releaseCountry, seed.Country)) candidate.Score += 5;
            List<Dictionary<string, object>> releaseLabels = Json.List(release, "labels");
            if (releaseLabels.Any(x => !String.IsNullOrWhiteSpace(seed.Label) && Same(Json.String(x, "name"), seed.Label))) candidate.Score += 8;
            if (releaseLabels.Any(x => !String.IsNullOrWhiteSpace(seed.Catalog) && Same(Json.String(x, "catno"), seed.Catalog))) candidate.Score += 15;

            List<Dictionary<string, object>> tracks = AudioTracks(release);
            int wanted = seed.Tracks.Count;
            if (wanted == 0) return candidate;
            if (tracks.Count == wanted) candidate.Score += 20;
            int maxStart = Math.Max(0, tracks.Count - wanted);
            int bestWindow = Int32.MinValue;
            for (int start = 0; start <= maxStart; start++)
            {
                int score = 0, titles = 0, durations = 0;
                int count = Math.Min(wanted, tracks.Count - start);
                for (int i = 0; i < count; i++)
                {
                    string discogsTitle = Json.String(tracks[start + i], "title");
                    if (SameTrackTitle(discogsTitle, seed.Tracks[i].Title)) { score += 9; titles++; }
                    double duration = ParseDuration(Json.String(tracks[start + i], "duration"));
                    if (duration > 0 && seed.Tracks[i].DurationSeconds > 0 && Math.Abs(duration - seed.Tracks[i].DurationSeconds) <= 3.0) { score += 2; durations++; }
                }
                if (titles == wanted && wanted > 0) score += 20;
                if (score > bestWindow)
                {
                    bestWindow = score;
                    candidate.MatchedTitles = titles;
                    candidate.MatchedDurations = durations;
                    candidate.TrackStart = start;
                }
            }
            if (bestWindow > 0) candidate.Score += bestWindow;
            return candidate;
        }

        internal static DiscogsCandidate SelectCandidate(DiscogsSearchSeed seed, IList<DiscogsCandidate> candidates, out string reason)
        {
            reason = "no match";
            if (candidates == null || candidates.Count == 0) return null;
            List<DiscogsCandidate> sorted = candidates.OrderByDescending(x => x.Score).ThenBy(x => x.ReleaseId).ToList();
            DiscogsCandidate top = sorted[0];
            int trackCount = seed == null ? 0 : seed.Tracks.Count;
            int titledTracks = seed == null ? 0 : seed.Tracks.Count(x => !String.IsNullOrWhiteSpace(x.Title));
            int minimumTitles = titledTracks <= 1 ? titledTracks : Math.Max(2, (int)Math.Ceiling(titledTracks * 0.60));
            int minimumScore = titledTracks == 0 ? 80 : (trackCount <= 1 ? 65 : 90);
            if (top.Score < minimumScore || (titledTracks > 0 && top.MatchedTitles < minimumTitles))
            {
                reason = "no sufficiently strong match (best score=" + top.Score.ToString(CultureInfo.InvariantCulture) + ", titles=" + top.MatchedTitles.ToString(CultureInfo.InvariantCulture) + "/" + titledTracks.ToString(CultureInfo.InvariantCulture) + ")";
                return null;
            }
            if (sorted.Count > 1 && top.Score - sorted[1].Score < 10)
            {
                reason = "ambiguous · releases " + top.ReleaseId + " and " + sorted[1].ReleaseId + " differ by only " + (top.Score - sorted[1].Score).ToString(CultureInfo.InvariantCulture) + " points";
                return null;
            }
            reason = "unambiguous";
            return top;
        }

        internal static void ApplyResolvedRelease(CdSnapshot snapshot, string releaseId, Dictionary<string, object> release, CdMetadataReport report, string matchDetail, double confidence)
        {
            if (snapshot == null || release == null || String.IsNullOrWhiteSpace(releaseId)) return;
            string page = ReleasePage(releaseId);
            snapshot.DiscogsReleaseId = releaseId;
            string album = Json.String(release, "title");
            string albumArtist = ReleaseArtists(release);
            string country = Json.String(release, "country");
            int year = Json.Int(release, "year");
            List<string> genres = Json.StringList(release, "genres");
            List<string> styles = Json.StringList(release, "styles");
            List<Dictionary<string, object>> labels = Json.List(release, "labels");
            string label = labels.Count > 0 ? Json.String(labels[0], "name") : "";
            string catalog = labels.Count > 0 ? Json.String(labels[0], "catno") : "";
            string genre = styles.Count > 0 ? String.Join(" / ", styles.ToArray()) : String.Join(" / ", genres.ToArray());
            string evidenceDetail = "Data provided by Discogs · " + (matchDetail ?? "Release");

            if (String.IsNullOrWhiteSpace(snapshot.Album)) snapshot.Album = album;
            if (String.IsNullOrWhiteSpace(snapshot.AlbumArtist)) snapshot.AlbumArtist = albumArtist;
            if (String.IsNullOrWhiteSpace(snapshot.Country)) snapshot.Country = country;
            if (String.IsNullOrWhiteSpace(snapshot.ReleaseDate) && year > 0) snapshot.ReleaseDate = year.ToString(CultureInfo.InvariantCulture);
            if (String.IsNullOrWhiteSpace(snapshot.Label)) snapshot.Label = label;
            if (String.IsNullOrWhiteSpace(snapshot.Catalog)) snapshot.Catalog = catalog;
            if (String.IsNullOrWhiteSpace(snapshot.Genre)) snapshot.Genre = genre;

            report.Add("Album", album, "Discogs", confidence, evidenceDetail, page);
            report.Add("Album Artist", albumArtist, "Discogs", confidence, evidenceDetail, page);
            report.Add("Country", country, "Discogs", confidence, evidenceDetail, page);
            report.Add("Date", year > 0 ? year.ToString(CultureInfo.InvariantCulture) : "", "Discogs", confidence, evidenceDetail, page);
            report.Add("Label", label, "Discogs", confidence, evidenceDetail, page);
            report.Add("Catalog", catalog, "Discogs", confidence, evidenceDetail, page);
            report.Add("Genre/Style", genre, "Discogs", Math.Min(confidence, 0.88), evidenceDetail, page);
            ApplyTrackEvidence(snapshot, release, report, page, evidenceDetail, styles, genres);
            report.Status("Discogs", "Data provided by Discogs · " + matchDetail + " · Release " + releaseId, page);
        }

        private static void ApplyTrackEvidence(CdSnapshot snapshot, Dictionary<string, object> release, CdMetadataReport report, string page, string detail, IList<string> styles, IList<string> genres)
        {
            List<Dictionary<string, object>> releaseTracks = AudioTracks(release);
            if (snapshot.Tracks.Count == 0 || releaseTracks.Count == 0) return;
            DiscogsSearchSeed seed = CaptureSeed(snapshot);
            DiscogsCandidate mapping = ScoreRelease(seed, snapshot.DiscogsReleaseId, release);
            string singleGenre = styles != null && styles.Count == 1 ? styles[0] : ((styles == null || styles.Count == 0) && genres != null && genres.Count == 1 ? genres[0] : "");
            int count = Math.Min(snapshot.Tracks.Count, Math.Max(0, releaseTracks.Count - mapping.TrackStart));
            for (int i = 0; i < count; i++)
            {
                Dictionary<string, object> rt = releaseTracks[mapping.TrackStart + i];
                CdTrackCapture target = snapshot.Tracks[i];
                string fullTitle = Json.String(rt, "title");
                string artist = TrackArtists(rt);
                string releaseArtist = ReleaseArtists(release);
                if (String.IsNullOrWhiteSpace(artist) && !IsVarious(releaseArtist)) artist = releaseArtist;

                string effectiveTitle = fullTitle;
                string baseTitle, version;
                string raw = String.IsNullOrWhiteSpace(target.RawTitle) ? target.Title : target.RawTitle;
                bool split = TrySplitVersion(fullTitle, out baseTitle, out version);
                if (split) effectiveTitle = baseTitle;
                else version = "";

                report.Add("Track " + target.Position + " Title", effectiveTitle, "Discogs", 0.88, detail, page);
                report.Add("Track " + target.Position + " Artist", artist, "Discogs", 0.88, detail + " · track artist credit", page);
                if (String.IsNullOrWhiteSpace(target.Artist) && !String.IsNullOrWhiteSpace(artist)) target.Artist = artist;
                if (String.IsNullOrWhiteSpace(target.Title) && !String.IsNullOrWhiteSpace(effectiveTitle)) target.Title = effectiveTitle;

                if (split)
                {
                    if (String.IsNullOrWhiteSpace(target.Version)) target.Version = version;
                    if (String.IsNullOrWhiteSpace(target.MetadataSource)) target.MetadataSource = "Discogs";
                    target.MetadataDetail = detail;
                    target.MetadataConfidence = Math.Max(target.MetadataConfidence, 0.88);
                    report.Add("Track " + target.Position + " Mix/Version", version, "Discogs", 0.88,
                        detail + " · final clearly version-like parenthesis group", page);
                }
                if (!String.IsNullOrWhiteSpace(singleGenre))
                {
                    if (String.IsNullOrWhiteSpace(target.Genre)) target.Genre = singleGenre;
                    report.Add("Track " + target.Position + " Genre", singleGenre, "Discogs", 0.72,
                        detail + " · single unambiguous release style projected to track", page);
                }
            }
        }

        internal static bool TrySplitVersion(string value, out string title, out string version)
        {
            title = value == null ? "" : value.Trim();
            version = "";
            List<TitleSplitCandidate> candidates = CdMetadataPipeline.SplitTitle(value);
            for (int i = 1; i < candidates.Count; i++)
            {
                if (!LooksLikeVersion(candidates[i].Version)) continue;
                title = candidates[i].Title;
                version = candidates[i].Version;
                return true;
            }
            return false;
        }

        private static bool LooksLikeVersion(string value)
        {
            string n = (value ?? "").ToLowerInvariant();
            string[] words = { "mix", "remix", "edit", "version", "dub", "radio", "club", "vocal", "instrumental", "extended", "original", "rework", "remaster", "bootleg" };
            return value != null && value.Length <= 120 && words.Any(x => n.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool HasCdFormat(Dictionary<string, object> release)
        {
            foreach (Dictionary<string, object> format in Json.List(release, "formats"))
                if (Json.String(format, "name").IndexOf("CD", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static List<Dictionary<string, object>> AudioTracks(Dictionary<string, object> release)
        {
            return Json.List(release, "tracklist").Where(x => !String.Equals(Json.String(x, "type_"), "heading", StringComparison.OrdinalIgnoreCase) && !String.IsNullOrWhiteSpace(Json.String(x, "title"))).ToList();
        }

        private static string ReleaseArtists(Dictionary<string, object> release) { return JoinArtists(Json.List(release, "artists")); }
        private static string TrackArtists(Dictionary<string, object> track) { return JoinArtists(Json.List(track, "artists")); }

        private static string JoinArtists(IList<Dictionary<string, object>> artists)
        {
            List<string> names = new List<string>();
            if (artists != null)
            {
                foreach (Dictionary<string, object> artist in artists)
                {
                    string name = Json.String(artist, "anv");
                    if (String.IsNullOrWhiteSpace(name)) name = Json.String(artist, "name");
                    if (!String.IsNullOrWhiteSpace(name)) names.Add(RemoveDiscogsDisambiguator(name));
                }
            }
            return String.Join(" & ", names.ToArray());
        }

        private static string RemoveDiscogsDisambiguator(string value)
        {
            string s = (value ?? "").Trim();
            int end = s.LastIndexOf(')');
            int start = s.LastIndexOf('(');
            if (start > 0 && end == s.Length - 1)
            {
                int number;
                if (Int32.TryParse(s.Substring(start + 1, end - start - 1), out number)) return s.Substring(0, start).TrimEnd();
            }
            return s;
        }

        private static bool SameTrackTitle(string a, string b)
        {
            string na = CdMetadataPipeline.Norm(a), nb = CdMetadataPipeline.Norm(b);
            if (na.Length == 0 || nb.Length == 0) return false;
            if (na == nb) return true;
            string title, version;
            if (TrySplitVersion(a, out title, out version) && CdMetadataPipeline.Norm(title) == nb) return true;
            if (TrySplitVersion(b, out title, out version) && CdMetadataPipeline.Norm(title) == na) return true;
            return false;
        }

        private static bool Same(string a, string b) { return CdMetadataPipeline.Norm(a) == CdMetadataPipeline.Norm(b) && CdMetadataPipeline.Norm(a).Length > 0; }
        private static bool SameArtist(string a, string b) { return IsVarious(a) && IsVarious(b) ? true : Same(a, b); }
        private static bool IsVarious(string value) { string n = CdMetadataPipeline.Norm(value); return n == "various" || n == "variousartists"; }

        private static double ParseDuration(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return 0;
            string[] parts = value.Trim().Split(':');
            int a, b, c;
            if (parts.Length == 2 && Int32.TryParse(parts[0], out a) && Int32.TryParse(parts[1], out b)) return a * 60 + b;
            if (parts.Length == 3 && Int32.TryParse(parts[0], out a) && Int32.TryParse(parts[1], out b) && Int32.TryParse(parts[2], out c)) return a * 3600 + b * 60 + c;
            return 0;
        }

        private static string ReleasePage(string releaseId) { return "https://www.discogs.com/release/" + Uri.EscapeDataString(releaseId ?? ""); }

        internal static string RunSelfTest()
        {
            DiscogsSearchSeed seed = new DiscogsSearchSeed { Album = "Example Album", AlbumArtist = "Various Artists" };
            seed.Tracks.Add(new DiscogsSeedTrack { Title = "Title (Title Extension) (Extended Mix)", Artist = "Artist One", DurationSeconds = 420 });
            seed.Tracks.Add(new DiscogsSeedTrack { Title = "Second Track", Artist = "Artist Two", DurationSeconds = 360 });
            Dictionary<string, object> exact = new Dictionary<string, object>();
            exact["title"] = "Example Album";
            exact["artists"] = new object[] { new Dictionary<string, object> { { "name", "Various" } } };
            exact["formats"] = new object[] { new Dictionary<string, object> { { "name", "CD" } } };
            exact["year"] = 1999;
            exact["country"] = "UK";
            exact["genres"] = new object[] { "Electronic" };
            exact["styles"] = new object[] { "House" };
            exact["labels"] = new object[] { new Dictionary<string, object> { { "name", "Fixture Label" }, { "catno", "FIX-001" } } };
            exact["tracklist"] = new object[]
            {
                new Dictionary<string, object>
                {
                    { "position", "1" }, { "title", "Title (Title Extension) (Extended Mix)" }, { "duration", "7:00" },
                    { "artists", new object[] { new Dictionary<string, object> { { "name", "Artist One" } } } }
                },
                new Dictionary<string, object> { { "position", "2" }, { "title", "Second Track" }, { "duration", "6:00" } }
            };
            Dictionary<string, object> wrong = new Dictionary<string, object>();
            wrong["title"] = "Another Album";
            wrong["artists"] = new object[] { new Dictionary<string, object> { { "name", "Other Artist" } } };
            wrong["formats"] = new object[] { new Dictionary<string, object> { { "name", "CD" } } };
            wrong["tracklist"] = new object[]
            {
                new Dictionary<string, object> { { "position", "1" }, { "title", "Wrong Track" }, { "duration", "2:00" } },
                new Dictionary<string, object> { { "position", "2" }, { "title", "Wrong Again" }, { "duration", "3:00" } }
            };
            DiscogsCandidate good = ScoreRelease(seed, "100", exact);
            DiscogsCandidate bad = ScoreRelease(seed, "200", wrong);
            if (good.Score <= bad.Score || good.MatchedTitles != 2 || good.MatchedDurations != 2) throw new InvalidDataException("Discogs independent scoring regression.");
            string reason;
            if (SelectCandidate(seed, new List<DiscogsCandidate> { good, bad }, out reason) != good) throw new InvalidDataException("Discogs independent unique-candidate selection regression: " + reason);
            DiscogsCandidate tie = ScoreRelease(seed, "101", exact);
            if (SelectCandidate(seed, new List<DiscogsCandidate> { good, tie }, out reason) != null || reason.IndexOf("ambiguous", StringComparison.OrdinalIgnoreCase) < 0) throw new InvalidDataException("Discogs ambiguity must fail closed.");
            string baseTitle, version;
            if (!TrySplitVersion("Title (Title Extension) (Extended Mix)", out baseTitle, out version) || baseTitle != "Title (Title Extension)" || version != "Extended Mix") throw new InvalidDataException("Discogs multi-parenthesis version split regression.");
            string url = BuildSearchUrl(seed);
            if (url.IndexOf("database/search", StringComparison.Ordinal) < 0 || url.IndexOf("release_title=Example%20Album", StringComparison.Ordinal) < 0 || url.IndexOf("artist=", StringComparison.Ordinal) >= 0) throw new InvalidDataException("Discogs independent search URL regression.");

            DiscogsSearchSeed manual = new DiscogsSearchSeed { Album = "Manual Album", AlbumArtist = "Manual Artist", Catalog = "CAT-17", ReleaseDate = "2001" };
            manual.Tracks.Add(new DiscogsSeedTrack { Title = "", Artist = "", DurationSeconds = 300 });
            manual.Tracks.Add(new DiscogsSeedTrack { Title = "", Artist = "", DurationSeconds = 360 });
            Dictionary<string, object> manualRelease = new Dictionary<string, object>();
            manualRelease["title"] = "Manual Album";
            manualRelease["artists"] = new object[] { new Dictionary<string, object> { { "name", "Manual Artist" } } };
            manualRelease["year"] = 2001;
            manualRelease["formats"] = new object[] { new Dictionary<string, object> { { "name", "CD" } } };
            manualRelease["labels"] = new object[] { new Dictionary<string, object> { { "name", "Some Label" }, { "catno", "CAT-17" } } };
            manualRelease["tracklist"] = new object[]
            {
                new Dictionary<string, object> { { "position", "1" }, { "title", "One" }, { "duration", "5:00" } },
                new Dictionary<string, object> { { "position", "2" }, { "title", "Two" }, { "duration", "6:00" } }
            };
            DiscogsCandidate manualCandidate = ScoreRelease(manual, "300", manualRelease);
            if (manualCandidate.MatchedTitles != 0 || manualCandidate.MatchedDurations != 2 || SelectCandidate(manual, new List<DiscogsCandidate> { manualCandidate }, out reason) != manualCandidate)
                throw new InvalidDataException("Discogs manual Artist/Album search-anchor regression.");
            string manualUrl = BuildSearchUrl(manual);
            if (manualUrl.IndexOf("release_title=Manual%20Album", StringComparison.Ordinal) < 0 || manualUrl.IndexOf("artist=Manual%20Artist", StringComparison.Ordinal) < 0 || manualUrl.IndexOf("catno=CAT-17", StringComparison.Ordinal) < 0 || manualUrl.IndexOf("year=2001", StringComparison.Ordinal) < 0)
                throw new InvalidDataException("Discogs manual search URL regression.");

            CdSnapshot evidenceSnapshot = new CdSnapshot();
            evidenceSnapshot.Album = "Example Album";
            evidenceSnapshot.AlbumArtist = "Various Artists";
            evidenceSnapshot.Tracks.Add(new CdTrackCapture { Position = 1, RawTitle = "", DurationSeconds = 420 });
            evidenceSnapshot.Tracks.Add(new CdTrackCapture { Position = 2, RawTitle = "Second Track", DurationSeconds = 360 });
            CdMetadataReport evidenceReport = new CdMetadataReport();
            ApplyResolvedRelease(evidenceSnapshot, "100", exact, evidenceReport, "fixture", 0.90);
            string[] requiredRelease = { "Album", "Album Artist", "Date", "Genre/Style", "Label", "Catalog", "Country" };
            foreach (string field in requiredRelease)
                if (!evidenceReport.Evidence.Any(x => x.Source == "Discogs" && x.Field == field && !String.IsNullOrWhiteSpace(x.Value)))
                    throw new InvalidDataException("Discogs release field-completeness regression: missing " + field + ".");
            string[] requiredTrack = { "Track 1 Artist", "Track 1 Title", "Track 1 Mix/Version", "Track 1 Genre" };
            foreach (string field in requiredTrack)
                if (!evidenceReport.Evidence.Any(x => x.Source == "Discogs" && x.Field == field && !String.IsNullOrWhiteSpace(x.Value)))
                    throw new InvalidDataException("Discogs track field-completeness regression: missing " + field + ".");
            MetadataEvidence discogsTitle = evidenceReport.Evidence.FirstOrDefault(x => x.Source == "Discogs" && x.Field == "Track 1 Title");
            MetadataEvidence discogsVersion = evidenceReport.Evidence.FirstOrDefault(x => x.Source == "Discogs" && x.Field == "Track 1 Mix/Version");
            if (discogsTitle == null || discogsTitle.Value != "Title (Title Extension)" || discogsVersion == null || discogsVersion.Value != "Extended Mix")
                throw new InvalidDataException("Discogs coupled Title/Mix evidence is inconsistent.");
            if (evidenceReport.Evidence.Any(x => x.Source == "Discogs" && x.Field == "Track 2 Artist" && CdMetadataPipeline.Norm(x.Value) == "various"))
                throw new InvalidDataException("Discogs projected a Various release artist onto an individual track without track-level artist evidence.");

            return "independent Discogs search + editable search anchors + no-title physical scoring + fail-closed candidate scoring + secure token gate + Discogs release/track field completeness + coherent title/mix evidence";
        }
    }
}
