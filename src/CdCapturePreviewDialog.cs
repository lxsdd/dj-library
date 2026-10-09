using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Navigation;

namespace DJLibrary
{
    public sealed class CdCaptureDecision
    {
        public bool CreateNewRelease { get; set; }
        public bool UseExistingCatalogDisc { get; set; }
        public CatalogRelease ExistingRelease { get; set; }
        public CatalogTocMatch ExistingCatalogDisc { get; set; }
    }

    public sealed class CdCapturePreviewDialog : Window
    {
        private readonly RadioButton _useExisting;
        private readonly RadioButton _newRelease;
        private readonly RadioButton _existingRelease;
        private readonly ComboBox _releaseCombo;
        private readonly Button _ok;
        private readonly CatalogTocMatch _duplicate;
        private readonly IList<CatalogTocMatch> _duplicates;
        private readonly CdSnapshot _physical;
        private readonly CdSnapshot _snapshot;
        private readonly Dictionary<string, TextBlock> _summary = new Dictionary<string, TextBlock>(StringComparer.OrdinalIgnoreCase);
        private readonly DataGrid _tracks;
        private readonly StackPanel _sourcePanel;
        private readonly Button _chooseValues;
        private readonly AppSettings _settings;
        private CdMetadataFetchOptions _defaults;
        private CdMetadataReport _report;
        private CdMetadataSelectionSession _selection;

        public CdCaptureDecision Decision { get; private set; }

