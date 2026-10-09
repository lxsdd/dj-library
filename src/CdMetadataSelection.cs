using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace DJLibrary
{
    public sealed partial class CdMetadataSelectionSession
    {
        private readonly CdSnapshot _snapshot;
        private readonly CdMetadataReport _report;
        private readonly Dictionary<string, bool> _enabled = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _sourceStatus = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public List<CdMetadataChoiceRow> Rows { get; private set; }
        public List<CdMetadataSourceToggle> Sources { get; private set; }

        public CdMetadataSelectionSession(CdSnapshot snapshot, CdMetadataReport report)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");
            if (report == null) throw new ArgumentNullException("report");
            _snapshot = snapshot;
            _report = report;
            Rows = new List<CdMetadataChoiceRow>();
            Sources = new List<CdMetadataSourceToggle>();
            InitializeSources();
            _baseSource = ChooseInitialBaseSource();
            RebuildRows();
        }

        private void InitializeSources()
        {
            string[] preferred = { "CD-TEXT", "Catalog", "foobar", "MusicBrainz", "Discogs", "Title Analysis" };
            foreach (string source in preferred) _enabled[source] = true;
            foreach (MetadataSourceStatus status in _report.Sources)
            {
                if (String.IsNullOrWhiteSpace(status.Source)) continue;
                string source = NormalizeSource(status.Source);
                if (!_enabled.ContainsKey(source)) _enabled[source] = true;
                string text;
                if (_sourceStatus.TryGetValue(source, out text) && !String.IsNullOrWhiteSpace(text))
                    _sourceStatus[source] = text + " · " + (status.Text ?? "");
                else _sourceStatus[source] = status.Text ?? "";
            }
            foreach (MetadataEvidence evidence in _report.Evidence)
            {
                if (String.IsNullOrWhiteSpace(evidence.Source)) continue;
                string source = NormalizeSource(evidence.Source);
                if (!_enabled.ContainsKey(source)) _enabled[source] = true;
            }
            Sources = _enabled.Keys
                .Where(x => !String.Equals(x, "Current", StringComparison.OrdinalIgnoreCase) && !String.Equals(x, "Manual", StringComparison.OrdinalIgnoreCase))
                .OrderBy(SourceOrder)
                .ThenBy(x => x, StringComparer.CurrentCultureIgnoreCase)
                .Select(delegate(string source)
                {
                    string status;
                    _sourceStatus.TryGetValue(source, out status);
                    return new CdMetadataSourceToggle { Source = source, Status = status ?? "", Enabled = _enabled[source] };
                }).ToList();
        }

        public void SetSourceEnabled(string source, bool enabled)
        {
            if (String.IsNullOrWhiteSpace(source)) return;
            _enabled[NormalizeSource(source)] = enabled;
            foreach (CdMetadataSourceToggle toggle in Sources)
                if (String.Equals(toggle.Source, NormalizeSource(source), StringComparison.OrdinalIgnoreCase)) toggle.Enabled = enabled;
            RebuildRows();
        }

        public void ResetAutomatic()
        {
            ResetOverrides();
        }

        public void RebuildRows()
        {

            List<CdMetadataChoiceRow> rows = new List<CdMetadataChoiceRow>();
            AddReleaseRow(rows, "release.album", "Release · Album", "Album", _snapshot.Album);
            AddReleaseRow(rows, "release.albumartist", "Release · Album Artist", "Album Artist", _snapshot.AlbumArtist);
            AddReleaseRow(rows, "release.date", "Release · Date/Year", "Date", _snapshot.ReleaseDate);
            AddReleaseRow(rows, "release.genre", "Release · Genre/Style", "Genre/Style", _snapshot.Genre);
            AddReleaseRow(rows, "release.label", "Release · Label", "Label", _snapshot.Label);
            AddReleaseRow(rows, "release.catalog", "Release · Catalog Number", "Catalog", _snapshot.Catalog);
            AddReleaseRow(rows, "release.country", "Release · Country", "Country", _snapshot.Country);

            foreach (CdTrackCapture track in _snapshot.Tracks.OrderBy(x => x.Position))
            {
                string prefix = "track." + track.Position.ToString(CultureInfo.InvariantCulture) + ".";
                string label = "Track " + track.Position.ToString("00", CultureInfo.CurrentCulture) + " · ";
                AddTrackRow(rows, prefix + "title", label + "Title", track.Position, "Title", track.Title);
                AddTrackRow(rows, prefix + "version", label + "Mix/Version", track.Position, "Mix/Version", track.Version);
                AddTrackRow(rows, prefix + "artist", label + "Artist", track.Position, "Artist", track.Artist);
                AddTrackRow(rows, prefix + "genre", label + "Genre", track.Position, "Genre", track.Genre);
            }

            Rows = rows;
            ApplyBaseAndOverrides();
        }

        private void AddReleaseRow(List<CdMetadataChoiceRow> rows, string key, string label, string evidenceField, string current)
        {
            AddRow(rows, key, label, current, _report.Evidence.Where(x => IsReleaseField(x.Field, evidenceField)).ToList());
        }

        private void AddTrackRow(List<CdMetadataChoiceRow> rows, string key, string label, int position, string suffix, string current)
        {
            string field = "Track " + position.ToString(CultureInfo.InvariantCulture) + " " + suffix;
            AddRow(rows, key, label, current, _report.Evidence.Where(x => String.Equals(x.Field, field, StringComparison.OrdinalIgnoreCase)).ToList());
        }

        private void AddRow(List<CdMetadataChoiceRow> rows, string key, string label, string current, IList<MetadataEvidence> evidence)
        {
            List<MetadataEvidence> all = new List<MetadataEvidence>();
            if (evidence != null) all.AddRange(evidence.Where(x => x != null && !String.IsNullOrWhiteSpace(x.Value)));
            if (!String.IsNullOrWhiteSpace(current) && !all.Any(x => String.Equals(NormalizeSource(x.Source), "Current", StringComparison.OrdinalIgnoreCase) && String.Equals(x.Value, current, StringComparison.Ordinal)))
                all.Add(new MetadataEvidence { Field = label, Value = current, Source = "Current", Confidence = 0.10, Detail = "current preview value before base/override selection" });

            List<MetadataEvidence> enabled = all.Where(delegate(MetadataEvidence item)
            {
                string source = NormalizeSource(item.Source);
                if (String.Equals(source, "Current", StringComparison.OrdinalIgnoreCase)) return true;
                bool value;
                return !_enabled.TryGetValue(source, out value) || value;
            }).ToList();

            MetadataEvidence best = BestAutomatic(enabled);
            CdMetadataChoiceOption automatic = new CdMetadataChoiceOption
            {
                Source = "Automatic",
                Value = best == null ? "" : best.Value,
                EffectiveSource = best == null ? "—" : NormalizeSource(best.Source),
                Confidence = best == null ? 0 : best.Confidence,
                Detail = best == null ? "no unambiguous value" : best.Detail,
                IsAutomatic = true
            };

            CdMetadataChoiceRow row = new CdMetadataChoiceRow
            {
                Key = key,
                FieldLabel = label,
                CurrentValue = current ?? "",
                HasConflict = enabled.Select(x => NormalizeValue(x.Value)).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).Count() > 1
            };
            row.Options.Add(automatic);
            row.Options.Add(new CdMetadataChoiceOption { Source = "Do Not Use", Value = "", IsNone = true });
            foreach (MetadataEvidence item in enabled
                .OrderByDescending(x => x.Confidence)
                .ThenByDescending(x => SourcePriority(NormalizeSource(x.Source)))
                .ThenBy(x => x.Source, StringComparer.CurrentCultureIgnoreCase))
            {
                string source = NormalizeSource(item.Source);
                if (row.Options.Any(x => !x.IsAutomatic && !x.IsNone && String.Equals(x.Source, source, StringComparison.OrdinalIgnoreCase) && String.Equals(x.Value, item.Value, StringComparison.Ordinal))) continue;
                row.Options.Add(new CdMetadataChoiceOption
                {
                    Source = source,
                    EffectiveSource = source,
                    Value = item.Value,
                    Confidence = item.Confidence,
                    Detail = item.Detail ?? ""
                });
            }
            row.SelectedOption = automatic;
            // Every modeled field remains reviewable even when the base and all
            // alternatives are empty. Empty/missing is a semantic state, not a
            // reason to remove the field from the review model.
            rows.Add(row);
        }

        private static MetadataEvidence BestAutomatic(IList<MetadataEvidence> values)
        {
            if (values == null || values.Count == 0) return null;
            List<MetadataEvidence> candidates = new List<MetadataEvidence>();
            foreach (IGrouping<string, MetadataEvidence> group in values.GroupBy(x => NormalizeSource(x.Source), StringComparer.OrdinalIgnoreCase))
            {
                List<string> distinct = group.Select(x => NormalizeValue(x.Value)).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).ToList();
                if (distinct.Count > 1) continue;
                MetadataEvidence bestInSource = group.OrderByDescending(x => x.Confidence).FirstOrDefault();
                if (bestInSource != null) candidates.Add(bestInSource);
            }
            return candidates
                .OrderByDescending(x => x.Confidence)
                .ThenByDescending(x => SourcePriority(NormalizeSource(x.Source)))
                .FirstOrDefault();
        }

        private static bool IsReleaseField(string actual, string wanted)
        {
            if (String.Equals(actual, wanted, StringComparison.OrdinalIgnoreCase)) return true;
            if (String.Equals(wanted, "Genre/Style", StringComparison.OrdinalIgnoreCase) && String.Equals(actual, "Genre", StringComparison.OrdinalIgnoreCase)) return true;
            if (String.Equals(wanted, "Date", StringComparison.OrdinalIgnoreCase) && (String.Equals(actual, "Date", StringComparison.OrdinalIgnoreCase) || String.Equals(actual, "Datum", StringComparison.OrdinalIgnoreCase))) return true;
            if (String.Equals(wanted, "Country", StringComparison.OrdinalIgnoreCase) && (String.Equals(actual, "Country", StringComparison.OrdinalIgnoreCase) || String.Equals(actual, "Land", StringComparison.OrdinalIgnoreCase))) return true;
            if ((String.Equals(wanted, "Catalog", StringComparison.OrdinalIgnoreCase) || String.Equals(wanted, "Catalog Number", StringComparison.OrdinalIgnoreCase)) && (String.Equals(actual, "Catalog", StringComparison.OrdinalIgnoreCase) || String.Equals(actual, "Katalog", StringComparison.OrdinalIgnoreCase))) return true;
            return false;
        }

        private static string NormalizeSource(string source)
        {
            if (String.IsNullOrWhiteSpace(source)) return "Unknown";
            string s = source.Trim();
            if (s.StartsWith("Discogs", StringComparison.OrdinalIgnoreCase)) return "Discogs";
            if (s.StartsWith("MusicBrainz", StringComparison.OrdinalIgnoreCase)) return "MusicBrainz";
            if (s.StartsWith("foobar", StringComparison.OrdinalIgnoreCase)) return "foobar";
            if (String.Equals(s,"Catalog",StringComparison.OrdinalIgnoreCase) || String.Equals(s,"Katalog",StringComparison.OrdinalIgnoreCase)) return "Catalog";
            if (String.Equals(s,"Title Analysis",StringComparison.OrdinalIgnoreCase) || String.Equals(s,"Titelanalyse",StringComparison.OrdinalIgnoreCase)) return "Title Analysis";
            if (String.Equals(s,"Manual",StringComparison.OrdinalIgnoreCase) || String.Equals(s,"Manuell",StringComparison.OrdinalIgnoreCase)) return "Manual";
            if (String.Equals(s,"Current",StringComparison.OrdinalIgnoreCase) || String.Equals(s,"Aktuell",StringComparison.OrdinalIgnoreCase)) return "Current";
            return s;
        }

        private static string NormalizeValue(string value)
        {
            return CdMetadataPipeline.Norm(value);
        }

        private static int SourcePriority(string source)
        {
            if (String.Equals(source, "Catalog", StringComparison.OrdinalIgnoreCase)) return 60;
            if (String.Equals(source, "foobar", StringComparison.OrdinalIgnoreCase)) return 55;
            if (String.Equals(source, "CD-TEXT", StringComparison.OrdinalIgnoreCase)) return 50;
            if (String.Equals(source, "Discogs", StringComparison.OrdinalIgnoreCase)) return 45;
            if (String.Equals(source, "MusicBrainz", StringComparison.OrdinalIgnoreCase)) return 40;
            if (String.Equals(source, "Title Analysis", StringComparison.OrdinalIgnoreCase)) return 25;
            if (String.Equals(source, "Current", StringComparison.OrdinalIgnoreCase)) return 5;
            return 10;
        }

        private static int SourceOrder(string source)
        {
            int p = SourcePriority(source);
            return -p;
        }

        private string ValueFor(string key, out string source)
        {
            source = "";
            CdMetadataChoiceRow row = Rows.FirstOrDefault(x => String.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
            if (row == null || row.SelectedOption == null) return "";
            CdMetadataChoiceOption selected = row.SelectedOption;
            source = selected.IsAutomatic ? selected.EffectiveSource : selected.Source;
            return selected.Value ?? "";
        }

        public void ApplyTo(CdSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");
            string ignored;
            snapshot.Album = ValueFor("release.album", out ignored);
            snapshot.AlbumArtist = ValueFor("release.albumartist", out ignored);
            snapshot.ReleaseDate = ValueFor("release.date", out ignored);
            snapshot.Genre = ValueFor("release.genre", out ignored);
            snapshot.Label = ValueFor("release.label", out ignored);
            snapshot.Catalog = ValueFor("release.catalog", out ignored);
            snapshot.Country = ValueFor("release.country", out ignored);

            foreach (CdTrackCapture track in snapshot.Tracks)
            {
                string prefix = "track." + track.Position.ToString(CultureInfo.InvariantCulture) + ".";
                List<string> sources = new List<string>();
                string source;
                track.Title = ValueFor(prefix + "title", out source); AddSource(sources, source);
                track.Version = ValueFor(prefix + "version", out source); AddSource(sources, source);
                track.Artist = ValueFor(prefix + "artist", out source); AddSource(sources, source);
                track.Genre = ValueFor(prefix + "genre", out source); AddSource(sources, source);
                string[] scopeKeys = { prefix + "artist", prefix + "title", prefix + "version", prefix + "genre" };
                track.MetadataSource = ReviewSummary(scopeKeys);
                string starting = String.Equals(BaseSource, "Recommended", StringComparison.OrdinalIgnoreCase) ? "Recommended values" : BaseSource + " starting values";
                track.MetadataDetail = "Starting values: " + starting + "; explicit field changes: " + scopeKeys.Count(IsOverride).ToString(CultureInfo.InvariantCulture);
                track.MetadataConfidence = 1.0;
            }
        }

        private static void AddSource(List<string> sources, string source)
        {
            if (String.IsNullOrWhiteSpace(source) || String.Equals(source, "—", StringComparison.Ordinal) ||
                String.Equals(source, "Do Not Use", StringComparison.OrdinalIgnoreCase) || String.Equals(source, "Current", StringComparison.OrdinalIgnoreCase)) return;
            sources.Add(source);
        }

        internal static string RunSelfTest()
        {
            CdSnapshot snapshot = new CdSnapshot();
            snapshot.Album = "Physical Album";
            snapshot.AlbumArtist = "Physical Artist";
            snapshot.Tracks.Add(new CdTrackCapture { Position = 1, Title = "Physical Title", RawTitle = "Physical Title", Artist = "Physical Artist", Genre = "Physical Genre" });

            CdMetadataReport report = new CdMetadataReport();
            report.Add("Album", "Catalog Album", "Catalog", 1.00, "exact toc", "");
            report.Add("Album Artist", "Catalog Artist", "Catalog", 1.00, "exact toc", "");
            report.Add("Track 1 Title", "Catalog Title", "Catalog", 1.00, "exact toc", "");
            report.Add("Track 1 Artist", "Catalog Track Artist", "Catalog", 1.00, "exact toc", "");
            report.Add("Track 1 Title", "Digital Title", "foobar", 1.00, "strong", "");
            report.Add("Track 1 Mix/Version", "Extended Mix", "foobar", 1.00, "strong", "");
            report.Add("Track 1 Genre", "Trance", "foobar", 1.00, "strong", "");

            CdMetadataSelectionSession session = new CdMetadataSelectionSession(snapshot, report);
            if (session.BaseSource != "Catalog")
                throw new InvalidDataException("Coherent base metadata set selection did not prefer the most complete returned set.");
            session.ApplyTo(snapshot);
            if (snapshot.Album != "Catalog Album" || snapshot.Tracks[0].Title != "Catalog Title" || snapshot.Tracks[0].Artist != "Catalog Track Artist")
                throw new InvalidDataException("Catalog base metadata set was not projected coherently.");
            if (!String.IsNullOrWhiteSpace(snapshot.Tracks[0].Version) || !String.IsNullOrWhiteSpace(snapshot.Tracks[0].Genre))
                throw new InvalidDataException("Fields not provided by the base metadata set were incorrectly retained.");

            session.SetBaseSource("foobar");
            session.ApplyTo(snapshot);
            if (!String.IsNullOrWhiteSpace(snapshot.Album) || snapshot.Tracks[0].Title != "Digital Title" || snapshot.Tracks[0].Version != "Extended Mix" || snapshot.Tracks[0].Genre != "Trance")
                throw new InvalidDataException("Switching the base metadata set did not reset the full metadata projection.");

            CdMetadataChoiceRow album = session.FindRow("release.album");
            CdMetadataChoiceOption catalogAlbum = album.Options.FirstOrDefault(x => String.Equals(x.Source, "Catalog", StringComparison.OrdinalIgnoreCase));
            if (catalogAlbum == null) throw new InvalidDataException("Catalog is not available as a release-field override.");
            session.SelectOption("release.album", catalogAlbum);
            session.ApplyTo(snapshot);
            if (snapshot.Album != "Catalog Album" || snapshot.Tracks[0].Title != "Digital Title" || !session.IsOverride("release.album"))
                throw new InvalidDataException("Scoped field override changed fields outside its scope.");

            session.ResetOverrides();
            session.ApplyTo(snapshot);
            if (!String.IsNullOrWhiteSpace(snapshot.Album) || session.OverrideCount != 0)
                throw new InvalidDataException("Reset Overrides did not restore the selected base metadata set.");

            return "physical identity preserved separately + coherent base metadata set + scoped overrides + base-switch reset semantics";
        }

    }
}
