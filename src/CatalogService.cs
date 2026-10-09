using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Web.Script.Serialization;

namespace DJLibrary
{
    public sealed partial class CatalogService : IDisposable
    {
        public const int CurrentSchemaVersion = 4;
        private readonly string _path;
        private WinSqliteDb _db;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();

        public string DatabasePath { get { return _path; } }
        public string RecoveryBackupPath { get { return _path + ".bak"; } }

        private CatalogService(string path)
        {
            _path = path;
            OpenDatabase();
        }

        public static string DefaultCatalogPath
        {
            get { return Path.Combine(SettingsManager.SettingsDirectory, "catalog-v0.4.sqlite"); }
        }

        public static CatalogService EnsureInitialized(string seedGzipPath)
        {
            string path = DefaultCatalogPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (!File.Exists(path))
            {
                if (String.IsNullOrWhiteSpace(seedGzipPath))
                    throw new FileNotFoundException("Kein persönlicher DJ-Library-Katalog vorhanden. Bitte einen bestehenden SQLite-Katalog ausdrücklich importieren.", path);
                InstallSeed(seedGzipPath, path);
            }

            try
            {
                CatalogService service = new CatalogService(path);
                service.ValidateAndMigrate();
                if (!File.Exists(service.RecoveryBackupPath)) service.Backup(service.RecoveryBackupPath);
                return service;
            }
            catch
            {
                if (TryRecover(path))
                {
                    CatalogService recovered = new CatalogService(path);
                    recovered.ValidateAndMigrate();
                    return recovered;
                }
                throw;
            }
        }

