using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace DJLibrary
{
    internal static partial class CdMetadataPostProcessor
    {
        public static string Augment(CdSnapshot snapshot, IList<CatalogTocMatch> duplicates, CdMetadataReport report)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");
            if (report == null) throw new ArgumentNullException("report");

            AddCdTextEvidence(snapshot, report);
            AddCatalogEvidence(duplicates, report);
            AddFoobarEvidence(snapshot, report);
            int parsed = AddSafeVersionEvidence(snapshot, report);
            if (parsed > 0)
                report.Status("Title Analysis", parsed.ToString(CultureInfo.CurrentCulture) +
                    " conservative Mix/Version suggestions from trailing version parentheses", "");
            else
                report.Status("Title Analysis", "no additional safe Mix/Version suggestions", "");

            snapshot.MetadataEvidence = report.Evidence;
            snapshot.MetadataSources = report.Sources;
            return "CD-TEXT evidence + exact-TOC catalog evidence + foobar evidence + conservative version parser";
        }

        private static void AddCdTextEvidence(CdSnapshot snapshot, CdMetadataReport report)
        {
            if (!snapshot.CdTextPresent) return;
            string detail = String.IsNullOrWhiteSpace(snapshot.CdTextSource)
                ? "read directly from disc"
                : snapshot.CdTextSource;

            report.Add("Album", snapshot.Album, "CD-TEXT", 0.98, detail, "");
            report.Add("Album Artist", snapshot.AlbumArtist, "CD-TEXT", 0.98, detail, "");

            foreach (CdTrackCapture track in snapshot.Tracks)
            {
                string rawTitle = String.IsNullOrWhiteSpace(track.RawTitle) ? track.Title : track.RawTitle;
                report.Add("Track " + track.Position + " Title", rawTitle, "CD-TEXT", 0.98, detail, "");
                report.Add("Track " + track.Position + " Artist", track.Artist, "CD-TEXT", 0.98, detail, "");
            }
        }

        private static void AddCatalogEvidence(IList<CatalogTocMatch> duplicates, CdMetadataReport report)
        {
            if (duplicates == null || duplicates.Count == 0)
            {
                report.Status("Catalog", "no exact full TOC match", "");
                return;
            }

            if (!File.Exists(CatalogService.DefaultCatalogPath))
            {
                report.Status("Catalog", "exact TOC match reported, but catalog file is unavailable", "");
                return;
            }

            int loaded = 0;
            try
            {
                using (CatalogService catalog = CatalogService.OpenForTesting(CatalogService.DefaultCatalogPath))
                {
                    foreach (CatalogTocMatch match in duplicates)
                    {
                        CatalogRelease release = catalog.GetRelease(match.ReleaseId);
                        CatalogDisc disc = catalog.GetDisc(match.DiscId);
                        if (release == null || disc == null) continue;
                        List<CatalogTrack> tracks = catalog.GetTracks(match.DiscId);
                        string detail = "Exact full TOC match · " + match.DisplayText;

                        report.Add("Album", release.Album, "Catalog", 1.00, detail, "");
                        report.Add("Album Artist", release.AlbumArtist, "Catalog", 1.00, detail, "");
                        report.Add("Date", release.ReleaseDate, "Catalog", 1.00, detail, "");
                        report.Add("Genre/Style", release.Genre, "Catalog", 1.00, detail, "");
                        report.Add("Label", release.Label, "Catalog", 1.00, detail, "");
                        report.Add("Catalog", release.Catalog, "Catalog", 1.00, detail, "");
                        report.Add("Country", release.Country, "Catalog", 1.00, detail, "");

                        foreach (CatalogTrack track in tracks)
                        {
                            report.Add("Track " + track.Position + " Title", track.Title, "Catalog", 1.00, detail, "");
                            report.Add("Track " + track.Position + " Artist", track.Artist, "Catalog", 1.00, detail, "");
                            report.Add("Track " + track.Position + " Mix/Version", track.Version, "Catalog", 1.00, detail, "");
                            report.Add("Track " + track.Position + " Genre", track.Genre, "Catalog", 1.00, detail, "");
                        }
                        loaded++;
                    }
                }
                report.Status("Catalog",
                    loaded.ToString(CultureInfo.CurrentCulture) + " exact full TOC " +
                    (loaded == 1 ? "match loaded as a metadata source" : "matches loaded as metadata sources"), "");
            }
            catch (Exception ex)
            {
                report.Status("Catalog", "TOC metadata could not be loaded: " + Clean(ex.Message), "");
            }
        }

        private static void AddFoobarEvidence(CdSnapshot snapshot, CdMetadataReport report)
        {
            foreach (CdTrackCapture track in snapshot.Tracks)
            {
                if (!String.Equals(track.MetadataSource, "foobar", StringComparison.OrdinalIgnoreCase)) continue;
                double confidence = track.MetadataConfidence > 0 ? track.MetadataConfidence : 0.95;
                string detail = String.IsNullOrWhiteSpace(track.MetadataDetail) ? "strong local digital match" : track.MetadataDetail;
                AddIfMissing(report, "Track " + track.Position + " Title", track.Title, "foobar", confidence, detail);
                AddIfMissing(report, "Track " + track.Position + " Artist", track.Artist, "foobar", confidence, detail);
                AddIfMissing(report, "Track " + track.Position + " Mix/Version", track.Version, "foobar", confidence, detail);
                AddIfMissing(report, "Track " + track.Position + " Genre", track.Genre, "foobar", confidence, detail);
            }
        }

        private static void AddIfMissing(CdMetadataReport report, string field, string value, string source, double confidence, string detail)
        {
            if (report == null || String.IsNullOrWhiteSpace(value)) return;
            bool exists = report.Evidence.Any(x => x != null &&
                String.Equals(x.Field, field, StringComparison.OrdinalIgnoreCase) &&
                String.Equals(x.Source, source, StringComparison.OrdinalIgnoreCase) &&
                !String.IsNullOrWhiteSpace(x.Value));
            if (!exists) report.Add(field, value, source, confidence, detail, "");
        }

        private static int AddSafeVersionEvidence(CdSnapshot snapshot, CdMetadataReport report)
        {
            int count = 0;
            foreach (CdTrackCapture track in snapshot.Tracks)
            {
                string raw = String.IsNullOrWhiteSpace(track.RawTitle) ? track.Title : track.RawTitle;
                string title, version;
                if (!TrySplitSafeVersion(raw, out title, out version)) continue;
                report.Add("Track " + track.Position + " Title", title, "Title Analysis", 0.78,
                    "only the final clearly version-like parenthesis group is split", "");
                report.Add("Track " + track.Position + " Mix/Version", version, "Title Analysis", 0.78,
                    "conservative version-marker detection", "");
                count++;
            }
            return count;
        }

        internal static bool TrySplitSafeVersion(string value, out string title, out string version)
        {
            title = (value ?? "").Trim();
            version = "";
            List<TitleSplitCandidate> candidates = CdMetadataPipeline.SplitTitle(value);
            for (int i = 1; i < candidates.Count; i++)
            {
                string v = candidates[i].Version ?? "";
                if (!LooksLikeVersion(v)) continue;
                title = candidates[i].Title;
                version = v;
                return true;
            }
            return false;
        }

        private static bool LooksLikeVersion(string value)
        {
            string n = (value ?? "").Trim().ToLowerInvariant();
            if (n.Length == 0 || n.Length > 120) return false;
            string[] markers =
            {
                "mix", "remix", "edit", "version", "dub", "radio", "club", "vocal",
                "instrumental", "extended", "original", "rework", "remaster", "bootleg",
                "mash up", "mashup"
            };
            return markers.Any(delegate(string marker)
            {
                return n.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0;
            });
        }

        internal static string RunSelfTest()
        {
            string title, version;
            if (!TrySplitSafeVersion("Will I (Discover Love) (Simon Templar Mix)", out title, out version) ||
                title != "Will I (Discover Love)" || version != "Simon Templar Mix")
                throw new InvalidDataException("Multi-parenthesis Title Analysis failed.");

            if (!TrySplitSafeVersion("Time Is Serene (Armin Van Buuren Mash Up)", out title, out version) ||
                title != "Time Is Serene" || version != "Armin Van Buuren Mash Up")
                throw new InvalidDataException("Mash-up Title Analysis failed.");

            if (TrySplitSafeVersion("Song Title (Live)", out title, out version))
                throw new InvalidDataException("Conservative Title Analysis split a non-version parenthesis.");

            CdSnapshot cdText = new CdSnapshot { CdTextPresent = true, CdTextSource = "self-test", Album = "CD-TEXT Album", AlbumArtist = "CD-TEXT Album Artist" };
            cdText.Tracks.Add(new CdTrackCapture { Position = 1, RawTitle = "CD-TEXT Track", Title = "CD-TEXT Track", Artist = "CD-TEXT Artist" });
            CdMetadataReport cdTextReport = new CdMetadataReport();
            AddCdTextEvidence(cdText, cdTextReport);
            string[] required = { "Album", "Album Artist", "Track 1 Title", "Track 1 Artist" };
            foreach (string field in required)
                if (!cdTextReport.Evidence.Any(x => x.Source == "CD-TEXT" && x.Field == field && !String.IsNullOrWhiteSpace(x.Value)))
                    throw new InvalidDataException("CD-TEXT field-completeness regression: missing " + field + ".");

            return "safe multi-parenthesis/mash-up version parser + CD-TEXT field completeness";
        }

        private static string Clean(string value)
        {
            string s = (value ?? "Error").Replace('\r', ' ').Replace('\n', ' ').Trim();
            return s.Length > 160 ? s.Substring(0, 160) + "…" : s;
        }
    }
}