        private CdCapturePreviewDialog(Window owner, string driveName, CdSnapshot snapshot, IList<CatalogRelease> releases,
            CatalogRelease suggested, IList<CatalogTocMatch> duplicates, CdMetadataFetchOptions defaults)
        {
            Owner = owner;
            Title = "Read Audio CD";
            GridRuntimeSupport.ApplyWindowIcon(this);
            Width = 1180;
            Height = 790;
            MinWidth = 900;
            MinHeight = 620;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowGeometrySettings.Attach(this, "cd.capture");
            if (snapshot == null) throw new ArgumentNullException("snapshot");
            _snapshot = snapshot;
            _physical = CloneSnapshot(snapshot);
            _duplicates = duplicates ?? new List<CatalogTocMatch>();
            _duplicate = _duplicates.FirstOrDefault();
            _settings = SettingsManager.Load();
            _defaults = defaults == null ? new CdMetadataFetchOptions() : defaults.Clone();

            DockPanel root = new DockPanel { Margin = new Thickness(12) };
            Content = root;

            StackPanel summary = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(summary, Dock.Top);
            root.Children.Add(summary);

            string cdx = CdxCompatibility.Text(CdxCompatibility.Classify(_snapshot.Toc, true));
            double duration = _snapshot.Tracks.Sum(x => Math.Max(0, x.DurationSeconds));
            summary.Children.Add(Line("Drive", driveName));
            summary.Children.Add(Line("Tracks", _snapshot.Tracks.Count.ToString()));
            summary.Children.Add(Line("Total Duration", UiHelpers.FormatDuration(duration)));
            summary.Children.Add(Line("CDX Compatible", cdx));
            summary.Children.Add(Line("CD-TEXT", CdTextStatusText(_snapshot)));
            summary.Children.Add(SummaryLine("albumArtist", "Album Artist", _snapshot.AlbumArtist));
            summary.Children.Add(SummaryLine("album", "Album", _snapshot.Album));
            summary.Children.Add(SummaryLine("date", "Date/Year", _snapshot.ReleaseDate));
            summary.Children.Add(SummaryLine("genre", "Genre/Style", _snapshot.Genre));
            summary.Children.Add(SummaryLine("label", "Label", _snapshot.Label));
            summary.Children.Add(SummaryLine("catalog", "Catalog Number", _snapshot.Catalog));
            summary.Children.Add(SummaryLine("country", "Country", _snapshot.Country));

            Expander sources = new Expander { Header = "Metadata Matching", IsExpanded = true, Margin = new Thickness(0, 5, 0, 5), ToolTip="Physical TOC/CD-TEXT are read first. Optional matching fetches additional metadata sets; Review Sources and Values chooses the base set and overrides." };
            _sourcePanel = new StackPanel { Margin = new Thickness(8, 4, 8, 4) };
            sources.Content = _sourcePanel;
            summary.Children.Add(sources);

            _chooseValues = new Button
            {
                Content = "Review Metadata…",
                MinWidth = 225,
                Padding = new Thickness(8, 3, 8, 3),
                IsEnabled = false,
                ToolTip = "Review the effective metadata result, missing fields and alternatives; choose starting values or apply explicit source changes where needed."
            };
            _chooseValues.Click += delegate
            {
                if (_selection == null || !CdMetadataChoiceDialog.Edit(this, _selection)) return;
                _selection.ApplyTo(_snapshot);
                RefreshPreview();
            };

            GroupBox destination = new GroupBox { Header = "Choose destination after reading and optional metadata matching", Margin = new Thickness(0, 10, 0, 0) };
            DockPanel.SetDock(destination, Dock.Bottom);
            StackPanel dest = new StackPanel { Margin = new Thickness(10) };
            destination.Content = dest;

            if (_duplicate != null)
            {
                _useExisting = new RadioButton
                {
                    Content = "Use existing disc (no duplicate): " + _duplicate.DisplayText,
                    IsChecked = true,
                    Margin = new Thickness(0, 0, 0, 7),
                    ToolTip = "Use the already cataloged disc identified by exact physical TOC. No duplicate disc is created and metadata matching remains comparison-only unless explicitly reviewed/applied."
                };
                dest.Children.Add(_useExisting);
            }
            _newRelease = new RadioButton { Content = "Create New Release", IsChecked = _duplicate == null, Margin = new Thickness(0, 0, 0, 7), ToolTip="Create a new catalog release from the physical disc plus the reviewed metadata result." };
            _existingRelease = new RadioButton { Content = "Add Disc to Existing Release", Margin = new Thickness(0, 0, 0, 6), ToolTip="Add this physical disc to an existing catalog release. The selected release is not silently overwritten by metadata matching." };
            dest.Children.Add(_newRelease);
            dest.Children.Add(_existingRelease);

            _releaseCombo = new ComboBox { MinWidth = 420, DisplayMemberPath = "DisplayText", IsEnabled = false, Margin = new Thickness(22, 0, 0, 10), ToolTip="Target catalog release used only when Add Disc to Existing Release is selected." };
            ToolTipService.SetShowOnDisabled(_releaseCombo,true);
            foreach (CatalogRelease release in releases ?? new List<CatalogRelease>()) _releaseCombo.Items.Add(release);
            if (suggested != null)
            {
                CatalogRelease match = _releaseCombo.Items.Cast<CatalogRelease>().FirstOrDefault(x => x.Id == suggested.Id);
                if (match != null) _releaseCombo.SelectedItem = match;
            }
            if (_releaseCombo.SelectedIndex < 0 && _releaseCombo.Items.Count > 0) _releaseCombo.SelectedIndex = 0;
            dest.Children.Add(_releaseCombo);

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            _ok = new Button { Content = "Continue…", Width = 100, IsDefault = true, Margin = new Thickness(5, 0, 0, 0), ToolTip="Continue with the selected catalog destination. Reviewed metadata values are applied to the preview before import." };
            ToolTipService.SetShowOnDisabled(_ok,true);
            Button cancel = new Button { Content = "Cancel", Width = 100, IsCancel = true, Margin = new Thickness(5, 0, 0, 0), ToolTip="Close Read Audio CD without importing the disc." };
            buttons.Children.Add(_ok); buttons.Children.Add(cancel); dest.Children.Add(buttons); root.Children.Add(destination);

            _tracks = CreateTrackGrid();
            root.Children.Add(_tracks);
            RefreshPreview();
            GridLayoutSettings.Apply(_settings, "cd.capture.tracks", _tracks);

            _existingRelease.Checked += delegate { _releaseCombo.IsEnabled = true; UpdateOk(); };
            _existingRelease.Unchecked += delegate { _releaseCombo.IsEnabled = false; UpdateOk(); };
            _newRelease.Checked += delegate { UpdateOk(); };
            if (_useExisting != null) _useExisting.Checked += delegate { UpdateOk(); };
            _releaseCombo.SelectionChanged += delegate { UpdateOk(); };
            _ok.Click += delegate
            {
                if (_selection != null) _selection.ApplyTo(_snapshot);
                Decision = new CdCaptureDecision
                {
                    UseExistingCatalogDisc = _useExisting != null && _useExisting.IsChecked == true,
                    ExistingCatalogDisc = _useExisting != null && _useExisting.IsChecked == true ? _duplicate : null,
                    CreateNewRelease = _newRelease.IsChecked == true,
                    ExistingRelease = _existingRelease.IsChecked == true ? _releaseCombo.SelectedItem as CatalogRelease : null
                };
                DialogResult = true;
            };
            Closing += delegate
            {
                AppSettings latest = SettingsManager.Load();
                GridLayoutSettings.Capture(latest, "cd.capture.tracks", _tracks);
                SettingsManager.Save(latest);
            };
            RefreshSourcePanel();
            UpdateOk();
        }

