using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DJLibrary
{
    public sealed partial class MainWindow : Window
    {
        private readonly DataStore _data;
        private readonly AppSettings _settings;
        private readonly DataGrid _trackGrid;
        private readonly DataGrid _cdGrid;
        private readonly TabControl _tabs;
        private readonly TextBox _searchBox;
        private readonly StackPanel _searchActions;
        private readonly Button _resetFiltersButton;
        private readonly ScrollViewer _filterScroll;
        private readonly StackPanel _filterOptions;
        private readonly ComboBox _genreCombo;
        private readonly ComboBox _digitalCombo;
        private readonly ComboBox _yearCombo;
        private readonly ComboBox _mediumCombo;
        private readonly ComboBox _mixCombo;
        private readonly ComboBox _labelCombo;
        private readonly ComboBox _issueCombo;
        private readonly ComboBox _cdxCombo;
        private readonly StackPanel _mediumPanel;
        private readonly StackPanel _cdxPanel;
        private readonly StackPanel _mixPanel;
        private readonly TextBlock _countText;
        private readonly TextBlock _statusText;
        private BridgeSnapshotStatus _bridgeStatus;
        private readonly DispatcherTimer _bridgeTimer;
        private readonly TextBlock _filterStatusText;
        private readonly TextBlock _digitalStatusText;
        private readonly DispatcherTimer _filterTimer;
        private ICollectionView _trackView;
        private ICollectionView _cdView;
        private long _activeBridgeGeneration;
        private int _activeBridgeItemCount;
        private string _activeBridgeDirectory = "";
        private string _activeBridgeSourceName = "";
        private string _bridgeDirectory;
        private bool _bridgeDirectoryPinned;
        private bool _suppressFilters;
        private bool _lastMainMaximized;
        private readonly FilterState _trackFilter = new FilterState();
        private readonly FilterState _cdFilter = new FilterState();

        private readonly List<ColumnSpec> _trackColumns;
        private readonly List<ColumnSpec> _cdColumns;

        public MainWindow(DataStore data)
        {
            _data = data;
            _settings = SettingsManager.Load();
            DiscPlaybackRule.Configure(_settings.DiscPlayback);
            _lastMainMaximized = _settings.Maximized;
            _bridgeDirectoryPinned = !String.IsNullOrWhiteSpace(_settings.BridgeDirectory);
            _bridgeDirectory = ResolveInitialBridgeDirectory(_settings.BridgeDirectory);

            // A discovered bridge is not the same thing as an activated bridge. Start with
            // an empty status and let MainLoaded perform the first authoritative load before
            // filter options are populated. This prevents a pre-existing generation from being
            // mistaken for already-applied live data.
            _bridgeStatus = new BridgeSnapshotStatus { DirectoryPath = _bridgeDirectory };

            Title = BuildInfo.WindowTitle;
            GridRuntimeSupport.ApplyWindowIcon(this);
            CopyFilter(_settings.TrackFilter, _trackFilter);
            CopyFilter(_settings.CdFilter, _cdFilter);
            MinWidth = 980;
            MinHeight = 600;
            Width = _settings.WindowWidth > 600 ? _settings.WindowWidth : 1380;
            Height = _settings.WindowHeight > 400 ? _settings.WindowHeight : 850;

            if (!Double.IsNaN(_settings.WindowLeft) && !Double.IsNaN(_settings.WindowTop))
            {
                Left = _settings.WindowLeft;
                Top = _settings.WindowTop;
                WindowStartupLocation = WindowStartupLocation.Manual;
                EnsureWindowOnVisibleDesktop();
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            _trackColumns = BuildTrackColumnSpecs();
            _cdColumns = BuildCdColumnSpecs();

            DockPanel root = new DockPanel();
            Content = root;

            Menu menu = BuildMenu();
            DockPanel.SetDock(menu, Dock.Top);
            root.Children.Add(menu);

            ToolBarTray toolBarTray = BuildToolBar();
            DockPanel.SetDock(toolBarTray, Dock.Top);
            root.Children.Add(toolBarTray);

            StatusBar status = new StatusBar();
            DockPanel.SetDock(status, Dock.Bottom);
            _statusText = new TextBlock();
            _statusText.Text = "Ready";
            _statusText.ToolTip = "Current view and number of visible records.";
            status.Items.Add(_statusText);
            status.Items.Add(new Separator());

            _filterStatusText = new TextBlock();
            _filterStatusText.Text = "Filters: off";
            _filterStatusText.ToolTip = "Number of active search/filter conditions.";
            status.Items.Add(_filterStatusText);
            status.Items.Add(new Separator());

            _digitalStatusText = new TextBlock();
            _digitalStatusText.Text = String.Format("Digital Index: Test · {0:N0} candidates · Genres: {1:N0} Tracks / {2:N0} CDs updated", _data.DigitalItemCount, _data.ProjectedTrackGenreCount, _data.ProjectedCdGenreCount);
            _digitalStatusText.ToolTip = "The qualified test index remains active until a complete Bridge snapshot has been activated successfully. LegacyGenre is always preserved.";
            status.Items.Add(_digitalStatusText);
            status.Items.Add(new Separator());

            _totalsText = new TextBlock();
            _totalsText.Text = String.Format("{0:N0} CDs · {1:N0} Tracks", _data.Cds.Count, _data.Tracks.Count);
            _totalsText.ToolTip = "Physical collection from the writable SQLite catalog (single source of truth).";
            status.Items.Add(_totalsText);
            root.Children.Add(status);

            Border filterBorder = new Border();
            filterBorder.BorderBrush = SystemColors.ControlLightBrush;
            filterBorder.BorderThickness = new Thickness(0, 0, 0, 1);
            filterBorder.Background = SystemColors.ControlBrush;
            filterBorder.Padding = new Thickness(8, 5, 8, 5);
            DockPanel.SetDock(filterBorder, Dock.Top);
            root.Children.Add(filterBorder);

            // Search + Reset stay together at the left. All secondary filter
            // groups have a single stable baseline: overflow scrolls horizontally
            // instead of wrapping into a second line at ordinary desktop widths.
            Grid filterLayout = new Grid();
            filterLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            filterLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            filterBorder.Child = filterLayout;

            _searchActions = InlinePanel();
            Grid.SetColumn(_searchActions, 0);
            filterLayout.Children.Add(_searchActions);

            _filterOptions = new StackPanel {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            _filterScroll = new ScrollViewer {
                Content = _filterOptions,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                CanContentScroll = false,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_filterScroll, 1);
            filterLayout.Children.Add(_filterScroll);
            StackPanel filter = _filterOptions;

            StackPanel searchPanel = _searchActions;
            searchPanel.Children.Add(FilterLabel("Search:", "Searches Artist, Title, Mix/Version, Album, Genre, Label, Catalog Number, and other core fields."));
            _searchBox = new TextBox();
            _searchBox.Width = 270;
            _searchBox.Height = 26;
            _searchBox.VerticalContentAlignment = VerticalAlignment.Center;
            _searchBox.Margin = new Thickness(4, 0, 6, 0);
            _searchBox.ToolTip = "Free-text search. Multiple terms are combined with AND. Ctrl+F focuses this field.";
            searchPanel.Children.Add(_searchBox);
            _resetFiltersButton = ToolButton("Reset Filters",
                "Reset search and all filters for the current view (Ctrl+L).",
                delegate { ResetFilters(); });
            _resetFiltersButton.Height = 26;
            _resetFiltersButton.Margin = new Thickness(0, 0, 10, 0);
            _resetFiltersButton.VerticalAlignment = VerticalAlignment.Center;
            searchPanel.Children.Add(_resetFiltersButton);

            StackPanel genrePanel = InlinePanel();
            genrePanel.Children.Add(FilterLabel("Genre:", "Filters by preferred genre. A strong digital match uses the current foobar GENRE; otherwise the historical collection genre is retained."));
            _genreCombo = Combo(145, "Genre filter for the current view.");
            genrePanel.Children.Add(_genreCombo);
            filter.Children.Add(genrePanel);

            StackPanel digitalPanel = InlinePanel();
            digitalPanel.Children.Add(FilterLabel("Digital:", "Filters by automatic digital-match quality."));
            _digitalCombo = Combo(135, "Final digital status is calculated automatically from the foobar Bridge index.");
            digitalPanel.Children.Add(_digitalCombo);
            filter.Children.Add(digitalPanel);

            StackPanel yearPanel = InlinePanel();
            yearPanel.Children.Add(FilterLabel("Year:", "Filters by release year."));
            _yearCombo = Combo(80, "Year filter.");
            yearPanel.Children.Add(_yearCombo);
            filter.Children.Add(yearPanel);

            StackPanel labelPanel = InlinePanel();
            labelPanel.Children.Add(FilterLabel("Label:", "Filters the current view by label."));
            _labelCombo = Combo(135, "Label filter for tracks and CDs.");
            labelPanel.Children.Add(_labelCombo);
            filter.Children.Add(labelPanel);

            StackPanel issuePanel = InlinePanel();
            issuePanel.Children.Add(FilterLabel("Check:", "Filters by documented legacy issues."));
            _issueCombo = Combo(115, "Shows all, only records with issues, or only records without issues.");
            issuePanel.Children.Add(_issueCombo);
            filter.Children.Add(issuePanel);

            _mediumPanel = InlinePanel();
            _mediumPanel.Children.Add(FilterLabel("Medium:", "Filters CDs by medium type."));
            _mediumCombo = Combo(110, "Medium type of the physical release.");
            _mediumPanel.Children.Add(_mediumCombo);
            filter.Children.Add(_mediumPanel);

            _cdxPanel = InlinePanel();
            _cdxPanel.Children.Add(FilterLabel("Disc playable:", "Filters CDs and their tracks according to the configured physical-disc playback limit."));
            _cdxCombo = Combo(115, "Compatibility of the containing physical disc: Yes, No, or Unknown.");
            _cdxPanel.Children.Add(_cdxCombo);
            filter.Children.Add(_cdxPanel);

            _mixPanel = InlinePanel();
            _mixPanel.Children.Add(FilterLabel("Mix:", "Filters tracks by whether a separate Mix/Version value is present."));
            _mixCombo = Combo(105, "Mix/Version filter.");
            _mixPanel.Children.Add(_mixCombo);
            filter.Children.Add(_mixPanel);

            // Die Trefferzahl steht bereits vollständig in der Statusleiste. Sie wird
            // weiterhin intern gepflegt, aber nicht noch einmal in der Filterleiste angezeigt.
            _countText = new TextBlock();

            _tabs = new TabControl();
            root.Children.Add(_tabs);

            TabItem trackTab = new TabItem();
            trackTab.Header = "Tracks";
            trackTab.ToolTip = "Track view of the physical CD collection.";
            _trackGrid = UiHelpers.CreateReadOnlyGrid();
            trackTab.Content = _trackGrid;
            _tabs.Items.Add(trackTab);

            TabItem cdTab = new TabItem();
            cdTab.Header = "CDs";
            cdTab.ToolTip = "CD/disc view of the physical collection.";
            _cdGrid = UiHelpers.CreateReadOnlyGrid();
            cdTab.Content = _cdGrid;
            _tabs.Items.Add(cdTab);

            AddColumns(_trackGrid, _trackColumns);
            AddColumns(_cdGrid, _cdColumns);
            _cdGrid.AlternatingRowBackground = null;
            _cdGrid.RowStyle = GridRuntimeSupport.CreateCdxRowStyle();
            _trackGrid.AlternatingRowBackground = null;
            _trackGrid.RowStyle = GridRuntimeSupport.CreateCdxRowStyle();
            bool cdxLayoutKnown = _settings.CdColumns != null && _settings.CdColumns.Any(delegate(ColumnSetting s)
            {
                return String.Equals(s.Key, "CdxCompatibilitySortKey", StringComparison.OrdinalIgnoreCase);
            });
            SettingsManager.ApplyColumns(_trackGrid, _settings.TrackColumns);
            SettingsManager.ApplyColumns(_cdGrid, _settings.CdColumns);
            if (!DiscPlaybackRule.Current.Enabled)
            {
                _trackGrid.Columns.First(x => x.SortMemberPath == "CdxCompatibilitySortKey").Visibility = Visibility.Collapsed;
                _cdGrid.Columns.First(x => x.SortMemberPath == "CdxCompatibilitySortKey").Visibility = Visibility.Collapsed;
            }
            if (!cdxLayoutKnown) PlaceColumnAfter(_cdGrid, "CdxCompatibilitySortKey", "DurationSeconds");
            AttachColumnContextMenus(_trackGrid);
            AttachColumnContextMenus(_cdGrid);

            _trackView = CollectionViewSource.GetDefaultView(_data.Tracks);
            _cdView = CollectionViewSource.GetDefaultView(_data.Cds);
            _trackView.Filter = TrackFilter;
            _cdView.Filter = CdFilter;
            _trackGrid.ItemsSource = _trackView;
            _cdGrid.ItemsSource = _cdView;

            _trackGrid.Sorting += GridSorting;
            _cdGrid.Sorting += GridSorting;
            _trackGrid.MouseDoubleClick += TrackDoubleClick;
            _cdGrid.MouseDoubleClick += CdDoubleClick;
            _trackGrid.PreviewKeyDown += GridKeyDown;
            _cdGrid.PreviewKeyDown += GridKeyDown;

            _bridgeTimer = new DispatcherTimer();
            _bridgeTimer.Interval = TimeSpan.FromSeconds(10);
            _bridgeTimer.Tick += delegate { RefreshBridgeStatus(); };
            _bridgeTimer.Start();

            _filterTimer = new DispatcherTimer();
            _filterTimer.Interval = TimeSpan.FromMilliseconds(160);
            _filterTimer.Tick += delegate
            {
                _filterTimer.Stop();
                RefreshCurrentView();
            };

            _searchBox.TextChanged += FilterChanged;
            _genreCombo.SelectionChanged += FilterChanged;
            _digitalCombo.SelectionChanged += FilterChanged;
            _yearCombo.SelectionChanged += FilterChanged;
            _labelCombo.SelectionChanged += FilterChanged;
            _issueCombo.SelectionChanged += FilterChanged;
            _mediumCombo.SelectionChanged += FilterChanged;
            _cdxCombo.SelectionChanged += FilterChanged;
            _mixCombo.SelectionChanged += FilterChanged;
            _tabs.SelectionChanged += TabsSelectionChanged;

            PreviewKeyDown += MainPreviewKeyDown;
            StateChanged += delegate { if (WindowState != WindowState.Minimized) _lastMainMaximized = WindowState == WindowState.Maximized; };
            Closing += MainClosing;
            Loaded += MainLoaded;

            if (!SettingsManager.ApplySorts(_trackView, _settings.TrackSorts))
                ApplyDefaultSort(_trackView, true);
            if (!SettingsManager.ApplySorts(_cdView, _settings.CdSorts))
                ApplyDefaultSort(_cdView, false);
            UpdateSortIndicators(_trackGrid, _trackView);
            UpdateSortIndicators(_cdGrid, _cdView);
            HorizontalScrollSupport.Enable(this);
            InitializeCatalogWorkspace();
        }

        private Menu BuildMenu()
        {
            Menu menu = new Menu();

            MenuItem file = new MenuItem { Header = "_File" };
            MenuItem exit = new MenuItem { Header = "_Exit" };
            exit.ToolTip = "Exit DJ Library and save window, column, and sort layouts.";
            exit.Click += delegate { Close(); };
            file.Items.Add(exit);
            menu.Items.Add(file);

            MenuItem view = new MenuItem { Header = "_View" };
            MenuItem tracks = new MenuItem { Header = "_Tracks	Ctrl+1" };
            tracks.ToolTip = "Switch to the track view.";
            tracks.Click += delegate { _tabs.SelectedIndex = 0; };
            view.Items.Add(tracks);
            MenuItem cds = new MenuItem { Header = "_CDs	Ctrl+2" };
            cds.ToolTip = "Switch to the CD view.";
            cds.Click += delegate { _tabs.SelectedIndex = 1; };
            view.Items.Add(cds);
            view.Items.Add(new Separator());
            MenuItem details = new MenuItem { Header = "_Open Details" };
            details.ToolTip = "Open details for the currently selected track or CD.";
            details.Click += delegate { ShowSelectedDetails(); };
            view.Items.Add(details);
            view.Items.Add(new Separator());
            MenuItem cols = new MenuItem { Header = "_Configure Columns…" };
            cols.Click += delegate { ConfigureColumns(); };
            cols.ToolTip = "Configure column visibility and order for the current view.";
            view.Items.Add(cols);
            MenuItem resetCols = new MenuItem { Header = "_Reset Column Layout" };
            resetCols.Click += delegate { ResetCurrentColumns(); };
            resetCols.ToolTip = "Reset order, visibility, and default widths for the current table.";
            view.Items.Add(resetCols);
            MenuItem resetSort = new MenuItem { Header = "_Reset Sorting" };
            resetSort.Click += delegate { ResetCurrentSort(); };
            resetSort.ToolTip = "Restore the DJ Library default sorting for the current view.";
            view.Items.Add(resetSort);
            menu.Items.Add(view);

            MenuItem filter = new MenuItem { Header = "_Filter" };
            MenuItem focus = new MenuItem { Header = "_Focus Search" };
            focus.ToolTip = "Focus the free-text search (Ctrl+F).";
            focus.Click += delegate { _searchBox.Focus(); _searchBox.SelectAll(); };
            filter.Items.Add(focus);
            MenuItem clear = new MenuItem { Header = "_Clear Filters	Ctrl+L" };
            clear.Click += delegate { ResetFilters(); };
            filter.Items.Add(clear);
            menu.Items.Add(filter);

            MenuItem extras = new MenuItem { Header = "_Tools" };
            MenuItem digital = new MenuItem { Header = "_Digital Index Status…" };
            digital.Click += delegate { ShowDigitalIndexStatus(); };
            extras.Items.Add(digital);
            MenuItem refreshDigital = new MenuItem { Header = "_Refresh Digital Index\tF5" };
            refreshDigital.ToolTip = "Check the selected foobar Bridge immediately for a new complete generation.";
            refreshDigital.Click += delegate { RefreshBridgeStatus(); RefreshCurrentView(); };
            extras.Items.Add(refreshDigital);
            MenuItem selectDigitalSource = new MenuItem { Header = "Select Digital _Source…" };
            selectDigitalSource.ToolTip = "Select bridge-state.tsv from another foobar2000 profile. The selection is saved.";
            selectDigitalSource.Click += delegate { SelectBridgeSource(); };
            extras.Items.Add(selectDigitalSource);
            MenuItem automaticDigitalSource = new MenuItem { Header = "_Select Digital Source Automatically" };
            automaticDigitalSource.ToolTip = "Clear a manual source selection. The standard profile is preferred; the legacy global RC1 Bridge is used only as a fallback.";
            automaticDigitalSource.Click += delegate { UseAutomaticBridgeSource(); };
            extras.Items.Add(automaticDigitalSource);
            extras.Items.Add(new Separator());
            MenuItem discogs = new MenuItem { Header = "_Discogs Access…" };
            discogs.ToolTip = "Configure the personal Discogs API token for independent CD searches. The token is encrypted for the current Windows user with DPAPI.";
            discogs.Click += delegate { DiscogsSettingsDialog.Show(this); };
            extras.Items.Add(discogs);
            extras.Items.Add(new Separator());
            MenuItem playback = new MenuItem { Header = "_Disc Playback Rule…" };
            playback.ToolTip = "Configure physical CD playability, MM:SS:FF limit and optional CD/track highlighting.";
            playback.Click += delegate { new DiscPlaybackRuleWindow(this, RefreshPlaybackRuleViews).ShowDialog(); };
            extras.Items.Add(playback);
            extras.Items.Add(new Separator());
            MenuItem genres = new MenuItem { Header = "_Genre Matching Status…" };
            genres.ToolTip = "Show how many historical genres were safely updated from the digital foobar collection.";
            genres.Click += delegate { ShowGenreProjectionStatus(); };
            extras.Items.Add(genres);
            extras.Items.Add(new Separator());
            MenuItem catalog = new MenuItem { Header = "_Catalog Manager…" };
            catalog.ToolTip = "Open the writable v0.4 catalog with CRUD, Undo, Backup/Restore, and Audio CD import.";
            catalog.Click += delegate { OpenWritableCatalog(); };
            extras.Items.Add(catalog);
            menu.Items.Add(extras);

            MenuItem help = new MenuItem { Header = "_Help" };
            MenuItem about = new MenuItem { Header = "_About DJ Library" };
            about.Click += delegate
            {
                MessageBox.Show(this,
                    BuildInfo.WindowTitle + "\n\n" +
                    "Native C#/WPF application for the modernized physical CD database.\n" +
                    String.Format(System.Globalization.CultureInfo.CurrentCulture, "{0:N0} Tracks · {1:N0} CDs.\n\n", _data.Tracks.Count, _data.Cds.Count) +
                    "Profil-lokale foobar-Bridge mit Multi-Instanz-Quellwahl, konservativer Genre-Projektion " +
                    "and complete legacy traceability.",
                    "About DJ Library", MessageBoxButton.OK, MessageBoxImage.Information);
            };
            help.Items.Add(about);
            menu.Items.Add(help);

            return menu;
        }

        private ToolBarTray BuildToolBar()
        {
            ToolBarTray tray = new ToolBarTray();
            tray.Background = SystemColors.ControlBrush;
            tray.IsLocked = true;

            ToolBar bar = new ToolBar();
            bar.ToolTip = "Quick access to Columns and Digital Index. Switch between Tracks and CDs using the tabs.";

            // v0.3.0: Keine zweite Track/CD-Navigation in der Toolbar. Die sichtbaren
            // Registerkarten über der Tabelle sind die einzige Hauptnavigation;
            // Ctrl+1 / Ctrl+2 und die Menüeinträge bleiben als Tastatur-/Menüweg erhalten.
            Button columns = ToolButton("Columns…", "Show/hide columns and configure their order.", delegate { ConfigureColumns(); });
            bar.Items.Add(columns);
            bar.Items.Add(new Separator());

            Button digital = ToolButton("Digital Index", "Show the automatic Digital Index status.", delegate { ShowDigitalIndexStatus(); });
            bar.Items.Add(digital);
            bar.Items.Add(new Separator());
            Button catalog = ToolButton("Catalog Manager…", "Open the writable v0.4 catalog.", delegate { OpenWritableCatalog(); });
            bar.Items.Add(catalog);

            tray.ToolBars.Add(bar);
            return tray;
        }

        private static Button ToolButton(string text, string tip, RoutedEventHandler handler)
        {
            Button b = new Button();
            b.Content = text;
            b.Padding = new Thickness(8, 2, 8, 2);
            b.Margin = new Thickness(1, 0, 1, 0);
            b.ToolTip = tip;
            b.Click += handler;
            return b;
        }

        private void OpenWritableCatalog()
        {
            string seed = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "catalog-seed-v1.sqlite.gz");
            try { WorkspaceManager.ShowCatalog(this, seed); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "The writable catalog could not be opened.\n\n" + ex.Message,
                    "Catalog Manager", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        internal static bool TryApplyBridgeSnapshot(DataStore data, string directory, BridgeSnapshotStatus probe, out BridgeSnapshotStatus verified, out int itemCount, out string validation)
        {
            verified = probe;
            itemCount = 0;
            validation = null;
            if (data == null) throw new ArgumentNullException("data");
            if (probe == null || !probe.Present || !probe.Complete || !probe.Compatible || !String.IsNullOrEmpty(probe.Error)) return false;

            verified = BridgeSnapshotReader.Inspect(directory, true);
            if (!verified.Present || !verified.Complete || !verified.Compatible || !String.IsNullOrEmpty(verified.Error)) return false;

            try
            {
                List<DigitalItem> liveItems = BridgeSnapshotReader.LoadItems(directory, verified);
                validation = data.ApplyLiveDigitalItems(liveItems);
                data.SetDigitalSourceDescription("Live-foobar-Bridge · " + verified.SourceDisplayName);
                itemCount = liveItems.Count;
                return true;
            }
            catch (Exception ex)
            {
                verified.Error = ex.Message;
                verified.Compatible = false;
                return false;
            }
        }

        private void RefreshBridgeStatus()
        {
            RefreshBridgeStatus(false, true);
        }

        private void RefreshBridgeStatus(bool forceLoad, bool refreshView)
        {
            if (!_bridgeDirectoryPinned) EnsureAutomaticBridgeDirectory();
            BridgeSnapshotStatus probe = BridgeSnapshotReader.Inspect(_bridgeDirectory, false);
            bool changed = probe.Present != _bridgeStatus.Present || probe.Generation != _bridgeStatus.Generation ||
                           probe.Complete != _bridgeStatus.Complete || probe.Compatible != _bridgeStatus.Compatible ||
                           probe.DeclaredItemCount != _bridgeStatus.DeclaredItemCount ||
                           !String.Equals(probe.LastChangeUtc, _bridgeStatus.LastChangeUtc, StringComparison.Ordinal) ||
                           !String.Equals(probe.SourceId, _bridgeStatus.SourceId, StringComparison.Ordinal) ||
                           !String.Equals(probe.ProducerVersion, _bridgeStatus.ProducerVersion, StringComparison.Ordinal) ||
                           !PathEquals(probe.DirectoryPath, _bridgeStatus.DirectoryPath) ||
                           !String.Equals(probe.Error, _bridgeStatus.Error, StringComparison.Ordinal);
            bool needsActivation = probe.Present && probe.Complete && probe.Compatible && String.IsNullOrEmpty(probe.Error) &&
                                   (_activeBridgeGeneration == 0 || probe.Generation != _activeBridgeGeneration ||
                                    !PathEquals(_activeBridgeDirectory, _bridgeDirectory));
            if (!changed && !forceLoad && !needsActivation) return;

            if ((forceLoad || needsActivation) && probe.Present && probe.Complete && probe.Compatible && String.IsNullOrEmpty(probe.Error))
            {
                BridgeSnapshotStatus verified;
                int itemCount;
                string validation;
                if (TryApplyBridgeSnapshot(_data, _bridgeDirectory, probe, out verified, out itemCount, out validation))
                {
                    probe = verified;
                    _activeBridgeGeneration = verified.Generation;
                    _activeBridgeItemCount = itemCount;
                    _activeBridgeDirectory = _bridgeDirectory;
                    _activeBridgeSourceName = verified.SourceDisplayName;
                    if (refreshView)
                    {
                        PopulateFilterOptions();
                        RefreshCurrentView();
                    }
                }
                else probe = verified;
            }
            _bridgeStatus = probe;
            UpdateDigitalStatusText();
        }

        private void UpdateDigitalStatusText()
        {
            if (_digitalStatusText == null) return;
            string index;
            if (_activeBridgeGeneration > 0 && _bridgeStatus.Present && _bridgeStatus.Complete && _bridgeStatus.Compatible &&
                String.IsNullOrEmpty(_bridgeStatus.Error) && _bridgeStatus.Generation == _activeBridgeGeneration &&
                PathEquals(_activeBridgeDirectory, _bridgeDirectory))
                index = String.Format("Digital Index: Live · {0} · Gen. {1:N0} · {2:N0} Items", _bridgeStatus.SourceDisplayName, _activeBridgeGeneration, _activeBridgeItemCount);
            else if (_activeBridgeGeneration > 0)
                index = String.Format("Digital Index: Live · {0} · Gen. {1:N0} · {2:N0} items · last valid state",
                    String.IsNullOrWhiteSpace(_activeBridgeSourceName) ? "foobar2000" : _activeBridgeSourceName, _activeBridgeGeneration, _activeBridgeItemCount);
            else if (_bridgeStatus.Present)
                index = _bridgeStatus.ShortText;
            else
                index = String.Format("Digital Index: Test · {0:N0} candidates", _data.DigitalItemCount);
            if (!String.IsNullOrEmpty(_bridgeStatus.Error)) index += " · Bridge-Error";
            else if (!_bridgeStatus.Present && _activeBridgeGeneration > 0) index += " · Bridge derzeit unavailable";
            _digitalStatusText.Text = String.Format("{0} · Genres: {1:N0} Tracks / {2:N0} CDs updated",
                index, _data.ProjectedTrackGenreCount, _data.ProjectedCdGenreCount);
            _digitalStatusText.ToolTip = (_activeBridgeGeneration > 0
                ? "Active, fully validated foobar Bridge snapshot. If a newer snapshot is invalid, the last valid live state remains active."
                : "The qualified test index remains active until a complete Bridge snapshot has been activated successfully. LegacyGenre is always preserved.") +
                "\n\nMatchverteilung: " + BuildDigitalMatchDistribution(false);
        }

        private string BuildDigitalMatchDistribution(bool multiline)
        {
            int strong, likely, candidate, none;
            _data.GetDigitalMatchCounts(out strong, out likely, out candidate, out none);
            if (multiline)
                return String.Format("Stark: {0:N0}\nWahrscheinlich: {1:N0}\nKandidat: {2:N0}\nKein Treffer: {3:N0}\nSumme: {4:N0} physische Tracks",
                    strong, likely, candidate, none, strong + likely + candidate + none);
            return String.Format("Stark {0:N0} · Wahrscheinlich {1:N0} · Kandidat {2:N0} · Kein Treffer {3:N0}",
                strong, likely, candidate, none);
        }

        private void ShowDigitalIndexStatus()
        {
            string sourceMode = _bridgeDirectoryPinned ? "Manual" : "Automatisch";
            bool inspectedIsActive = _activeBridgeGeneration > 0 && _bridgeStatus.Present && _bridgeStatus.Complete && _bridgeStatus.Compatible &&
                String.IsNullOrEmpty(_bridgeStatus.Error) && _bridgeStatus.Generation == _activeBridgeGeneration &&
                PathEquals(_activeBridgeDirectory, _bridgeDirectory);

            string current;
            if (_bridgeStatus.Present)
            {
                string heading = inspectedIsActive
                    ? (_bridgeDirectoryPinned ? "Manually selected foobar Bridge is active." : "Automatically selected foobar Bridge is active.")
                    : (_bridgeDirectoryPinned ? "Manually selected foobar Bridge detected." : "Automatically selected foobar Bridge detected.");
                current = heading +
                    "\n\nSource: " + _bridgeStatus.SourceDisplayName +
                    "\nSelection mode: " + sourceMode +
                    String.Format("\nGeneration: {0:N0}", _bridgeStatus.Generation) +
                    String.Format("\nItems: {0:N0}", _bridgeStatus.VerifiedItemCount > 0 ? _bridgeStatus.VerifiedItemCount : _bridgeStatus.DeclaredItemCount) +
                    (String.IsNullOrWhiteSpace(_bridgeStatus.ProfilePath) ? "" : "\nfoobar-Profil: " + _bridgeStatus.ProfilePath) +
                    (String.IsNullOrWhiteSpace(_bridgeStatus.ProducerVersion) ? "" : "\nBridge-Version: " + _bridgeStatus.ProducerVersion) +
                    "\nLast change (UTC): " + (_bridgeStatus.LastChangeUtc ?? "—") +
                    "\nBridge folder: " + _bridgeStatus.DirectoryPath +
                    (String.IsNullOrEmpty(_bridgeStatus.Error) ? "" : "\nError: " + _bridgeStatus.Error);
            }
            else
            {
                current = (_bridgeDirectoryPinned ? "Manually selected" : "Automatically selected") +
                    " Digital source is currently unavailable.\n\nSelection mode: " + sourceMode +
                    "\nBridge folder: " + _bridgeDirectory;
            }

            if (_activeBridgeGeneration > 0 && !inspectedIsActive)
                current += String.Format("\n\nThe last valid snapshot remains active: {0}, generation {1:N0}, {2:N0} items.",
                    String.IsNullOrWhiteSpace(_activeBridgeSourceName) ? "foobar2000" : _activeBridgeSourceName, _activeBridgeGeneration, _activeBridgeItemCount);
            else if (_activeBridgeGeneration == 0)
                current += String.Format("\n\nUntil a valid Bridge is activated, the app uses the qualified fallback test index with {0:N0} digital candidates.", _data.DigitalItemCount);

            current += "\n\nTrack matching for the currently active digital collection:\n" + BuildDigitalMatchDistribution(true);

            current += "\n\nAutomatische Quellwahl bevorzugt die profil-lokale Bridge des Standardprofils unter: " + BridgeSnapshotReader.StandardProfileDirectory +
                       "\nThe former global RC1 location is read only as a compatibility fallback: " + BridgeSnapshotReader.LegacyDirectory +
                       "\n\nUse Tools → Select Digital Source… to choose another or portable foobar2000 profile.";

            MessageBox.Show(this, current, "Digital Index", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ShowGenreProjectionStatus()
        {
            MessageBox.Show(this,
                String.Format("{0:N0} von {1:N0} Tracks verwenden bereits ein sicher projiziertes digitales GENRE.\n", _data.ProjectedTrackGenreCount, _data.Tracks.Count) +
                String.Format("{0:N0} von {1:N0} CDs besitzen einen ausreichend starken digitalen Genre-Konsens.\n\n", _data.ProjectedCdGenreCount, _data.Cds.Count) +
                "Automation rule: digital genres are applied only for strong track matches. " +
                "Broad legacy categories such as 'General Trance' are not renamed globally because the current collection maps them to several more specific genres. " +
                "The historical genre is always preserved and remains visible in Details.",
                "Genre-Abgleich", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private static TextBlock FilterLabel(string text, string tip)
        {
            TextBlock t = new TextBlock();
            t.Text = text;
            t.Margin = new Thickness(2, 0, 2, 0);
            t.TextAlignment = TextAlignment.Left;
            t.VerticalAlignment = VerticalAlignment.Center;
            t.ToolTip = tip;
            return t;
        }

        private static ComboBox Combo(double width, string tip)
        {
            ComboBox c = new ComboBox();
            c.Width = width;
            c.Height = 26;
            c.Margin = new Thickness(3, 0, 10, 0);
            c.VerticalContentAlignment = VerticalAlignment.Center;
            c.ToolTip = tip;
            return c;
        }

        private static StackPanel InlinePanel()
        {
            StackPanel p = new StackPanel();
            p.Orientation = Orientation.Horizontal;
            p.VerticalAlignment = VerticalAlignment.Center;
            p.Height = 26;
            p.Margin = new Thickness(0, 1, 0, 1);
            return p;
        }

        private static bool PathEquals(string a, string b)
        {
            if (String.IsNullOrWhiteSpace(a) || String.IsNullOrWhiteSpace(b)) return false;
            try
            {
                return String.Equals(System.IO.Path.GetFullPath(a).TrimEnd(System.IO.Path.DirectorySeparatorChar),
                    System.IO.Path.GetFullPath(b).TrimEnd(System.IO.Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
            }
            catch { return String.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        }

        private static string ResolveInitialBridgeDirectory(string saved)
        {
            if (!String.IsNullOrWhiteSpace(saved)) return saved;
            if (BridgeSnapshotReader.HasStateFile(BridgeSnapshotReader.StandardProfileDirectory)) return BridgeSnapshotReader.StandardProfileDirectory;
            if (BridgeSnapshotReader.HasStateFile(BridgeSnapshotReader.LegacyDirectory)) return BridgeSnapshotReader.LegacyDirectory;
            return BridgeSnapshotReader.StandardProfileDirectory;
        }

        private void EnsureAutomaticBridgeDirectory()
        {
            string target = BridgeSnapshotReader.HasStateFile(BridgeSnapshotReader.StandardProfileDirectory)
                ? BridgeSnapshotReader.StandardProfileDirectory
                : (BridgeSnapshotReader.HasStateFile(BridgeSnapshotReader.LegacyDirectory) ? BridgeSnapshotReader.LegacyDirectory : BridgeSnapshotReader.StandardProfileDirectory);
            if (!PathEquals(_bridgeDirectory, target))
            {
                _bridgeDirectory = target;
                _bridgeStatus = new BridgeSnapshotStatus { DirectoryPath = target };
            }
        }

        private void SelectBridgeSource()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "DJ Library — foobar-Bridge-Source select";
            dialog.Filter = "foobar Bridge state (bridge-state.tsv)|bridge-state.tsv|TSV-Fileen (*.tsv)|*.tsv|All Fileen (*.*)|*.*";
            dialog.FileName = "bridge-state.tsv";
            try
            {
                string initial = _bridgeDirectoryPinned ? _bridgeDirectory : _settings.LastManualBridgeDirectory;
                if (!String.IsNullOrWhiteSpace(initial) && System.IO.Directory.Exists(initial)) dialog.InitialDirectory = initial;
            }
            catch { }
            bool? result = dialog.ShowDialog(this);
            if (result != true) return;

            string directory = System.IO.Path.GetDirectoryName(dialog.FileName);
            BridgeSnapshotStatus probe = BridgeSnapshotReader.Inspect(directory, false);
            if (!probe.Present || !probe.Compatible || !probe.Complete || !String.IsNullOrEmpty(probe.Error))
            {
                MessageBox.Show(this, "The selected file does not belong to a complete compatible Bridge snapshot.\n\n" +
                    (probe.Error ?? probe.ShortText), "Digital Source", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BridgeSnapshotStatus verified; int itemCount; string validation;
            if (!TryApplyBridgeSnapshot(_data, directory, probe, out verified, out itemCount, out validation))
            {
                MessageBox.Show(this, "The selected Bridge could not be activated.\n\n" + (verified.Error ?? "Unknown error"),
                    "Digital Source", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _bridgeDirectory = directory;
            _bridgeDirectoryPinned = true;
            _settings.BridgeDirectory = directory;
            _settings.LastManualBridgeDirectory = directory;
            RememberBridgeDirectory(directory);
            _bridgeStatus = verified;
            _activeBridgeGeneration = verified.Generation;
            _activeBridgeItemCount = itemCount;
            _activeBridgeDirectory = directory;
            _activeBridgeSourceName = verified.SourceDisplayName;
            SaveBridgePreferences();
            PopulateFilterOptions();
            RefreshCurrentView();
            UpdateDigitalStatusText();
        }

        private void RememberBridgeDirectory(string directory)
        {
            if (String.IsNullOrWhiteSpace(directory)) return;
            if (_settings.KnownBridgeDirectories == null) _settings.KnownBridgeDirectories = new List<string>();
            for (int i = _settings.KnownBridgeDirectories.Count - 1; i >= 0; i--)
            {
                if (PathEquals(_settings.KnownBridgeDirectories[i], directory)) _settings.KnownBridgeDirectories.RemoveAt(i);
            }
            _settings.KnownBridgeDirectories.Insert(0, directory);
            while (_settings.KnownBridgeDirectories.Count > 8) _settings.KnownBridgeDirectories.RemoveAt(_settings.KnownBridgeDirectories.Count - 1);
        }

        private void UseAutomaticBridgeSource()
        {
            _bridgeDirectoryPinned = false;
            _settings.BridgeDirectory = "";
            SaveBridgePreferences();
            EnsureAutomaticBridgeDirectory();
            RefreshBridgeStatus(true, true);
        }

        private void SaveBridgePreferences()
        {
            SettingsManager.Update(delegate(AppSettings latest)
            {
                latest.BridgeDirectory = _settings.BridgeDirectory ?? "";
                latest.LastManualBridgeDirectory = _settings.LastManualBridgeDirectory ?? "";
                latest.KnownBridgeDirectories = _settings.KnownBridgeDirectories == null
                    ? new List<string>()
                    : new List<string>(_settings.KnownBridgeDirectories);
            });
        }

        private void EnsureWindowOnVisibleDesktop()
        {
            // Settings can point to a monitor that has since been disconnected. Keep enough of the
            // restored window on the current virtual desktop so the app can always be recovered.
            double vl = SystemParameters.VirtualScreenLeft;
            double vt = SystemParameters.VirtualScreenTop;
            double vr = vl + SystemParameters.VirtualScreenWidth;
            double vb = vt + SystemParameters.VirtualScreenHeight;
            double w = Math.Min(Math.Max(Width, MinWidth), SystemParameters.VirtualScreenWidth);
            double h = Math.Min(Math.Max(Height, MinHeight), SystemParameters.VirtualScreenHeight);
            Width = w; Height = h;
            const double visible = 120.0;
            if (Left + visible > vr) Left = vr - visible;
            if (Top + visible > vb) Top = vb - visible;
            if (Left + w - visible < vl) Left = vl - w + visible;
            if (Top + h - visible < vt) Top = vt - h + visible;
        }

        private void MainLoaded(object sender, RoutedEventArgs e)
        {
            int selected = _settings.SelectedTab;
            if (selected < 0 || selected > 1) selected = 0;
            _tabs.SelectedIndex = selected;

            // Resolve and activate the selected/profile-local authoritative bridge generation
            // before building filter option lists. This also migrates automatic RC1-global
            // fallback use to the profile-local RC2 location as soon as it appears.
            RefreshBridgeStatus(true, false);
            PopulateFilterOptions();

            if (_settings.Maximized)
                WindowState = WindowState.Maximized;

            _searchBox.Focus();
            RefreshCurrentView();
        }

        private void MainPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.F)
            {
                _searchBox.Focus();
                _searchBox.SelectAll();
                e.Handled = true;
            }
            else if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.D1)
            {
                _tabs.SelectedIndex = 0; _trackGrid.Focus(); e.Handled = true;
            }
            else if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.D2)
            {
                _tabs.SelectedIndex = 1; _cdGrid.Focus(); e.Handled = true;
            }
            else if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.L)
            {
                ResetFilters(); e.Handled = true;
            }
            else if (e.Key == Key.F5)
            {
                RefreshBridgeStatus(); RefreshCurrentView(); e.Handled = true;
            }
            else if (e.Key == Key.Escape && _searchBox.IsKeyboardFocusWithin)
            {
                _searchBox.Text = "";
                e.Handled = true;
            }
        }

        private void GridKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ShowSelectedDetails();
                e.Handled = true;
            }
        }

        private void ShowSelectedDetails()
        {
            if (_tabs.SelectedIndex == 0)
            {
                TrackRow t = _trackGrid.SelectedItem as TrackRow;
                if (t == null) return;
                TrackDetailWindow w = new TrackDetailWindow(this, t, _data);
                w.ShowDialog();
            }
            else
            {
                CdRow c = _cdGrid.SelectedItem as CdRow;
                if (c == null) return;
                CdDetailWindow w = new CdDetailWindow(this, c, _data);
                w.ShowDialog();
            }
        }

        private void MainClosing(object sender, CancelEventArgs e)
        {
            if (_bridgeTimer != null) _bridgeTimer.Stop();
            ShutdownCatalogWorkspace();
            Rect bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            _settings.WindowLeft = bounds.Left;
            _settings.WindowTop = bounds.Top;
            _settings.WindowWidth = bounds.Width;
            _settings.WindowHeight = bounds.Height;
            _settings.Maximized = _lastMainMaximized;
            _settings.SelectedTab = _tabs.SelectedIndex;
            _settings.TrackColumns = SettingsManager.CaptureColumns(_trackGrid);
            _settings.CdColumns = SettingsManager.CaptureColumns(_cdGrid);
            _settings.TrackSorts = SettingsManager.CaptureSorts(_trackView);
            _settings.CdSorts = SettingsManager.CaptureSorts(_cdView);
            SaveCurrentFilterState();
            _settings.TrackFilter = CloneFilter(_trackFilter);
            _settings.CdFilter = CloneFilter(_cdFilter);
            // Catalog Manager and recurring task windows may have saved geometry
            // after MainWindow loaded _settings. Merge only MainWindow-owned state
            // into the latest snapshot so those newer writes survive shutdown.
            AppSettings latest = SettingsManager.Load();
            SettingsManager.CopyMainOwnedState(_settings, latest);
            SettingsManager.Save(latest);
        }

        private void AddColumns(DataGrid grid, List<ColumnSpec> specs)
        {
            foreach (ColumnSpec s in specs)
            {
                if (s.Key == "CdxCompatibilitySortKey") grid.Columns.Add(GridRuntimeSupport.CreateCdxColumn(s));
                else grid.Columns.Add(UiHelpers.TextColumn(s));
            }
        }

        private static void PlaceColumnAfter(DataGrid grid, string key, string predecessorKey)
        {
            DataGridColumn column = grid.Columns.FirstOrDefault(delegate(DataGridColumn c) { return c.SortMemberPath == key; });
            DataGridColumn predecessor = grid.Columns.FirstOrDefault(delegate(DataGridColumn c) { return c.SortMemberPath == predecessorKey; });
            if (column == null || predecessor == null) return;
            try { column.DisplayIndex = Math.Min(grid.Columns.Count - 1, predecessor.DisplayIndex + 1); } catch { }
        }

        private List<ColumnSpec> BuildTrackColumnSpecs()
        {
            List<ColumnSpec> x = new List<ColumnSpec>();
            x.Add(new ColumnSpec("Artist", "Artist", "Track artist. Systematic legacy prefixes such as '01. ' are cleaned; the raw value is preserved internally.", "Artist", 190, true));
            x.Add(new ColumnSpec("Title", "Title", "Base title of the track.", "Title", 230, true));
            x.Add(new ColumnSpec("Version", "Mix / Version", "Separate Mix/Version value stored as a normal data field.", "Version", 220, true));
            x.Add(new ColumnSpec("Album", "CD / Album", "Physical CD / album containing the track.", "Album", 235, true));
            x.Add(new ColumnSpec("TrackNumber", "#", "Track number on the disc.", "TrackNumber", 48, true));
            x.Add(new ColumnSpec("DiscNumber", "Disc", "Disc number within a multi-disc set.", "DiscText", 58, true));
            x.Add(new ColumnSpec("DurationSeconds", "Duration", "Track duration.", "DurationText", 78, true));
            x.Add(new ColumnSpec("CdxCompatibilitySortKey", "Disc playable", "Compatibility of the physical CD containing this track (configurable under Tools → Disc Playback Rule).", "CdxCompatibilityText", 112, true));
            x.Add(new ColumnSpec("Date", "Year", "Release year / DATE.", "Date", 68, true));
            x.Add(new ColumnSpec("Genre", "Genre", "Preferred genre: use foobar GENRE for a strong digital match; otherwise keep the historical collection genre.", "Genre", 150, true));
            x.Add(new ColumnSpec("GenreSourceText", "Genre-Source", "Shows whether the displayed genre comes from a strong digital match or the legacy database.", "GenreSourceText", 190, false));
            x.Add(new ColumnSpec("Bpm", "BPM", "BPM value used by DJ Library.", "BpmText", 70, true));
            x.Add(new ColumnSpec("Label", "Label", "Label of the physical release.", "Label", 160, true));
            x.Add(new ColumnSpec("Catalog", "Catalog", "Catalog number of the physical release.", "Catalog", 120, true));
            x.Add(new ColumnSpec("DigitalLevel", "Digital", "Automatically determined digital-match status.", "DigitalText", 145, true));
            x.Add(new ColumnSpec("IssueCode", "Has Issues", "Shows whether a specific data issue is documented for this legacy track.", "IssueText", 95, false));
            return x;
        }

        private List<ColumnSpec> BuildCdColumnSpecs()
        {
            List<ColumnSpec> x = new List<ColumnSpec>();
            x.Add(new ColumnSpec("AlbumArtist", "Album Artist", "Album Artist of the release. Sorting by this column automatically uses Album/CD as the secondary sort key.", "AlbumArtist", 190, true));
            x.Add(new ColumnSpec("Album", "Album / CD", "Title of the album / CD.", "Album", 270, true));
            x.Add(new ColumnSpec("DiscNumber", "Disc", "Disc Number innerhalb eines Mehrfachsets.", "DiscText", 62, true));
            x.Add(new ColumnSpec("Tracks", "Tracks", "Number of logically cataloged tracks.", "Tracks", 65, true));
            x.Add(new ColumnSpec("DurationSeconds", "Total Duration", "Physical CD duration, primarily derived from TOC and lead-out.", "DurationText", 90, true));
            x.Add(new ColumnSpec("CdxCompatibilitySortKey", "Disc playable", "Configurable physical CD playback rule; 75 frames/second, derived from complete TOC.", "CdxCompatibilityText", 118, true));
            x.Add(new ColumnSpec("Date", "Year", "Release year / DATE.", "Date", 68, true));
            x.Add(new ColumnSpec("Genre", "Genre", "Preferred CD genre. Updated only when strongly matched digital track genres reach a strong consensus.", "Genre", 160, true));
            x.Add(new ColumnSpec("GenreSourceText", "Genre-Source", "Shows whether the CD genre comes from digital track consensus or the legacy database.", "GenreSourceText", 175, false));
            x.Add(new ColumnSpec("Label", "Label", "Label of the physical release.", "Label", 170, true));
            x.Add(new ColumnSpec("Catalog", "Catalog", "Catalog number of the specific physical release.", "Catalog", 125, true));
            x.Add(new ColumnSpec("Medium", "Medium", "Physical media type.", "Medium", 105, true));
            x.Add(new ColumnSpec("StrongTracks", "Digital", "Share of tracks with a strong digital match in the currently active digital collection.", "DigitalText", 100, true));
            x.Add(new ColumnSpec("LayoutKind", "Special Case", "Only special disc structures are shown. Normal CDs remain blank here.", "SpecialText", 120, true));
            x.Add(new ColumnSpec("IssueCode", "Has Issues", "Shows whether a documented legacy issue exists. Details explain the exact reason.", "IssueText", 95, true));
            return x;
        }

        private void GridSorting(object sender, DataGridSortingEventArgs e)
        {
            e.Handled = true;
            DataGrid grid = sender as DataGrid;
            if (grid == null || String.IsNullOrEmpty(e.Column.SortMemberPath)) return;

            ICollectionView view = grid.ItemsSource as ICollectionView;
            if (view == null) return;

            ListSortDirection direction = e.Column.SortDirection == ListSortDirection.Ascending ?
                ListSortDirection.Descending : ListSortDirection.Ascending;

            bool multi = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

            using (view.DeferRefresh())
            {
                if (!multi)
                {
                    view.SortDescriptions.Clear();
                }
                else
                {
                    RemoveSort(view, e.Column.SortMemberPath);
                }

                view.SortDescriptions.Add(new SortDescription(e.Column.SortMemberPath, direction));

                if (!multi)
                {
                    AddAutomaticSecondarySorts(view, grid == _trackGrid, e.Column.SortMemberPath);
                }
            }
            UpdateSortIndicators(grid, view);
        }

        private static void RemoveSort(ICollectionView view, string property)
        {
            int i;
            for (i = view.SortDescriptions.Count - 1; i >= 0; i--)
            {
                if (String.Equals(view.SortDescriptions[i].PropertyName, property, StringComparison.OrdinalIgnoreCase))
                    view.SortDescriptions.RemoveAt(i);
            }
        }

        private static void AddAutomaticSecondarySorts(ICollectionView view, bool trackMode, string primary)
        {
            if (!trackMode && primary == "AlbumArtist")
            {
                view.SortDescriptions.Add(new SortDescription("Album", ListSortDirection.Ascending));
                view.SortDescriptions.Add(new SortDescription("DiscNumber", ListSortDirection.Ascending));
            }
            else if (trackMode && primary == "Artist")
            {
                view.SortDescriptions.Add(new SortDescription("Title", ListSortDirection.Ascending));
                view.SortDescriptions.Add(new SortDescription("Version", ListSortDirection.Ascending));
                view.SortDescriptions.Add(new SortDescription("Album", ListSortDirection.Ascending));
            }
        }

        private static void ApplyDefaultSort(ICollectionView view, bool trackMode)
        {
            view.SortDescriptions.Clear();
            if (trackMode)
            {
                view.SortDescriptions.Add(new SortDescription("Artist", ListSortDirection.Ascending));
                view.SortDescriptions.Add(new SortDescription("Title", ListSortDirection.Ascending));
                view.SortDescriptions.Add(new SortDescription("Version", ListSortDirection.Ascending));
                view.SortDescriptions.Add(new SortDescription("Album", ListSortDirection.Ascending));
            }
            else
            {
                view.SortDescriptions.Add(new SortDescription("AlbumArtist", ListSortDirection.Ascending));
                view.SortDescriptions.Add(new SortDescription("Album", ListSortDirection.Ascending));
                view.SortDescriptions.Add(new SortDescription("DiscNumber", ListSortDirection.Ascending));
            }
        }

        private void ResetCurrentSort()
        {
            bool tracks = _tabs.SelectedIndex == 0;
            ICollectionView view = tracks ? _trackView : _cdView;
            DataGrid grid = tracks ? _trackGrid : _cdGrid;
            ApplyDefaultSort(view, tracks);
            UpdateSortIndicators(grid, view);
        }

        private static void UpdateSortIndicators(DataGrid grid, ICollectionView view)
        {
            foreach (DataGridColumn c in grid.Columns) c.SortDirection = null;
            foreach (SortDescription d in view.SortDescriptions)
            {
                DataGridColumn c = grid.Columns.FirstOrDefault(delegate(DataGridColumn x)
                {
                    return String.Equals(x.SortMemberPath, d.PropertyName, StringComparison.OrdinalIgnoreCase);
                });
                if (c != null) c.SortDirection = d.Direction;
            }
        }

        private void ApplySortFromMenu(DataGrid grid, DataGridColumn column, ListSortDirection direction, bool append)
        {
            if (grid == null || column == null || String.IsNullOrEmpty(column.SortMemberPath)) return;
            ICollectionView view = grid.ItemsSource as ICollectionView;
            if (view == null) return;

            using (view.DeferRefresh())
            {
                if (!append)
                    view.SortDescriptions.Clear();
                else
                    RemoveSort(view, column.SortMemberPath);

                view.SortDescriptions.Add(new SortDescription(column.SortMemberPath, direction));
                if (!append) AddAutomaticSecondarySorts(view, grid == _trackGrid, column.SortMemberPath);
            }
            UpdateSortIndicators(grid, view);
        }

        private void AttachColumnContextMenus(DataGrid grid)
        {
            foreach (DataGridColumn rawColumn in grid.Columns)
            {
                DataGridColumn column = rawColumn;
                TextBlock header = column.Header as TextBlock;
                if (header == null) continue;

                ContextMenu menu = new ContextMenu();

                MenuItem asc = new MenuItem { Header = "Sort Ascending" };
                asc.ToolTip = "Sort by this column; appropriate secondary sort keys are added automatically.";
                asc.Click += delegate { ApplySortFromMenu(grid, column, ListSortDirection.Ascending, false); };
                menu.Items.Add(asc);

                MenuItem desc = new MenuItem { Header = "Sort Descending" };
                desc.ToolTip = "Sort by this column in descending order.";
                desc.Click += delegate { ApplySortFromMenu(grid, column, ListSortDirection.Descending, false); };
                menu.Items.Add(desc);

                MenuItem addAsc = new MenuItem { Header = "Add Ascending Sort" };
                addAsc.ToolTip = "Add this column as another sort level; equivalent to Shift+Click.";
                addAsc.Click += delegate { ApplySortFromMenu(grid, column, ListSortDirection.Ascending, true); };
                menu.Items.Add(addAsc);

                MenuItem addDesc = new MenuItem { Header = "Add Descending Sort" };
                addDesc.ToolTip = "Add this column as another descending sort level.";
                addDesc.Click += delegate { ApplySortFromMenu(grid, column, ListSortDirection.Descending, true); };
                menu.Items.Add(addDesc);

                menu.Items.Add(new Separator());

                MenuItem hide = new MenuItem { Header = "Hide This Column" };
                hide.ToolTip = "Hide this column. It can be enabled again through 'Columns…'.";
                hide.Click += delegate { column.Visibility = Visibility.Collapsed; };
                menu.Items.Add(hide);

                MenuItem configure = new MenuItem { Header = "Configure Columns…" };
                configure.Click += delegate { ConfigureColumns(); };
                menu.Items.Add(configure);

                MenuItem reset = new MenuItem { Header = "Reset Column Layout" };
                reset.Click += delegate { ResetCurrentColumns(); };
                menu.Items.Add(reset);

                header.ContextMenu = menu;
            }
        }

        private void TrackDoubleClick(object sender, MouseButtonEventArgs e)
        {
            TrackRow t = _trackGrid.SelectedItem as TrackRow;
            if (t == null) return;
            TrackDetailWindow w = new TrackDetailWindow(this, t, _data);
            w.ShowDialog();
        }

        private void CdDoubleClick(object sender, MouseButtonEventArgs e)
        {
            CdRow c = _cdGrid.SelectedItem as CdRow;
            if (c == null) return;
            CdDetailWindow w = new CdDetailWindow(this, c, _data);
            w.ShowDialog();
        }

        private void FilterChanged(object sender, EventArgs e)
        {
            if (_suppressFilters) return;
            SaveCurrentFilterState();
            _filterTimer.Stop();
            _filterTimer.Start();
        }

        private void TabsSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!Object.ReferenceEquals(e.Source, _tabs)) return;
            if (_tabs.SelectedIndex < 0) return;
            if (_filterTimer != null) _filterTimer.Stop();
            PopulateFilterOptions();
            RefreshCurrentView();
        }

        private void PopulateFilterOptions()
        {
            _suppressFilters = true;
            bool tracks = _tabs.SelectedIndex == 0;

            FilterState state = tracks ? _trackFilter : _cdFilter;

            IEnumerable<string> genres = tracks ?
                _data.Tracks.Select(delegate(TrackRow x) { return x.Genre; }) :
                _data.Cds.Select(delegate(CdRow x) { return x.Genre; });

            SetComboItems(_genreCombo, "All Genres", genres.Where(delegate(string x) { return !String.IsNullOrEmpty(x); }).Distinct().OrderBy(delegate(string x) { return x; }), state.Genre);

            IEnumerable<string> years = tracks ?
                _data.Tracks.Select(delegate(TrackRow x) { return x.Date; }) :
                _data.Cds.Select(delegate(CdRow x) { return x.Date; });
            SetComboItems(_yearCombo, "All", years.Where(delegate(string x) { return !String.IsNullOrEmpty(x); }).Distinct().OrderByDescending(delegate(string x) { return x; }), state.Year);

            IEnumerable<string> labels = tracks ?
                _data.Tracks.Select(delegate(TrackRow x) { return x.Label; }) :
                _data.Cds.Select(delegate(CdRow x) { return x.Label; });
            SetComboItems(_labelCombo, "All Labels", labels.Where(delegate(string x) { return !String.IsNullOrEmpty(x); }).Distinct().OrderBy(delegate(string x) { return x; }), state.Label);

            List<string> issues = new List<string>();
            issues.Add("All");
            issues.Add("Issues Only");
            issues.Add("No Issues");
            SetComboItems(_issueCombo, null, issues, state.Issues);

            List<string> digital = new List<string>();
            digital.Add("All");
            if (tracks)
            {
                digital.Add("Strong");
                digital.Add("Likely");
                digital.Add("Candidate");
                digital.Add("No Match");
            }
            else
            {
                digital.Add("All Tracks Strong");
                digital.Add("Partially Strong");
                digital.Add("No Strong Match");
            }
            SetComboItems(_digitalCombo, null, digital, state.Digital);

            _mediumPanel.Visibility = tracks ? Visibility.Collapsed : Visibility.Visible;
            _cdxPanel.Visibility = DiscPlaybackRule.Current.Enabled ? Visibility.Visible : Visibility.Collapsed;
            _mixPanel.Visibility = tracks ? Visibility.Visible : Visibility.Collapsed;

            List<string> cdxValues = new List<string>();
            cdxValues.Add("All");
            cdxValues.Add("Yes");
            cdxValues.Add("No");
            cdxValues.Add("Unknown");
            SetComboItems(_cdxCombo, null, cdxValues, state.Cdx);

            if (!tracks)
            {
                IEnumerable<string> mediums = _data.Cds.Select(delegate(CdRow x) { return x.Medium; });
                SetComboItems(_mediumCombo, "All", mediums.Where(delegate(string x) { return !String.IsNullOrEmpty(x); }).Distinct().OrderBy(delegate(string x) { return x; }), state.Medium);
            }
            else
            {
                List<string> mixes = new List<string>();
                mixes.Add("All");
                mixes.Add("With Mix");
                mixes.Add("Without Mix");
                SetComboItems(_mixCombo, null, mixes, state.Mix);
            }

            _searchBox.Text = state.Search ?? "";
            _suppressFilters = false;
            SaveCurrentFilterState();
        }

        private static void SetComboItems(ComboBox combo, string first, IEnumerable<string> values, string selected)
        {
            combo.Items.Clear();
            if (!String.IsNullOrEmpty(first)) combo.Items.Add(first);
            foreach (string s in values) combo.Items.Add(s);

            if (!String.IsNullOrEmpty(selected) && combo.Items.Contains(selected))
                combo.SelectedItem = selected;
            else if (combo.Items.Count > 0)
                combo.SelectedIndex = 0;
        }

        private void SaveCurrentFilterState()
        {
            FilterState state = _tabs.SelectedIndex == 0 ? _trackFilter : _cdFilter;
            state.Search = _searchBox.Text ?? "";
            state.Genre = Selected(_genreCombo);
            state.Digital = Selected(_digitalCombo);
            state.Year = Selected(_yearCombo);
            state.Medium = Selected(_mediumCombo);
            state.Cdx = DiscPlaybackRule.Current.Enabled ? Selected(_cdxCombo) : "";
            state.Mix = Selected(_mixCombo);
            state.Label = Selected(_labelCombo);
            state.Issues = Selected(_issueCombo);
        }

        private static string Selected(ComboBox c)
        {
            object x = c.SelectedItem;
            return x == null ? "" : x.ToString();
        }

        private void ResetFilters()
        {
            FilterState state = _tabs.SelectedIndex == 0 ? _trackFilter : _cdFilter;
            state.Search = "";
            state.Genre = "";
            state.Digital = "";
            state.Year = "";
            state.Medium = "";
            state.Cdx = "";
            state.Mix = "";
            state.Label = "";
            state.Issues = "";
            _searchBox.Text = "";
            PopulateFilterOptions();
            RefreshCurrentView();
        }

        private bool TrackFilter(object obj)
        {
            TrackRow t = obj as TrackRow;
            if (t == null) return false;
            if (!UiHelpers.ContainsAllTerms(t.SearchText, _searchBox.Text)) return false;

            string genre = _trackFilter.Genre;
            if (!String.IsNullOrEmpty(genre) && genre != "All Genres" && !String.Equals(t.Genre, genre, StringComparison.OrdinalIgnoreCase)) return false;

            string year = _trackFilter.Year;
            if (!String.IsNullOrEmpty(year) && year != "All" && !String.Equals(t.Date, year, StringComparison.OrdinalIgnoreCase)) return false;

            string label = _trackFilter.Label;
            if (!String.IsNullOrEmpty(label) && label != "All Labels" && !String.Equals(t.Label, label, StringComparison.OrdinalIgnoreCase)) return false;

            string issues = _trackFilter.Issues;
            if (issues == "Issues Only" && String.IsNullOrEmpty(t.IssueCode)) return false;
            if (issues == "No Issues" && !String.IsNullOrEmpty(t.IssueCode)) return false;

            string digital = _trackFilter.Digital;
            if (!String.IsNullOrEmpty(digital) && digital != "All")
            {
                if (digital == "Strong" && t.DigitalLevel != "strong") return false;
                if (digital == "Likely" && t.DigitalLevel != "likely") return false;
                if (digital == "Candidate" && t.DigitalLevel != "candidate") return false;
                if (digital == "No Match" && t.DigitalLevel != "none") return false;
            }

            string cdx = _trackFilter.Cdx;
            if (DiscPlaybackRule.Current.Enabled && !String.IsNullOrEmpty(cdx) && cdx != "All" &&
                !String.Equals(t.CdxCompatibilityText, cdx, StringComparison.OrdinalIgnoreCase)) return false;

            string mix = _trackFilter.Mix;
            if (mix == "With Mix" && String.IsNullOrEmpty(t.Version)) return false;
            if (mix == "Without Mix" && !String.IsNullOrEmpty(t.Version)) return false;

            return true;
        }

        private bool CdFilter(object obj)
        {
            CdRow c = obj as CdRow;
            if (c == null) return false;
            if (!UiHelpers.ContainsAllTerms(c.SearchText, _searchBox.Text)) return false;

            string genre = _cdFilter.Genre;
            if (!String.IsNullOrEmpty(genre) && genre != "All Genres" && !String.Equals(c.Genre, genre, StringComparison.OrdinalIgnoreCase)) return false;

            string year = _cdFilter.Year;
            if (!String.IsNullOrEmpty(year) && year != "All" && !String.Equals(c.Date, year, StringComparison.OrdinalIgnoreCase)) return false;

            string label = _cdFilter.Label;
            if (!String.IsNullOrEmpty(label) && label != "All Labels" && !String.Equals(c.Label, label, StringComparison.OrdinalIgnoreCase)) return false;

            string issues = _cdFilter.Issues;
            if (issues == "Issues Only" && String.IsNullOrEmpty(c.IssueCode)) return false;
            if (issues == "No Issues" && !String.IsNullOrEmpty(c.IssueCode)) return false;

            string medium = _cdFilter.Medium;
            if (!String.IsNullOrEmpty(medium) && medium != "All" && !String.Equals(c.Medium, medium, StringComparison.OrdinalIgnoreCase)) return false;

            string cdx = _cdFilter.Cdx;
            if (DiscPlaybackRule.Current.Enabled && !String.IsNullOrEmpty(cdx) && cdx != "All" &&
                !String.Equals(c.CdxCompatibilityText, cdx, StringComparison.OrdinalIgnoreCase)) return false;

            string digital = _cdFilter.Digital;
            if (!String.IsNullOrEmpty(digital) && digital != "All")
            {
                if (digital == "All Tracks Strong" && !(c.Tracks > 0 && c.StrongTracks == c.Tracks)) return false;
                if (digital == "Partially Strong" && !(c.StrongTracks > 0 && c.StrongTracks < c.Tracks)) return false;
                if (digital == "No Strong Match" && c.StrongTracks != 0) return false;
            }

            return true;
        }

        private void RefreshCurrentView()
        {
            SaveCurrentFilterState();
            if (_tabs.SelectedIndex == 0)
            {
                _trackView.Refresh();
                int n = CountView(_trackView);
                _countText.Text = String.Format("{0:N0} matches", n);
                _statusText.Text = String.Format("Tracks · {0:N0} von {1:N0} sichtbar", n, _data.Tracks.Count);
                _filterStatusText.Text = "Filter: " + CountActiveFilters(_trackFilter).ToString();
            }
            else
            {
                _cdView.Refresh();
                int n = CountView(_cdView);
                _countText.Text = String.Format("{0:N0} matches", n);
                _statusText.Text = String.Format("CDs · {0:N0} von {1:N0} sichtbar", n, _data.Cds.Count);
                _filterStatusText.Text = "Filter: " + CountActiveFilters(_cdFilter).ToString();
            }
        }

        private int CountActiveFilters(FilterState state)
        {
            int n = 0;
            if (!String.IsNullOrWhiteSpace(_searchBox.Text)) n++;
            if (!String.IsNullOrEmpty(state.Genre) && state.Genre != "All Genres") n++;
            if (!String.IsNullOrEmpty(state.Digital) && state.Digital != "All") n++;
            if (!String.IsNullOrEmpty(state.Year) && state.Year != "All") n++;
            if (!String.IsNullOrEmpty(state.Label) && state.Label != "All Labels") n++;
            if (!String.IsNullOrEmpty(state.Issues) && state.Issues != "All") n++;
            if (!String.IsNullOrEmpty(state.Medium) && state.Medium != "All") n++;
            if (DiscPlaybackRule.Current.Enabled && !String.IsNullOrEmpty(state.Cdx) && state.Cdx != "All") n++;
            if (!String.IsNullOrEmpty(state.Mix) && state.Mix != "All") n++;
            return n;
        }

        private static int CountView(ICollectionView view)
        {
            int n = 0;
            foreach (object x in view) n++;
            return n;
        }

        private static void CopyFilter(FilterState source, FilterState target)
        {
            if (source == null || target == null) return;
            target.Search = source.Search ?? "";
            target.Genre = source.Genre ?? "";
            target.Digital = source.Digital ?? "";
            target.Year = source.Year ?? "";
            target.Medium = source.Medium ?? "";
            target.Cdx = source.Cdx ?? "";
            target.Mix = source.Mix ?? "";
            target.Label = source.Label ?? "";
            target.Issues = source.Issues ?? "";
        }

        internal string ValidateRuntimeUiContract()
    {
        if (Icon == null) throw new InvalidOperationException("UI-Produktionspfad: MainWindow-Icon fehlt.");
        if (!GridRuntimeSupport.HasCdxColumn(_cdGrid) || !GridRuntimeSupport.HasCdxColumn(_trackGrid))
            throw new InvalidOperationException("UI production path: playback column missing from CD or track grid.");
        if (_cdGrid.RowStyle == null) throw new InvalidOperationException("UI-Produktionspfad: MainWindow-CD-Grid besitzt keine CDX-Zeilenmarkierung.");
        if (!GridRuntimeSupport.HasClipboardMenu(_cdGrid) || !GridRuntimeSupport.HasClipboardMenu(_trackGrid))
            throw new InvalidOperationException("UI-Produktionspfad: Clipboard-Wiring fehlt an MainWindow-Grids.");

        _tabs.SelectedIndex = 1;
        PopulateFilterOptions();
        if (_cdxPanel.Visibility != Visibility.Visible || !_cdxCombo.Items.Contains("Yes") || !_cdxCombo.Items.Contains("No") || !_cdxCombo.Items.Contains("Unknown"))
            throw new InvalidOperationException("UI production path: CDX filter is not fully wired.");

        CdRow incompatible = _data.Cds.FirstOrDefault(delegate(CdRow c) { return c.CdxCompatibilityState == CdxCompatibilityState.Incompatible; });
        CdRow compatible = _data.Cds.FirstOrDefault(delegate(CdRow c) { return c.CdxCompatibilityState == CdxCompatibilityState.Compatible; });
        if (incompatible == null || compatible == null) throw new InvalidOperationException("UI-Produktionspfad: CDX-Filter-Testdaten fehlen.");
        string old = _cdFilter.Cdx;
        _cdFilter.Cdx = "No";
        bool acceptsNo = CdFilter(incompatible);
        bool rejectsYes = !CdFilter(compatible);
        _cdFilter.Cdx = old;
        if (!acceptsNo || !rejectsYes) throw new InvalidOperationException("UI production path: CDX filter predicate is ineffective.");
        if (incompatible.CdxCompatibilityText != "No") throw new InvalidOperationException("UI production path: CDX No contains unexpected extra glyphs.");
        TrackRow incompatibleTrack = _data.Tracks.FirstOrDefault(x => x.DiscId == incompatible.DiscId);
        if (incompatibleTrack == null || incompatibleTrack.CdxCompatibilityText != "No" ||
            incompatibleTrack.DiscToc != incompatible.Toc)
            throw new InvalidOperationException("UI production path: tracks did not inherit their parent disc playback limit.");

        return "MainWindow: CDX-Spalte + disc-spezifische Rotmarkierung + Filter Yes/No/Unknown + explizites Clipboard + Icon";
    }

        private static FilterState CloneFilter(FilterState source)
        {
            FilterState copy = new FilterState();
            CopyFilter(source, copy);
            return copy;
        }

        private void RefreshPlaybackRuleViews()
        {
            // Profile setting already saved atomically by the rule dialog.
            _trackFilter.Cdx = "";
            _cdFilter.Cdx = "";
            foreach (DataGrid grid in new[] { _trackGrid, _cdGrid })
            {
                DataGridColumn column = grid.Columns.FirstOrDefault(x => x.SortMemberPath == "CdxCompatibilitySortKey");
                if (column != null) column.Visibility = DiscPlaybackRule.Current.Enabled ? Visibility.Visible : Visibility.Collapsed;
            }
            PopulateFilterOptions();
            _trackView.Refresh();
            _cdView.Refresh();
            _trackGrid.Items.Refresh();
            _cdGrid.Items.Refresh();
            RefreshCurrentView();
        }

        private void ConfigureColumns()
        {
            DataGrid grid = _tabs.SelectedIndex == 0 ? _trackGrid : _cdGrid;
            string title = _tabs.SelectedIndex == 0 ? "Configure Track Columns" : "Configure CD Columns";
            ColumnsWindow w = new ColumnsWindow(this, grid, title);
            w.ShowDialog();
        }

        private void ResetCurrentColumns()
        {
            DataGrid grid = _tabs.SelectedIndex == 0 ? _trackGrid : _cdGrid;
            List<ColumnSpec> specs = _tabs.SelectedIndex == 0 ? _trackColumns : _cdColumns;

            Dictionary<string, ColumnSpec> map = specs.ToDictionary(delegate(ColumnSpec x) { return x.Key; }, StringComparer.OrdinalIgnoreCase);
            int i;
            for (i = 0; i < grid.Columns.Count; i++)
            {
                DataGridColumn c = grid.Columns[i];
                ColumnSpec s;
                if (map.TryGetValue(c.SortMemberPath, out s))
                {
                    c.Width = new DataGridLength(s.Width);
                    c.Visibility = s.Visible ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            for (i = 0; i < specs.Count; i++)
            {
                DataGridColumn col = grid.Columns.FirstOrDefault(delegate(DataGridColumn c) { return c.SortMemberPath == specs[i].Key; });
                if (col != null)
                {
                    try { col.DisplayIndex = i; }
                    catch { }
                }
            }
        }
    }
}
