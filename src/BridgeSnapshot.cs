using System;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace DJLibrary
{
    public sealed class BridgeSnapshotStatus
    {
        public bool Present { get; set; }
        public bool Complete { get; set; }
        public bool Compatible { get; set; }
        public int SchemaVersion { get; set; }
        public long Generation { get; set; }
        public int DeclaredItemCount { get; set; }
        public int VerifiedItemCount { get; set; }
        public string LastChangeUtc { get; set; }
        public string DirectoryPath { get; set; }
        public string Error { get; set; }
        public string SourceId { get; set; }
        public string SourceName { get; set; }
        public string ProfilePath { get; set; }
        public string ProducerVersion { get; set; }
        public string ProducerPid { get; set; }

        public string SourceDisplayName
        {
            get
            {
                if (!String.IsNullOrWhiteSpace(SourceName)) return SourceName;
                if (!String.IsNullOrWhiteSpace(ProfilePath))
                {
                    try
                    {
                        string name = Path.GetFileName(ProfilePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                        if (!String.IsNullOrWhiteSpace(name)) return name;
                    }
                    catch { }
                }
                if (!String.IsNullOrWhiteSpace(DirectoryPath) && String.Equals(DirectoryPath, BridgeSnapshotReader.LegacyDirectory, StringComparison.OrdinalIgnoreCase))
                    return "Legacy-Bridge";
                return "foobar2000";
            }
        }

        public string ShortText
        {
            get
            {
                if (!Present) return "Bridge: noch nicht eingerichtet";
                if (!Compatible) return "Bridge: inkompatibel";
                if (!Complete) return "Bridge: Synchronisierung läuft";
                return String.Format(CultureInfo.CurrentCulture, "Bridge: {0} · Gen. {1:N0} · {2:N0} Items", SourceDisplayName, Generation, VerifiedItemCount);
            }
        }
    }

    // Dependency-free consumer for the bridge's atomic snapshot contract.
    // The foobar component owns/writes the cache; DJ Library only reads it.
    public static class BridgeSnapshotReader
    {
        // Keep the established v1 constants as source-compatibility aliases.
        public const int SchemaVersion = 1;
        public const int SchemaVersionV2 = 2;
        public const int SchemaVersionV3 = 3;
        public const int ItemColumnCount = 24;
        public const int ItemColumnCountV2 = 25;
        public const int ItemColumnCountV3 = 26;
        public const string ItemHeader = "path\tsubsong\tartist\tartists\ttitle\toriginal_title\tremixed_by\talbum\talbum_artist\ttrack_number\ttotal_tracks\tdisc_number\ttotal_discs\tdate\tgenre\tstyle\tbpm\tlabel\tcatalog_number\tduration_seconds\tisrc\tcodec\tbitrate\ttag_fingerprint";
        public const string ItemHeaderV2 = ItemHeader + "\textra_metadata_json";
        public const string ItemHeaderV3 = ItemHeaderV2 + "\tmetadata_vectors_json";

        public static string StandardProfileDirectory
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "foobar2000-v2", "foo_dj_library_bridge"); }
        }

        public static string LegacyDirectory
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DJLibrary", "bridge"); }
        }

        // Kept as a source-compatibility alias for older self-tests/callers. New runtime code
        // should use the selected bridge directory rather than assuming a global default.
        public static string DefaultDirectory
        {
            get { return StandardProfileDirectory; }
        }

        public static bool HasStateFile(string directory)
        {
            if (String.IsNullOrWhiteSpace(directory)) return false;
            try { return File.Exists(Path.Combine(directory, "bridge-state.tsv")); }
            catch { return false; }
        }

        public static BridgeSnapshotStatus Inspect(string directory, bool verifyItems)
        {
            BridgeSnapshotStatus s = new BridgeSnapshotStatus();
            s.DirectoryPath = directory;
            if (String.IsNullOrWhiteSpace(directory)) return s;
            string statePath = Path.Combine(directory, "bridge-state.tsv");
            string itemPath = Path.Combine(directory, "digital-items.tsv.gz");
            if (!File.Exists(statePath)) return s;
            s.Present = true;
            try
            {
                byte[] stateBefore = File.ReadAllBytes(statePath);
                s = ParseStateBytes(stateBefore, directory);
                if (!s.Compatible || !s.Complete) return s;
                if (!File.Exists(itemPath)) throw new InvalidDataException("bridge-state.tsv ist vollständig, aber digital-items.tsv.gz fehlt.");
                if (verifyItems)
                {
                    int count = VerifyItems(itemPath, s.SchemaVersion);
                    s.VerifiedItemCount = count;
                    if (count != s.DeclaredItemCount) throw new InvalidDataException("Bridge item_count=" + s.DeclaredItemCount + ", Snapshot=" + count + ".");
                    byte[] stateAfter = File.ReadAllBytes(statePath);
                    if (!SameBytes(stateBefore, stateAfter))
                        throw new InvalidDataException("Bridge-Generation änderte sich während der Prüfung; Snapshot wird verworfen und erneut eingelesen.");
                }
                else s.VerifiedItemCount = s.DeclaredItemCount;
            }
            catch (Exception ex)
            {
                s.Error = ex.Message;
                s.Compatible = false;
            }
            return s;
        }

        public static List<DigitalItem> LoadItems(string directory, BridgeSnapshotStatus expected)
        {
            if (expected == null || !expected.Present || !expected.Complete || !expected.Compatible || !String.IsNullOrEmpty(expected.Error))
                throw new InvalidDataException("Bridge-Snapshot ist nicht autoritativ.");
            string statePath = Path.Combine(directory, "bridge-state.tsv");
            string itemPath = Path.Combine(directory, "digital-items.tsv.gz");
            byte[] stateBefore = File.ReadAllBytes(statePath);
            BridgeSnapshotStatus actual = ParseStateBytes(stateBefore, directory);
            if (!actual.Compatible || !actual.Complete || !String.IsNullOrEmpty(actual.Error) ||
                actual.SchemaVersion != expected.SchemaVersion || actual.Generation != expected.Generation || actual.DeclaredItemCount != expected.DeclaredItemCount ||
                !String.Equals(actual.SourceId ?? "", expected.SourceId ?? "", StringComparison.Ordinal))
                throw new InvalidDataException("Bridge-Generation, Schema oder Quelle änderte sich vor dem Laden; vorheriger Stand bleibt aktiv.");
            List<DigitalItem> items = new List<DigitalItem>();
            HashSet<string> identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int expectedColumns = ColumnCountForSchema(actual.SchemaVersion);
            using (FileStream fs = File.Open(itemPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (GZipStream gz = new GZipStream(fs, CompressionMode.Decompress))
            using (StreamReader sr = new StreamReader(gz, new UTF8Encoding(false), true, 65536))
            {
                string header = sr.ReadLine();
                ValidateHeader(header, actual.SchemaVersion);
                string line; int id = 0; int lineNo = 1;
                while ((line = sr.ReadLine()) != null)
                {
                    lineNo++; string[] p = line.Split('\t');
                    if (p.Length != expectedColumns) throw new InvalidDataException("digital-items.tsv.gz: Zeile " + lineNo + " hat nicht " + expectedColumns + " Spalten.");
                    DigitalItem d = new DigitalItem(); d.ItemId = ++id; d.Path = p[0];
                    if (String.IsNullOrWhiteSpace(d.Path)) throw new InvalidDataException("digital-items.tsv.gz: Zeile " + lineNo + " hat keinen Pfad.");
                    int subsong; if (!Int32.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out subsong) || subsong < 0) throw new InvalidDataException("digital-items.tsv.gz: Zeile " + lineNo + " hat ungültigen subsong."); d.Subsong = subsong;
                    string identity = d.Path + "\n" + d.Subsong.ToString(CultureInfo.InvariantCulture);
                    if (!identities.Add(identity)) throw new InvalidDataException("digital-items.tsv.gz: doppelte Identität path+subsong in Zeile " + lineNo + ".");
                    d.Artist=p[2]; d.Title=p[4]; d.OriginalTitle=p[5]; d.RemixedBy=p[6]; d.Album=p[7]; d.AlbumArtist=p[8]; d.Genre=p[14]; d.Style=p[15];
                    double bpm; Double.TryParse(p[16], NumberStyles.Float, CultureInfo.InvariantCulture, out bpm); d.Bpm=bpm; d.Label=p[17]; d.Catalog=p[18];
                    double dur = 0; if (!String.IsNullOrWhiteSpace(p[19]) && (!Double.TryParse(p[19], NumberStyles.Float, CultureInfo.InvariantCulture, out dur) || dur < 0 || Double.IsNaN(dur) || Double.IsInfinity(dur))) throw new InvalidDataException("digital-items.tsv.gz: Zeile " + lineNo + " hat ungültige duration_seconds."); d.DurationSeconds=dur; d.Codec=p[21]; d.Bitrate=p[22]; d.Fingerprint=p[23];
                    // Schema v2 appends extra_metadata_json at p[24]; schema v3 additionally appends
                    // metadata_vectors_json at p[25]. Preserve the lossless v3 payload separately from
                    // the legacy projection so the shared Metadata Normalizer can consume it read-only.
                    if (actual.SchemaVersion == SchemaVersionV3) d.MetadataVectorsJson = p[25];
                    items.Add(d);
                }
            }
            byte[] stateAfter = File.ReadAllBytes(statePath);
            if (!SameBytes(stateBefore, stateAfter)) throw new InvalidDataException("Bridge-Generation änderte sich während des Ladens; vorheriger Stand bleibt aktiv.");
            if (items.Count != expected.DeclaredItemCount) throw new InvalidDataException("Bridge item_count änderte sich während des Ladens.");
            return items;
        }

        public static void ValidateSchemaV3Contract()
        {
            string directory = Path.Combine(Path.GetTempPath(), "DJLibrary-BridgeV3-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string statePath = Path.Combine(directory, "bridge-state.tsv");
                string itemPath = Path.Combine(directory, "digital-items.tsv.gz");
                string state = "schema_version\t3\n" + "generation\t7\n" + "complete\t1\n" +
                    "item_count\t1\n" + "last_change_utc\t2026-10-07T12:00:00Z\n" +
                    "source_id\tself-test-v3\n" + "source_name\tSchema v3 self-test\n" +
                    "profile_path\tC:\\foobar2000\\profile\n" + "producer_version\t0.1.0-rc4\n" + "producer_pid\t1\n";
                File.WriteAllText(statePath, state, new UTF8Encoding(false));

                string[] fields = new string[ItemColumnCountV3];
                for (int i = 0; i < fields.Length; i++) fields[i] = "";
                fields[0] = "C:\\Music\\test.flac";
                fields[1] = "0";
                fields[2] = "Artist";
                fields[4] = "Title";
                fields[14] = "House";
                fields[19] = "300";
                fields[21] = "FLAC";
                fields[22] = "900";
                fields[23] = "0123456789abcdef";
                fields[24] = "{}";
                fields[25] = "[{\"name\":\"ARTIST\",\"values\":[\"Artist\",\"Guest\"]}]";

                using (FileStream fs = File.Create(itemPath))
                using (GZipStream gz = new GZipStream(fs, CompressionMode.Compress))
                using (StreamWriter sw = new StreamWriter(gz, new UTF8Encoding(false)))
                {
                    sw.WriteLine(ItemHeaderV3);
                    sw.WriteLine(String.Join("\t", fields));
                }

                BridgeSnapshotStatus status = Inspect(directory, true);
                if (!status.Present || !status.Complete || !status.Compatible || status.SchemaVersion != SchemaVersionV3 ||
                    status.VerifiedItemCount != 1 || !String.IsNullOrEmpty(status.Error))
                    throw new InvalidOperationException("Bridge schema-v3 compatibility self-test failed.");

                List<DigitalItem> items = LoadItems(directory, status);
                if (items.Count != 1 || items[0].Path != fields[0] || items[0].Artist != "Artist" ||
                    items[0].Title != "Title" || items[0].Genre != "House" || items[0].Fingerprint != fields[23] ||
                    items[0].MetadataVectorsJson != fields[25])
                    throw new InvalidOperationException("Bridge schema-v3 row projection self-test failed.");
            }
            finally
            {
                try { Directory.Delete(directory, true); } catch { }
            }
        }

        private static BridgeSnapshotStatus ParseStateBytes(byte[] bytes, string directory)
        {
            BridgeSnapshotStatus s = new BridgeSnapshotStatus();
            s.DirectoryPath = directory; s.Present = true;
            string[] lines = Encoding.UTF8.GetString(bytes).Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None);
            Dictionary<string,string> values = new Dictionary<string,string>(StringComparer.Ordinal);
            for (int i=0;i<lines.Length;i++)
            {
                if (String.IsNullOrWhiteSpace(lines[i])) continue;
                int tab=lines[i].IndexOf('\t');
                if (tab<=0 || tab != lines[i].LastIndexOf('\t')) throw new InvalidDataException("bridge-state.tsv: ungültige Zeile " + (i+1) + ".");
                string key=lines[i].Substring(0,tab), value=lines[i].Substring(tab+1);
                if (values.ContainsKey(key)) throw new InvalidDataException("bridge-state.tsv: doppelter Schlüssel " + key + ".");
                values.Add(key,value);
            }
            string[] required=new string[]{"schema_version","generation","complete","item_count","last_change_utc"};
            foreach(string key in required) if(!values.ContainsKey(key)) throw new InvalidDataException("bridge-state.tsv: Pflichtfeld fehlt: " + key + ".");
            int schema; if(!Int32.TryParse(values["schema_version"],NumberStyles.Integer,CultureInfo.InvariantCulture,out schema)) throw new InvalidDataException("bridge-state.tsv: schema_version ungültig.");
            s.SchemaVersion = schema;
            s.Compatible = schema == SchemaVersion || schema == SchemaVersionV2 || schema == SchemaVersionV3;
            if(!s.Compatible){s.Error="Schema "+schema+" wird nicht unterstützt (erwartet v1, v2 oder v3)."; return s;}
            long generation; if(!Int64.TryParse(values["generation"],NumberStyles.Integer,CultureInfo.InvariantCulture,out generation) || generation<=0) throw new InvalidDataException("Bridge generation fehlt oder ist ungültig."); s.Generation=generation;
            if(values["complete"]!="0" && values["complete"]!="1") throw new InvalidDataException("bridge-state.tsv: complete muss 0 oder 1 sein."); s.Complete=values["complete"]=="1";
            int count; if(!Int32.TryParse(values["item_count"],NumberStyles.Integer,CultureInfo.InvariantCulture,out count) || count<0) throw new InvalidDataException("Bridge item_count ist ungültig."); s.DeclaredItemCount=count;
            DateTime utc; if(!DateTime.TryParse(values["last_change_utc"],CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out utc)) throw new InvalidDataException("bridge-state.tsv: last_change_utc ist ungültig."); s.LastChangeUtc=values["last_change_utc"];
            string v;
            if(values.TryGetValue("source_id",out v)) s.SourceId=v;
            if(values.TryGetValue("source_name",out v)) s.SourceName=v;
            if(values.TryGetValue("profile_path",out v)) s.ProfilePath=v;
            if(values.TryGetValue("producer_version",out v)) s.ProducerVersion=v;
            if(values.TryGetValue("producer_pid",out v)) s.ProducerPid=v;
            return s;
        }

        private static int ColumnCountForSchema(int schema)
        {
            if (schema == SchemaVersion) return ItemColumnCount;
            if (schema == SchemaVersionV2) return ItemColumnCountV2;
            if (schema == SchemaVersionV3) return ItemColumnCountV3;
            throw new InvalidDataException("Nicht unterstütztes Bridge-Schema " + schema + ".");
        }

        private static void ValidateHeader(string header, int schema)
        {
            string expected = schema == SchemaVersion ? ItemHeader : schema == SchemaVersionV2 ? ItemHeaderV2 : schema == SchemaVersionV3 ? ItemHeaderV3 : null;
            if (expected == null || !String.Equals(header, expected, StringComparison.Ordinal))
                throw new InvalidDataException("Ungültiger Bridge-Snapshot-Header für Schema v" + schema + ".");
        }

        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (Object.ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static int VerifyItems(string path, int schema)
        {
            int count = 0;
            int expectedColumns = ColumnCountForSchema(schema);
            using (FileStream fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (GZipStream gz = new GZipStream(fs, CompressionMode.Decompress))
            using (StreamReader sr = new StreamReader(gz, new UTF8Encoding(false), true, 65536))
            {
                string header = sr.ReadLine();
                ValidateHeader(header, schema);
                string line;
                int lineNo = 1;
                while ((line = sr.ReadLine()) != null)
                {
                    lineNo++;
                    if (line.Split('\t').Length != expectedColumns) throw new InvalidDataException("digital-items.tsv.gz: Zeile " + lineNo + " hat nicht " + expectedColumns + " Spalten.");
                    count++;
                }
            }
            return count;
        }
    }
}