        private static DataGrid CreateTrackGrid()
        {
            DataGrid grid = UiHelpers.CreateReadOnlyGrid();
            grid.Columns.Add(UiHelpers.BoundTextColumn("#", "Physical track order from the disc TOC.", "Position", "Position", new DataGridLength(45),45,"PositionToolTip"));
            grid.Columns.Add(UiHelpers.BoundTextColumn("Artist", "Current effective track artist after metadata review.", "Artist", "Artist", new DataGridLength(220),140,"Artist"));
            grid.Columns.Add(UiHelpers.BoundTextColumn("Title", "Current effective track title after metadata review.", "Title", "Title", new DataGridLength(1,DataGridLengthUnitType.Star),220,"Title"));
            grid.Columns.Add(UiHelpers.BoundTextColumn("Mix/Version", "Structured mix/version metadata.", "Version", "Version", new DataGridLength(215),150,"Version"));
            grid.Columns.Add(UiHelpers.BoundTextColumn("Genre", "Current effective track genre.", "Genre", "Genre", new DataGridLength(145),110,"Genre"));
            grid.Columns.Add(UiHelpers.BoundTextColumn("Review", "Compact metadata-review status for this track. Hover for source/evidence details where available.", "Source", "Source", new DataGridLength(145),115,"SourceToolTip"));
            grid.Columns.Add(UiHelpers.BoundTextColumn("Duration", "Physical track duration derived from the Audio CD TOC; metadata matching does not replace it.", "DurationText", "DurationSeconds", new DataGridLength(85),75,"DurationText"));
            GridGovernance.Apply(grid);
            return grid;
        }

        private void StartMetadataLookup()
        {
            CdMetadataSourcePickerResult picked;
            CdMetadataFetchOptions lookupDefaults = BuildLookupDefaults();
            if (!CdMetadataSourcePickerDialog.Choose(this, lookupDefaults, out picked) || picked == null || picked.Options == null) return;
            _defaults = picked.Options.Clone();
            if (picked.RememberAsDefault)
            {
                AppSettings latest = SettingsManager.Load();
                latest.CdMetadataDefaults = new CdMetadataFetchOptions
                {
                    UseCatalog=_defaults.UseCatalog, UseFoobar=_defaults.UseFoobar, UseMusicBrainz=_defaults.UseMusicBrainz,
                    UseDiscogs=_defaults.UseDiscogs, UseTitleAnalysis=_defaults.UseTitleAnalysis
                };
                SettingsManager.Save(latest);
            }

            RestoreSnapshot(_snapshot, _physical);
            _report = CdMetadataPipeline.Enrich(_snapshot, _defaults);
            CdMetadataPostProcessor.Augment(_snapshot, _duplicates, _report, _defaults);
            _selection = new CdMetadataSelectionSession(_snapshot, _report);
            // The initial base is a deterministic starting point. The user may
            // review/change it explicitly; physical identity remains in _physical.
            _selection.ApplyTo(_snapshot);
            _chooseValues.IsEnabled = true;
            RefreshSourcePanel();
            RefreshPreview();
        }

