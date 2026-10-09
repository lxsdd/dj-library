using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Web.Script.Serialization;

namespace DJLibrary
{
    internal static class CatalogMultiDiscCompatibility
    {
        private const string MarkerKey = "multidisc_reconciled_v1";
        private const string ChangeCountKey = "multidisc_reconcile_changes";
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        internal static void PrepareOwnedCatalog()
        {
            try
            {
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DJ Library");
                string path = Path.Combine(directory, "catalog-v0.4.sqlite");
                Directory.CreateDirectory(directory);

                // Public builds never bootstrap any catalog from packaged fixtures.
                // A missing catalog is handled by explicit first-start onboarding.
                if (!File.Exists(path)) return;

                int changed = ReconcilePath(path, true);
                if (changed >= 0)
                    WriteDiagnostic("Multi-disc-Reconciliation: " + changed.ToString(CultureInfo.InvariantCulture) + " Release-Gesamtzahlen angehoben; Rohwerte unverändert.");
            }
            catch (Exception ex)
            {
                WriteDiagnostic("Multi-disc-Reconciliation konnte nicht abgeschlossen werden: " + ex.Message);
            }
        }

        internal static void ValidateSelfTest(string baseDirectory)
        {
            if (NormalizeMediumDisplay("Double CD", 3) != "3× CD")
                throw new InvalidDataException("Multi-disc-Selbsttest: 3er-Set wird weiterhin als Double CD dargestellt.");
            if (NormalizeMediumDisplay("Double CD", 8) != "8× CD")
                throw new InvalidDataException("Multi-disc-Selbsttest: 8er-Set wird nicht korrekt dargestellt.");
            if (NormalizeMediumDisplay("CD", 2) != "2× CD")
                throw new InvalidDataException("Multi-disc-Selbsttest: 2er-CD-Set wird nicht korrekt dargestellt.");

            string seed = Path.Combine(baseDirectory, "data", "catalog-seed-v1.sqlite.gz");
            if (!File.Exists(seed)) throw new FileNotFoundException("Multi-disc-Selbsttest: Catalog-Seed fehlt.", seed);

            string tempDir = Path.Combine(Path.GetTempPath(), "DJLibrary-multidisc-selftest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string path = Path.Combine(tempDir, "catalog.sqlite");
            try
            {
                InstallSeed(seed, path);
                // Simulate a historical owner catalog that declares 69 of its
                // synthetic two-disc releases incorrectly as single-disc.
                // Reconciliation must recover counts from actual discs/legacy JSON.
                using (WinSqliteDb synthetic = new WinSqliteDb(path))
                {
                    synthetic.Execute("UPDATE release SET total_discs=1 WHERE id BETWEEN 840 AND 908 AND total_discs=2");
                    if (Convert.ToInt64(synthetic.Scalar("SELECT COUNT(*) FROM release WHERE id BETWEEN 840 AND 908 AND total_discs=1"), CultureInfo.InvariantCulture) != 69)
                        throw new InvalidDataException("Synthetic multi-disc test did not create 69 declared-total regressions.");
                }
                int changed = ReconcilePath(path, false);
                if (changed != 69)
                    throw new InvalidDataException("Multi-disc-Selbsttest: erwartet 69 korrigierte Release-Gesamtzahlen, erhalten " + changed.ToString(CultureInfo.InvariantCulture) + ".");

                using (WinSqliteDb db = new WinSqliteDb(path))
                {
                    if (!String.Equals(db.QuickCheck(), "ok", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Multi-disc-Selbsttest: quick_check failed.");
                    if (ToLong(db.Scalar("SELECT COUNT(*) FROM pragma_foreign_key_check")) != 0)
                        throw new InvalidDataException("Multi-disc-Selbsttest: foreign_key_check failed.");
                    if (ToLong(db.Scalar("SELECT total_discs FROM release WHERE id=1061")) != 3)
                        throw new InvalidDataException("Multi-disc-Selbsttest: synthetisches 3er-Release fehlt.");
                    if (ToLong(db.Scalar("SELECT COUNT(*) FROM disc WHERE release_id=1061 AND medium='Double CD'")) != 1)
                        throw new InvalidDataException("Multi-disc-Selbsttest: synthetischer Legacy-Medium-Randfall fehlt.");

                    AssertReleaseCount(db, 1, 839);
                    AssertReleaseCount(db, 2, 221);
                    AssertReleaseCount(db, 3, 45);
                    AssertReleaseCount(db, 4, 8);
                    AssertReleaseCount(db, 5, 5);
                    AssertReleaseCount(db, 8, 1);

                    if (ToLong(db.Scalar("SELECT COUNT(DISTINCT r.id) FROM release r JOIN disc d ON d.release_id=r.id WHERE r.total_discs>2 AND d.medium='Double CD'")) != 57)
                        throw new InvalidDataException("Multi-disc-Selbsttest: erwartete 57 Legacy-'Double CD'-Mehrfachsets >2 not found.");
                }
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        private static void AssertReleaseCount(WinSqliteDb db, int totalDiscs, int expected)
        {
            long actual = ToLong(db.Scalar("SELECT COUNT(*) FROM release WHERE total_discs=?", totalDiscs));
            if (actual != expected)
                throw new InvalidDataException("Multi-disc-Selbsttest: total_discs=" + totalDiscs.ToString(CultureInfo.InvariantCulture) + " erwartet " + expected.ToString(CultureInfo.InvariantCulture) + ", erhalten " + actual.ToString(CultureInfo.InvariantCulture) + ".");
        }

        private static void InstallSeed(string seedGzipPath, string destination)
        {
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
                    throw new InvalidDataException("Multi-disc seed quick_check failed.");
            }
            if (File.Exists(destination)) File.Delete(temp);
            else File.Move(temp, destination);
        }

        // Returns -1 when already reconciled; otherwise the number of release rows raised.
        private static int ReconcilePath(string path, bool preserveBackup)
        {
            using (WinSqliteDb db = new WinSqliteDb(path))
            {
                if (!String.Equals(db.QuickCheck(), "ok", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Multi-disc-Catalog quick_check failed.");

                object marker = db.Scalar("SELECT value FROM meta WHERE key=?", MarkerKey);
                if (String.Equals(Convert.ToString(marker, CultureInfo.InvariantCulture), "1", StringComparison.Ordinal)) return -1;

                if (preserveBackup)
                {
                    string premigration = path + ".pre-multidisc-v1.bak";
                    if (!File.Exists(premigration)) WriteValidatedBackup(db, premigration, false);
                }

                int changed = 0;
                db.Transaction(delegate
                {
                    foreach (Dictionary<string, object> release in db.Query("SELECT id,total_discs FROM release ORDER BY id"))
                    {
                        long releaseId = ToLong(release["id"]);
                        int current = Math.Max(1, (int)ToLong(release["total_discs"]));
                        int declared = current;

                        declared = Math.Max(declared, (int)ToLong(db.Scalar("SELECT MAX(disc_number) FROM disc WHERE release_id=?", releaseId)));
                        foreach (Dictionary<string, object> disc in db.Query("SELECT legacy_json FROM disc WHERE release_id=?", releaseId))
                        {
                            int legacyTotal = TotalDiscsFromLegacyJson(Convert.ToString(disc["legacy_json"], CultureInfo.InvariantCulture));
                            if (legacyTotal > declared) declared = legacyTotal;
                        }

                        // The set-size is release metadata, not the number of stored discs. Never
                        // reduce a user-expanded value when the library contains only a subset.
                        if (declared > current)
                        {
                            db.Execute("UPDATE release SET total_discs=? WHERE id=?", declared, releaseId);
                            changed++;
                        }
                    }
                    db.Execute("INSERT OR REPLACE INTO meta(key,value) VALUES(?,?)", ChangeCountKey, changed.ToString(CultureInfo.InvariantCulture));
                    db.Execute("INSERT OR REPLACE INTO meta(key,value) VALUES(?,?)", MarkerKey, "1");
                });

                if (ToLong(db.Scalar("SELECT COUNT(*) FROM pragma_foreign_key_check")) != 0)
                    throw new InvalidDataException("Multi-disc reconciliation foreign_key_check failed.");

                // Keep automatic corruption recovery aligned with the now-correct primary catalog.
                if (preserveBackup) WriteValidatedBackup(db, path + ".bak", true);
                return changed;
            }
        }

        private static void WriteValidatedBackup(WinSqliteDb db, string destination, bool replace)
        {
            string temp = destination + ".tmp";
            if (File.Exists(temp)) File.Delete(temp);
            db.BackupTo(temp);
            using (WinSqliteDb check = new WinSqliteDb(temp))
            {
                if (!String.Equals(check.QuickCheck(), "ok", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Multi-disc Backup quick_check failed.");
            }
            if (File.Exists(destination))
            {
                if (replace) File.Replace(temp, destination, null, true);
                else File.Delete(temp);
            }
            else File.Move(temp, destination);
        }

        internal static string DisplayMedium(object row)
        {
            if (row == null) return "";
            string medium = PropertyString(row, "Medium");
            int totalDiscs = PropertyInt(row, "TotalDiscs");
            if (totalDiscs <= 0) totalDiscs = TotalDiscsFromLegacyJson(PropertyString(row, "LegacyJson"));
            return NormalizeMediumDisplay(medium, totalDiscs);
        }

        internal static string NormalizeMediumDisplay(string medium, int totalDiscs)
        {
            string value = (medium ?? "").Trim();
            if (totalDiscs <= 1 || value.Length == 0) return value;

            if (String.Equals(value, "Double CD", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(value, "CD", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(value, "Triple CD", StringComparison.OrdinalIgnoreCase))
                return totalDiscs.ToString(CultureInfo.CurrentCulture) + "× CD";

            if (String.Equals(value, "CD Single", StringComparison.OrdinalIgnoreCase))
                return totalDiscs.ToString(CultureInfo.CurrentCulture) + "× CD Single";

            return value;
        }

        internal static int TotalDiscsFromLegacyJson(string json)
        {
            if (String.IsNullOrWhiteSpace(json)) return 0;
            try
            {
                object parsed = Json.DeserializeObject(json);
                IDictionary dictionary = parsed as IDictionary;
                if (dictionary == null) return 0;
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (!String.Equals(Convert.ToString(entry.Key, CultureInfo.InvariantCulture), "TotalDiscs", StringComparison.OrdinalIgnoreCase)) continue;
                    int total;
                    if (Int32.TryParse(Convert.ToString(entry.Value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out total))
                        return Math.Max(0, total);
                }
            }
            catch { }
            return 0;
        }

        private static string PropertyString(object row, string name)
        {
            PropertyInfo p = row.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            return p == null ? "" : Convert.ToString(p.GetValue(row, null), CultureInfo.InvariantCulture) ?? "";
        }

        private static int PropertyInt(object row, string name)
        {
            PropertyInfo p = row.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (p == null) return 0;
            try { return Convert.ToInt32(p.GetValue(row, null), CultureInfo.InvariantCulture); }
            catch { return 0; }
        }

        private static long ToLong(object value)
        {
            if (value == null) return 0;
            try { return Convert.ToInt64(value, CultureInfo.InvariantCulture); }
            catch { return 0; }
        }

        private static void WriteDiagnostic(string message)
        {
            try
            {
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DJ Library");
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, "diagnostics.log"), DateTime.Now.ToString("s", CultureInfo.InvariantCulture) + " " + message + Environment.NewLine);
            }
            catch { }
        }
    }
}