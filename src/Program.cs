using System;
using System.IO;
using System.Windows;

namespace DJLibrary
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string dataDir = Path.Combine(baseDir, "data");

                bool headlessSelfTest = args != null && Array.Exists(args, delegate(string x) { return String.Equals(x, "--self-test-ci", StringComparison.OrdinalIgnoreCase); });
                bool selfTest = headlessSelfTest || (args != null && Array.Exists(args, delegate(string x) { return String.Equals(x, "--self-test", StringComparison.OrdinalIgnoreCase); }));

                DataStore data = new DataStore();
                string validation;
                string sampleBridgeDir = Path.Combine(baseDir, "bridge-test");
                BridgeSnapshotStatus sampleBridge = null;
                if (selfTest)
                {
                    string[] required = new string[]
                    {
                        Path.Combine(dataDir, "tracks.tsv.gz"),
                        Path.Combine(dataDir, "cds.tsv.gz"),
                        Path.Combine(dataDir, "matches.tsv.gz"),
                        Path.Combine(dataDir, "catalog-seed-v1.sqlite.gz")
                    };
                    foreach (string requiredFile in required)
                        if (!File.Exists(requiredFile)) throw new FileNotFoundException("Qualifikationsdatei fehlt.", requiredFile);
                    data.Load(dataDir);
                    validation = data.Validate();
                    sampleBridge = BridgeSnapshotReader.Inspect(sampleBridgeDir, true);
                }
                else
                {
                    // The distributable application must not bootstrap a private music
                    // collection from a packaged fixture. Preserve existing user catalogs.
                    if (!File.Exists(CatalogService.DefaultCatalogPath))
                    {
                        MessageBoxResult choice = MessageBox.Show(
                            "Es ist noch kein persönlicher DJ-Library-Katalog vorhanden.\n\n" +
                            "Ja: Einen neuen, vollständig leeren Katalog erstellen.\n" +
                            "Nein: Vorhandene DJ-Library-SQLite-Datei bzw. Sicherung importieren.\n" +
                            "Abbrechen: Anwendung schließen.\n\n" +
                            "Es werden keine Musik- oder Beispieldaten installiert.",
                            "DJ Library — Persönlichen Katalog einrichten",
                            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                        if (choice == MessageBoxResult.Cancel) return;
                        if (choice == MessageBoxResult.Yes)
                        {
                            CatalogService.CreateEmptyCatalog();
                        }
                        else
                        {
                            Microsoft.Win32.OpenFileDialog picker = new Microsoft.Win32.OpenFileDialog();
                            picker.Title = "DJ-Library-Katalog oder SQLite-Sicherung importieren";
                            picker.Filter = "SQLite-Katalog (*.sqlite;*.db;*.bak)|*.sqlite;*.db;*.bak|Alle Dateien (*.*)|*.*";
                            picker.CheckFileExists = true;
                            picker.Multiselect = false;
                            if (picker.ShowDialog() != true) return;
                            CatalogService.ImportExistingCatalog(picker.FileName);
                        }
                    }
                    using (CatalogService catalog = CatalogService.EnsureInitialized(null))
                        data.LoadFromCatalog(catalog, Path.Combine(dataDir, "matches.tsv.gz"));
                    validation = data.Validate();
                }
                if (selfTest)
                {
                    if (!sampleBridge.Present || !sampleBridge.Compatible || !sampleBridge.Complete || sampleBridge.VerifiedItemCount != sampleBridge.DeclaredItemCount) throw new InvalidDataException("Bridge-Snapshot-Selbsttest failed: " + (sampleBridge.Error ?? "unvollständig"));
                    if (sampleBridge.SourceId != "fixture-profile-001" || sampleBridge.SourceName != "SelfTest foobar profile" || sampleBridge.ProducerVersion != "0.1.0-rc2") throw new InvalidDataException("Bridge-Sourcenmetadaten-Selbsttest failed.");
                    if (String.Equals(BridgeSnapshotReader.StandardProfileDirectory, BridgeSnapshotReader.LegacyDirectory, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Bridge-Pfad-Selbsttest: profil-lokale und Legacy-Ablage sind identisch.");
                    int baselineStrong, baselineLikely, baselineCandidate, baselineNone;
                    data.GetDigitalMatchCounts(out baselineStrong, out baselineLikely, out baselineCandidate, out baselineNone);
                    if (baselineStrong != 2518 || baselineLikely != 1748 || baselineCandidate != 2159 || baselineNone != 9015 || baselineStrong + baselineLikely + baselineCandidate + baselineNone != data.Tracks.Count)
                        throw new InvalidDataException("Digital-Matchverteilungs-Selbsttest: eingebetteter Referenzbestand weicht unerwartet ab.");
                    System.Collections.Generic.List<DigitalItem> sampleItems = BridgeSnapshotReader.LoadItems(sampleBridgeDir, sampleBridge);
                    if (sampleItems.Count != sampleBridge.VerifiedItemCount) throw new InvalidDataException("Bridge-Loader-Selbsttest: Itemzahl stimmt nicht.");

                    // Deterministic TOCTOU regression test: an inspected generation must never load
                    // a different state that became current before payload loading started.
                    string raceDir = Path.Combine(Path.GetTempPath(), "DJLibrary-bridge-race-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(raceDir);
                    try
                    {
                        File.Copy(Path.Combine(sampleBridgeDir, "digital-items.tsv.gz"), Path.Combine(raceDir, "digital-items.tsv.gz"));
                        File.Copy(Path.Combine(sampleBridgeDir, "bridge-state.tsv"), Path.Combine(raceDir, "bridge-state.tsv"));
                        BridgeSnapshotStatus expectedRace = BridgeSnapshotReader.Inspect(raceDir, true);
                        string raceState = File.ReadAllText(Path.Combine(raceDir, "bridge-state.tsv"));
                        raceState = raceState.Replace("generation\t" + expectedRace.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture), "generation\t" + (expectedRace.Generation + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
                        File.WriteAllText(Path.Combine(raceDir, "bridge-state.tsv"), raceState, new System.Text.UTF8Encoding(false));
                        bool rejected = false;
                        try { BridgeSnapshotReader.LoadItems(raceDir, expectedRace); }
                        catch (InvalidDataException) { rejected = true; }
                        if (!rejected) throw new InvalidDataException("Bridge-TOCTOU-Selbsttest: Generationstausch vor LoadItems wurde nicht discarded.");
                    }
                    finally { try { Directory.Delete(raceDir, true); } catch { } }

                    // Matcher ambiguity regression: structured base-title + version is accepted,
                    // but two equally strong candidates with conflicting genres must preserve LegacyGenre.
                    TrackRow versioned = null;
                    foreach (TrackRow candidate in data.Tracks) if (!String.IsNullOrWhiteSpace(candidate.Version)) { versioned = candidate; break; }
                    if (versioned == null) throw new InvalidDataException("Live-Matcher-Selbsttest: kein Track With Mix/Version/Version im Fixture.");
                    DigitalItem ma = new DigitalItem(); ma.ItemId=9000001; ma.Path=@"C:\Music\matcher-a.flac"; ma.Artist=versioned.Artist; ma.Title=versioned.Title; ma.OriginalTitle=versioned.Title; ma.RemixedBy=versioned.Version; ma.DurationSeconds=versioned.DurationSeconds; ma.Genre="Matcher-Test-A";
                    System.Collections.Generic.List<DigitalItem> matcherOne = new System.Collections.Generic.List<DigitalItem>(); matcherOne.Add(ma);
                    data.ApplyLiveDigitalItems(matcherOne);
                    if (!versioned.GenreProjected || versioned.Genre != "Matcher-Test-A") throw new InvalidDataException("Live-Matcher-Selbsttest: strukturierter Mix/Versions-Match wurde nicht projiziert.");
                    DigitalItem mb = new DigitalItem(); mb.ItemId=9000002; mb.Path=@"C:\Music\matcher-b.flac"; mb.Artist=versioned.Artist; mb.Title=versioned.Title; mb.OriginalTitle=versioned.Title; mb.RemixedBy=versioned.Version; mb.DurationSeconds=versioned.DurationSeconds; mb.Genre="Matcher-Test-B";
                    matcherOne.Add(mb); data.ApplyLiveDigitalItems(matcherOne);
                    if (versioned.GenreProjected || versioned.Genre != versioned.LegacyGenre) throw new InvalidDataException("Live-Matcher-Selbsttest: Genre-Konflikt hat LegacyGenre überschrieben.");

                    // Four-state live matcher regression: same artist/base title with matching duration but
                    // wrong explicit version is visible as likely; without supporting duration it remains
                    // a simple candidate. Neither weaker level may project GENRE.
                    DigitalItem ml = new DigitalItem(); ml.ItemId=9000003; ml.Path=@"C:\Music\matcher-likely.flac"; ml.Artist=versioned.Artist; ml.Title=versioned.Title; ml.OriginalTitle=versioned.Title; ml.RemixedBy="Different Mix"; ml.DurationSeconds=versioned.DurationSeconds; ml.Genre="Must-Not-Project";
                    System.Collections.Generic.List<DigitalItem> matcherWeak = new System.Collections.Generic.List<DigitalItem>(); matcherWeak.Add(ml);
                    data.ApplyLiveDigitalItems(matcherWeak);
                    if (versioned.DigitalLevel != "likely" || versioned.LikelyCount != 1 || versioned.GenreProjected || versioned.Genre != versioned.LegacyGenre) throw new InvalidDataException("Live-Matcher-Selbsttest: likely-Klassifikation/Genre-Sperre failed.");
                    DigitalItem mc = new DigitalItem(); mc.ItemId=9000004; mc.Path=@"C:\Music\matcher-candidate.flac"; mc.Artist=versioned.Artist; mc.Title=versioned.Title; mc.OriginalTitle=versioned.Title; mc.RemixedBy="Different Mix"; mc.DurationSeconds=versioned.DurationSeconds > 0 ? versioned.DurationSeconds + 30 : 30; mc.Genre="Must-Not-Project";
                    matcherWeak.Clear(); matcherWeak.Add(mc); data.ApplyLiveDigitalItems(matcherWeak);
                    if (versioned.DigitalLevel != "candidate" || versioned.CandidateCount != 1 || versioned.GenreProjected || versioned.Genre != versioned.LegacyGenre) throw new InvalidDataException("Live-Matcher-Selbsttest: candidate-Klassifikation/Genre-Sperre failed.");

                    // SearchText must be rebuilt when a live generation removes a previous projection.
                    matcherOne.Clear(); matcherOne.Add(ma); data.ApplyLiveDigitalItems(matcherOne);
                    if (versioned.SearchText.IndexOf("matcher-test-a", StringComparison.OrdinalIgnoreCase) < 0) throw new InvalidDataException("Live-Search-Selbsttest: projiziertes Genre fehlt im SearchText.");
                    data.ApplyLiveDigitalItems(new System.Collections.Generic.List<DigitalItem>());
                    if (versioned.SearchText.IndexOf("matcher-test-a", StringComparison.OrdinalIgnoreCase) >= 0) throw new InvalidDataException("Live-Search-Selbsttest: veraltetes projiziertes Genre blieb im SearchText.");

                    CdRow digitalLabelTest = new CdRow(); digitalLabelTest.StrongTracks = 0; digitalLabelTest.Tracks = 10;
                    if (digitalLabelTest.DigitalText != "0/10") throw new InvalidDataException("CD-Digitalanzeige-Selbsttest: erwartet 0/10 ohne missverständliches 'stark'.");

                    string liveValidation = data.ApplyLiveDigitalItems(sampleItems);
                    data.Validate();

                    // RC10 regression: the same activation helper used by MainLoaded must apply
                    // an already-present bridge generation to a fresh DataStore on the first pass.
                    DataStore startupData = new DataStore();
                    startupData.Load(dataDir);
                    BridgeSnapshotStatus startupProbe = BridgeSnapshotReader.Inspect(sampleBridgeDir, false);
                    BridgeSnapshotStatus startupVerified;
                    int startupItemCount;
                    string startupValidation;
                    bool startupApplied = MainWindow.TryApplyBridgeSnapshot(startupData, sampleBridgeDir, startupProbe, out startupVerified, out startupItemCount, out startupValidation);
                    if (!startupApplied || startupVerified.Generation != sampleBridge.Generation || startupItemCount != sampleItems.Count)
                        throw new InvalidDataException("Bridge-Startup-Selbsttest: presente Generation wurde beim ersten Aktivierungsdurchlauf nicht geladen.");
                    if (!startupData.DigitalSourceDescription.StartsWith("Live-foobar-Bridge", StringComparison.Ordinal) || startupData.DigitalItemCount != sampleItems.Count)
                        throw new InvalidDataException("Bridge-Startup-Selbsttest: DataStore blieb auf dem Testindex.");

                    string uiValidation = UiHelpers.ValidateReadOnlyGridVisualContract();
                    string uiRuntimeValidation = UiRuntimeSelfTest.Run(data);
                    string settingsValidation = ValidateSettingsRecovery();
                    string settingsMergeValidation = SettingsManager.ValidateStaleSnapshotMergeContract();
                    string metadataSelectionValidation = CdMetadataSelectionSession.RunSelfTest();
                    string metadataBulkValidation = CdMetadataSelectionSession.ValidateBulkSourceContract();
                    settingsValidation = settingsValidation + " + " + settingsMergeValidation + " + " + metadataSelectionValidation + " + " + metadataBulkValidation;
                    string catalogValidation = CatalogSelfTest.Run(baseDir);
                    string catalogRuntimeValidation = CatalogRuntimeSelfTest.Run(baseDir);
                    string report = "DJ Library Native " + BuildInfo.DisplayVersion + " self-test\r\n" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n" + validation + "\r\nPASS: Bridge-Snapshot-Vertrag + Loader + Generation-Race-Abwehr + Matcher-Ambiguitäts-Firewall, Generation " + sampleBridge.Generation + ", " + sampleBridge.VerifiedItemCount + " Items\r\nPASS: " + liveValidation + "\r\nPASS: presente Bridge-Generation wird beim Programmstart über den autoritativen Aktivierungspfad geladen; profil-lokale Sourcenmetadaten werden gelesen.\r\nPASS: Referenz-Matchverteilung = 2.518 stark / 1.748 wahrscheinlich / 2.159 Kandidat / 9.015 kein Treffer.\r\nPASS: Live-Matcher erzeugt strong/likely/candidate/none; nur strong darf GENRE projizieren; SearchText wird pro Generation neu aufgebaut.\r\nPASS: CD-Digitalanzeige nutzt nur das Verhältnis (z. B. 0/10) ohne missverständliches Statuswort.\r\nPASS: " + settingsValidation + "\r\nPASS: " + catalogValidation + "\r\nPASS: " + catalogRuntimeValidation + "\r\nPASS: " + uiRuntimeValidation + "\r\nPASS: " + uiValidation + "\r\n";
                    File.WriteAllText(Path.Combine(baseDir, "SELF-TEST.txt"), report);
                    if (!headlessSelfTest) MessageBox.Show(report, "DJ Library — Self-Test", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnLastWindowClose;
                MainWindow win = new MainWindow(data);
                app.Run(win);
            }
            catch (Exception ex)
            {
                bool headlessSelfTest = args != null && Array.Exists(args, delegate(string x) { return String.Equals(x, "--self-test-ci", StringComparison.OrdinalIgnoreCase); });
                if (headlessSelfTest)
                {
                    try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SELF-TEST-ERROR.txt"), ex.ToString()); } catch { }
                    Environment.ExitCode = 1;
                    return;
                }
                MessageBox.Show(
                    "DJ Library encountered an unexpected error.\n\n" + ex.ToString(),
                    "DJ Library — Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private static string ValidateSettingsRecovery()
        {
            string temp = Path.Combine(Path.GetTempPath(), "DJLibrary-settings-selftest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                string primary = Path.Combine(temp, "settings.xml");
                string backup = primary + ".bak";

                AppSettings expected = new AppSettings();
                expected.SelectedTab = 1;
                expected.BridgeDirectory = @"C:\selftest\bridge";
                System.Xml.Serialization.XmlSerializer serializer = new System.Xml.Serialization.XmlSerializer(typeof(AppSettings));
                using (FileStream fs = new FileStream(backup, FileMode.Create, FileAccess.Write, FileShare.None))
                    serializer.Serialize(fs, expected);

                File.WriteAllText(primary, "<broken-settings", new System.Text.UTF8Encoding(false));
                AppSettings recovered = SettingsManager.LoadForTesting(primary, backup);
                if (recovered == null || recovered.SelectedTab != 1 || recovered.BridgeDirectory != expected.BridgeDirectory)
                    throw new InvalidDataException("Settings-Backup-Recovery failed.");

                File.WriteAllText(backup, "<also-broken", new System.Text.UTF8Encoding(false));
                AppSettings fallback = SettingsManager.LoadForTesting(primary, backup);
                if (fallback == null || fallback.SelectedTab != 0 || Math.Abs(fallback.WindowWidth - 1380) > 0.001 || Math.Abs(fallback.WindowHeight - 850) > 0.001)
                    throw new InvalidDataException("Settings-Default-Fallback failed.");

                return "Settings-Recovery: kaputte Primärdatei -> .bak; kaputte Primärdatei + .bak -> sichere Defaults";
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch { }
            }
        }

    }
}