        private CdMetadataFetchOptions BuildLookupDefaults()
        {
            CdMetadataFetchOptions value = _defaults == null ? new CdMetadataFetchOptions() : _defaults.Clone();
            if (String.IsNullOrWhiteSpace(value.SearchAlbumArtist)) value.SearchAlbumArtist = _physical.AlbumArtist ?? "";
            if (String.IsNullOrWhiteSpace(value.SearchAlbum)) value.SearchAlbum = _physical.Album ?? "";
            if (String.IsNullOrWhiteSpace(value.SearchYear)) value.SearchYear = _physical.ReleaseDate ?? "";
            if (String.IsNullOrWhiteSpace(value.SearchLabel)) value.SearchLabel = _physical.Label ?? "";
            if (String.IsNullOrWhiteSpace(value.SearchCatalog)) value.SearchCatalog = _physical.Catalog ?? "";
            if (String.IsNullOrWhiteSpace(value.SearchCountry)) value.SearchCountry = _physical.Country ?? "";

            if (_duplicate != null && File.Exists(CatalogService.DefaultCatalogPath))
            {
                try
                {
                    using (CatalogService catalog = CatalogService.OpenForTesting(CatalogService.DefaultCatalogPath))
                    {
                        CatalogRelease release = catalog.GetRelease(_duplicate.ReleaseId);
                        if (release != null)
                        {
                            if (String.IsNullOrWhiteSpace(value.SearchAlbumArtist)) value.SearchAlbumArtist = release.AlbumArtist ?? "";
                            if (String.IsNullOrWhiteSpace(value.SearchAlbum)) value.SearchAlbum = release.Album ?? "";
                            if (String.IsNullOrWhiteSpace(value.SearchYear)) value.SearchYear = release.ReleaseDate ?? "";
                            if (String.IsNullOrWhiteSpace(value.SearchLabel)) value.SearchLabel = release.Label ?? "";
                            if (String.IsNullOrWhiteSpace(value.SearchCatalog)) value.SearchCatalog = release.Catalog ?? "";
                            if (String.IsNullOrWhiteSpace(value.SearchCountry)) value.SearchCountry = release.Country ?? "";
                        }
                    }
                }
                catch { }
            }
            return value;
        }

        private void RefreshSourcePanel()
        {
            if (_sourcePanel == null) return;
            Panel previousParent = _chooseValues.Parent as Panel;
            if (previousParent != null) previousParent.Children.Remove(_chooseValues);
            _sourcePanel.Children.Clear();
            _sourcePanel.Children.Add(new TextBlock
            {
                Text = "Physical TOC/CD-TEXT stay authoritative. Match Metadata fetches optional comparison sources; Review Metadata resolves only metadata values.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4),
                ToolTip="Track order and durations always remain physical. Metadata matching never replaces them."
            });

            List<MetadataSourceStatus> statuses=new List<MetadataSourceStatus>();
            statuses.Add(new MetadataSourceStatus { Source="CD-TEXT",Text=CdTextStatusText(_physical),Url="" });
            if(_duplicate!=null) statuses.Add(new MetadataSourceStatus { Source="Catalog",Text="exact full TOC match: "+_duplicate.DisplayText,Url="" });
            if(_report!=null)
            {
                foreach(MetadataSourceStatus status in _report.Sources)
                {
                    if(String.Equals(status.Source,"CD-TEXT",StringComparison.OrdinalIgnoreCase)) continue;
                    statuses.Add(status);
                }
            }
            int usable=statuses.Count(x=>!String.IsNullOrWhiteSpace(x.Text) && x.Text.IndexOf("no match",StringComparison.OrdinalIgnoreCase)<0 && x.Text.IndexOf("cannot",StringComparison.OrdinalIgnoreCase)<0 && x.Text.IndexOf("not present",StringComparison.OrdinalIgnoreCase)<0 && x.Text.IndexOf("no additional",StringComparison.OrdinalIgnoreCase)<0);
            TextBlock summary=new TextBlock { Text="Metadata sources: "+usable.ToString()+" usable · "+Math.Max(0,statuses.Count-usable).ToString()+" unavailable/no additional result",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,2,0,2),ToolTip="Expand Source details to see provider-specific matching evidence and links." };
            _sourcePanel.Children.Add(summary);
            StackPanel detailPanel=new StackPanel { Margin=new Thickness(8,3,0,3) };
            foreach(MetadataSourceStatus status in statuses) detailPanel.Children.Add(SourceLine(status));
            Expander details=new Expander { Header="Source details",IsExpanded=false,Content=detailPanel,Margin=new Thickness(0,1,0,3),ToolTip="Provider-specific diagnostics and Open Source links." };
            _sourcePanel.Children.Add(details);

