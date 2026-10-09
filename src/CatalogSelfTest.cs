using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace DJLibrary
{
    public static class CatalogSelfTest
    {
        public static string Run(string baseDirectory)
        {
            string seedGzip = Path.Combine(baseDirectory, "data", "catalog-seed-v1.sqlite.gz");
            if (!File.Exists(seedGzip)) throw new FileNotFoundException("v0.4 Catalog-Seed fehlt.", seedGzip);

            string tempRoot = Path.Combine(Path.GetTempPath(), "DJLibrary-v04-selftest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            string dbPath = Path.Combine(tempRoot, "catalog.sqlite");
            try
            {
                Decompress(seedGzip, dbPath);
                string report;
                using (CatalogService catalog = CatalogService.OpenForTesting(dbPath))
                {
                    if (catalog.SchemaVersion != 4) throw new InvalidDataException("Schema-v1→v4-Migration failed.");
                    if (!File.Exists(dbPath + ".pre-v4.bak")) throw new InvalidDataException("Schema-v4 Pre-Migration-Backup fehlt.");
                    using (WinSqliteDb preV4 = new WinSqliteDb(dbPath + ".pre-v4.bak"))
                        if (!String.Equals(preV4.QuickCheck(), "ok", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Schema-v4 Pre-Migration-Backup ist beschädigt.");

                    CatalogCounts baseline = catalog.GetCounts();
                    AssertCounts(baseline, 1119, 1315, 15440, "Baseline");
                    if (!String.Equals(catalog.QuickCheck(), "ok", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("quick_check != ok");
                    if (catalog.ForeignKeyViolationCount() != 0) throw new InvalidDataException("foreign_key_check meldet Verstöße.");
                    if (catalog.CountDiscsByCdTextStatus("legacy_unknown") != 1315)
                        throw new InvalidDataException("Legacy-CD-TEXT-Leerwerte wurden nicht verlustfrei als ungeprüft migriert.");

                    if (catalog.CdxMarkerCleanupCount != 22) throw new InvalidDataException("Schema-v4 °-Bereinigung: erwartet 22, erhalten " + catalog.CdxMarkerCleanupCount + ".");
                    if (catalog.GetReleases().Any(x => CdxCompatibility.HasHistoricalMarker(x.Album)))
                        throw new InvalidDataException("Schema-v4 °-Bereinigung: aktive Release-Namen enthalten noch historische Marker.");
                    long cdxYes = catalog.CountDiscsByCdxCompatibility(CdxCompatibilityState.Compatible);
                    long cdxNo = catalog.CountDiscsByCdxCompatibility(CdxCompatibilityState.Incompatible);
                    long cdxUnknown = catalog.CountDiscsByCdxCompatibility(CdxCompatibilityState.Unknown);
                    if (cdxYes != 1274 || cdxNo != 39 || cdxUnknown != 2)
                        throw new InvalidDataException("CDX-Kompatibilitätsverteilung weicht von 1274/39/2 ab: " + cdxYes + "/" + cdxNo + "/" + cdxUnknown + ".");

                    CatalogDisc malformedTocDisc = catalog.GetDisc(41);
                    if (malformedTocDisc == null || malformedTocDisc.CdxCompatibilityState != CdxCompatibilityState.Unknown)
                        throw new InvalidDataException("CDX-Selbsttest: Disc 41 muss wegen nicht-monotoner physischer TOC 'Unbekannt' sein.");
                    CatalogDisc incompleteTocDisc = catalog.GetDisc(1104);
                    if (incompleteTocDisc == null || incompleteTocDisc.CdxCompatibilityState != CdxCompatibilityState.Unknown)
                        throw new InvalidDataException("CDX-Selbsttest: Disc 1104 muss wegen unvollständiger TOC 'Unbekannt' sein.");

                    CatalogRelease alexRelease = catalog.GetReleases().FirstOrDefault(x => String.Equals(x.Album, "Fixture Boundary #310", StringComparison.Ordinal));
                    if (alexRelease == null) throw new InvalidDataException("Schema-v4 °-Bereinigung: Fixture Boundary #310 fehlt.");
                    CatalogDisc alexDisc = catalog.GetDiscs(alexRelease.Id).FirstOrDefault();
                    int alexFrames;
                    if (alexDisc == null || alexDisc.CdxCompatibilityState != CdxCompatibilityState.Incompatible ||
                        !CdxCompatibility.TryGetDurationFrames(alexDisc.Toc, alexDisc.TocComplete, out alexFrames) || alexFrames != 361025)
                        throw new InvalidDataException("CDX-Selbsttest: synthetische Boundary #310 muss 361025 physische Frames und 'Nein' ergeben.");

                    int legacyMarkers = 0;
                    JavaScriptSerializer json = new JavaScriptSerializer();
                    foreach (CatalogRelease r in catalog.GetReleases())
                    {
                        foreach (CatalogDisc d in catalog.GetDiscs(r.Id))
                        {
                            Dictionary<string, object> raw = json.DeserializeObject(d.LegacyJson) as Dictionary<string, object>;
                            object rawAlbum;
                            if (raw != null && raw.TryGetValue("Album", out rawAlbum) && CdxCompatibility.HasHistoricalMarker(Convert.ToString(rawAlbum, CultureInfo.InvariantCulture))) legacyMarkers++;
                        }
                    }
                    if (legacyMarkers != 22) throw new InvalidDataException("Legacy-Provenienz verlor °-Marker: erwartet 22, erhalten " + legacyMarkers + ".");

                    if (CdxCompatibility.Classify("150 360148", true) != CdxCompatibilityState.Compatible) throw new InvalidDataException("CDX-Grenze 79:59:73 muss Ja sein.");
                    if (CdxCompatibility.Classify("150 360149", true) != CdxCompatibilityState.Incompatible) throw new InvalidDataException("CDX-Grenze 79:59:74 muss Nein sein.");
                    if (CdxCompatibility.Classify("150 360150", true) != CdxCompatibilityState.Incompatible) throw new InvalidDataException("CDX-Grenze 80:00:00 muss Nein sein.");
                    if (CdxCompatibility.Classify("150 360150", false) != CdxCompatibilityState.Unknown) throw new InvalidDataException("Unvollständige TOC muss CDX=Unbekannt sein.");
                    if (CdxCompatibility.Classify("150 360150 2965", true) != CdxCompatibilityState.Unknown) throw new InvalidDataException("Nicht-monotone TOC muss CDX=Unbekannt sein.");
                    if (CdxCompatibility.Classify("150 broken", true) != CdxCompatibilityState.Unknown) throw new InvalidDataException("Ungültige TOC muss CDX=Unbekannt sein.");
                    if (CdxCompatibility.FormatMsf(359999) != "79:59:74") throw new InvalidDataException("CDX MSF-Grenzformatierung failed.");

                    CatalogRelease release = new CatalogRelease();
                    release.AlbumArtist = "SelfTest";
                    release.Album = "CRUD";
                    release.Genre = "House";
                    release.TotalDiscs = 2;
                    long rid = catalog.CreateRelease(release);

                    CatalogDisc d1 = new CatalogDisc { ReleaseId = rid, DiscNumber = 1, Medium = "CD" };
                    CatalogDisc d2 = new CatalogDisc { ReleaseId = rid, DiscNumber = 2, Medium = "CD" };
                    long did1 = catalog.AddDisc(d1);
                    long did2 = catalog.AddDisc(d2);

                    long t1 = catalog.AddTrack(new CatalogTrack { DiscId = did1, Position = 1, Artist = "A", Title = "One", Genre = "House" });
                    long t2 = catalog.AddTrack(new CatalogTrack { DiscId = did1, Position = 2, Artist = "A", Title = "Two", Genre = "House" });

                    List<CatalogSearchDocument> searchDocuments = catalog.GetSearchDocuments();
                    CatalogSearchResult titleSearch = CatalogService.SearchDocuments(searchDocuments, "One");
                    if (!titleSearch.ReleaseIds.Contains(rid) || !titleSearch.DiscIds.Contains(did1) || !titleSearch.TrackIds.Contains(t1))
                        throw new InvalidDataException("In-Memory-Search: Tracktitel findet Hierarchie nicht.");
                    CatalogSearchResult ancestorSearch = CatalogService.SearchDocuments(searchDocuments, "CRUD");
                    if (!ancestorSearch.ReleaseIds.Contains(rid) || !ancestorSearch.DiscIds.Contains(did1) || !ancestorSearch.TrackIds.Contains(t1) || !ancestorSearch.TrackIds.Contains(t2))
                        throw new InvalidDataException("In-Memory-Search: Releasefeld hält untergeordnete Tracks nicht sichtbar.");

                    CatalogDisc editDisc = catalog.GetDisc(did2);
                    editDisc.Medium = "CD-R";
                    editDisc.CdTextPresent = true;
                    editDisc.CdTextStatus = "manual_present";
                    catalog.UpdateDisc(editDisc);
                    CatalogDisc verifyDisc = catalog.GetDisc(did2);
                    if (verifyDisc.Medium != "CD-R" || !verifyDisc.CdTextPresent || verifyDisc.CdTextStatus != "manual_present")
                        throw new InvalidDataException("Disc update / manueller CD-TEXT-Status failed.");

                    CatalogTrack editTrack = catalog.GetTrack(t1);
                    editTrack.Title = "One Edit";
                    editTrack.Bpm = 128;
                    catalog.UpdateTrack(editTrack);
                    CatalogTrack verifyTrack = catalog.GetTrack(t1);
                    if (verifyTrack.Title != "One Edit" || Math.Abs(verifyTrack.Bpm - 128) > 0.001) throw new InvalidDataException("Track update failed.");

                    catalog.ReorderTracks(did1, new long[] { t2, t1 });
                    List<CatalogTrack> reordered = catalog.GetTracks(did1);
                    if (reordered.Count != 2 || reordered[0].Id != t2 || reordered[1].Id != t1) throw new InvalidDataException("collision-safe reorder failed.");

                    catalog.UndoLast();
                    List<CatalogTrack> undoOrder = catalog.GetTracks(did1);
                    if (undoOrder[0].Id != t1 || undoOrder[1].Id != t2 || !catalog.CanRedo) throw new InvalidDataException("Undo/Redo-Stack nach Reorder failed.");
                    catalog.RedoLast();
                    List<CatalogTrack> redoOrder = catalog.GetTracks(did1);
                    if (redoOrder[0].Id != t2 || redoOrder[1].Id != t1 || catalog.CanRedo) throw new InvalidDataException("Redo nach Reorder failed.");
                    catalog.UndoLast();
                    if (!catalog.CanRedo) throw new InvalidDataException("Redo-Branch nach zweitem Undo fehlt.");

                    release = catalog.GetRelease(rid);
                    release.Label = "Test Label";
                    catalog.UpdateRelease(release);
                    if (catalog.GetRelease(rid).Label != "Test Label") throw new InvalidDataException("Release update failed.");
                    if (catalog.CanRedo) throw new InvalidDataException("Neue Mutation hat den Redo-Branch nicht verworfen.");

                    string deleteTrackGroup = catalog.DeleteTrack(t2);
                    if (catalog.GetTrack(t2) != null) throw new InvalidDataException("Track delete failed.");
                    catalog.UndoGroup(deleteTrackGroup);
                    if (catalog.GetTrack(t2) == null || catalog.GetTrack(t2).Title != "Two") throw new InvalidDataException("Track stable-ID undo failed.");

                    long undoDisc = catalog.AddDisc(new CatalogDisc { ReleaseId = rid, DiscNumber = 4, Medium = "CD" });
                    if (catalog.GetRelease(rid).TotalDiscs != 4) throw new InvalidDataException("Mehrfach-CD total_discs wurde nicht automatisch erweitert.");
                    CatalogHistoryEntry addDiscHistory = catalog.GetHistory(50).FirstOrDefault(x => x.Entity == "disc" && x.EntityId == undoDisc && x.Operation == "create");
                    if (addDiscHistory == null) throw new InvalidDataException("Disc-create History fehlt.");
                    catalog.UndoGroup(addDiscHistory.TransactionGroup);
                    if (catalog.GetDisc(undoDisc) != null || catalog.GetRelease(rid).TotalDiscs != 2)
                        throw new InvalidDataException("Mehrfach-CD Undo hat Release-Zähler nicht wiederhergestellt.");
                    catalog.RedoGroup(addDiscHistory.TransactionGroup);
                    if (catalog.GetDisc(undoDisc) == null || catalog.GetRelease(rid).TotalDiscs != 4)
                        throw new InvalidDataException("Mehrfach-CD Redo hat Release-Zähler nicht wiederhergestellt.");
                    long undoDiscTrack = catalog.AddTrack(new CatalogTrack { DiscId = undoDisc, Position = 1, Artist = "B", Title = "DiscTrack" });
                    string deleteDiscGroup = catalog.DeleteDisc(undoDisc);
                    if (catalog.GetDisc(undoDisc) != null) throw new InvalidDataException("Disc delete failed.");
                    catalog.UndoGroup(deleteDiscGroup);
                    if (catalog.GetDisc(undoDisc) == null || catalog.GetTrack(undoDiscTrack) == null) throw new InvalidDataException("Disc graph undo failed.");

                    CdSnapshot snapshot = new CdSnapshot();
                    snapshot.DriveId = "sim0";
                    snapshot.Toc = "150 13650 28650";
                    snapshot.CdTextPresent = true;
                    snapshot.CdTextStatus = "drive_present";
                    snapshot.Album = "Disc";
                    snapshot.AlbumArtist = "Artist";
                    snapshot.Tracks.Add(new CdTrackCapture { Position = 1, DurationSeconds = 180, Title = "Alpha", Artist = "Artist" });
                    snapshot.Tracks.Add(new CdTrackCapture { Position = 2, DurationSeconds = 200, Title = "", Artist = "" });
                    long importedDisc = catalog.ImportCd(rid, new SimulatedCdDrive(snapshot).Capture(), 3, "CD");
                    List<CatalogTrack> imported = catalog.GetTracks(importedDisc);
                    CatalogDisc importedDiscRow = catalog.GetDisc(importedDisc);
                    if (imported.Count != 2 || imported[0].Title != "Alpha" || imported[1].Title != "" || imported[1].Artist != "")
                        throw new InvalidDataException("CD import / no-invented-CDTEXT failed.");
                    if (importedDiscRow == null || !importedDiscRow.CdTextPresent || importedDiscRow.CdTextStatus != "drive_present")
                        throw new InvalidDataException("CD-TEXT-Provenienz des Laufwerksimports fehlt.");
                    CatalogHistoryEntry cdImport = catalog.GetHistory(50).FirstOrDefault(x => x.EntityId == importedDisc && x.Entity == "disc" && x.Operation == "create");
                    if (cdImport == null) throw new InvalidDataException("CD import history group fehlt.");
                    catalog.UndoGroup(cdImport.TransactionGroup);
                    if (catalog.GetDisc(importedDisc) != null) throw new InvalidDataException("Atomarer CD import undo failed.");

                    CdSnapshot noTextSnapshot = new CdSnapshot();
                    noTextSnapshot.DriveId = "sim1";
                    noTextSnapshot.Toc = "150 13650";
                    noTextSnapshot.CdTextPresent = false;
                    noTextSnapshot.CdTextStatus = "drive_absent";
                    noTextSnapshot.Tracks.Add(new CdTrackCapture { Position = 1, DurationSeconds = 180, Title = "", Artist = "" });
                    long noTextDisc = catalog.ImportCd(rid, noTextSnapshot, 5, "CD");
                    CatalogDisc noTextRow = catalog.GetDisc(noTextDisc);
                    if (noTextRow == null || noTextRow.CdTextPresent || noTextRow.CdTextStatus != "drive_absent")
                        throw new InvalidDataException("Geprüft fehlender CD-TEXT wurde nicht vom ungeprüften Legacy-Status getrennt.");
                    CatalogHistoryEntry noTextImport = catalog.GetHistory(50).FirstOrDefault(x => x.EntityId == noTextDisc && x.Entity == "disc" && x.Operation == "create");
                    if (noTextImport == null) throw new InvalidDataException("CD import history group für fehlenden CD-TEXT fehlt.");
                    catalog.UndoGroup(noTextImport.TransactionGroup);

                    CatalogCounts beforeInvalid = catalog.GetCounts();
                    CdSnapshot invalid = new CdSnapshot();
                    invalid.Tracks.Add(new CdTrackCapture { Position = 2, DurationSeconds = 1 });
                    bool rejected = false;
                    try { catalog.ImportCd(rid, invalid, 5, "CD"); }
                    catch (ArgumentException) { rejected = true; }
                    if (!rejected) throw new InvalidDataException("Ungültiger CD-Snapshot wurde akzeptiert.");
                    CatalogCounts afterInvalid = catalog.GetCounts();
                    AssertCounts(afterInvalid, beforeInvalid.Releases, beforeInvalid.Discs, beforeInvalid.Tracks, "invalid-CD side-effect");

                    string releaseDeleteGroup = catalog.DeleteRelease(rid);
                    AssertCounts(catalog.GetCounts(), 1119, 1315, 15440, "release delete");
                    catalog.UndoGroup(releaseDeleteGroup);
                    if (catalog.GetRelease(rid) == null) throw new InvalidDataException("Release graph undo failed.");
                    catalog.DeleteRelease(rid);
                    AssertCounts(catalog.GetCounts(), 1119, 1315, 15440, "final baseline");

                    string backup = Path.Combine(tempRoot, "backup.sqlite");
                    catalog.Backup(backup);
                    using (CatalogService backupCheck = CatalogService.OpenForTesting(backup))
                    {
                        AssertCounts(backupCheck.GetCounts(), 1119, 1315, 15440, "backup");
                        if (backupCheck.ForeignKeyViolationCount() != 0) throw new InvalidDataException("Backup foreign_key_check failed.");
                    }

                    long transient = catalog.CreateRelease(new CatalogRelease { AlbumArtist = "Transient", Album = "Restore me away" });
                    if (catalog.GetRelease(transient) == null) throw new InvalidDataException("Restore precondition fehlt.");
                    using (CatalogService recoveryAfterMutation = CatalogService.OpenForTesting(catalog.RecoveryBackupPath))
                    {
                        AssertCounts(recoveryAfterMutation.GetCounts(), 1120, 1315, 15440, "automatic recovery backup after mutation");
                    }
                    catalog.Restore(backup);
                    if (catalog.GetRelease(transient) != null) throw new InvalidDataException("Restore hat transienten Datensatz nicht entfernt.");
                    AssertCounts(catalog.GetCounts(), 1119, 1315, 15440, "restore");
                    using (CatalogService recoveryAfterRestore = CatalogService.OpenForTesting(catalog.RecoveryBackupPath))
                    {
                        AssertCounts(recoveryAfterRestore.GetCounts(), 1119, 1315, 15440, "automatic recovery backup after restore");
                    }

                    byte[] toc = SyntheticToc();
                    CdSnapshot parsed = WindowsCdDrive.ParseToc(toc, toc.Length);
                    if (parsed.Tracks.Count != 2 || parsed.Toc != "150 13650 28650") throw new InvalidDataException("TOC parser failed.");
                    if (Math.Abs(parsed.Tracks[0].DurationSeconds - 180.0) > 0.01 || Math.Abs(parsed.Tracks[1].DurationSeconds - 200.0) > 0.01)
                        throw new InvalidDataException("TOC duration parser failed.");

                    if (!UiHelpers.ContainsAllTerms("artist alpha club mix", "alpha mix")) throw new InvalidDataException("Search helper regression.");
                    if (UiHelpers.FormatDuration(455) != "7:35") throw new InvalidDataException("compact track duration format regression.");
                    if (UiHelpers.FormatDuration(3723) != "1:02:03") throw new InvalidDataException("hour duration format regression.");
                    if (UiHelpers.FormatDuration(0) != "—") throw new InvalidDataException("unknown duration placeholder regression.");
                    double parsedDuration;
                    if (!UiHelpers.TryParseDuration("1:02:03", out parsedDuration) || Math.Abs(parsedDuration - 3723) > 0.001) throw new InvalidDataException("h:mm:ss duration parser regression.");
                    if (!UiHelpers.TryParseDuration("7:35", out parsedDuration) || Math.Abs(parsedDuration - 455) > 0.001) throw new InvalidDataException("m:ss duration parser regression.");
                    if (!UiHelpers.TryParseDuration("455", out parsedDuration) || Math.Abs(parsedDuration - 455) > 0.001) throw new InvalidDataException("seconds duration parser regression.");
                    if (!UiHelpers.TryParseDuration("", out parsedDuration) || Math.Abs(parsedDuration) > 0.001) throw new InvalidDataException("empty duration parser regression.");
                    if (GridRuntimeSupport.FormatClipboardValue(true) != "Yes" || GridRuntimeSupport.FormatClipboardValue(false) != "No") throw new InvalidDataException("Clipboard bool formatting regression.");
                    string metadataPipeline = CdMetadataPipeline.RunSelfTest();

                    report = "schema-v4 migration + CDX compatibility 1274/39/2 + trailing-degree cleanup 22 + CRUD + in-memory hierarchical search + multi-disc sync + stable-ID undo/redo + redo-branch invalidation + collision-safe reorder + atomic CD import + CD-TEXT provenance + no-invented CD-TEXT + adaptive compact durations + selectable/copyable metadata + backup/restore + automatic recovery backup + TOC parser + " + metadataPipeline + " + counts=1119/1315/15440";
                }

                string recoveryBackup = dbPath + ".bak";
                if (!File.Exists(recoveryBackup)) throw new InvalidDataException("Recovery-Backup fehlt.");
                File.WriteAllBytes(dbPath, Encoding.ASCII.GetBytes("broken-sqlite"));
                using (CatalogService recovered = CatalogService.OpenForTestingWithRecovery(dbPath))
                {
                    AssertCounts(recovered.GetCounts(), 1119, 1315, 15440, "corruption recovery");
                    if (recovered.ForeignKeyViolationCount() != 0) throw new InvalidDataException("Recovery foreign_key_check failed.");
                }

                return report + " + corruption-recovery";
            }
            finally
            {
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        }

        private static void Decompress(string gzipPath, string destination)
        {
            using (FileStream input = File.OpenRead(gzipPath))
            using (GZipStream gzip = new GZipStream(input, CompressionMode.Decompress))
            using (FileStream output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
                gzip.CopyTo(output);
        }

        private static void AssertCounts(CatalogCounts value, long releases, long discs, long tracks, string label)
        {
            if (value.Releases != releases || value.Discs != discs || value.Tracks != tracks)
                throw new InvalidDataException(label + " counts=" + value.Releases + "/" + value.Discs + "/" + value.Tracks +
                    " expected=" + releases + "/" + discs + "/" + tracks);
        }

        private static byte[] SyntheticToc()
        {
            byte[] b = new byte[28];
            b[2] = 1; b[3] = 2;
            SetTrack(b, 4, 1, 150);
            SetTrack(b, 12, 2, 13650);
            SetTrack(b, 20, 0xAA, 28650);
            return b;
        }

        private static void SetTrack(byte[] b, int offset, int track, int frames)
        {
            int totalSeconds = frames / 75;
            int f = frames % 75;
            int m = totalSeconds / 60;
            int s = totalSeconds % 60;
            b[offset + 2] = (byte)track;
            b[offset + 5] = (byte)m;
            b[offset + 6] = (byte)s;
            b[offset + 7] = (byte)f;
        }
    }
}
