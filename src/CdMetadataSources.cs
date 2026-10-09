using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace DJLibrary
{
    public sealed class MetadataEvidence
    {
        public string Field { get; set; }
        public string Value { get; set; }
        public string Source { get; set; }
        public string Detail { get; set; }
        public string Url { get; set; }
        public double Confidence { get; set; }

        public MetadataEvidence()
        {
            Field = Value = Source = Detail = Url = "";
        }
    }

    public sealed class MetadataSourceStatus
    {
        public string Source { get; set; }
        public string Text { get; set; }
        public string Url { get; set; }

        public MetadataSourceStatus()
        {
            Source = Text = Url = "";
        }
    }

    public sealed class CdMetadataReport
    {
        public List<MetadataEvidence> Evidence { get; private set; }
        public List<MetadataSourceStatus> Sources { get; private set; }

        public CdMetadataReport()
        {
            Evidence = new List<MetadataEvidence>();
            Sources = new List<MetadataSourceStatus>();
        }

        public void Status(string source, string text, string url)
        {
            Sources.Add(new MetadataSourceStatus { Source = source ?? "", Text = text ?? "", Url = url ?? "" });
        }

        public void Add(string field, string value, string source, double confidence, string detail, string url)
        {
            if (String.IsNullOrWhiteSpace(value)) return;
            Evidence.Add(new MetadataEvidence
            {
                Field = field ?? "",
                Value = value.Trim(),
                Source = source ?? "",
                Confidence = confidence,
                Detail = detail ?? "",
                Url = url ?? ""
            });
        }
    }

    public sealed class CatalogTocMatch
    {
        public long ReleaseId { get; set; }
        public long DiscId { get; set; }
        public int DiscNumber { get; set; }
        public string AlbumArtist { get; set; }
        public string Album { get; set; }

        public CatalogTocMatch()
        {
            AlbumArtist = Album = "";
        }

        public string DisplayText
        {
            get
            {
                string release = (String.IsNullOrWhiteSpace(AlbumArtist) ? "—" : AlbumArtist) + " — " +
                                 (String.IsNullOrWhiteSpace(Album) ? "—" : Album);
                return release + " · Disc " + DiscNumber.ToString(CultureInfo.CurrentCulture);
            }
        }
    }

    internal sealed class TitleSplitCandidate
    {
        public string Title;
        public string Version;
    }

    internal sealed class DigitalTrackMatch
    {
        public DigitalItem Item;
        public int Score;
        public bool Structured;
        public string BaseTitle;
        public string Version;
    }

    public static partial class CdMetadataPipeline
    {
        private static readonly object BridgeLock = new object();
        private static string _cachedBridgeDirectory = "";
        private static long _cachedBridgeGeneration;
        private static List<DigitalItem> _cachedBridgeItems;

        public static CdMetadataReport Enrich(CdSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");
            CdMetadataReport report = new CdMetadataReport();
            foreach (CdTrackCapture track in snapshot.Tracks)
                if (String.IsNullOrWhiteSpace(track.RawTitle)) track.RawTitle = track.Title ?? "";

            report.Status("CD-TEXT", CdTextSummary(snapshot), "");
            ApplyFoobar(snapshot, LoadBridgeItems(report), report);

            // Capture the independent Discogs search seed before MusicBrainz can fill any
            // previously empty release fields. This guarantees that Discogs discovery is
            // based only on the physical disc/CD-TEXT/TOC-derived track data.
            DiscogsSearchSeed discogsSeed = DiscogsIndependentMetadataSource.CaptureSeed(snapshot);

            MusicBrainzLookup lookup = null;
            try { lookup = MusicBrainzMetadataSource.Apply(snapshot, report); }
            catch (Exception ex) { report.Status("MusicBrainz", "online matching failed: " + CleanError(ex.Message), "https://musicbrainz.org/"); }

            try
            {
                DiscogsIndependentMetadataSource.Apply(snapshot, discogsSeed,
                    lookup == null ? "" : lookup.DiscogsReleaseId, report);
            }
            catch (Exception ex)
            {
                report.Status("Discogs", "Data provided by Discogs · independent matching failed: " + CleanError(ex.Message), "https://www.discogs.com/");
            }

            snapshot.MetadataEvidence = report.Evidence;
            snapshot.MetadataSources = report.Sources;
            return report;
        }

        private static string CdTextSummary(CdSnapshot snapshot)
        {
            if (snapshot.CdTextStatus == "drive_present")
                return "present · " + (String.IsNullOrWhiteSpace(snapshot.CdTextSource) ? "read directly from disc" : snapshot.CdTextSource);
            if (snapshot.CdTextStatus == "drive_absent") return "checked: not present";
            if (snapshot.CdTextStatus == "drive_read_error") return "read error";
            return "cannot be checked reliably";
        }

        private static List<DigitalItem> LoadBridgeItems(CdMetadataReport report)
        {
            string directory = "";
            try
            {
                AppSettings settings = SettingsManager.Load();
                directory = settings == null ? "" : settings.BridgeDirectory;
            }
            catch { }
            if (String.IsNullOrWhiteSpace(directory) || !BridgeSnapshotReader.HasStateFile(directory))
            {
                if (BridgeSnapshotReader.HasStateFile(BridgeSnapshotReader.StandardProfileDirectory)) directory = BridgeSnapshotReader.StandardProfileDirectory;
                else if (BridgeSnapshotReader.HasStateFile(BridgeSnapshotReader.LegacyDirectory)) directory = BridgeSnapshotReader.LegacyDirectory;
            }
            if (String.IsNullOrWhiteSpace(directory) || !BridgeSnapshotReader.HasStateFile(directory))
            {
                report.Status("foobar", "no complete Bridge source available", "");
                return new List<DigitalItem>();
            }

            try
            {
                BridgeSnapshotStatus status = BridgeSnapshotReader.Inspect(directory, true);
                if (!status.Present || !status.Complete || !status.Compatible || !String.IsNullOrEmpty(status.Error))
                {
                    report.Status("foobar", "Bridge is not authoritative: " + (status.Error ?? "incomplete"), "");
                    return new List<DigitalItem>();
                }
                lock (BridgeLock)
                {
                    if (_cachedBridgeItems == null || _cachedBridgeGeneration != status.Generation ||
                        !String.Equals(_cachedBridgeDirectory, directory, StringComparison.OrdinalIgnoreCase))
                    {
                        _cachedBridgeItems = BridgeSnapshotReader.LoadItems(directory, status);
                        _cachedBridgeGeneration = status.Generation;
                        _cachedBridgeDirectory = directory;
                    }
                    report.Status("foobar", String.Format(CultureInfo.CurrentCulture,
                        "{0:N0} Items · {1} · Gen. {2:N0}", _cachedBridgeItems.Count, status.SourceDisplayName, status.Generation), "");
                    return new List<DigitalItem>(_cachedBridgeItems);
                }
            }
            catch (Exception ex)
            {
                report.Status("foobar", "Bridge matching failed: " + CleanError(ex.Message), "");
                return new List<DigitalItem>();
            }
        }

        internal static void ApplyFoobar(CdSnapshot snapshot, IList<DigitalItem> items, CdMetadataReport report)
        {
            if (items == null || items.Count == 0) return;
            int enriched = 0;
            foreach (CdTrackCapture track in snapshot.Tracks)
            {
                string raw = String.IsNullOrWhiteSpace(track.RawTitle) ? track.Title : track.RawTitle;
                List<DigitalTrackMatch> matches = new List<DigitalTrackMatch>();
                foreach (DigitalItem item in items)
                {
                    if (!Same(item.Artist, track.Artist)) continue;
                    bool durationKnown = track.DurationSeconds > 0 && item.DurationSeconds > 0;
                    double durationDiff = durationKnown ? Math.Abs(track.DurationSeconds - item.DurationSeconds) : 0;
                    if (durationKnown && durationDiff > 3.0) continue;

                    int score = durationKnown ? (durationDiff <= 1.5 ? 20 : 12) : 4;
                    string baseTitle = !String.IsNullOrWhiteSpace(item.OriginalTitle) ? item.OriginalTitle : item.Title;
                    string version = item.RemixedBy ?? "";
                    bool structured = false;
                    if (Same(item.Title, raw)) score += 60;
                    if (Same(baseTitle, raw)) score += 35;
                    if (!String.IsNullOrWhiteSpace(version))
                    {
                        foreach (TitleSplitCandidate split in SplitTitle(raw))
                        {
                            if (Same(split.Title, baseTitle) && Same(split.Version, version))
                            {
                                score += 75;
                                structured = true;
                                break;
                            }
                        }
                    }
                    if (score >= 60)
                        matches.Add(new DigitalTrackMatch { Item = item, Score = score, Structured = structured, BaseTitle = baseTitle ?? "", Version = version });
                }

                if (matches.Count == 0) continue;
                matches.Sort(delegate(DigitalTrackMatch a, DigitalTrackMatch b) { return b.Score.CompareTo(a.Score); });
                DigitalTrackMatch best = matches[0];
                List<DigitalTrackMatch> tied = matches.Where(x => x.Score == best.Score).ToList();
                if (best.Score < 80) continue;

                bool sameIdentity = tied.All(x => Same(x.BaseTitle, best.BaseTitle) && Same(x.Version, best.Version));
                if (!sameIdentity) continue;

                report.Add("Track " + track.Position + " Artist", best.Item.Artist, "foobar", 1.0,
                    "strong digital match · source item artist", "");
                report.Add("Track " + track.Position + " Title", best.BaseTitle, "foobar", 1.0,
                    "strong digital match · source item title", "");

                if (!String.IsNullOrWhiteSpace(best.Version) && (best.Structured || Same(best.Item.Title, raw)))
                {
                    track.Title = best.BaseTitle;
                    track.Version = best.Version;
                    report.Add("Track " + track.Position + " Mix/Version", track.Version, "foobar", 1.0,
                        "Artist + structured Title/Mix + duration", "");
                }

                List<string> genres = tied.Select(x => x.Item.Genre == null ? "" : x.Item.Genre.Trim())
                    .Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (genres.Count == 1)
                {
                    track.Genre = genres[0];
                    report.Add("Track " + track.Position + " Genre", track.Genre, "foobar", 1.0,
                        "unique strong digital match", "");
                }
                track.MetadataSource = "foobar";
                track.MetadataConfidence = 1.0;
                track.MetadataDetail = "strong · score=" + best.Score.ToString(CultureInfo.InvariantCulture);
                enriched++;
            }
            if (enriched > 0)
                report.Status("foobar", report.Sources.Last(x => x.Source == "foobar").Text +
                    String.Format(CultureInfo.CurrentCulture, " · {0:N0} CD tracks structured", enriched), "");
        }

        internal static List<TitleSplitCandidate> SplitTitle(string value)
        {
            List<TitleSplitCandidate> result = new List<TitleSplitCandidate>();
            string current = (value ?? "").Trim();
            result.Add(new TitleSplitCandidate { Title = current, Version = "" });
            int end = current.Length - 1;
            while (end >= 0 && current[end] == ')')
            {
                int depth = 0;
                int start = -1;
                for (int i = end; i >= 0; i--)
                {
                    if (current[i] == ')') depth++;
                    else if (current[i] == '(')
                    {
                        depth--;
                        if (depth == 0) { start = i; break; }
                    }
                }
                if (start <= 0) break;
                string version = current.Substring(start + 1, end - start - 1).Trim();
                string title = current.Substring(0, start).TrimEnd();
                if (title.Length == 0 || version.Length == 0) break;
                result.Add(new TitleSplitCandidate { Title = title, Version = version });
                current = title;
                end = current.Length - 1;
            }
            return result;
        }

        internal static string MusicBrainzDiscId(string toc)
        {
            int[] offsets = ParseToc(toc);
            int tracks = offsets.Length - 1;
            if (tracks < 1 || tracks > 99) throw new InvalidDataException("MusicBrainz Disc ID: invalid track count.");
            StringBuilder source = new StringBuilder();
            source.Append(1.ToString("X2", CultureInfo.InvariantCulture));
            source.Append(tracks.ToString("X2", CultureInfo.InvariantCulture));
            source.Append(offsets[offsets.Length - 1].ToString("X8", CultureInfo.InvariantCulture));
            for (int i = 0; i < 99; i++)
            {
                int value = i < tracks ? offsets[i] : 0;
                source.Append(value.ToString("X8", CultureInfo.InvariantCulture));
            }
            byte[] digest;
            using (SHA1 sha = SHA1.Create()) digest = sha.ComputeHash(Encoding.ASCII.GetBytes(source.ToString()));
            return Convert.ToBase64String(digest).Replace('+', '.').Replace('/', '_').Replace('=', '-');
        }

        internal static string MusicBrainzToc(string toc)
        {
            int[] offsets = ParseToc(toc);
            int tracks = offsets.Length - 1;
            return "1+" + tracks.ToString(CultureInfo.InvariantCulture) + "+" +
                   offsets[offsets.Length - 1].ToString(CultureInfo.InvariantCulture) + "+" +
                   String.Join("+", offsets.Take(tracks).Select(x => x.ToString(CultureInfo.InvariantCulture)).ToArray());
        }

        private static int[] ParseToc(string toc)
        {
            if (String.IsNullOrWhiteSpace(toc)) throw new InvalidDataException("TOC is missing.");
            string[] parts = toc.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts.Length > 100) throw new InvalidDataException("TOC has invalid length.");
            int[] values = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!Int32.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i]) || values[i] < 0)
                    throw new InvalidDataException("TOC contains invalid offsets.");
                if (i > 0 && values[i] <= values[i - 1]) throw new InvalidDataException("TOC offsets are not strictly increasing.");
            }
            return values;
        }

        private static bool Same(string a, string b) { return Norm(a) == Norm(b) && Norm(a).Length > 0; }
        internal static string Norm(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return "";
            StringBuilder b = new StringBuilder();
            foreach (char ch in value.ToLowerInvariant()) if (Char.IsLetterOrDigit(ch)) b.Append(ch);
            return b.ToString();
        }

        internal static string RunSelfTest()
        {
            string exampleToc = "150 15363 32314 46592 63414 80489 95462";
            string discId = MusicBrainzDiscId(exampleToc);
            if (discId != "49HHV7Eb8UKF3aQiNmu1GR8vKTY-")
                throw new InvalidDataException("MusicBrainz Disc-ID Berechnung failed: " + discId);

            CdSnapshot snapshot = new CdSnapshot();
            snapshot.Toc = "150 31650";
            snapshot.Tracks.Add(new CdTrackCapture
            {
                Position = 1, Artist = "Example Artist", Title = "Title (Title Extension) (Extended Mix)",
                RawTitle = "Title (Title Extension) (Extended Mix)", DurationSeconds = 420
            });
            DigitalItem digital = new DigitalItem
            {
                ItemId = 1, Artist = "Example Artist", Title = "Title (Title Extension)",
                OriginalTitle = "Title (Title Extension)", RemixedBy = "Extended Mix", Genre = "Trance", DurationSeconds = 421
            };
            CdMetadataReport report = new CdMetadataReport();
            report.Status("foobar", "self-test", "");
            ApplyFoobar(snapshot, new List<DigitalItem> { digital }, report);
            CdTrackCapture t = snapshot.Tracks[0];
            if (t.Title != "Title (Title Extension)" || t.Version != "Extended Mix" || t.Genre != "Trance" ||
                t.RawTitle != "Title (Title Extension) (Extended Mix)")
                throw new InvalidDataException("Evidenzbasiertes Mehrfachklammer-/Mix-Splitting failed.");
            string[] foobarFields = { "Track 1 Artist", "Track 1 Title", "Track 1 Mix/Version", "Track 1 Genre" };
            foreach (string field in foobarFields)
                if (!report.Evidence.Any(x => x.Source == "foobar" && x.Field == field && !String.IsNullOrWhiteSpace(x.Value)))
                    throw new InvalidDataException("foobar field-completeness regression: missing " + field + ".");
            string musicBrainzTest = MusicBrainzMetadataSource.ValidateEvidenceCompletenessContract();
            string discogsTest = DiscogsIndependentMetadataSource.RunSelfTest();
            return "metadata-source pipeline + MusicBrainz Disc ID + provider field-completeness contract + evidence-based multi-parenthesis mix split + " + musicBrainzTest + " + " + discogsTest;
        }

        private static string CleanError(string value)
        {
            string s = (value ?? "Error").Replace('\r', ' ').Replace('\n', ' ').Trim();
            return s.Length > 180 ? s.Substring(0, 180) + "…" : s;
        }
    }

    internal sealed class MusicBrainzLookup
    {
        public string ReleaseId = "";
        public string DiscogsReleaseId = "";
        public string ReleaseUrl = "";
    }

    internal static class MusicBrainzMetadataSource
    {
        private static readonly object RateLock = new object();
        private static DateTime _lastRequestUtc = DateTime.MinValue;

        public static MusicBrainzLookup Apply(CdSnapshot snapshot, CdMetadataReport report)
        {
            if (String.IsNullOrWhiteSpace(snapshot.Toc) || snapshot.Tracks.Count == 0) return null;
            string discId = CdMetadataPipeline.MusicBrainzDiscId(snapshot.Toc);
            string toc = CdMetadataPipeline.MusicBrainzToc(snapshot.Toc);
            snapshot.MusicBrainzDiscId = discId;
            string endpoint = "https://musicbrainz.org/ws/2/discid/" + Uri.EscapeDataString(discId) +
                "?fmt=json&cdstubs=no&inc=artist-credits+recordings+release-groups+labels+url-rels+genres&toc=" + toc;
            Dictionary<string, object> root = GetJson(endpoint, true, null);
            List<Dictionary<string, object>> releases = Json.List(root, "releases");
            if (releases.Count == 0)
            {
                report.Status("MusicBrainz", "no Disc-ID/TOC match", "https://musicbrainz.org/cdtoc/attach?id=" + discId);
                return null;
            }

            List<Tuple<int, Dictionary<string, object>>> scored = new List<Tuple<int, Dictionary<string, object>>>();
            foreach (Dictionary<string, object> release in releases)
                scored.Add(Tuple.Create(ScoreRelease(snapshot, release), release));
            scored.Sort(delegate(Tuple<int, Dictionary<string, object>> a, Tuple<int, Dictionary<string, object>> b) { return b.Item1.CompareTo(a.Item1); });
            if (scored.Count > 1 && scored[0].Item1 - scored[1].Item1 < 8)
            {
                report.Status("MusicBrainz", "ambiguous Disc-ID/TOC match · no release fields selected automatically",
                    "https://musicbrainz.org/cdtoc/attach?id=" + discId);
                return null;
            }

            Dictionary<string, object> chosen = scored[0].Item2;
            string releaseId = Json.String(chosen, "id");
            string releaseUrl = String.IsNullOrWhiteSpace(releaseId) ? "https://musicbrainz.org/" : "https://musicbrainz.org/release/" + releaseId;
            snapshot.MusicBrainzReleaseId = releaseId;
            FillRelease(snapshot, chosen, report, releaseUrl);
            FillTracks(snapshot, chosen, report, releaseUrl);
            string discogs = FindDiscogsReleaseId(chosen);
            if (!String.IsNullOrWhiteSpace(discogs)) snapshot.DiscogsReleaseId = discogs;
            report.Status("MusicBrainz", "unique Disc-ID/TOC match · Release " + releaseId, releaseUrl);
            return new MusicBrainzLookup { ReleaseId = releaseId, DiscogsReleaseId = discogs, ReleaseUrl = releaseUrl };
        }

        private static int ScoreRelease(CdSnapshot snapshot, Dictionary<string, object> release)
        {
            int score = 0;
            string title = Json.String(release, "title");
            if (CdMetadataPipeline.Norm(title) == CdMetadataPipeline.Norm(snapshot.Album) && !String.IsNullOrWhiteSpace(snapshot.Album)) score += 30;
            string artist = Json.ArtistCredit(release);
            if (CdMetadataPipeline.Norm(artist) == CdMetadataPipeline.Norm(snapshot.AlbumArtist) && !String.IsNullOrWhiteSpace(snapshot.AlbumArtist)) score += 20;
            Dictionary<string, object> medium = BestMedium(release, snapshot.Tracks.Count);
            if (medium != null)
            {
                int count = Json.Int(medium, "track-count");
                if (count == snapshot.Tracks.Count) score += 30;
                List<Dictionary<string, object>> tracks = Json.List(medium, "tracks");
                int n = Math.Min(tracks.Count, snapshot.Tracks.Count);
                for (int i = 0; i < n; i++)
                {
                    string mt = Json.String(tracks[i], "title");
                    if (String.IsNullOrWhiteSpace(mt)) mt = Json.String(Json.Object(tracks[i], "recording"), "title");
                    if (CdMetadataPipeline.Norm(mt) == CdMetadataPipeline.Norm(snapshot.Tracks[i].RawTitle)) score += 3;
                    int length = Json.Int(tracks[i], "length");
                    if (length > 0 && snapshot.Tracks[i].DurationSeconds > 0 && Math.Abs(length / 1000.0 - snapshot.Tracks[i].DurationSeconds) <= 3.0) score += 1;
                }
            }
            return score;
        }

        private static void FillRelease(CdSnapshot snapshot, Dictionary<string, object> release, CdMetadataReport report, string url)
        {
            string title = Json.String(release, "title");
            string artist = Json.ArtistCredit(release);
            string date = Json.String(release, "date");
            string country = Json.String(release, "country");
            List<Dictionary<string, object>> labels = Json.List(release, "label-info");
            string label = "", catalog = "";
            if (labels.Count > 0)
            {
                catalog = Json.String(labels[0], "catalog-number");
                label = Json.String(Json.Object(labels[0], "label"), "name");
            }
            Dictionary<string, object> group = Json.Object(release, "release-group");
            List<Dictionary<string, object>> genres = Json.List(group, "genres");
            string genre = genres.Count == 0 ? "" : String.Join(" / ", genres.Select(x => Json.String(x, "name")).Where(x => x.Length > 0).ToArray());

            if (String.IsNullOrWhiteSpace(snapshot.Album)) snapshot.Album = title;
            if (String.IsNullOrWhiteSpace(snapshot.AlbumArtist)) snapshot.AlbumArtist = artist;
            if (String.IsNullOrWhiteSpace(snapshot.ReleaseDate)) snapshot.ReleaseDate = date;
            if (String.IsNullOrWhiteSpace(snapshot.Country)) snapshot.Country = country;
            if (String.IsNullOrWhiteSpace(snapshot.Label)) snapshot.Label = label;
            if (String.IsNullOrWhiteSpace(snapshot.Catalog)) snapshot.Catalog = catalog;
            if (String.IsNullOrWhiteSpace(snapshot.Genre)) snapshot.Genre = genre;
            report.Add("Album", title, "MusicBrainz", 0.95, "Disc-ID/TOC-Release", url);
            report.Add("Album Artist", artist, "MusicBrainz", 0.95, "Disc-ID/TOC-Release", url);
            report.Add("Date", date, "MusicBrainz", 0.95, "Release", url);
            report.Add("Country", country, "MusicBrainz", 0.95, "Release", url);
            report.Add("Label", label, "MusicBrainz", 0.95, "Release", url);
            report.Add("Catalog", catalog, "MusicBrainz", 0.95, "Release", url);
            report.Add("Genre", genre, "MusicBrainz", 0.80, "Release-Group Genre", url);
        }

        private static void FillTracks(CdSnapshot snapshot, Dictionary<string, object> release, CdMetadataReport report, string url)
        {
            Dictionary<string, object> medium = BestMedium(release, snapshot.Tracks.Count);
            if (medium == null) return;
            List<Dictionary<string, object>> tracks = Json.List(medium, "tracks");
            int n = Math.Min(tracks.Count, snapshot.Tracks.Count);
            for (int i = 0; i < n; i++)
            {
                Dictionary<string, object> mt = tracks[i];
                Dictionary<string, object> recording = Json.Object(mt, "recording");
                string title = Json.String(mt, "title");
                if (String.IsNullOrWhiteSpace(title)) title = Json.String(recording, "title");
                string artist = Json.ArtistCredit(mt);
                if (String.IsNullOrWhiteSpace(artist)) artist = Json.ArtistCredit(recording);
                List<Dictionary<string, object>> trackGenres = Json.List(recording, "genres");
                if (trackGenres.Count == 0) trackGenres = Json.List(mt, "genres");
                string genre = String.Join(" / ", trackGenres.Select(x => Json.String(x, "name"))
                    .Where(x => !String.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
                if (String.IsNullOrWhiteSpace(snapshot.Tracks[i].Title)) snapshot.Tracks[i].Title = title;
                if (String.IsNullOrWhiteSpace(snapshot.Tracks[i].Artist)) snapshot.Tracks[i].Artist = artist;
                report.Add("Track " + (i + 1) + " Title", title, "MusicBrainz", 0.90, "Disc-ID/TOC Tracklist", url);
                report.Add("Track " + (i + 1) + " Artist", artist, "MusicBrainz", 0.90, "Disc-ID/TOC Tracklist artist credit", url);
                report.Add("Track " + (i + 1) + " Genre", genre, "MusicBrainz", 0.72, "Recording genre", url);
            }
        }

        internal static string ValidateEvidenceCompletenessContract()
        {
            Dictionary<string, object> release = new Dictionary<string, object>();
            release["title"] = "Fixture Album";
            release["date"] = "1999-05-31";
            release["country"] = "GB";
            release["artist-credit"] = new object[] { new Dictionary<string, object> { { "name", "Fixture Album Artist" } } };
            release["label-info"] = new object[]
            {
                new Dictionary<string, object>
                {
                    { "catalog-number", "CAT-001" },
                    { "label", new Dictionary<string, object> { { "name", "Fixture Label" } } }
                }
            };
            release["release-group"] = new Dictionary<string, object>
            {
                { "genres", new object[] { new Dictionary<string, object> { { "name", "House" } } } }
            };
            Dictionary<string, object> recording = new Dictionary<string, object>();
            recording["title"] = "Fixture Track";
            recording["artist-credit"] = new object[] { new Dictionary<string, object> { { "name", "Fixture Track Artist" } } };
            recording["genres"] = new object[] { new Dictionary<string, object> { { "name", "Deep House" }, { "count", 5 } } };
            Dictionary<string, object> track = new Dictionary<string, object>();
            track["title"] = "Fixture Track";
            track["length"] = 240000;
            track["recording"] = recording;
            Dictionary<string, object> medium = new Dictionary<string, object>();
            medium["track-count"] = 1;
            medium["tracks"] = new object[] { track };
            release["media"] = new object[] { medium };

            CdSnapshot snapshot = new CdSnapshot();
            snapshot.Tracks.Add(new CdTrackCapture { Position = 1, DurationSeconds = 240 });
            CdMetadataReport report = new CdMetadataReport();
            FillRelease(snapshot, release, report, "https://musicbrainz.org/release/fixture");
            FillTracks(snapshot, release, report, "https://musicbrainz.org/release/fixture");
            string[] required =
            {
                "Album", "Album Artist", "Date", "Genre", "Label", "Catalog", "Country",
                "Track 1 Artist", "Track 1 Title", "Track 1 Genre"
            };
            foreach (string field in required)
                if (!report.Evidence.Any(x => x.Source == "MusicBrainz" && x.Field == field && !String.IsNullOrWhiteSpace(x.Value)))
                    throw new InvalidDataException("MusicBrainz field-completeness regression: missing " + field + ".");
            if (snapshot.Tracks[0].Artist != "Fixture Track Artist")
                throw new InvalidDataException("MusicBrainz parsed track artist was not projected into the working snapshot.");
            return "MusicBrainz field completeness: release identity + Track Artist/Title/Genre provenance";
        }

        private static Dictionary<string, object> BestMedium(Dictionary<string, object> release, int count)
        {
            List<Dictionary<string, object>> media = Json.List(release, "media");
            Dictionary<string, object> best = null;
            foreach (Dictionary<string, object> medium in media)
            {
                if (Json.Int(medium, "track-count") == count) return medium;
                if (best == null) best = medium;
            }
            return best;
        }

        private static string FindDiscogsReleaseId(Dictionary<string, object> release)
        {
            foreach (Dictionary<string, object> relation in Json.List(release, "relations"))
            {
                string resource = Json.String(Json.Object(relation, "url"), "resource");
                if (resource.IndexOf("discogs.com/release/", StringComparison.OrdinalIgnoreCase) < 0) continue;
                string tail = resource.Substring(resource.LastIndexOf('/') + 1);
                int dash = tail.IndexOf('-');
                if (dash > 0) tail = tail.Substring(0, dash);
                long id;
                if (Int64.TryParse(tail, NumberStyles.Integer, CultureInfo.InvariantCulture, out id) && id > 0) return id.ToString(CultureInfo.InvariantCulture);
            }
            return "";
        }

        internal static Dictionary<string, object> GetJson(string url, bool rateLimited, string token)
        {
            if (rateLimited)
            {
                lock (RateLock)
                {
                    TimeSpan elapsed = DateTime.UtcNow - _lastRequestUtc;
                    if (elapsed.TotalMilliseconds < 1100) Thread.Sleep((int)(1100 - elapsed.TotalMilliseconds));
                    _lastRequestUtc = DateTime.UtcNow;
                }
            }
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "DJLibrary/0.4.0 (https://github.com/lxsdd/dj-library)";
            request.Accept = "application/json";
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.Timeout = 9000;
            request.ReadWriteTimeout = 9000;
            if (!String.IsNullOrWhiteSpace(token)) request.Headers[HttpRequestHeader.Authorization] = "Discogs token=" + token;
            using (WebResponse response = request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                return new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 }.DeserializeObject(reader.ReadToEnd()) as Dictionary<string, object>;
        }
    }

    internal static class DiscogsMetadataSource
    {
        public static void Apply(CdSnapshot snapshot, string releaseId, CdMetadataReport report)
        {
            if (String.IsNullOrWhiteSpace(releaseId)) return;
            string api = "https://api.discogs.com/releases/" + Uri.EscapeDataString(releaseId);
            Dictionary<string, object> release = MusicBrainzMetadataSource.GetJson(api, false, null);
            if (release == null) throw new InvalidDataException("Empty Discogs response.");
            DiscogsIndependentMetadataSource.ApplyResolvedRelease(snapshot, releaseId, release, report,
                "MusicBrainz link (fallback; independent Discogs search was not unambiguous/available)", 0.90);
        }
    }

    internal static class Json
    {
        public static Dictionary<string, object> Object(Dictionary<string, object> source, string key)
        {
            if (source == null) return new Dictionary<string, object>();
            object value;
            if (!source.TryGetValue(key, out value) || value == null) return new Dictionary<string, object>();
            Dictionary<string, object> typed = value as Dictionary<string, object>;
            if (typed != null) return typed;
            IDictionary generic = value as IDictionary;
            Dictionary<string, object> result = new Dictionary<string, object>();
            if (generic != null) foreach (DictionaryEntry e in generic) result[Convert.ToString(e.Key, CultureInfo.InvariantCulture)] = e.Value;
            return result;
        }

        public static List<Dictionary<string, object>> List(Dictionary<string, object> source, string key)
        {
            List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();
            if (source == null) return result;
            object value;
            if (!source.TryGetValue(key, out value) || value == null) return result;
            IEnumerable sequence = value as IEnumerable;
            if (sequence == null || value is string) return result;
            foreach (object item in sequence)
            {
                Dictionary<string, object> typed = item as Dictionary<string, object>;
                if (typed != null) result.Add(typed);
                else
                {
                    IDictionary generic = item as IDictionary;
                    if (generic == null) continue;
                    Dictionary<string, object> d = new Dictionary<string, object>();
                    foreach (DictionaryEntry e in generic) d[Convert.ToString(e.Key, CultureInfo.InvariantCulture)] = e.Value;
                    result.Add(d);
                }
            }
            return result;
        }

        public static string String(Dictionary<string, object> source, string key)
        {
            if (source == null) return "";
            object value;
            return source.TryGetValue(key, out value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : "";
        }

        public static int Int(Dictionary<string, object> source, string key)
        {
            string value = String(source, key);
            int result;
            return Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result) ? result : 0;
        }

        public static List<string> StringList(Dictionary<string, object> source, string key)
        {
            List<string> result = new List<string>();
            if (source == null) return result;
            object value;
            if (!source.TryGetValue(key, out value) || value == null) return result;
            IEnumerable sequence = value as IEnumerable;
            if (sequence == null || value is string) return result;
            foreach (object item in sequence)
            {
                string s = Convert.ToString(item, CultureInfo.InvariantCulture);
                if (!System.String.IsNullOrWhiteSpace(s)) result.Add(s.Trim());
            }
            return result;
        }

        public static string ArtistCredit(Dictionary<string, object> source)
        {
            List<Dictionary<string, object>> credits = List(source, "artist-credit");
            if (credits.Count == 0) return "";
            StringBuilder b = new StringBuilder();
            foreach (Dictionary<string, object> credit in credits)
            {
                string name = String(credit, "name");
                if (System.String.IsNullOrWhiteSpace(name)) name = String(Object(credit, "artist"), "name");
                if (b.Length > 0 && b[b.Length - 1] != ' ') b.Append(' ');
                b.Append(name);
                string join = String(credit, "joinphrase");
                if (!System.String.IsNullOrEmpty(join)) b.Append(join);
            }
            return b.ToString().Trim();
        }
    }
}