            if (_selection != null)
            {
                string starting=String.Equals(_selection.BaseSource,"Recommended",StringComparison.OrdinalIgnoreCase)?"Recommended values":_selection.BaseSource+" starting values";
                TextBlock resultLine=new TextBlock
                {
                    Text="Metadata result: "+starting+" · "+_selection.OverallReviewSummary(),
                    FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,1),
                    ToolTip="Open Review Metadata to inspect missing fields, alternatives, current sources and explicit field/source changes."
                };
                _sourcePanel.Children.Add(resultLine);
            }

            StackPanel metadataButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 7, 0, 1) };
            Button lookup = new Button { Content = "Match Metadata…", MinWidth = 165, Margin = new Thickness(0,0,6,0), Padding = new Thickness(8,3,8,3), ToolTip="Choose local/online sources and fetch comparison metadata. Physical TOC, order and durations are unchanged." };
            lookup.Click += delegate { StartMetadataLookup(); };
            metadataButtons.Children.Add(lookup);
            _chooseValues.IsEnabled = _selection != null;
            metadataButtons.Children.Add(_chooseValues);
            _sourcePanel.Children.Add(metadataButtons);
            if (_duplicate != null)
                _sourcePanel.Children.Add(new TextBlock { Text = "Existing exact-TOC Catalog values remain the safe starting point until you explicitly change the reviewed metadata result.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,5,0,0), ToolTip="Matching never silently overwrites an existing catalog record." });
        }

        private void RefreshPreview()
        {
            TextBlock value;
            if (_summary.TryGetValue("albumArtist", out value)) value.Text = Convert.ToString(value.Tag) + ": " + Safe(_snapshot.AlbumArtist);
            if (_summary.TryGetValue("album", out value)) value.Text = Convert.ToString(value.Tag) + ": " + Safe(_snapshot.Album);
            if (_summary.TryGetValue("date", out value)) value.Text = Convert.ToString(value.Tag) + ": " + Safe(_snapshot.ReleaseDate);
            if (_summary.TryGetValue("genre", out value)) value.Text = Convert.ToString(value.Tag) + ": " + Safe(_snapshot.Genre);
            if (_summary.TryGetValue("label", out value)) value.Text = Convert.ToString(value.Tag) + ": " + Safe(_snapshot.Label);
            if (_summary.TryGetValue("catalog", out value)) value.Text = Convert.ToString(value.Tag) + ": " + Safe(_snapshot.Catalog);
            if (_summary.TryGetValue("country", out value)) value.Text = Convert.ToString(value.Tag) + ": " + Safe(_snapshot.Country);
            if (_tracks != null) _tracks.ItemsSource = _snapshot.Tracks.Select(x => new PreviewTrack(x, _snapshot.CdTextPresent)).ToList();
        }

        private TextBlock SummaryLine(string key, string label, string value)
        {
            TextBlock text = new TextBlock { Text = label + ": " + Safe(value), Tag = label, Margin = new Thickness(0,1,0,1), TextWrapping = TextWrapping.Wrap };
            _summary[key] = text; return text;
        }

        private static string Safe(string value) { return String.IsNullOrWhiteSpace(value) ? "—" : value; }
        private void UpdateOk()
        {
            if (_ok == null) return;
            _ok.IsEnabled = (_useExisting != null && _useExisting.IsChecked == true) || _newRelease.IsChecked == true || (_existingRelease.IsChecked == true && _releaseCombo.SelectedItem != null);
        }
        private static TextBlock Line(string label, string value) { return new TextBlock { Text = label + ": " + Safe(value), Margin = new Thickness(0,1,0,1) }; }

        private static TextBlock SourceLine(MetadataSourceStatus status)
        {
            TextBlock line = new TextBlock { Margin = new Thickness(0,1,0,1), TextWrapping = TextWrapping.Wrap, ToolTip=(status.Source ?? "Source") + ": " + (status.Text ?? "") };
            line.Inlines.Add(new Run((status.Source ?? "Source") + ": " + (status.Text ?? "")));
            if (!String.IsNullOrWhiteSpace(status.Url))
            {
                line.Inlines.Add(new Run(" · "));
                Hyperlink link = new Hyperlink(new Run("Open Source")) { NavigateUri = new Uri(status.Url) };
                link.RequestNavigate += delegate(object sender, RequestNavigateEventArgs e)
                {
                    try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); } catch { }
                    e.Handled = true;
                };
                line.Inlines.Add(link);
            }
            return line;
        }

        private static string CdTextStatusText(CdSnapshot snapshot)
        {
            if (snapshot.CdTextStatus == "drive_present") return "present · " + (String.IsNullOrWhiteSpace(snapshot.CdTextSource) ? "read directly from disc" : snapshot.CdTextSource);
            if (snapshot.CdTextStatus == "drive_absent") return "checked: not present";
            if (snapshot.CdTextStatus == "drive_read_error") return "read error";
            return "cannot be checked reliably through this access path";
        }

        public static bool Choose(Window owner, string driveName, CdSnapshot snapshot, IList<CatalogRelease> releases, CatalogRelease suggested,
            IList<CatalogTocMatch> duplicates, CdMetadataFetchOptions defaults, out CdCaptureDecision decision)
        {
            CdCapturePreviewDialog dialog = new CdCapturePreviewDialog(owner, driveName, snapshot, releases, suggested, duplicates, defaults);
            bool ok = dialog.ShowDialog() == true;
            decision = ok ? dialog.Decision : null;
            return ok;
        }

        internal static string ValidateManualMetadataSelectionContract()
        {
            CdSnapshot snapshot = new CdSnapshot { Toc = "150 45150", CdTextPresent = true, CdTextStatus = "drive_present", Album = "Physical Album", AlbumArtist = "Physical Artist" };
            snapshot.Tracks.Add(new CdTrackCapture { Position = 1, Title = "Physical Title (Extended Mix)", RawTitle = "Physical Title (Extended Mix)", Artist = "Physical Artist", DurationSeconds = 600 });
            CdCapturePreviewDialog dialog = new CdCapturePreviewDialog(null, "D:", snapshot, new List<CatalogRelease>(), null, new List<CatalogTocMatch>(), new CdMetadataFetchOptions());
            if (dialog._selection != null || dialog._report != null || dialog._chooseValues.IsEnabled)
                throw new InvalidOperationException("Audio CD production path must not perform additional metadata matching before explicit source selection.");
            GridGovernance.Validate(dialog._tracks, "Audio CD Track Preview");
            if (dialog._chooseValues.ToolTip == null || dialog._releaseCombo.ToolTip == null || dialog._ok.ToolTip == null)
                throw new InvalidOperationException("Audio CD production path is missing required discoverability tooltips.");
            FrameworkElement metadataSourceHeader = dialog._tracks.Columns.FirstOrDefault(x => x.SortMemberPath == "Source").Header as FrameworkElement;
            if (metadataSourceHeader == null || metadataSourceHeader.ToolTip == null)
                throw new InvalidOperationException("Audio CD Review column has no production tooltip.");
            string[] expected = { "Position", "Artist", "Title", "Version" };
            for (int i=0;i<expected.Length;i++)
                if (!String.Equals(dialog._tracks.Columns[i].SortMemberPath, expected[i], StringComparison.Ordinal))
                    throw new InvalidOperationException("Audio CD Trackvorschau verletzt Standardreihenfolge #/Artist/Title/Mix-Version.");

            // Regression: the source area is rebuilt after every completed metadata lookup.
            // Reusing _chooseValues must detach it from the previous button row first; otherwise
            // WPF throws InvalidOperationException/AddLogicalChild on the second refresh.
            dialog.RefreshSourcePanel();
            dialog.RefreshSourcePanel();
            Panel choiceParent = dialog._chooseValues.Parent as Panel;
            if (choiceParent == null || !dialog._sourcePanel.Children.Contains(choiceParent))
                throw new InvalidOperationException("Metadatenbereich verliert den wiederverwendeten Feldwerte-Button beim wiederholten Refresh.");
            return "physical-first TOC/CD-TEXT preview + concise/collapsible source diagnostics + explicit pre-fetch source selection + governed Review grid + reentrant metadata source-panel rebuild";
        }

        private static CdSnapshot CloneSnapshot(CdSnapshot source)
        {
            CdSnapshot copy = new CdSnapshot();
            RestoreSnapshot(copy, source); return copy;
        }

        private static void RestoreSnapshot(CdSnapshot target, CdSnapshot source)
        {
            target.DriveId=source.DriveId; target.Toc=source.Toc; target.CdTextPresent=source.CdTextPresent; target.CdTextStatus=source.CdTextStatus; target.CdTextSource=source.CdTextSource; target.CdTextError=source.CdTextError;
            target.Album=source.Album; target.AlbumArtist=source.AlbumArtist; target.ReleaseDate=source.ReleaseDate; target.Genre=source.Genre; target.Label=source.Label; target.Catalog=source.Catalog; target.Country=source.Country;
            target.MusicBrainzDiscId=""; target.MusicBrainzReleaseId=""; target.DiscogsReleaseId=""; target.MetadataEvidence=new List<MetadataEvidence>(); target.MetadataSources=new List<MetadataSourceStatus>();
            target.Tracks.Clear();
            foreach (CdTrackCapture track in source.Tracks)
                target.Tracks.Add(new CdTrackCapture { Position=track.Position, DurationSeconds=track.DurationSeconds, Title=track.Title, RawTitle=track.RawTitle, Artist=track.Artist, Version=track.Version, Genre=track.Genre, MetadataSource=track.MetadataSource, MetadataDetail=track.MetadataDetail, MetadataConfidence=track.MetadataConfidence });
        }

        private sealed class PreviewTrack
        {
            public int Position { get; private set; }
            public string Title { get; private set; }
            public string Version { get; private set; }
            public string Artist { get; private set; }
            public string Genre { get; private set; }
            public string Source { get; private set; }
            public string SourceToolTip { get; private set; }
            public string PositionToolTip { get { return "Physical track position " + Position.ToString() + "."; } }
            public double DurationSeconds { get; private set; }
            public string DurationText { get; private set; }
            public PreviewTrack(CdTrackCapture source, bool cdTextPresent)
            {
                Position=source.Position; Title=Safe(source.Title); Version=Safe(source.Version); Artist=Safe(source.Artist); Genre=Safe(source.Genre);
                Source=String.IsNullOrWhiteSpace(source.MetadataSource)?(cdTextPresent?"CD-TEXT":"TOC"):source.MetadataSource;
                SourceToolTip=String.IsNullOrWhiteSpace(source.MetadataDetail)?("Metadata review: "+Source):source.MetadataDetail;
                DurationSeconds=source.DurationSeconds; DurationText=UiHelpers.FormatDuration(source.DurationSeconds);
            }
        }
    }
}
