using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace DJLibrary
{
    public sealed class TrackDetailWindow : Window
    {
        public TrackDetailWindow(Window owner, TrackRow track, DataStore data)
        {
            Owner = owner;
            Title = "Trackdetails — " + track.Artist + " — " + track.DisplayTitle;
            GridRuntimeSupport.ApplyWindowIcon(this);
            Width = 1140;
            Height = 720;
            MinWidth = 780;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            WindowGeometrySettings.Attach(this, "track.details");

            Grid root = new Grid();
            root.Margin = new Thickness(8);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;

            Grid summary = new Grid();
            summary.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            summary.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            summary.Margin = new Thickness(0, 0, 0, 8);
            Grid.SetRow(summary, 0);
            root.Children.Add(summary);

            GroupBox infoGroup = new GroupBox();
            infoGroup.Header = "Track / Release";
            infoGroup.Margin = new Thickness(0, 0, 8, 0);
            infoGroup.ToolTip = "Kernmetadaten des physischen CD-Tracks aus der migrierten WenSoftware-Datenbank.";
            Grid.SetColumn(infoGroup, 0);
            summary.Children.Add(infoGroup);

            Grid info = UiHelpers.CreateFourColumnFieldGrid();
            infoGroup.Content = info;
            UiHelpers.AddField(info, 0, 0, "Artist", track.Artist, "Cleaned artist. Systematic legacy prefixes such as '01. ' are not shown as part of the artist name.");
            UiHelpers.AddField(info, 0, 1, "Title", track.Title, "Basistitel des Tracks.");
            UiHelpers.AddField(info, 1, 0, "Mix / Version", track.Version, "Separate Mix/Version information, stored separately from the base title.");
            UiHelpers.AddField(info, 1, 1, "Album / CD", track.Album, "Physische CD bzw. Album, auf dem der Track katalogisiert ist.");
            UiHelpers.AddField(info, 2, 0, "Position", "Track " + track.TrackNumber.ToString() + ", Disc " + track.DiscText, "Track and disc position.");
            UiHelpers.AddField(info, 2, 1, "Duration", track.DurationText, "Track duration from the legacy collection.");
            UiHelpers.AddField(info, 3, 0, "Year", track.Date, "Release year / DATE.");
            UiHelpers.AddField(info, 3, 1, "Genre", track.Genre, "Preferred genre. A strong digital match uses GENRE from the digital foobar collection; otherwise the legacy genre is retained.");
            UiHelpers.AddField(info, 4, 0, "BPM", track.BpmText, "Production BPM field. Current values can be enriched from the digital collection.");
            UiHelpers.AddField(info, 4, 1, "Label", track.Label, "Label of the physical release.");
            UiHelpers.AddField(info, 5, 0, "Catalog", track.Catalog, "Catalog number of the physical release.");
            if (!String.IsNullOrEmpty(track.LegacyArtistRaw) && !String.Equals(track.LegacyArtistRaw, track.Artist, StringComparison.Ordinal))
                UiHelpers.AddField(info, 5, 1, "Legacy Artist", track.LegacyArtistRaw, "Unmodified original Artist value from Music Library.");
            UiHelpers.AddField(info, 6, 0, "Genre-Source", track.GenreSourceText, "Shows whether the displayed genre comes from the legacy Music Library database or a strong digital track match.");
            if (track.GenreProjected && !String.Equals(track.LegacyGenre, track.Genre, StringComparison.OrdinalIgnoreCase))
                UiHelpers.AddField(info, 6, 1, "Legacy Genre", track.LegacyGenre, "Unmodified historical Music Library genre, preserved for traceability.");

            GroupBox digitalGroup = new GroupBox();
            digitalGroup.Header = "Digital / Matching";
            digitalGroup.ToolTip = "Automatic matching against the digital foobar collection. Source refers to the currently authoritative digital collection.";
            Grid.SetColumn(digitalGroup, 1);
            summary.Children.Add(digitalGroup);

            Grid digitalInfo = UiHelpers.CreateTwoColumnFieldGrid();
            digitalGroup.Content = digitalInfo;
            UiHelpers.AddField(digitalInfo, 0, 0, "Status", track.DigitalText, "Automatisch ermittelter Digitalstatus.");
            UiHelpers.AddField(digitalInfo, 1, 0, "candidates", track.DigitalCount.ToString(), "Anzahl aller digitalen candidates.");
            UiHelpers.AddField(digitalInfo, 2, 0, "Stark", track.StrongCount.ToString(), "Candidates with Artist + base title + Mix/Version and matching duration.");
            UiHelpers.AddField(digitalInfo, 3, 0, "Wahrscheinlich", track.LikelyCount.ToString(), "Candidates with a strong but not yet complete match.");
            UiHelpers.AddField(digitalInfo, 4, 0, "Einfach", track.CandidateCount.ToString(), "Einfache Artist-/Title-candidates.");
            UiHelpers.AddField(digitalInfo, 5, 0, "Source", data.DigitalSourceDescription, "Shows which digital collection produced the displayed matches and genre projections.");
            if (!String.IsNullOrEmpty(track.IssueCode))
            {
                UiHelpers.AddField(digitalInfo, 6, 0, "Has Issueskeit", track.IssueCode, "Legacy-Has Issueskeit dieses Tracks.");
                UiHelpers.AddField(digitalInfo, 7, 0, "Explanation", track.IssueDetail, "Specific reason for the issue.");
            }

            GroupBox matchGroup = new GroupBox();
            matchGroup.Header = "Digitale candidates";
            matchGroup.ToolTip = "Digital foobar candidates. Track identity and release identity are evaluated separately.";
            Grid.SetRow(matchGroup, 1);
            root.Children.Add(matchGroup);

            DataGrid grid = UiHelpers.CreateReadOnlyGrid();
            grid.CanUserReorderColumns = true;
            matchGroup.Content = grid;

            AddColumn(grid, "Level", "Quality of automatic matching.", "LevelText", 105);
            AddColumn(grid, "Match-Grund", "Specific evidence or matching method for this candidate.", "MethodText", 240);
            AddColumn(grid, "Konfidenz", "Interner Konfidenzwert des Matchers.", "ConfidenceText", 85);
            AddColumn(grid, "Artist", "Artist des digitalen candidates.", "Artist", 155);
            AddColumn(grid, "Title", "Title des digitalen candidates.", "Title", 190);
            AddColumn(grid, "Mix / Version", "REMIXED BY bzw. Mix/Version des digitalen candidates.", "RemixedBy", 180);
            AddColumn(grid, "Album", "Digitaler Release; kann von der physischen CD abweichen.", "Album", 190);
            AddColumn(grid, "Duration", "Digital duration.", "DurationText", 75);
            AddColumn(grid, "BPM", "BPM der digitalen File.", "BpmText", 70);
            AddColumn(grid, "Genre", "GENRE tag of the digital candidate; preferred for genre projection on a strong track match.", "Genre", 145);
            AddColumn(grid, "Label", "Label des digitalen Releases.", "Label", 150);
            AddColumn(grid, "Catalog", "Catalog Number des digitalen Releases.", "Catalog", 120);
            AddColumn(grid, "Codec", "Codec der digitalen File.", "Codec", 75);
            AddColumn(grid, "Pfad", "Pfad plus Subsong identifizieren den digitalen foobar-Eintrag.", "Path", 360);

            List<MatchRow> matches = data.GetMatchesForTrack(track.TrackId);
            grid.ItemsSource = matches;

            StackPanel actions = new StackPanel();
            actions.Orientation = Orientation.Horizontal;
            actions.HorizontalAlignment = HorizontalAlignment.Right;
            actions.Margin = new Thickness(0, 8, 0, 0);
            Grid.SetRow(actions, 2);
            root.Children.Add(actions);

            Button previewNormalizer = new Button();
            previewNormalizer.Content = "Normalizer-Vorschau…";
            previewNormalizer.Padding = new Thickness(14, 4, 14, 4);
            previewNormalizer.Margin = new Thickness(0, 0, 8, 0);
            previewNormalizer.ToolTip = "Analysiert den ausgewählten digitalen Bridge-Kandidaten read-only mit dem gemeinsamen DJ Metadata Normalizer.";
            previewNormalizer.IsEnabled = false;
            actions.Children.Add(previewNormalizer);

            Action showNormalizerPreview = delegate
            {
                MatchRow selected = grid.SelectedItem as MatchRow;
                if (selected == null) return;
                DigitalItem item = data.GetActiveDigitalItem(selected.DigitalItemId);
                if (item == null)
                {
                    MessageBox.Show(
                        this,
                        "Für diesen Kandidaten ist kein aktuelles Bridge-v3-Metadatendokument verfügbar.\n\n" +
                        "Die Normalizer-Vorschau arbeitet ausschließlich auf dem aktiven verlustfreien Bridge-Snapshot.",
                        "DJ Metadata Normalizer",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                try
                {
                    MetadataNormalizerPreviewModel model = MetadataNormalizerPreview.AnalyzeForPreview(
                        item, AppDomain.CurrentDomain.BaseDirectory);
                    MetadataNormalizerPreviewWindow preview = new MetadataNormalizerPreviewWindow(this, item, model);
                    preview.ShowDialog();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        this,
                        "Die Normalizer-Vorschau wurde sicher abgebrochen. Es wurde nichts verändert.\n\n" + ex.Message,
                        "DJ Metadata Normalizer",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            };

            grid.SelectionChanged += delegate
            {
                MatchRow selected = grid.SelectedItem as MatchRow;
                previewNormalizer.IsEnabled = selected != null && data.GetActiveDigitalItem(selected.DigitalItemId) != null;
            };
            grid.MouseDoubleClick += delegate
            {
                if (previewNormalizer.IsEnabled) showNormalizerPreview();
            };
            previewNormalizer.Click += delegate { showNormalizerPreview(); };

            Button close = new Button();
            close.Content = "Close";
            close.Padding = new Thickness(14, 4, 14, 4);
            close.ToolTip = "Close track details.";
            close.Click += delegate { Close(); };
            actions.Children.Add(close);
            PreviewKeyDown += delegate(object sender, System.Windows.Input.KeyEventArgs e) { if (e.Key == System.Windows.Input.Key.Escape) { Close(); e.Handled = true; } };
            HorizontalScrollSupport.Enable(this);
        }

        private static void AddColumn(DataGrid grid, string header, string tip, string path, double width)
        {
            ColumnSpec spec = new ColumnSpec(path, header, tip, path, width, true);
            grid.Columns.Add(UiHelpers.TextColumn(spec));
        }
    }

    public sealed class CdDetailWindow : Window
    {
        public CdDetailWindow(Window owner, CdRow cd, DataStore data)
        {
            Owner = owner;
            Title = "CD-Details — " + cd.AlbumArtist + " — " + cd.Album;
            GridRuntimeSupport.ApplyWindowIcon(this);
            Width = 1160;
            Height = 760;
            MinWidth = 780;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            WindowGeometrySettings.Attach(this, "cd.details");

            DockPanel root = new DockPanel();
            root.Margin = new Thickness(8);
            Content = root;

            Button close = new Button();
            close.Content = "Close";
            close.Padding = new Thickness(14, 4, 14, 4);
            close.Margin = new Thickness(0, 8, 0, 0);
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.ToolTip = "Close CD details.";
            close.Click += delegate { Close(); };
            DockPanel.SetDock(close, Dock.Bottom);
            root.Children.Add(close);

            TabControl tabs = new TabControl();
            root.Children.Add(tabs);

            tabs.Items.Add(CreateOverviewTab(cd));
            tabs.Items.Add(CreateTracksTab(cd, data));
            tabs.Items.Add(CreateTechnicalTab(cd));
            PreviewKeyDown += delegate(object sender, System.Windows.Input.KeyEventArgs e) { if (e.Key == System.Windows.Input.Key.Escape) { Close(); e.Handled = true; } };
            HorizontalScrollSupport.Enable(this);
        }

        private static TabItem CreateOverviewTab(CdRow cd)
        {
            TabItem tab = new TabItem();
            tab.Header = "Overview";
            tab.ToolTip = "Important DJ and release information for the physical CD.";

            ScrollViewer scroll = new ScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            tab.Content = scroll;

            StackPanel stack = new StackPanel();
            stack.Margin = new Thickness(6);
            scroll.Content = stack;

            GroupBox releaseGroup = new GroupBox();
            releaseGroup.Header = "Release";
            releaseGroup.Margin = new Thickness(0, 0, 0, 8);
            releaseGroup.ToolTip = "Metadaten der Release als Ganzes.";
            Grid release = UiHelpers.CreateFourColumnFieldGrid();
            releaseGroup.Content = release;
            UiHelpers.AddField(release, 0, 0, "Album Artist", cd.AlbumArtist, "Album Artist bzw. Interpret der Release.");
            UiHelpers.AddField(release, 0, 1, "Album / CD", cd.Album, "Title des Albums bzw. der CD.");
            UiHelpers.AddField(release, 1, 0, "Year", cd.Date, "Release year / DATE.");
            UiHelpers.AddField(release, 1, 1, "Genre", cd.Genre, "Preferred CD genre. It is updated only when strongly matched digital track genres provide sufficient consensus.");
            UiHelpers.AddField(release, 2, 0, "Label", cd.Label, "Label of the physical release.");
            UiHelpers.AddField(release, 2, 1, "Catalog", cd.Catalog, "Catalog Number der konkreten physischen Release.");
            UiHelpers.AddField(release, 3, 0, "Country", cd.Country, "Country der Release, soweit gepflegt.");
            UiHelpers.AddField(release, 3, 1, "Genre-Source", cd.GenreSourceText, "For 'Digital Track Consensus', at least 50% of tracks must have strong digital matches and at least 80% of that evidence must agree on genre.");
            if (cd.GenreProjected && !String.Equals(cd.LegacyGenre, cd.Genre, StringComparison.OrdinalIgnoreCase))
            {
                UiHelpers.AddField(release, 4, 0, "Legacy Genre", cd.LegacyGenre, "Historisches Music-Library-Genre der CD.");
                UiHelpers.AddField(release, 4, 1, "Digitale Genres", cd.DigitalGenres, "All safely projected GENRE values for tracks on this CD, ordered by frequency.");
            }
            stack.Children.Add(releaseGroup);

            GroupBox discGroup = new GroupBox();
            discGroup.Header = "Physische CD";
            discGroup.Margin = new Thickness(0, 0, 0, 8);
            discGroup.ToolTip = "Information about the specific physical disc.";
            Grid disc = UiHelpers.CreateFourColumnFieldGrid();
            discGroup.Content = disc;
            UiHelpers.AddField(disc, 0, 0, "Disc", cd.DiscText, "Disc Number innerhalb eines Mehrfachsets.");
            UiHelpers.AddField(disc, 0, 1, "Tracks", cd.Tracks.ToString(), "Anzahl der logisch katalogisierten Tracks.");
            UiHelpers.AddField(disc, 1, 0, "Total Duration", cd.DurationText, "Physical CD duration, primarily derived from TOC and lead-out.");
            UiHelpers.AddField(disc, 1, 1, "Medium", cd.Medium, "Physischer Medientyp.");
            UiHelpers.AddField(disc, 2, 0, "Digital", cd.DigitalText, "Number of strongly matched tracks relative to the CD track count, based on the currently active digital collection.");
            UiHelpers.AddField(disc, 2, 1, "CDX Compatible", cd.CdxCompatibilityText, cd.CdxCompatibilityToolTip);
            UiHelpers.AddField(disc, 3, 0, "Stark", cd.StrongTracks.ToString(), "Tracks mit starkem Digitalmatch.");
            UiHelpers.AddField(disc, 3, 1, "Wahrscheinlich", cd.LikelyTracks.ToString(), "Tracks mit wahrscheinlichem Digitalmatch.");
            UiHelpers.AddField(disc, 4, 0, "Kandidat", cd.CandidateTracks.ToString(), "Tracks mit einfachem Digitalkandidaten.");
            UiHelpers.AddField(disc, 4, 1, "Kein Treffer", cd.NoMatchTracks.ToString(), "Tracks ohne Digitalkandidaten.");
            if (!String.IsNullOrEmpty(cd.SpecialText))
                UiHelpers.AddField(disc, 5, 0, "Sonderfall", cd.SpecialText, "Besondere physische/logische Disc-Struktur.");
            stack.Children.Add(discGroup);

            if (!String.IsNullOrEmpty(cd.IssueCode))
            {
                GroupBox issueGroup = new GroupBox();
                issueGroup.Header = "Has Issueskeit";
                issueGroup.ToolTip = "Documented review note from legacy migration.";
                Grid issue = UiHelpers.CreateTwoColumnFieldGrid();
                issueGroup.Content = issue;
                UiHelpers.AddField(issue, 0, 0, "Code", cd.IssueCode, "Internal review code.");
                UiHelpers.AddField(issue, 1, 0, "Explanation", cd.IssueDetail, "Specific reason why the CD is flagged.");
                stack.Children.Add(issueGroup);
            }

            return tab;
        }

        private static TabItem CreateTracksTab(CdRow cd, DataStore data)
        {
            TabItem tab = new TabItem();
            tab.Header = "Tracks";
            tab.ToolTip = "All logisch katalogisierten Tracks dieser CD.";

            DataGrid grid = UiHelpers.CreateReadOnlyGrid();
            grid.Margin = new Thickness(4);
            tab.Content = grid;

            AddTrackColumn(grid, "#", "Tracknummer auf der CD.", "TrackNumber", 50);
            AddTrackColumn(grid, "Artist", "Bereinigter Track-Artist.", "Artist", 185);
            AddTrackColumn(grid, "Title", "Basistitel.", "Title", 240);
            AddTrackColumn(grid, "Mix / Version", "Separate version or remix name.", "Version", 220);
            AddTrackColumn(grid, "Duration", "Tracklaufzeit.", "DurationText", 80);
            AddTrackColumn(grid, "BPM", "BPM, when available.", "BpmText", 70);
            AddTrackColumn(grid, "Genre", "Bevorzugtes Genre; bei sicherem Digitalmatch aus der modernen foobar-Sammlung.", "Genre", 145);
            AddTrackColumn(grid, "Digital", "Automatischer Digital-Matchstatus.", "DigitalText", 145);

            grid.ItemsSource = data.GetTracksForDisc(cd.DiscId);
            grid.MouseDoubleClick += delegate
            {
                TrackRow selected = grid.SelectedItem as TrackRow;
                if (selected != null)
                {
                    TrackDetailWindow w = new TrackDetailWindow(Window.GetWindow(grid), selected, data);
                    w.ShowDialog();
                }
            };
            return tab;
        }

        private static TabItem CreateTechnicalTab(CdRow cd)
        {
            TabItem tab = new TabItem();
            tab.Header = "Technisch / Legacy";
            tab.ToolTip = "Rarely needed technical and historical information.";

            ScrollViewer scroll = new ScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            tab.Content = scroll;

            Grid info = UiHelpers.CreateFourColumnFieldGrid();
            scroll.Content = info;

            UiHelpers.AddBooleanField(info, 0, 0, "TOC Complete", cd.TocComplete, "Ob die gespeicherte Table of Contents zur katalogisierten Trackzahl passt.");
            UiHelpers.AddField(info, 0, 1, "Durationnquelle", cd.DurationSource, "Source der angezeigten physischen CD-Total Duration.");
            UiHelpers.AddField(info, 1, 0, "TOC", cd.Toc, "Original stored CD TOC/offsets; an important physical identity anchor.");
            UiHelpers.AddBooleanField(info, 1, 1, "CD-TEXT Available", cd.CdTextPresent, "Indicates whether DJ Library has verified CD-TEXT. An unchecked box means it is currently unavailable; legacy records were often not checked.");
            UiHelpers.AddField(info, 2, 0, "Physische Tracks", cd.PhysicalTrackCount.ToString(), "Anzahl der physischen Audio-Tracks laut Disc-Struktur.");
            UiHelpers.AddField(info, 2, 1, "Logische Tracks", cd.LogicalTrackCount.ToString(), "Anzahl der in Music Library logisch katalogisierten Title.");
            UiHelpers.AddField(info, 3, 0, "Layout", String.IsNullOrEmpty(cd.SpecialText) ? "Normal" : cd.SpecialText, "Physical/logical disc structure. 'Normal' is not shown in the main view.");
            UiHelpers.AddField(info, 3, 1, "Legacy Serial", cd.LegacySerial, "Historische WenSoftware-Kennung; nur zur Nachvollziehbarkeit erhalten.");
            UiHelpers.AddField(info, 4, 0, "Legacy Packaging", cd.LegacyPackaging, "Historical Music Library field, e.g. 'Non-Stop Mix' or 'Studio'.");
            UiHelpers.AddField(info, 4, 1, "Legacy ReleaseType", cd.LegacyReleaseType, "Historical bit value; not part of the modern DJ model.");
            UiHelpers.AddField(info, 5, 0, "Has Issueskeit", cd.IssueCode, "Legacy review note, if present.");
            UiHelpers.AddField(info, 5, 1, "Explanation", cd.IssueDetail, "Specific explanation of the review note.");
            return tab;
        }

        private static void AddTrackColumn(DataGrid grid, string header, string tip, string path, double width)
        {
            grid.Columns.Add(UiHelpers.TextColumn(new ColumnSpec(path, header, tip, path, width, true)));
        }
    }
}