        // A clean public install creates an empty schema-v4 catalog on demand.
        // It never imports CI fixtures or touches existing personal data.
        public static void CreateEmptyCatalog()
        {
            string destination = DefaultCatalogPath;
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            if (File.Exists(destination))
                throw new IOException("Ein persönlicher DJ-Library-Katalog existiert bereits.");

            string staged = destination + ".empty-" + Guid.NewGuid().ToString("N");
            string ready = staged + ".ready";
            try
            {
                using (WinSqliteDb db = new WinSqliteDb(staged))
                {
                    db.Execute("PRAGMA foreign_keys=ON");
                    db.Execute("CREATE TABLE meta(key TEXT PRIMARY KEY, value TEXT NOT NULL)");
                    db.Execute("CREATE TABLE release(id INTEGER PRIMARY KEY, album_artist TEXT NOT NULL DEFAULT '', album TEXT NOT NULL DEFAULT '', release_date TEXT NOT NULL DEFAULT '', genre TEXT NOT NULL DEFAULT '', label TEXT NOT NULL DEFAULT '', catalog TEXT NOT NULL DEFAULT '', country TEXT NOT NULL DEFAULT '', total_discs INTEGER NOT NULL DEFAULT 1 CHECK(total_discs>0))");
                    db.Execute("CREATE TABLE disc(id INTEGER PRIMARY KEY, release_id INTEGER NOT NULL REFERENCES release(id) ON DELETE CASCADE, legacy_album_id INTEGER, disc_number INTEGER NOT NULL DEFAULT 1 CHECK(disc_number>0), medium TEXT NOT NULL DEFAULT '', toc TEXT NOT NULL DEFAULT '', toc_complete INTEGER NOT NULL DEFAULT 0 CHECK(toc_complete IN(0,1)), cd_text_present INTEGER NOT NULL DEFAULT 0 CHECK(cd_text_present IN(0,1)), cd_text_status TEXT NOT NULL DEFAULT 'legacy_unknown', duration_seconds REAL NOT NULL DEFAULT 0 CHECK(duration_seconds>=0), legacy_json TEXT NOT NULL DEFAULT '{}')");
                    db.Execute("CREATE TABLE track(id INTEGER PRIMARY KEY, disc_id INTEGER NOT NULL REFERENCES disc(id) ON DELETE CASCADE, position INTEGER NOT NULL CHECK(position>0), artist TEXT NOT NULL DEFAULT '', title TEXT NOT NULL DEFAULT '', version TEXT NOT NULL DEFAULT '', release_date TEXT NOT NULL DEFAULT '', genre TEXT NOT NULL DEFAULT '', legacy_genre TEXT NOT NULL DEFAULT '', bpm REAL NOT NULL DEFAULT 0 CHECK(bpm>=0), duration_seconds REAL NOT NULL DEFAULT 0 CHECK(duration_seconds>=0), legacy_artist_raw TEXT NOT NULL DEFAULT '', legacy_json TEXT NOT NULL DEFAULT '{}', UNIQUE(disc_id,position))");
                    db.Execute("CREATE TABLE change_log(id INTEGER PRIMARY KEY AUTOINCREMENT, changed_utc TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, entity TEXT NOT NULL, entity_id INTEGER, operation TEXT NOT NULL, before_json TEXT, after_json TEXT)");
                    db.Execute("CREATE TABLE undo_entry(id INTEGER PRIMARY KEY AUTOINCREMENT, changed_utc TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, tx_group TEXT NOT NULL, entity TEXT NOT NULL, entity_id INTEGER, operation TEXT NOT NULL, before_json TEXT, after_json TEXT, undone INTEGER NOT NULL DEFAULT 0 CHECK(undone IN(0,1)))");
                    db.Execute("CREATE INDEX ix_disc_release ON disc(release_id)");
                    db.Execute("CREATE INDEX ix_track_artist_title ON track(artist,title)");
                    db.Execute("CREATE INDEX ix_undo_group ON undo_entry(tx_group,id)");
                    db.Execute("INSERT INTO meta(key,value) VALUES('schema_version','4')");
                    db.Execute("INSERT INTO meta(key,value) VALUES('product_target','0.4')");
                    db.Execute("INSERT INTO meta(key,value) VALUES('legacy_source_policy','read-only')");
                    db.Execute("INSERT INTO meta(key,value) VALUES('cdx_marker_cleanup_count','0')");
                }

                using (CatalogService validated = OpenForTesting(staged))
                {
                    CatalogCounts totals = validated.GetCounts();
                    if (totals.Releases != 0 || totals.Discs != 0 || totals.Tracks != 0 ||
                        validated.ForeignKeyViolationCount() != 0 ||
                        !String.Equals(validated.QuickCheck(), "ok", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Die neu erstellte leere Datenbank ist nicht gültig.");
                    validated.Backup(ready);
                }
                using (CatalogService check = OpenForTesting(ready))
                {
                    if (check.SchemaVersion != CurrentSchemaVersion || check.ForeignKeyViolationCount() != 0)
                        throw new InvalidDataException("Der leere Katalog konnte nicht qualifiziert werden.");
                }
                if (File.Exists(destination))
                    throw new IOException("Zwischenzeitlich wurde ein persönlicher Katalog angelegt. Erstellung abgebrochen.");
                File.Move(ready, destination);
            }
            finally
            {
                foreach (string file in new[] { staged, staged + ".bak", ready, ready + ".bak" })
                {
                    try { if (File.Exists(file)) File.Delete(file); } catch { }
                    DeleteSidecars(file);
                }
            }
        }

        // Public-source runtime: an existing user-owned catalog remains authoritative.
        // Import is explicit, staged, schema-checked and never edits the source backup.
        // A packaged sample catalog must not be installed implicitly.
        public static void ImportExistingCatalog(string sourcePath)
        {
            if (String.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                throw new FileNotFoundException("Wähle eine vorhandene DJ-Library-SQLite-Datenbank oder eine DJ-Library-Sicherung.", sourcePath);
            string destination = DefaultCatalogPath;
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            if (File.Exists(destination))
                throw new IOException("Ein persönlicher Katalog existiert bereits. Der Import würde ihn ersetzen und wurde abgebrochen.");
            if (String.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Quell- und Zielkatalog dürfen nicht identisch sein.");
            if (File.Exists(sourcePath + "-wal") || File.Exists(sourcePath + "-shm"))
                throw new IOException("Die gewählte Datenbank enthält aktive SQLite-Journaldateien. Erstelle zuerst eine DJ-Library-Sicherung und importiere diese.");

            string staged = destination + ".import-" + Guid.NewGuid().ToString("N");
            string ready = staged + ".ready";
            try
            {
                File.Copy(sourcePath, staged, false);
                using (CatalogService verified = OpenForTesting(staged))
                {
                    if (verified.ForeignKeyViolationCount() != 0 || !String.Equals(verified.QuickCheck(), "ok", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Der ausgewählte Katalog besteht die Integritätsprüfung nicht.");
                    verified.Backup(ready);
                }
                using (CatalogService check = OpenForTesting(ready))
                {
                    if (check.SchemaVersion != CurrentSchemaVersion || check.ForeignKeyViolationCount() != 0)
                        throw new InvalidDataException("Der importierte Katalog hat kein gültiges Schema.");
                }
                if (File.Exists(destination))
                    throw new IOException("Der persönliche Katalog wurde inzwischen angelegt. Import sicher abgebrochen.");
                File.Move(ready, destination);
            }
            finally
            {
                foreach (string file in new[] { staged, staged + ".bak", staged + ".pre-v2.bak",
                    staged + ".pre-v3.bak", staged + ".pre-v4.bak", ready, ready + ".bak" })
                {
                    try { if (File.Exists(file)) File.Delete(file); } catch { }
                    DeleteSidecars(file);
                }
            }
        }

        public static CatalogService OpenForTesting(string path)
        {
            CatalogService service = new CatalogService(path);
            service.ValidateAndMigrate();
            return service;
        }

        public static CatalogService OpenForTestingWithRecovery(string path)
        {
            try
            {
                CatalogService service = new CatalogService(path);
                service.ValidateAndMigrate();
                return service;
            }
            catch
            {
                if (!TryRecover(path)) throw;
                CatalogService recovered = new CatalogService(path);
                recovered.ValidateAndMigrate();
                return recovered;
            }
        }

        private static void InstallSeed(string seedGzipPath, string destination)
        {
            if (!File.Exists(seedGzipPath)) throw new FileNotFoundException("v0.4 Catalog-Seed fehlt.", seedGzipPath);
            string temp = destination + ".seed.tmp";
            if (File.Exists(temp)) File.Delete(temp);
            using (FileStream input = File.OpenRead(seedGzipPath))
            using (GZipStream gzip = new GZipStream(input, CompressionMode.Decompress))
            using (FileStream output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                gzip.CopyTo(output);
                output.Flush(true);
            }
            using (WinSqliteDb check = new WinSqliteDb(temp))
            {
                if (!String.Equals(check.QuickCheck(), "ok", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Der v0.4 Catalog-Seed besteht quick_check nicht.");
            }
            if (File.Exists(destination)) throw new IOException("Catalogziel wurde während der Initialisierung angelegt: " + destination);
            File.Move(temp, destination);
        }

        private static bool TryRecover(string path)
        {
            string backup = path + ".bak";
            if (!File.Exists(backup)) return false;
            try
            {
                using (WinSqliteDb check = new WinSqliteDb(backup))
                {
                    if (!String.Equals(check.QuickCheck(), "ok", StringComparison.OrdinalIgnoreCase)) return false;
                }
                string corrupt = path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                if (File.Exists(path)) File.Move(path, corrupt);
                DeleteSidecars(path);
                File.Copy(backup, path, false);
                return true;
            }
            catch { return false; }
        }

        private static void DeleteSidecars(string path)
        {
            try { if (File.Exists(path + "-wal")) File.Delete(path + "-wal"); } catch { }
            try { if (File.Exists(path + "-shm")) File.Delete(path + "-shm"); } catch { }
        }

        private void OpenDatabase()
        {
            _db = new WinSqliteDb(_path);
            try
            {
                _db.Execute("PRAGMA foreign_keys=ON");
                _db.Execute("PRAGMA journal_mode=WAL");
                _db.Execute("PRAGMA synchronous=FULL");
                _db.Execute("PRAGMA wal_autocheckpoint=1000");
            }
            catch
            {
                _db.Dispose();
                _db = null;
                throw;
            }
        }

        public void Dispose()
        {
            if (_db != null) { _db.Dispose(); _db = null; }
        }

        private void ValidateAndMigrate()
        {
            if (!String.Equals(_db.QuickCheck(), "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Catalog quick_check failed.");

            object value = _db.Scalar("SELECT value FROM meta WHERE key='schema_version'");
            int version;
            if (value == null || !Int32.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out version))
                throw new InvalidDataException("Catalog schema_version fehlt oder ist invalid.");

            if (version == 1)
            {
                string premigration = _path + ".pre-v2.bak";
                if (!File.Exists(premigration)) Backup(premigration);
                _db.Transaction(delegate
                {
                    _db.Execute("CREATE TABLE IF NOT EXISTS undo_entry(id INTEGER PRIMARY KEY AUTOINCREMENT, changed_utc TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, tx_group TEXT NOT NULL, entity TEXT NOT NULL, entity_id INTEGER, operation TEXT NOT NULL, before_json TEXT, after_json TEXT, undone INTEGER NOT NULL DEFAULT 0 CHECK(undone IN(0,1)))");
                    _db.Execute("CREATE INDEX IF NOT EXISTS ix_undo_group ON undo_entry(tx_group,id)");
                    _db.Execute("UPDATE meta SET value='2' WHERE key='schema_version'");
                });
                version = 2;
            }
            if (version == 2)
            {
                string premigration = _path + ".pre-v3.bak";
                if (!File.Exists(premigration)) Backup(premigration);
                _db.Transaction(delegate
                {
                    _db.Execute("ALTER TABLE disc ADD COLUMN cd_text_status TEXT NOT NULL DEFAULT 'legacy_unknown'");
                    _db.Execute("UPDATE disc SET cd_text_status=CASE WHEN cd_text_present=1 THEN 'legacy_present' ELSE 'legacy_unknown' END");
                    _db.Execute("UPDATE meta SET value='3' WHERE key='schema_version'");
                });
                version = 3;
            }
            if (version == 3)
            {
                string premigration = _path + ".pre-v4.bak";
                if (!File.Exists(premigration)) Backup(premigration);
                _db.Transaction(delegate
                {
                    int cleaned = 0;
                    List<Dictionary<string, object>> releases = _db.Query("SELECT id,album FROM release ORDER BY id");
                    foreach (Dictionary<string, object> release in releases)
                    {
                        string album = ToStringValue(release["album"]);
                        if (!CdxCompatibility.HasHistoricalMarker(album)) continue;
                        long releaseId = ToLong(release["id"]);
                        bool confirmedIncompatible = false;
                        foreach (Dictionary<string, object> disc in _db.Query("SELECT toc,toc_complete FROM disc WHERE release_id=? ORDER BY disc_number,id", releaseId))
                        {
                            if (CdxCompatibility.Classify(ToStringValue(disc["toc"]), ToLong(disc["toc_complete"]) != 0) == CdxCompatibilityState.Incompatible)
                            {
                                confirmedIncompatible = true;
                                break;
                            }
                        }
                        if (!confirmedIncompatible) continue;
                        _db.Execute("UPDATE release SET album=? WHERE id=?", CdxCompatibility.RemoveHistoricalMarker(album), releaseId);
                        cleaned++;
                    }
                    _db.Execute("INSERT OR REPLACE INTO meta(key,value) VALUES('cdx_marker_cleanup_count',?)", cleaned.ToString(CultureInfo.InvariantCulture));
                    _db.Execute("UPDATE meta SET value='4' WHERE key='schema_version'");
                });
                version = 4;
            }
            if (version != CurrentSchemaVersion)
                throw new InvalidDataException("Nicht unterstützte Catalog-Schemaversion " + version + ".");

            object fk = _db.Scalar("SELECT COUNT(*) FROM pragma_foreign_key_check");
            if (ToLong(fk) != 0) throw new InvalidDataException("Catalog foreign_key_check meldet Verstöße.");
        }

        public int SchemaVersion
        {
            get { return Convert.ToInt32(_db.Scalar("SELECT value FROM meta WHERE key='schema_version'"), CultureInfo.InvariantCulture); }
        }

        public int CdxMarkerCleanupCount
        {
            get
            {
                object value = _db.Scalar("SELECT value FROM meta WHERE key='cdx_marker_cleanup_count'");
                int count;
                return value != null && Int32.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out count) ? count : 0;
            }
        }

        public CatalogCounts GetCounts()
        {
            CatalogCounts c = new CatalogCounts();
            c.Releases = ToLong(_db.Scalar("SELECT COUNT(*) FROM release"));
            c.Discs = ToLong(_db.Scalar("SELECT COUNT(*) FROM disc"));
            c.Tracks = ToLong(_db.Scalar("SELECT COUNT(*) FROM track"));
            return c;
        }

        public string QuickCheck()
        {
            return _db.QuickCheck();
        }

        public long ForeignKeyViolationCount()
        {
            return ToLong(_db.Scalar("SELECT COUNT(*) FROM pragma_foreign_key_check"));
        }

        public long CountDiscsByCdTextStatus(string status)
        {
            return ToLong(_db.Scalar("SELECT COUNT(*) FROM disc WHERE cd_text_status=?", status ?? ""));
        }

        public long CountDiscsByCdxCompatibility(CdxCompatibilityState state)
        {
            long count = 0;
            foreach (Dictionary<string, object> row in _db.Query("SELECT toc,toc_complete FROM disc"))
                if (CdxCompatibility.Classify(ToStringValue(row["toc"]), ToLong(row["toc_complete"]) != 0) == state) count++;
            return count;
        }

        public List<CatalogRelease> GetReleases()
        {
            return _db.Query("SELECT id,album_artist,album,release_date,genre,label,catalog,country,total_discs FROM release ORDER BY album_artist COLLATE NOCASE,album COLLATE NOCASE,id")
                .Select(MapRelease).ToList();
        }

        public CatalogRelease GetRelease(long id)
        {
            List<Dictionary<string, object>> rows = _db.Query("SELECT id,album_artist,album,release_date,genre,label,catalog,country,total_discs FROM release WHERE id=?", id);
            return rows.Count == 0 ? null : MapRelease(rows[0]);
        }

        public List<CatalogDisc> GetDiscs(long releaseId)
        {
            return _db.Query("SELECT id,release_id,legacy_album_id,disc_number,medium,toc,toc_complete,cd_text_present,cd_text_status,duration_seconds,legacy_json FROM disc WHERE release_id=? ORDER BY disc_number,id", releaseId)
                .Select(MapDisc).ToList();
        }

        public CatalogDisc GetDisc(long id)
        {
            List<Dictionary<string, object>> rows = _db.Query("SELECT id,release_id,legacy_album_id,disc_number,medium,toc,toc_complete,cd_text_present,cd_text_status,duration_seconds,legacy_json FROM disc WHERE id=?", id);
            return rows.Count == 0 ? null : MapDisc(rows[0]);
        }

        public List<CatalogTrack> GetTracks(long discId)
        {
            return _db.Query("SELECT id,disc_id,position,artist,title,version,release_date,genre,legacy_genre,bpm,duration_seconds,legacy_artist_raw,legacy_json FROM track WHERE disc_id=? ORDER BY position,id", discId)
                .Select(MapTrack).ToList();
        }

        public CatalogTrack GetTrack(long id)
        {
            List<Dictionary<string, object>> rows = _db.Query("SELECT id,disc_id,position,artist,title,version,release_date,genre,legacy_genre,bpm,duration_seconds,legacy_artist_raw,legacy_json FROM track WHERE id=?", id);
            return rows.Count == 0 ? null : MapTrack(rows[0]);
        }

        public List<CatalogTocMatch> FindByToc(string toc)
        {
            if (String.IsNullOrWhiteSpace(toc)) return new List<CatalogTocMatch>();
            return _db.Query("SELECT r.id AS release_id,d.id AS disc_id,d.disc_number,r.album_artist,r.album FROM disc d JOIN release r ON r.id=d.release_id WHERE d.toc_complete=1 AND d.toc=? ORDER BY r.id,d.disc_number,d.id", toc.Trim())
                .Select(delegate(Dictionary<string, object> row)
                {
                    return new CatalogTocMatch
                    {
                        ReleaseId = ToLong(row["release_id"]),
                        DiscId = ToLong(row["disc_id"]),
                        DiscNumber = (int)ToLong(row["disc_number"]),
                        AlbumArtist = ToStringValue(row["album_artist"]),
                        Album = ToStringValue(row["album"])
                    };
                }).ToList();
        }

        public List<CatalogSearchDocument> GetSearchDocuments()
        {
            return _db.Query(
                "SELECT r.id AS release_id,d.id AS disc_id,t.id AS track_id," +
                "r.album_artist,r.album,r.release_date AS release_release_date,r.genre AS release_genre,r.label,r.catalog,r.country," +
                "d.disc_number,d.medium,d.toc," +
                "t.artist,t.title,t.version,t.release_date AS track_release_date,t.genre AS track_genre,t.legacy_genre,t.legacy_artist_raw " +
                "FROM release r LEFT JOIN disc d ON d.release_id=r.id LEFT JOIN track t ON t.disc_id=d.id")
                .Select(delegate(Dictionary<string, object> r)
                {
                    CatalogSearchDocument d = new CatalogSearchDocument();
                    d.ReleaseId = ToLong(r["release_id"]);
                    d.DiscId = ToLong(r["disc_id"]);
                    d.TrackId = ToLong(r["track_id"]);
                    d.SearchText = String.Join(" ", new string[]
                    {
                        ToStringValue(r["album_artist"]), ToStringValue(r["album"]), ToStringValue(r["release_release_date"]),
                        ToStringValue(r["release_genre"]), ToStringValue(r["label"]), ToStringValue(r["catalog"]), ToStringValue(r["country"]),
                        ToStringValue(r["disc_number"]), ToStringValue(r["medium"]), ToStringValue(r["toc"]),
                        ToStringValue(r["artist"]), ToStringValue(r["title"]), ToStringValue(r["version"]),
                        ToStringValue(r["track_release_date"]), ToStringValue(r["track_genre"]),
                        ToStringValue(r["legacy_genre"]), ToStringValue(r["legacy_artist_raw"])
                    });
                    return d;
                }).ToList();
        }

        public static CatalogSearchResult SearchDocuments(IList<CatalogSearchDocument> documents, string query)
        {
            CatalogSearchResult result = new CatalogSearchResult();
            if (documents == null || String.IsNullOrWhiteSpace(query)) return result;
            foreach (CatalogSearchDocument d in documents)
            {
                if (!UiHelpers.ContainsAllTerms(d.SearchText, query)) continue;
                if (d.ReleaseId > 0) result.ReleaseIds.Add(d.ReleaseId);
                if (d.DiscId > 0) result.DiscIds.Add(d.DiscId);
                if (d.TrackId > 0) result.TrackIds.Add(d.TrackId);
            }
            return result;
        }

        public HashSet<long> FindReleaseIds(string query)
        {
            return SearchIds(
                "SELECT DISTINCT r.id FROM release r LEFT JOIN disc d ON d.release_id=r.id LEFT JOIN track t ON t.disc_id=d.id " +
                "WHERE (r.album_artist||' '||r.album||' '||r.release_date||' '||r.genre||' '||r.label||' '||r.catalog||' '||r.country||' '||" +
                "COALESCE(d.medium,'')||' '||COALESCE(d.toc,'')||' '||COALESCE(t.artist,'')||' '||COALESCE(t.title,'')||' '||" +
                "COALESCE(t.version,'')||' '||COALESCE(t.release_date,'')||' '||COALESCE(t.genre,'')||' '||COALESCE(t.legacy_genre,'')) LIKE ? ESCAPE '\\'",
                query);
        }

        public HashSet<long> FindDiscIds(long releaseId, string query)
        {
            return SearchIds(
                "SELECT DISTINCT d.id FROM disc d JOIN release r ON r.id=d.release_id LEFT JOIN track t ON t.disc_id=d.id " +
                "WHERE d.release_id=" + releaseId.ToString(CultureInfo.InvariantCulture) + " AND (r.album_artist||' '||r.album||' '||r.release_date||' '||r.genre||' '||" +
                "r.label||' '||r.catalog||' '||r.country||' '||d.medium||' '||d.toc||' '||COALESCE(t.artist,'')||' '||COALESCE(t.title,'')||' '||" +
                "COALESCE(t.version,'')||' '||COALESCE(t.release_date,'')||' '||COALESCE(t.genre,'')||' '||COALESCE(t.legacy_genre,'')) LIKE ? ESCAPE '\\'",
                query);
        }

        public HashSet<long> FindTrackIds(long discId, string query)
        {
            return SearchIds(
                "SELECT DISTINCT t.id FROM track t JOIN disc d ON d.id=t.disc_id JOIN release r ON r.id=d.release_id " +
                "WHERE t.disc_id=" + discId.ToString(CultureInfo.InvariantCulture) + " AND (r.album_artist||' '||r.album||' '||r.release_date||' '||r.genre||' '||" +
                "r.label||' '||r.catalog||' '||r.country||' '||d.medium||' '||d.toc||' '||t.artist||' '||t.title||' '||t.version||' '||t.release_date||' '||" +
                "t.genre||' '||t.legacy_genre||' '||t.legacy_artist_raw) LIKE ? ESCAPE '\\'",
                query);
        }

        private HashSet<long> SearchIds(string sql, string query)
        {
            string[] terms = (query ?? "").Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            HashSet<long> result = null;
            foreach (string term in terms)
            {
                string pattern = "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
                HashSet<long> current = new HashSet<long>(_db.Query(sql, pattern).Select(x => ToLong(x["id"])));
                if (result == null) result = current;
                else result.IntersectWith(current);
                if (result.Count == 0) break;
            }
            return result ?? new HashSet<long>();
        }

        public long CreateRelease(CatalogRelease r)
        {
            if (r == null) throw new ArgumentNullException("r");
            string group = Group("create-release");
            long id = 0;
            _db.Transaction(delegate
            {
                _db.Execute("INSERT INTO release(album_artist,album,release_date,genre,label,catalog,country,total_discs) VALUES(?,?,?,?,?,?,?,?)",
                    Nz(r.AlbumArtist), Nz(r.Album), Nz(r.ReleaseDate), Nz(r.Genre), Nz(r.Label), Nz(r.Catalog), Nz(r.Country), Math.Max(1, r.TotalDiscs));
                id = _db.LastInsertRowId;
                Log(group, "release", id, "create", null, Snapshot("release", id));
            });
            RefreshRecoveryBackup();
            return id;
        }

        public void UpdateRelease(CatalogRelease r)
        {
            if (r == null || r.Id <= 0) throw new ArgumentException("Release-ID fehlt.");
            Dictionary<string, object> before = Snapshot("release", r.Id);
            if (before == null) throw new KeyNotFoundException("Release " + r.Id + " fehlt.");
            string group = Group("update-release");
            _db.Transaction(delegate
            {
                _db.Execute("UPDATE release SET album_artist=?,album=?,release_date=?,genre=?,label=?,catalog=?,country=?,total_discs=? WHERE id=?",
                    Nz(r.AlbumArtist), Nz(r.Album), Nz(r.ReleaseDate), Nz(r.Genre), Nz(r.Label), Nz(r.Catalog), Nz(r.Country), Math.Max(1, r.TotalDiscs), r.Id);
                Log(group, "release", r.Id, "update", before, Snapshot("release", r.Id));
            });
            RefreshRecoveryBackup();
        }

        public long AddDisc(CatalogDisc d)
        {
            if (d == null) throw new ArgumentNullException("d");
            if (GetRelease(d.ReleaseId) == null) throw new KeyNotFoundException("Release fehlt.");
            string group = Group("create-disc");
            long id = 0;
            Dictionary<string, object> releaseBefore = Snapshot("release", d.ReleaseId);
            _db.Transaction(delegate
            {
                _db.Execute("INSERT INTO disc(release_id,legacy_album_id,disc_number,medium,toc,toc_complete,cd_text_present,cd_text_status,duration_seconds,legacy_json) VALUES(?,?,?,?,?,?,?,?,?,?)",
                    d.ReleaseId, d.LegacyAlbumId.HasValue ? (object)d.LegacyAlbumId.Value : null, Math.Max(1, d.DiscNumber), Nz(d.Medium), Nz(d.Toc), d.TocComplete, d.CdTextPresent, NormalizeCdTextStatus(d.CdTextStatus, d.CdTextPresent), Math.Max(0, d.DurationSeconds), NzJson(d.LegacyJson));
                id = _db.LastInsertRowId;
                _db.Execute("UPDATE release SET total_discs=CASE WHEN total_discs<? THEN ? ELSE total_discs END WHERE id=?",
                    Math.Max(1, d.DiscNumber), Math.Max(1, d.DiscNumber), d.ReleaseId);
                Dictionary<string, object> releaseAfter = Snapshot("release", d.ReleaseId);
                if (ToLong(releaseBefore["total_discs"]) != ToLong(releaseAfter["total_discs"]))
                    Log(group, "release", d.ReleaseId, "update", releaseBefore, releaseAfter);
                Log(group, "disc", id, "create", null, Snapshot("disc", id));
            });
            RefreshRecoveryBackup();
            return id;
        }

        public void UpdateDisc(CatalogDisc d)
        {
            if (d == null || d.Id <= 0) throw new ArgumentException("Disc-ID fehlt.");
            Dictionary<string, object> before = Snapshot("disc", d.Id);
            if (before == null) throw new KeyNotFoundException("Disc " + d.Id + " fehlt.");
            string group = Group("update-disc");
            _db.Transaction(delegate
            {
                _db.Execute("UPDATE disc SET disc_number=?,medium=?,toc=?,toc_complete=?,cd_text_present=?,cd_text_status=?,duration_seconds=? WHERE id=?",
                    Math.Max(1, d.DiscNumber), Nz(d.Medium), Nz(d.Toc), d.TocComplete, d.CdTextPresent, NormalizeCdTextStatus(d.CdTextStatus, d.CdTextPresent), Math.Max(0, d.DurationSeconds), d.Id);
                Log(group, "disc", d.Id, "update", before, Snapshot("disc", d.Id));
            });
            RefreshRecoveryBackup();
        }

        public long AddTrack(CatalogTrack t)
        {
            if (t == null) throw new ArgumentNullException("t");
            if (GetDisc(t.DiscId) == null) throw new KeyNotFoundException("Disc fehlt.");
            string group = Group("create-track");
            long id = 0;
            _db.Transaction(delegate
            {
                _db.Execute("INSERT INTO track(disc_id,position,artist,title,version,release_date,genre,legacy_genre,bpm,duration_seconds,legacy_artist_raw,legacy_json) VALUES(?,?,?,?,?,?,?,?,?,?,?,?)",
                    t.DiscId, Math.Max(1, t.Position), Nz(t.Artist), Nz(t.Title), Nz(t.Version), Nz(t.ReleaseDate), Nz(t.Genre), Nz(t.LegacyGenre), Math.Max(0, t.Bpm), Math.Max(0, t.DurationSeconds), Nz(t.LegacyArtistRaw), NzJson(t.LegacyJson));
                id = _db.LastInsertRowId;
                Log(group, "track", id, "create", null, Snapshot("track", id));
            });
            RefreshRecoveryBackup();
            return id;
        }

        public void UpdateTrack(CatalogTrack t)
        {
            if (t == null || t.Id <= 0) throw new ArgumentException("Track-ID fehlt.");
            Dictionary<string, object> before = Snapshot("track", t.Id);
            if (before == null) throw new KeyNotFoundException("Track " + t.Id + " fehlt.");
            string group = Group("update-track");
            _db.Transaction(delegate
            {
                _db.Execute("UPDATE track SET position=?,artist=?,title=?,version=?,release_date=?,genre=?,bpm=?,duration_seconds=? WHERE id=?",
                    Math.Max(1, t.Position), Nz(t.Artist), Nz(t.Title), Nz(t.Version), Nz(t.ReleaseDate), Nz(t.Genre), Math.Max(0, t.Bpm), Math.Max(0, t.DurationSeconds), t.Id);
                Log(group, "track", t.Id, "update", before, Snapshot("track", t.Id));
            });
            RefreshRecoveryBackup();
        }

        public void ReorderTracks(long discId, IList<long> orderedIds)
        {
            List<CatalogTrack> existing = GetTracks(discId);
            List<long> current = existing.Select(delegate(CatalogTrack x) { return x.Id; }).ToList();
            if (orderedIds == null || current.Count != orderedIds.Count || current.OrderBy(x => x).SequenceEqual(orderedIds.OrderBy(x => x)) == false)
                throw new ArgumentException("orderedIds muss jeden Disc-Track exakt einmal enthalten.");
            string group = Group("reorder");
            Dictionary<string, object> before = new Dictionary<string, object>(); before["order"] = current.ToArray();
            Dictionary<string, object> after = new Dictionary<string, object>(); after["order"] = orderedIds.ToArray();
            _db.Transaction(delegate
            {
                int offset = existing.Count == 0 ? 100 : existing.Max(x => x.Position) + existing.Count + 100;
                for (int i = 0; i < orderedIds.Count; i++) _db.Execute("UPDATE track SET position=? WHERE id=?", offset + i + 1, orderedIds[i]);
                for (int i = 0; i < orderedIds.Count; i++) _db.Execute("UPDATE track SET position=? WHERE id=?", i + 1, orderedIds[i]);
                Log(group, "disc", discId, "reorder", before, after);
            });
            RefreshRecoveryBackup();
        }

        public string DeleteTrack(long id)
        {
            Dictionary<string, object> before = Snapshot("track", id);
            if (before == null) throw new KeyNotFoundException("Track fehlt.");
            string group = Group("delete-track");
            _db.Transaction(delegate { Log(group, "track", id, "delete", before, null); _db.Execute("DELETE FROM track WHERE id=?", id); });
            RefreshRecoveryBackup();
            return group;
        }

        public string DeleteDisc(long id)
        {
            Dictionary<string, object> disc = Snapshot("disc", id);
            if (disc == null) throw new KeyNotFoundException("Disc fehlt.");
            List<Dictionary<string, object>> tracks = _db.Query("SELECT * FROM track WHERE disc_id=? ORDER BY position,id", id);
            Dictionary<string, object> before = new Dictionary<string, object>(); before["disc"] = disc; before["tracks"] = tracks;
            string group = Group("delete-disc");
            _db.Transaction(delegate { Log(group, "disc", id, "delete", before, null); _db.Execute("DELETE FROM disc WHERE id=?", id); });
            RefreshRecoveryBackup();
            return group;
        }

        public string DeleteRelease(long id)
        {
            Dictionary<string, object> release = Snapshot("release", id);
            if (release == null) throw new KeyNotFoundException("Release fehlt.");
            List<Dictionary<string, object>> discs = _db.Query("SELECT * FROM disc WHERE release_id=? ORDER BY disc_number,id", id);
            List<Dictionary<string, object>> tracks = _db.Query("SELECT t.* FROM track t JOIN disc d ON d.id=t.disc_id WHERE d.release_id=? ORDER BY t.disc_id,t.position", id);
            Dictionary<string, object> before = new Dictionary<string, object>(); before["release"] = release; before["discs"] = discs; before["tracks"] = tracks;
            string group = Group("delete-release");
            _db.Transaction(delegate { Log(group, "release", id, "delete", before, null); _db.Execute("DELETE FROM release WHERE id=?", id); });
            RefreshRecoveryBackup();
            return group;
        }

        public long ImportCd(long releaseId, CdSnapshot snapshot, int discNumber, string medium)
        {
            ValidateCdSnapshot(releaseId, snapshot);
            string group = Group("cd-import");
            long discId = 0;
            Dictionary<string, object> releaseBefore = Snapshot("release", releaseId);
            _db.Transaction(delegate
            {
                double duration = snapshot.Tracks.Sum(x => x.DurationSeconds);
                Dictionary<string, object> discEvidence = new Dictionary<string, object>();
                discEvidence["musicbrainz_disc_id"] = Nz(snapshot.MusicBrainzDiscId);
                discEvidence["musicbrainz_release_id"] = Nz(snapshot.MusicBrainzReleaseId);
                discEvidence["discogs_release_id"] = Nz(snapshot.DiscogsReleaseId);
                discEvidence["cd_text_source"] = Nz(snapshot.CdTextSource);
                discEvidence["metadata_sources"] = snapshot.MetadataSources == null ? new object[0] : snapshot.MetadataSources.Select(x => (object)new Dictionary<string, object> { { "source", x.Source }, { "text", x.Text }, { "url", x.Url } }).ToArray();
                string discJson = _json.Serialize(discEvidence);
                _db.Execute("INSERT INTO disc(release_id,disc_number,medium,toc,toc_complete,cd_text_present,cd_text_status,duration_seconds,legacy_json) VALUES(?,?,?,?,1,?,?,?,?)",
                    releaseId, Math.Max(1, discNumber), Nz(medium), Nz(snapshot.Toc), snapshot.CdTextPresent, NormalizeCdTextStatus(snapshot.CdTextStatus, snapshot.CdTextPresent), duration, discJson);
                discId = _db.LastInsertRowId;
                _db.Execute("UPDATE release SET total_discs=CASE WHEN total_discs<? THEN ? ELSE total_discs END WHERE id=?",
                    Math.Max(1, discNumber), Math.Max(1, discNumber), releaseId);
                Dictionary<string, object> releaseAfter = Snapshot("release", releaseId);
                if (ToLong(releaseBefore["total_discs"]) != ToLong(releaseAfter["total_discs"]))
                    Log(group, "release", releaseId, "update", releaseBefore, releaseAfter);
                Log(group, "disc", discId, "create", null, Snapshot("disc", discId));
                foreach (CdTrackCapture ct in snapshot.Tracks)
                {
                    Dictionary<string, object> trackEvidence = new Dictionary<string, object>();
                    trackEvidence["raw_cd_text_title"] = Nz(ct.RawTitle);
                    trackEvidence["metadata_source"] = Nz(ct.MetadataSource);
                    trackEvidence["metadata_detail"] = Nz(ct.MetadataDetail);
                    trackEvidence["metadata_confidence"] = ct.MetadataConfidence;
                    string trackJson = _json.Serialize(trackEvidence);
                    _db.Execute("INSERT INTO track(disc_id,position,artist,title,version,release_date,genre,legacy_genre,bpm,duration_seconds,legacy_artist_raw,legacy_json) VALUES(?,?,?,?,?,?,?,?,?,?,?,?)",
                        discId, ct.Position, Nz(ct.Artist), Nz(ct.Title), Nz(ct.Version), "", Nz(ct.Genre), "", 0, Math.Max(0, ct.DurationSeconds), Nz(ct.Artist), trackJson);
                    long trackId = _db.LastInsertRowId;
                    Log(group, "track", trackId, "create", null, Snapshot("track", trackId));
                }
            });
            RefreshRecoveryBackup();
            return discId;
        }

        private void ValidateCdSnapshot(long releaseId, CdSnapshot snapshot)
        {
            if (GetRelease(releaseId) == null) throw new KeyNotFoundException("Release fehlt.");
            if (snapshot == null || snapshot.Tracks == null || snapshot.Tracks.Count == 0) throw new ArgumentException("CD enthält keine Tracks.");
            for (int i = 0; i < snapshot.Tracks.Count; i++)
            {
                if (snapshot.Tracks[i].Position != i + 1) throw new ArgumentException("Trackpositionen müssen lückenlos bei 1 beginnen.");
                if (snapshot.Tracks[i].DurationSeconds < 0) throw new ArgumentException("Tracklaufzeit darf nicht negativ sein.");
            }
        }

        public List<CatalogHistoryEntry> GetHistory(int limit)
        {
            return _db.Query("SELECT id,changed_utc,tx_group,entity,entity_id,operation,undone FROM undo_entry ORDER BY id DESC LIMIT ?", Math.Max(1, limit))
                .Select(delegate(Dictionary<string, object> r)
                {
                    CatalogHistoryEntry e = new CatalogHistoryEntry();
                    e.Id = ToLong(r["id"]); e.ChangedUtc = ToStringValue(r["changed_utc"]); e.TransactionGroup = ToStringValue(r["tx_group"]);
                    e.Entity = ToStringValue(r["entity"]); e.EntityId = ToLong(r["entity_id"]); e.Operation = ToStringValue(r["operation"]); e.Undone = ToLong(r["undone"]) != 0;
                    e.Redoable = e.Undone;
                    return e;
                }).ToList();
        }

        public bool CanUndo
        {
            get { return ToLong(_db.Scalar("SELECT COUNT(*) FROM undo_entry WHERE undone=0")) > 0; }
        }

        public bool CanRedo
        {
            get { return ToLong(_db.Scalar("SELECT COUNT(*) FROM undo_entry WHERE undone=1")) > 0; }
        }

        public string UndoLast()
        {
            object group = _db.Scalar("SELECT tx_group FROM undo_entry WHERE undone=0 ORDER BY id DESC LIMIT 1");
            if (group == null) throw new InvalidOperationException("Nichts rückgängig zu machen.");
            string value = Convert.ToString(group, CultureInfo.InvariantCulture);
            UndoGroup(value);
            return value;
        }

        public void UndoGroup(string group)
        {
            List<Dictionary<string, object>> entries = _db.Query("SELECT * FROM undo_entry WHERE tx_group=? AND undone=0 ORDER BY id DESC", group);
            if (entries.Count == 0) throw new InvalidOperationException("Undo-Gruppe fehlt oder wurde bereits rückgängig gemacht.");
            _db.Transaction(delegate
            {
                foreach (Dictionary<string, object> e in entries) UndoEntry(e);
                _db.Execute("UPDATE undo_entry SET undone=1 WHERE tx_group=?", group);
            });
            RefreshRecoveryBackup();
        }

        public string RedoLast()
        {
            object group = _db.Scalar("SELECT tx_group FROM undo_entry WHERE undone=1 ORDER BY id ASC LIMIT 1");
            if (group == null) throw new InvalidOperationException("Nichts zu wiederholen.");
            string value = Convert.ToString(group, CultureInfo.InvariantCulture);
            RedoGroup(value);
            return value;
        }

        public void RedoGroup(string group)
        {
            List<Dictionary<string, object>> entries = _db.Query("SELECT * FROM undo_entry WHERE tx_group=? AND undone=1 ORDER BY id ASC", group);
            if (entries.Count == 0) throw new InvalidOperationException("Redo-Gruppe fehlt oder wurde bereits wiederholt.");
            _db.Transaction(delegate
            {
                foreach (Dictionary<string, object> e in entries) RedoEntry(e);
                _db.Execute("UPDATE undo_entry SET undone=0 WHERE tx_group=?", group);
            });
            RefreshRecoveryBackup();
        }

        private void UndoEntry(Dictionary<string, object> e)
        {
            string entity = ToStringValue(e["entity"]);
            string operation = ToStringValue(e["operation"]);
            long id = ToLong(e["entity_id"]);
            string beforeJson = e["before_json"] == null ? null : ToStringValue(e["before_json"]);

            if (operation == "create")
            {
                if (entity != "release" && entity != "disc" && entity != "track") throw new InvalidOperationException("Unbekannte Undo-Entity.");
                _db.Execute("DELETE FROM " + entity + " WHERE id=?", id);
            }
            else if (operation == "update")
            {
                RestoreRow(entity, DeserializeDictionary(beforeJson), false);
            }
            else if (operation == "reorder")
            {
                Dictionary<string, object> before = DeserializeDictionary(beforeJson);
                IList order = before["order"] as IList;
                if (order == null) throw new InvalidDataException("Undo-Reihenfolge fehlt.");
                List<CatalogTrack> rows = GetTracks(id);
                int offset = rows.Count == 0 ? 100 : rows.Max(x => x.Position) + rows.Count + 100;
                for (int i = 0; i < order.Count; i++) _db.Execute("UPDATE track SET position=? WHERE id=?", offset + i + 1, Convert.ToInt64(order[i], CultureInfo.InvariantCulture));
                for (int i = 0; i < order.Count; i++) _db.Execute("UPDATE track SET position=? WHERE id=?", i + 1, Convert.ToInt64(order[i], CultureInfo.InvariantCulture));
            }
            else if (operation == "delete" && entity == "track")
            {
                RestoreRow("track", DeserializeDictionary(beforeJson), true);
            }
            else if (operation == "delete" && entity == "disc")
            {
                Dictionary<string, object> before = DeserializeDictionary(beforeJson);
                RestoreRow("disc", AsDictionary(before["disc"]), true);
                foreach (object row in AsList(before["tracks"])) RestoreRow("track", AsDictionary(row), true);
            }
            else if (operation == "delete" && entity == "release")
            {
                Dictionary<string, object> before = DeserializeDictionary(beforeJson);
                RestoreRow("release", AsDictionary(before["release"]), true);
                foreach (object row in AsList(before["discs"])) RestoreRow("disc", AsDictionary(row), true);
                foreach (object row in AsList(before["tracks"])) RestoreRow("track", AsDictionary(row), true);
            }
            else throw new InvalidOperationException("Nicht unterstützte Undo-Operation " + operation + " / " + entity);
        }

        private void RedoEntry(Dictionary<string, object> e)
        {
            string entity = ToStringValue(e["entity"]);
            string operation = ToStringValue(e["operation"]);
            long id = ToLong(e["entity_id"]);
            string afterJson = e["after_json"] == null ? null : ToStringValue(e["after_json"]);

            if (operation == "create")
            {
                RestoreRow(entity, DeserializeDictionary(afterJson), true);
            }
            else if (operation == "update")
            {
                RestoreRow(entity, DeserializeDictionary(afterJson), false);
            }
            else if (operation == "reorder")
            {
                Dictionary<string, object> after = DeserializeDictionary(afterJson);
                IList order = after["order"] as IList;
                if (order == null) throw new InvalidDataException("Redo-Reihenfolge fehlt.");
                List<CatalogTrack> rows = GetTracks(id);
                int offset = rows.Count == 0 ? 100 : rows.Max(x => x.Position) + rows.Count + 100;
                for (int i = 0; i < order.Count; i++) _db.Execute("UPDATE track SET position=? WHERE id=?", offset + i + 1, Convert.ToInt64(order[i], CultureInfo.InvariantCulture));
                for (int i = 0; i < order.Count; i++) _db.Execute("UPDATE track SET position=? WHERE id=?", i + 1, Convert.ToInt64(order[i], CultureInfo.InvariantCulture));
            }
            else if (operation == "delete")
            {
                if (entity != "release" && entity != "disc" && entity != "track") throw new InvalidOperationException("Unbekannte Redo-Entity.");
                _db.Execute("DELETE FROM " + entity + " WHERE id=?", id);
            }
            else throw new InvalidOperationException("Nicht unterstützte Redo-Operation " + operation + " / " + entity);
        }

        private void RestoreRow(string table, Dictionary<string, object> row, bool insert)
        {
            if (table == "disc" && row != null && !row.ContainsKey("cd_text_status"))
            {
                bool present = row.ContainsKey("cd_text_present") && ToLong(row["cd_text_present"]) != 0;
                row["cd_text_status"] = present ? "legacy_present" : "legacy_unknown";
            }

            string[] allowed;
            if (table == "release") allowed = new[] { "id","album_artist","album","release_date","genre","label","catalog","country","total_discs" };
            else if (table == "disc") allowed = new[] { "id","release_id","legacy_album_id","disc_number","medium","toc","toc_complete","cd_text_present","cd_text_status","duration_seconds","legacy_json" };
            else if (table == "track") allowed = new[] { "id","disc_id","position","artist","title","version","release_date","genre","legacy_genre","bpm","duration_seconds","legacy_artist_raw","legacy_json" };
            else throw new InvalidOperationException("Unbekannte Tabelle.");

            if (insert)
            {
                List<string> cols = allowed.Where(row.ContainsKey).ToList();
                object[] values = cols.Select(c => NormalizeJsonValue(row[c])).ToArray();
                string placeholders = String.Join(",", cols.Select(x => "?").ToArray());
                _db.Execute("INSERT INTO " + table + "(" + String.Join(",", cols.ToArray()) + ") VALUES(" + placeholders + ")", values);
            }
            else
            {
                long id = Convert.ToInt64(row["id"], CultureInfo.InvariantCulture);
                List<string> cols = allowed.Where(c => c != "id" && row.ContainsKey(c)).ToList();
                object[] values = cols.Select(c => NormalizeJsonValue(row[c])).Concat(new object[] { id }).ToArray();
                _db.Execute("UPDATE " + table + " SET " + String.Join(",", cols.Select(c => c + "=?").ToArray()) + " WHERE id=?", values);
            }
        }

        public void Backup(string destination)
        {
            if (String.IsNullOrWhiteSpace(destination)) throw new ArgumentException("Backup-Ziel fehlt.");
            string full = Path.GetFullPath(destination);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            string temp = full + ".tmp";
            if (File.Exists(temp)) File.Delete(temp);
            _db.BackupTo(temp);
            using (WinSqliteDb check = new WinSqliteDb(temp))
            {
                if (!String.Equals(check.QuickCheck(), "ok", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Backup quick_check failed.");
            }
            if (File.Exists(full)) File.Replace(temp, full, null, true);
            else File.Move(temp, full);
        }

        public void RefreshRecoveryBackup()
        {
            Backup(RecoveryBackupPath);
        }

        public void Restore(string source)
        {
            if (!File.Exists(source)) throw new FileNotFoundException("Backup fehlt.", source);
            using (WinSqliteDb check = new WinSqliteDb(source))
            {
                if (!String.Equals(check.QuickCheck(), "ok", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Restore-Source besteht quick_check nicht.");
            }
            if (_db != null) { _db.Dispose(); _db = null; }
            DeleteSidecars(_path);
            string temp = _path + ".restore.tmp";
            File.Copy(source, temp, true);
            if (File.Exists(_path)) File.Replace(temp, _path, _path + ".before-restore.bak", true);
            else File.Move(temp, _path);
            OpenDatabase();
            ValidateAndMigrate();
            RefreshRecoveryBackup();
        }

        private Dictionary<string, object> Snapshot(string table, long id)
        {
            if (table != "release" && table != "disc" && table != "track") throw new InvalidOperationException("Ungültige Snapshot-Tabelle.");
            List<Dictionary<string, object>> rows = _db.Query("SELECT * FROM " + table + " WHERE id=?", id);
            return rows.Count == 0 ? null : rows[0];
        }

        private void Log(string group, string entity, long id, string operation, object before, object after)
        {
            // Any new user mutation creates a new history branch; redo entries from the old branch are no longer valid.
            _db.Execute("DELETE FROM undo_entry WHERE undone=1");
            _db.Execute("INSERT INTO undo_entry(tx_group,entity,entity_id,operation,before_json,after_json) VALUES(?,?,?,?,?,?)",
                group, entity, id, operation, before == null ? null : _json.Serialize(before), after == null ? null : _json.Serialize(after));
        }

        private string Group(string prefix)
        {
            return prefix + "-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmss.fffffffZ", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N");
        }

        private Dictionary<string, object> DeserializeDictionary(string json)
        {
            object value = _json.DeserializeObject(json);
            return AsDictionary(value);
        }

        private static Dictionary<string, object> AsDictionary(object value)
        {
            Dictionary<string, object> typed = value as Dictionary<string, object>;
            if (typed != null) return typed;
            IDictionary generic = value as IDictionary;
            if (generic == null) throw new InvalidDataException("JSON-Objekt erwartet.");
            Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry e in generic) result[Convert.ToString(e.Key, CultureInfo.InvariantCulture)] = e.Value;
            return result;
        }

        private static IList AsList(object value)
        {
            IList list = value as IList;
            if (list == null) throw new InvalidDataException("JSON-Liste erwartet.");
            return list;
        }

        private static object NormalizeJsonValue(object value)
        {
            if (value == null) return null;
            if (value is int || value is long || value is double || value is string || value is bool) return value;
            if (value is decimal) return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static CatalogRelease MapRelease(Dictionary<string, object> r)
        {
            CatalogRelease x = new CatalogRelease();
            x.Id=ToLong(r["id"]); x.AlbumArtist=ToStringValue(r["album_artist"]); x.Album=ToStringValue(r["album"]);
            x.ReleaseDate=ToStringValue(r["release_date"]); x.Genre=ToStringValue(r["genre"]); x.Label=ToStringValue(r["label"]);
            x.Catalog=ToStringValue(r["catalog"]); x.Country=ToStringValue(r["country"]); x.TotalDiscs=(int)ToLong(r["total_discs"]);
            return x;
        }

        private static CatalogDisc MapDisc(Dictionary<string, object> r)
        {
            CatalogDisc x = new CatalogDisc();
            x.Id=ToLong(r["id"]); x.ReleaseId=ToLong(r["release_id"]);
            if (r["legacy_album_id"] != null) x.LegacyAlbumId=ToLong(r["legacy_album_id"]);
            x.DiscNumber=(int)ToLong(r["disc_number"]); x.Medium=ToStringValue(r["medium"]); x.Toc=ToStringValue(r["toc"]);
            x.TocComplete=ToLong(r["toc_complete"])!=0; x.CdTextPresent=ToLong(r["cd_text_present"])!=0; x.CdTextStatus=ToStringValue(r["cd_text_status"]);
            x.DurationSeconds=ToDouble(r["duration_seconds"]); x.LegacyJson=ToStringValue(r["legacy_json"]);
            return x;
        }

        private static CatalogTrack MapTrack(Dictionary<string, object> r)
        {
            CatalogTrack x = new CatalogTrack();
            x.Id=ToLong(r["id"]); x.DiscId=ToLong(r["disc_id"]); x.Position=(int)ToLong(r["position"]);
            x.Artist=ToStringValue(r["artist"]); x.Title=ToStringValue(r["title"]); x.Version=ToStringValue(r["version"]);
            x.ReleaseDate=ToStringValue(r["release_date"]); x.Genre=ToStringValue(r["genre"]); x.LegacyGenre=ToStringValue(r["legacy_genre"]);
            x.Bpm=ToDouble(r["bpm"]); x.DurationSeconds=ToDouble(r["duration_seconds"]); x.LegacyArtistRaw=ToStringValue(r["legacy_artist_raw"]); x.LegacyJson=ToStringValue(r["legacy_json"]);
            return x;
        }

        private static string NormalizeCdTextStatus(string status, bool present)
        {
            if (String.IsNullOrWhiteSpace(status)) return present ? "manual_present" : "manual_unknown";
            string value = status.Trim().ToLowerInvariant();
            if (present)
            {
                if (value == "drive_present" || value == "legacy_present" || value == "manual_present") return value;
                return "manual_present";
            }
            if (value == "drive_absent" || value == "drive_unavailable" || value == "drive_read_error" || value == "legacy_unknown" || value == "manual_unknown") return value;
            return "manual_unknown";
        }

        private static string Nz(string value) { return value ?? ""; }
        private static string NzJson(string value) { return String.IsNullOrWhiteSpace(value) ? "{}" : value; }
        private static string ToStringValue(object value) { return value == null ? "" : Convert.ToString(value, CultureInfo.InvariantCulture); }
        private static long ToLong(object value) { return value == null ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture); }
        private static double ToDouble(object value) { return value == null ? 0 : Convert.ToDouble(value, CultureInfo.InvariantCulture); }
    }
}
