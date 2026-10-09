using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DJLibrary
{
    public sealed partial class CatalogWindow : Window
    {
        private CatalogService _catalog;
        private readonly string _seedPath;
        private readonly TextBox _searchBox;
        private readonly DataGrid _releaseGrid;
        private readonly DataGrid _discGrid;
        private readonly DataGrid _trackGrid;
        private readonly ListBox _historyList;
        private readonly TextBlock _status;
        private readonly DispatcherTimer _searchTimer;
        private readonly Button _undoButton;
        private readonly Button _redoButton;
        private readonly Button _addDiscButton;
        private readonly Button _addTrackButton;
        private readonly Button _editReleaseButton;
        private readonly Button _deleteReleaseButton;
        private readonly Button _editDiscButton;
        private readonly Button _deleteDiscButton;
        private readonly Button _editTrackButton;
        private readonly Button _deleteTrackButton;
        private readonly Button _moveUpButton;
        private readonly Button _moveDownButton;
        private DataGrid _activeGrid;
        private List<CatalogRelease> _allReleases = new List<CatalogRelease>();
        private List<CatalogRelease> _releases = new List<CatalogRelease>();
        private List<CatalogDisc> _discs = new List<CatalogDisc>();
        private List<CatalogTrack> _tracks = new List<CatalogTrack>();
        private List<CatalogSearchDocument> _searchDocuments = new List<CatalogSearchDocument>();
        private CatalogSearchResult _searchResult = new CatalogSearchResult();
        private CatalogCounts _counts;
        private string _quickCheck = "";

        public CatalogWindow(Window owner, string seedPath)
        {
            if (owner != null) Owner = owner;
            _seedPath = seedPath;
            Title = "DJ Library — Catalog Manager";
            GridRuntimeSupport.ApplyWindowIcon(this);
            Width = 1380;
            Height = 860;
            MinWidth = 980;
            MinHeight = 620;
            WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;

            _catalog = CatalogService.EnsureInitialized(seedPath);

            DockPanel root = new DockPanel();
            Content = root;

            Menu menu = BuildMenu();
            DockPanel.SetDock(menu, Dock.Top);
            root.Children.Add(menu);

            ToolBarTray tray = new ToolBarTray();
            tray.IsLocked = true;
            ToolBar bar = new ToolBar();
            tray.ToolBars.Add(bar);
            DockPanel.SetDock(tray, Dock.Top);
            root.Children.Add(tray);

            _undoButton = IconButton("↶", "Undo (Ctrl+Z)", delegate { UndoLast(); });
            _redoButton = IconButton("↷", "Redo (Ctrl+Y)", delegate { RedoLast(); });
            bar.Items.Add(_undoButton);
            bar.Items.Add(_redoButton);
            bar.Items.Add(new Separator());
            bar.Items.Add(Button("Backup…", "Create a validated SQLite backup of the writable catalog.", delegate { Backup(); }));

            DockPanel searchPanel = new DockPanel();
            searchPanel.Margin = new Thickness(8, 6, 8, 6);
            DockPanel.SetDock(searchPanel, Dock.Top);
            root.Children.Add(searchPanel);

            TextBlock searchLabel = new TextBlock();
            searchLabel.Text = "Search:";
            searchLabel.VerticalAlignment = VerticalAlignment.Center;
            searchLabel.Margin = new Thickness(0, 0, 6, 0);
            DockPanel.SetDock(searchLabel, Dock.Left);
            searchPanel.Children.Add(searchLabel);

            Button clearSearch = Button("Reset", "Clear catalog search.", delegate { _searchTimer.Stop(); _searchBox.Text = ""; ApplySearch(false); });
            DockPanel.SetDock(clearSearch, Dock.Right);
            searchPanel.Children.Add(clearSearch);

            _searchBox = new TextBox();
            _searchBox.Height = 26;
            _searchBox.VerticalContentAlignment = VerticalAlignment.Center;
            _searchBox.ToolTip = "Filters release, disc, and track fields. Multiple terms are combined with AND.";
            _searchTimer = new DispatcherTimer();
            _searchTimer.Interval = TimeSpan.FromMilliseconds(120);
            _searchTimer.Tick += delegate
            {
                _searchTimer.Stop();
                ApplySearch(true);
            };
            _searchBox.TextChanged += delegate
            {
                _searchTimer.Stop();
                _searchTimer.Start();
            };
            searchPanel.Children.Add(_searchBox);

            _status = new TextBlock();
            _status.Margin = new Thickness(8, 4, 8, 6);
            DockPanel.SetDock(_status, Dock.Bottom);
            root.Children.Add(_status);

            Grid grid = new Grid();
            grid.Margin = new Thickness(8, 0, 8, 8);
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.15, GridUnitType.Star), MinHeight = 150 });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(5) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0.55, GridUnitType.Star), MinHeight = 105 });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(5) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.35, GridUnitType.Star), MinHeight = 170 });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.Children.Add(grid);

            _releaseGrid = CreateGrid();
            _discGrid = CreateGrid();
            _trackGrid = CreateGrid();
            ConfigureReleaseColumns();
            ConfigureDiscColumns();
            ConfigureTrackColumns();

            Button addRelease = Button("＋ New Release", "Create a new release.", delegate { AddRelease(); });
            _editReleaseButton = Button("✎ Edit", "Edit selected release (F2).", delegate { EditRelease(); });
            _deleteReleaseButton = Button("✕ Delete", "Delete selected release including discs and tracks (Del).", delegate { DeleteRelease(); });

            _addDiscButton = Button("＋ Add Disc", "Add a new disc to the selected release.", delegate { AddDisc(); });
            _editDiscButton = Button("✎ Edit", "Edit selected disc (F2).", delegate { EditDisc(); });
            _deleteDiscButton = Button("✕ Delete", "Delete selected disc including tracks (Del).", delegate { DeleteDisc(); });
            Button captureDisc = Button("Audio CD…", "Read the Audio CD first, then import it as a new release or as a disc of an existing release.", delegate { CaptureCd(); });

            _addTrackButton = Button("＋ Add Track", "Add a new track to the selected disc.", delegate { AddTrack(); });
            _editTrackButton = Button("✎ Edit", "Edit selected track (F2).", delegate { EditTrack(); });
            _deleteTrackButton = Button("✕ Delete", "Delete selected track (Del).", delegate { DeleteTrack(); });
            _moveUpButton = IconButton("↑", "Move selected track up one position.", delegate { MoveTrack(-1); });
            _moveDownButton = IconButton("↓", "Move selected track down one position.", delegate { MoveTrack(1); });

            Border releaseBox = Section("Releases", _releaseGrid, addRelease, _editReleaseButton, _deleteReleaseButton);
            Border discBox = Section("Discs of Selected Release", _discGrid, _addDiscButton, _editDiscButton, _deleteDiscButton, captureDisc);
            Border trackBox = Section("Tracks of Selected Disc", _trackGrid, _addTrackButton, _editTrackButton, _deleteTrackButton, _moveUpButton, _moveDownButton);
            Grid.SetRow(releaseBox, 0);
            Grid.SetRow(discBox, 2);
            Grid.SetRow(trackBox, 4);
            grid.Children.Add(releaseBox);
            grid.Children.Add(discBox);
            grid.Children.Add(trackBox);

            GridSplitter split1 = new GridSplitter { Height = 5, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center, Background = SystemColors.ControlLightBrush, ResizeDirection = GridResizeDirection.Rows, ResizeBehavior = GridResizeBehavior.PreviousAndNext };
            Grid.SetRow(split1, 1);
            grid.Children.Add(split1);
            GridSplitter split2 = new GridSplitter { Height = 5, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center, Background = SystemColors.ControlLightBrush, ResizeDirection = GridResizeDirection.Rows, ResizeBehavior = GridResizeBehavior.PreviousAndNext };
            Grid.SetRow(split2, 3);
            grid.Children.Add(split2);

            _historyList = new ListBox();
            _historyList.DisplayMemberPath = "DisplayText";
            _historyList.MinHeight = 90;
            _historyList.MaxHeight = 180;
            Expander history = new Expander();
            history.Header = "Change History";
            history.IsExpanded = false;
            history.Margin = new Thickness(3, 3, 3, 0);
            history.Content = _historyList;
            history.ToolTip = "Recent catalog changes; collapsed by default to leave more room for releases and tracks.";
            Grid.SetRow(history, 5);
            grid.Children.Add(history);

            _releaseGrid.SelectionChanged += delegate { RefreshDiscsAndTracks(); UpdateCommandState(); };
            _discGrid.SelectionChanged += delegate { RefreshTracks(); UpdateCommandState(); };
            _trackGrid.SelectionChanged += delegate { UpdateCommandState(); };
            _releaseGrid.GotKeyboardFocus += delegate { _activeGrid = _releaseGrid; UpdateCommandState(); };
            _discGrid.GotKeyboardFocus += delegate { _activeGrid = _discGrid; UpdateCommandState(); };
            _trackGrid.GotKeyboardFocus += delegate { _activeGrid = _trackGrid; UpdateCommandState(); };
            _trackGrid.MouseDoubleClick += delegate { EditTrack(); };
            _releaseGrid.MouseDoubleClick += delegate { EditRelease(); };
            _discGrid.MouseDoubleClick += delegate { EditDisc(); };
            _activeGrid = _releaseGrid;

            Closed += delegate
            {
                SaveCatalogGridLayouts();
                if (_catalog != null) { _catalog.Dispose(); _catalog = null; }
            };

            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                bool gridFocus = _releaseGrid.IsKeyboardFocusWithin || _discGrid.IsKeyboardFocusWithin || _trackGrid.IsKeyboardFocusWithin;
                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.Z && !_searchBox.IsKeyboardFocusWithin)
                {
                    UndoLast();
                    e.Handled = true;
                }
                else if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.Y && !_searchBox.IsKeyboardFocusWithin)
                {
                    RedoLast();
                    e.Handled = true;
                }
                else if (e.Key == Key.F2 && gridFocus)
                {
                    EditSelected();
                    e.Handled = true;
                }
                else if (e.Key == Key.Delete && gridFocus)
                {
                    DeleteSelected();
                    e.Handled = true;
                }
            };

            HorizontalScrollSupport.Enable(this);
            ReloadData(false);
        }

        private Menu BuildMenu()
        {
            Menu menu = new Menu();
            MenuItem file = new MenuItem { Header = "_File" };
            MenuItem backup = new MenuItem { Header = "_Create Backup…" };
            backup.Click += delegate { Backup(); };
            file.Items.Add(backup);
            MenuItem restore = new MenuItem { Header = "_Restore Backup…" };
            restore.Click += delegate { Restore(); };
            file.Items.Add(restore);
            file.Items.Add(new Separator());
            MenuItem close = new MenuItem { Header = "_Close" };
            close.Click += delegate { Close(); };
            file.Items.Add(close);
            menu.Items.Add(file);

            MenuItem create = new MenuItem { Header = "_New" };
            MenuItem createRelease = new MenuItem { Header = "_Release" };
            createRelease.Click += delegate { AddRelease(); };
            create.Items.Add(createRelease);
            MenuItem createDisc = new MenuItem { Header = "_Disc" };
            createDisc.Click += delegate { AddDisc(); };
            create.Items.Add(createDisc);
            MenuItem createTrack = new MenuItem { Header = "_Track" };
            createTrack.Click += delegate { AddTrack(); };
            create.Items.Add(createTrack);
            menu.Items.Add(create);

            MenuItem edit = new MenuItem { Header = "_Edit" };
            MenuItem undo = new MenuItem { Header = "_Undo", InputGestureText = "Ctrl+Z" };
            undo.Click += delegate { UndoLast(); };
            edit.Items.Add(undo);
            MenuItem redo = new MenuItem { Header = "_Redo", InputGestureText = "Ctrl+Y" };
            redo.Click += delegate { RedoLast(); };
            edit.Items.Add(redo);
            edit.Items.Add(new Separator());
            MenuItem editSelection = new MenuItem { Header = "_Edit Selection", InputGestureText = "F2" };
            editSelection.Click += delegate { EditSelected(); };
            edit.Items.Add(editSelection);
            MenuItem deleteSelection = new MenuItem { Header = "_Delete Selection", InputGestureText = "Del" };
            deleteSelection.Click += delegate { DeleteSelected(); };
            edit.Items.Add(deleteSelection);
            edit.Items.Add(new Separator());
            MenuItem refresh = new MenuItem { Header = "_Refresh" };
            refresh.Click += delegate { ReloadData(false); };
            edit.Items.Add(refresh);
            menu.Items.Add(edit);

            MenuItem cd = new MenuItem { Header = "_Audio CD" };
            MenuItem capture = new MenuItem { Header = "_Read New Disc…" };
            capture.Click += delegate { CaptureCd(); };
            cd.Items.Add(capture);
            menu.Items.Add(cd);
            return menu;
        }

        private static Border Section(string title, UIElement child, params Button[] actions)
        {
            Border border = new Border();
            border.BorderBrush = SystemColors.ControlDarkBrush;
            border.BorderThickness = new Thickness(1);
            border.Margin = new Thickness(3);

            DockPanel panel = new DockPanel();
            DockPanel header = new DockPanel();
            header.Margin = new Thickness(5, 3, 5, 3);
            DockPanel.SetDock(header, Dock.Top);

            StackPanel buttons = new StackPanel();
            buttons.Orientation = Orientation.Horizontal;
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            DockPanel.SetDock(buttons, Dock.Right);
            foreach (Button action in actions) buttons.Children.Add(action);
            header.Children.Add(buttons);

            TextBlock titleBlock = new TextBlock();
            titleBlock.Text = title;
            titleBlock.FontWeight = FontWeights.SemiBold;
            titleBlock.VerticalAlignment = VerticalAlignment.Center;
            titleBlock.Margin = new Thickness(2, 0, 12, 0);
            header.Children.Add(titleBlock);

            panel.Children.Add(header);
            panel.Children.Add(child);
            border.Child = panel;
            return border;
        }

        private static Button Button(string text, string tip, RoutedEventHandler handler)
        {
            Button b = new Button();
            b.Content = text;
            b.ToolTip = tip;
            b.Padding = new Thickness(8, 2, 8, 2);
            b.Margin = new Thickness(1, 0, 1, 0);
            b.Click += handler;
            return b;
        }

        private static Button IconButton(string glyph, string tip, RoutedEventHandler handler)
        {
            Button b = Button(glyph, tip, handler);
            b.FontSize = 18;
            b.Padding = new Thickness(7, 0, 7, 1);
            b.MinWidth = 32;
            return b;
        }

        private static DataGrid CreateGrid()
        {
            DataGrid g = UiHelpers.CreateReadOnlyGrid();
            GridGovernance.Apply(g);
            return g;
        }

        private void ConfigureReleaseColumns()
        {
            AddTextColumn(_releaseGrid, "Artist", "AlbumArtist", 150);
            AddTextColumn(_releaseGrid, "Album", "Album", 180);
            AddTextColumn(_releaseGrid, "Year", "ReleaseDate", 65);
            AddTextColumn(_releaseGrid, "Genre", "Genre", 110);
            AddTextColumn(_releaseGrid, "Label", "Label", 120);
            AddTextColumn(_releaseGrid, "Catalog Number", "Catalog", 120);
            AddTextColumn(_releaseGrid, "Country", "Country", 70);
            AddTextColumn(_releaseGrid, "Discs", "TotalDiscs", 55);
        }

        private void ConfigureDiscColumns()
        {
            ConfigureDiscColumns(_discGrid);
        }

        private static void ConfigureDiscColumns(DataGrid grid)
        {
            AddTextColumn(grid, "Disc", "DiscNumber", 60);
            AddTextColumn(grid, "Medium", "Medium", 110);
            AddTextColumn(grid, "Duration", "DurationText", "DurationSeconds", 95);
            grid.Columns.Add(GridRuntimeSupport.CreateCdxColumn(new ColumnSpec("CdxCompatibilitySortKey", "Disc playable",
                "Physical TOC-based compatibility using the current user-defined player rule.", "CdxCompatibilityText", 118, DiscPlaybackRule.Current.Enabled)));
            grid.AlternatingRowBackground = null;
            grid.RowStyle = GridRuntimeSupport.CreateCdxRowStyle();
            AddCheckColumn(grid, "TOC Complete", "TocComplete", 110, "Stored TOC is complete.");
            AddCheckColumn(grid, "CD-TEXT Available", "CdTextPresent", 120, "DJ Library has verified CD-TEXT. An unchecked box only means it is currently unavailable.");
        }

        internal static string ValidateDiscGridRuntimeContract()
        {
            DataGrid grid = CreateGrid();
            ConfigureDiscColumns(grid);
            if (!GridRuntimeSupport.HasCdxColumn(grid)) throw new InvalidOperationException("UI-Produktionspfad: Catalog-Disc-Grid besitzt keine echte CDX-Spalte.");
            if (!GridRuntimeSupport.HasDeterministicCdxRowStyle(grid)) throw new InvalidOperationException("UI-Produktionspfad: Catalog-Disc-Grid besitzt keine deterministische CDX-Zeilenmarkierung gegen AlternatingRowBackground.");
            if (!GridRuntimeSupport.HasClipboardMenu(grid)) throw new InvalidOperationException("UI production path: Catalog disc grid has no clipboard menu.");
            DataGrid tracks = CreateGrid();
            tracks.Columns.Add(GridRuntimeSupport.CreateCdxColumn(new ColumnSpec("CdxCompatibilitySortKey", "Disc playable",
                "Inherits the parent physical CD's TOC.", "CdxCompatibilityText", 112, true)));
            tracks.RowStyle = GridRuntimeSupport.CreateCdxRowStyle();
            if (!GridRuntimeSupport.HasCdxColumn(tracks) || !GridRuntimeSupport.HasDeterministicCdxRowStyle(tracks))
                throw new InvalidOperationException("UI production path: catalog tracks lack playback-status column or row marker.");
            return "Catalog: physical CD/track playability column + marker + Clipboard";
        }

        private void ConfigureTrackColumns()
        {
            AddTextColumn(_trackGrid, "#", "Position", 45);
            AddTextColumn(_trackGrid, "Artist", "Artist", 140);
            AddTextColumn(_trackGrid, "Title", "Title", 170);
            AddTextColumn(_trackGrid, "Mix/Version", "Version", 120);
            AddTextColumn(_trackGrid, "Genre", "Genre", 100);
            AddTextColumn(_trackGrid, "BPM", "Bpm", 65);
            AddTextColumn(_trackGrid, "Duration", "DurationText", "DurationSeconds", 86);
            _trackGrid.Columns.Add(GridRuntimeSupport.CreateCdxColumn(new ColumnSpec("CdxCompatibilitySortKey", "Disc playable",
                "Compatibility of this track's containing physical CD.", "CdxCompatibilityText", 112, DiscPlaybackRule.Current.Enabled)));
            _trackGrid.AlternatingRowBackground = null;
            _trackGrid.RowStyle = GridRuntimeSupport.CreateCdxRowStyle();
        }

        private static void AddTextColumn(DataGrid grid, string header, string path, double width)
        {
            AddTextColumn(grid, header, path, path, width);
        }

        private static void AddTextColumn(DataGrid grid, string header, string path, string sortPath, double width)
        {
            DataGridTextColumn c = new DataGridTextColumn();
            c.Header = header;
            c.Binding = new System.Windows.Data.Binding(path);
            c.SortMemberPath = sortPath;
            c.Width = new DataGridLength(width);
            grid.Columns.Add(c);
        }

        private static void AddCheckColumn(DataGrid grid, string header, string path, double width, string tip)
        {
            DataGridCheckBoxColumn c = new DataGridCheckBoxColumn();
            c.Header = UiHelpers.Header(header, tip);
            c.Binding = new Binding(path);
            c.SortMemberPath = path;
            c.Width = new DataGridLength(width);
            c.IsReadOnly = true;
            c.IsThreeState = false;
            grid.Columns.Add(c);
        }

        private CatalogRelease SelectedRelease { get { return _releaseGrid.SelectedItem as CatalogRelease; } }
        private CatalogDisc SelectedDisc { get { return _discGrid.SelectedItem as CatalogDisc; } }
        private CatalogTrack SelectedTrack { get { return _trackGrid.SelectedItem as CatalogTrack; } }

        private void ReloadData(bool keepSelection)
        {
            if (_catalog == null) return;
            if (_catalogInitialLoadComplete)
            {
                GridLayoutSettings.Capture(_workspaceSettings, "catalog.releases", _releaseGrid);
                GridLayoutSettings.Capture(_workspaceSettings, "catalog.discs", _discGrid);
                GridLayoutSettings.Capture(_workspaceSettings, "catalog.tracks", _trackGrid);
            }
            _allReleases = _catalog.GetReleases();
            _searchDocuments = _catalog.GetSearchDocuments();
            _counts = _catalog.GetCounts();
            _quickCheck = _catalog.QuickCheck();
            ApplySearch(keepSelection);
            RefreshHistoryAndCommands();
            RefreshStatus(null);
            RestoreCatalogGridLayouts();
            NotifyCatalogReloaded();
        }

        private void ApplySearch(bool keepSelection)
        {
            if (_catalog == null) return;
            long releaseId = keepSelection && SelectedRelease != null ? SelectedRelease.Id : 0;
            string query = (_searchBox.Text ?? "").Trim();
            if (query.Length == 0)
            {
                _searchResult = new CatalogSearchResult();
                _releases = new List<CatalogRelease>(_allReleases);
            }
            else
            {
                _searchResult = CatalogService.SearchDocuments(_searchDocuments, query);
                _releases = _allReleases.Where(x => _searchResult.ReleaseIds.Contains(x.Id)).ToList();
            }

            _releaseGrid.ItemsSource = _releases;
            if (releaseId > 0) SelectById(_releaseGrid, _releases.Cast<object>(), releaseId);
            if (_releaseGrid.SelectedItem == null && _releases.Count > 0) _releaseGrid.SelectedIndex = 0;
            RefreshDiscsAndTracks();
        }

        private void RefreshDiscsAndTracks()
        {
            CatalogRelease r = SelectedRelease;
            _discs = r == null ? new List<CatalogDisc>() : _catalog.GetDiscs(r.Id);
            string query = (_searchBox.Text ?? "").Trim();
            if (r != null && query.Length > 0)
                _discs = _discs.Where(x => _searchResult.DiscIds.Contains(x.Id)).ToList();

            _discGrid.ItemsSource = _discs;
            if (_discs.Count > 0) _discGrid.SelectedIndex = 0;
            else _trackGrid.ItemsSource = new List<CatalogTrack>();
            RefreshTracks();
        }

        private void RefreshTracks()
        {
            CatalogDisc d = SelectedDisc;
            _tracks = d == null ? new List<CatalogTrack>() : _catalog.GetTracks(d.Id);
            foreach (CatalogTrack track in _tracks)
            {
                track.ParentDiscToc = d.Toc;
                track.ParentDiscTocComplete = d.TocComplete;
            }
            string query = (_searchBox.Text ?? "").Trim();
            if (d != null && query.Length > 0)
                _tracks = _tracks.Where(x => _searchResult.TrackIds.Contains(x.Id)).ToList();
            _trackGrid.ItemsSource = _tracks;
        }

        private void RefreshHistoryAndCommands()
        {
            _historyList.ItemsSource = _catalog.GetHistory(100);
            _undoButton.IsEnabled = _catalog.CanUndo;
            _redoButton.IsEnabled = _catalog.CanRedo;
            UpdateCommandState();
        }

        private void UpdateCommandState()
        {
            bool releaseSelected = SelectedRelease != null;
            bool discSelected = SelectedDisc != null;
            bool trackSelected = SelectedTrack != null;

            _addDiscButton.IsEnabled = releaseSelected;
            _addTrackButton.IsEnabled = discSelected;
            _editReleaseButton.IsEnabled = releaseSelected;
            _deleteReleaseButton.IsEnabled = releaseSelected;
            _editDiscButton.IsEnabled = discSelected;
            _deleteDiscButton.IsEnabled = discSelected;
            _editTrackButton.IsEnabled = trackSelected;
            _deleteTrackButton.IsEnabled = trackSelected;
            _moveUpButton.IsEnabled = trackSelected;
            _moveDownButton.IsEnabled = trackSelected;
        }

        private void EditSelected()
        {
            if (_activeGrid == _trackGrid) EditTrack();
            else if (_activeGrid == _discGrid) EditDisc();
            else EditRelease();
        }

        private void DeleteSelected()
        {
            if (_activeGrid == _trackGrid) DeleteTrack();
            else if (_activeGrid == _discGrid) DeleteDisc();
            else DeleteRelease();
        }

        private void RefreshStatus(string suffix)
        {
            string text = String.Format("Catalog: {0:N0} Releases · {1:N0} CDs · {2:N0} Tracks · Schema v{3} · quick_check={4}",
                _counts.Releases, _counts.Discs, _counts.Tracks, _catalog.SchemaVersion, _quickCheck);
            if (!String.IsNullOrEmpty(suffix)) text += " · " + suffix;
            _status.Text = text;
        }

        private void RefreshAfterMutation(string status)
        {
            ReloadData(true);
            RefreshStatus(status);
        }

        private static void SelectById(DataGrid grid, IEnumerable<object> values, long id)
        {
            foreach (object value in values)
            {
                CatalogRelease r = value as CatalogRelease;
                if (r != null && r.Id == id) { grid.SelectedItem = value; grid.ScrollIntoView(value); return; }
            }
        }

        private void AddRelease()
        {
            CatalogRelease value = new CatalogRelease();
            if (!UnifiedEditors.EditRelease(this, "Release anlegen", value)) return;
            long id = _catalog.CreateRelease(value);
            ReloadData(false);
            foreach (CatalogRelease r in _releases) if (r.Id == id) { _releaseGrid.SelectedItem = r; break; }
        }

        private void EditRelease()
        {
            CatalogRelease current = SelectedRelease;
            if (current == null) return;
            CatalogRelease value = Clone(current);
            if (!UnifiedEditors.EditRelease(this, "Edit Release", value)) return;
            _catalog.UpdateRelease(value);
            ReloadData(true);
        }

        private void DeleteRelease()
        {
            CatalogRelease r = SelectedRelease;
            if (r == null) return;
            if (MessageBox.Show(this, "Release „" + r.DisplayText + "” including all discs and tracks?\n\nThe action can be restored with Undo.",
                "Delete Release", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            _catalog.DeleteRelease(r.Id);
            ReloadData(false);
        }

        private void AddDisc()
        {
            CatalogRelease r = SelectedRelease;
            if (r == null) { MessageBox.Show(this, "Bitte zuerst ein Release select."); return; }
            CatalogDisc d = new CatalogDisc();
            d.ReleaseId = r.Id;
            List<CatalogDisc> existing = _catalog.GetDiscs(r.Id);
            d.DiscNumber = existing.Count == 0 ? 1 : existing.Max(x => x.DiscNumber) + 1;
            if (!UnifiedEditors.EditDisc(this, "Disc anlegen", d)) return;
            long id = _catalog.AddDisc(d);
            ReloadData(true);
            foreach (CatalogDisc row in _discs) if (row.Id == id) { _discGrid.SelectedItem = row; _discGrid.ScrollIntoView(row); break; }
        }

        private void EditDisc()
        {
            CatalogDisc current = SelectedDisc;
            if (current == null) return;
            CatalogDisc value = Clone(current);
            if (!UnifiedEditors.EditDisc(this, "Edit Disc", value)) return;
            _catalog.UpdateDisc(value);
            ReloadData(true);
        }

        private void DeleteDisc()
        {
            CatalogDisc d = SelectedDisc;
            if (d == null) return;
            if (MessageBox.Show(this, "Disc " + d.DiscNumber + " including all tracks?\n\nThe action can be restored with Undo.",
                "Delete Disc", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            _catalog.DeleteDisc(d.Id);
            ReloadData(true);
        }

        private void AddTrack()
        {
            CatalogDisc d = SelectedDisc;
            if (d == null) { MessageBox.Show(this, "Select a disc first."); return; }
            CatalogTrack t = new CatalogTrack();
            t.DiscId = d.Id;
            List<CatalogTrack> existing = _catalog.GetTracks(d.Id);
            t.Position = existing.Count == 0 ? 1 : existing.Max(x => x.Position) + 1;
            if (!UnifiedEditors.EditTrack(this, "Create Track", t)) return;
            long id = _catalog.AddTrack(t);
            ReloadData(true);
            foreach (CatalogTrack row in _tracks) if (row.Id == id) { _trackGrid.SelectedItem = row; _trackGrid.ScrollIntoView(row); break; }
        }

        private void EditTrack()
        {
            CatalogTrack current = SelectedTrack;
            if (current == null) return;
            CatalogTrack value = Clone(current);
            if (!UnifiedEditors.EditTrack(this, "Edit Track", value)) return;
            _catalog.UpdateTrack(value);
            ReloadData(true);
        }

        private void DeleteTrack()
        {
            CatalogTrack t = SelectedTrack;
            if (t == null) return;
            if (MessageBox.Show(this, "Track „" + t.DisplayTitle + "”?\n\nThe action can be restored with Undo.",
                "Delete Track", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            _catalog.DeleteTrack(t.Id);
            ReloadData(true);
        }

        private void MoveTrack(int delta)
        {
            CatalogTrack t = SelectedTrack;
            CatalogDisc d = SelectedDisc;
            if (t == null || d == null) return;
            List<CatalogTrack> all = _catalog.GetTracks(d.Id);
            int index = all.FindIndex(x => x.Id == t.Id);
            int target = index + delta;
            if (index < 0 || target < 0 || target >= all.Count) return;
            CatalogTrack swap = all[index];
            all[index] = all[target];
            all[target] = swap;
            _catalog.ReorderTracks(d.Id, all.Select(x => x.Id).ToList());
            ReloadData(true);
            foreach (CatalogTrack row in _tracks) if (row.Id == t.Id) { _trackGrid.SelectedItem = row; break; }
        }

        private void CaptureCd()
        {
            List<WindowsCdDrive> drives = WindowsCdDrive.Enumerate();
            if (drives.Count == 0) { MessageBox.Show(this, "No optical CD drive was found.", "Audio CD", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            WindowsCdDrive drive = drives.Count == 1 ? drives[0] : DrivePickerDialog.Choose(this, drives);
            if (drive == null) return;

            CdSnapshot snapshot;
            try { snapshot = drive.Capture(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Audio CD could not be read", MessageBoxButton.OK, MessageBoxImage.Error); return; }

            // Physical acquisition ends here: TOC + CD-TEXT are shown before any optional
            // catalog/foobar/online provider is consulted. The preview owns the explicit
            // pre-fetch source selection and only then starts enrichment.
            List<CatalogTocMatch> duplicates = _catalog.FindByToc(snapshot.Toc);
            CdCaptureDecision decision;
            if (!CdCapturePreviewDialog.Choose(this, drive.DisplayName, snapshot, _allReleases, SelectedRelease, duplicates, MetadataDefaults, out decision)) return;
            if (decision.UseExistingCatalogDisc && decision.ExistingCatalogDisc != null)
            {
                CatalogTocMatch match = decision.ExistingCatalogDisc;
                ReloadData(false);
                foreach (CatalogRelease row in _releases)
                    if (row.Id == match.ReleaseId) { _releaseGrid.SelectedItem = row; _releaseGrid.ScrollIntoView(row); break; }
                RefreshDiscsAndTracks();
                foreach (CatalogDisc row in _discs)
                    if (row.Id == match.DiscId) { _discGrid.SelectedItem = row; _discGrid.ScrollIntoView(row); break; }
                RefreshStatus("Identical TOC already present · no duplicate created");
                return;
            }

            CatalogRelease release;
            int discNumber;
            if (decision.CreateNewRelease)
            {
                CatalogRelease value = new CatalogRelease();
                value.AlbumArtist = snapshot.AlbumArtist ?? ""; value.Album = snapshot.Album ?? ""; value.ReleaseDate = snapshot.ReleaseDate ?? "";
                value.Genre = snapshot.Genre ?? ""; value.Label = snapshot.Label ?? ""; value.Catalog = snapshot.Catalog ?? ""; value.Country = snapshot.Country ?? "";
                if (!UnifiedEditors.EditRelease(this, "Create Release from Audio CD", value)) return;
                long releaseId = _catalog.CreateRelease(value); release = _catalog.GetRelease(releaseId); discNumber = 1;
            }
            else
            {
                release = decision.ExistingRelease; if (release == null) return;
                List<CatalogDisc> existing = _catalog.GetDiscs(release.Id); discNumber = existing.Count == 0 ? 1 : existing.Max(x => x.DiscNumber) + 1;
            }

            long discId = _catalog.ImportCd(release.Id, snapshot, discNumber, "CD");
            ReloadData(false);
            foreach (CatalogRelease row in _releases) if (row.Id == release.Id) { _releaseGrid.SelectedItem = row; _releaseGrid.ScrollIntoView(row); break; }
            RefreshDiscsAndTracks();
            foreach (CatalogDisc row in _discs) if (row.Id == discId) { _discGrid.SelectedItem = row; _discGrid.ScrollIntoView(row); break; }
            RefreshStatus("Audio CD read and imported");
        }

        private void UndoLast()
        {
            if (!_catalog.CanUndo) return;
            try
            {
                _catalog.UndoLast();
                ReloadData(false);
                RefreshStatus("Undo");
            }
            catch (InvalidOperationException ex) { RefreshStatus(ex.Message); }
        }

        private void RedoLast()
        {
            if (!_catalog.CanRedo) return;
            try
            {
                _catalog.RedoLast();
                ReloadData(false);
                RefreshStatus("Redo");
            }
            catch (InvalidOperationException ex) { RefreshStatus(ex.Message); }
        }

        private void Backup()
        {
            SaveFileDialog dlg = new SaveFileDialog();
            dlg.Filter = "DJ Library SQLite Backup (*.sqlite)|*.sqlite|All Fileen (*.*)|*.*";
            dlg.FileName = "dj-library-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".sqlite";
            if (dlg.ShowDialog(this) != true) return;
            _catalog.Backup(dlg.FileName);
            MessageBox.Show(this, "Backup erfolgreich erstellt:\n" + dlg.FileName, "Backup", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Restore()
        {
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Filter = "DJ Library SQLite Backup (*.sqlite;*.bak)|*.sqlite;*.bak|All Fileen (*.*)|*.*";
            if (dlg.ShowDialog(this) != true) return;
            if (MessageBox.Show(this, "Den aktuellen Catalog durch dieses validierte Backup ersetzen?\n\nVor dem Restore bleibt automatisch eine .before-restore.bak erhalten.",
                "Backup wiederherstellen", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            _catalog.Restore(dlg.FileName);
            ReloadData(false);
        }

        private static CatalogRelease Clone(CatalogRelease x)
        {
            return new CatalogRelease { Id=x.Id, AlbumArtist=x.AlbumArtist, Album=x.Album, ReleaseDate=x.ReleaseDate, Genre=x.Genre, Label=x.Label, Catalog=x.Catalog, Country=x.Country, TotalDiscs=x.TotalDiscs };
        }
        private static CatalogDisc Clone(CatalogDisc x)
        {
            return new CatalogDisc { Id=x.Id, ReleaseId=x.ReleaseId, LegacyAlbumId=x.LegacyAlbumId, DiscNumber=x.DiscNumber, Medium=x.Medium, Toc=x.Toc, TocComplete=x.TocComplete, CdTextPresent=x.CdTextPresent, CdTextStatus=x.CdTextStatus, DurationSeconds=x.DurationSeconds, LegacyJson=x.LegacyJson };
        }
        private static CatalogTrack Clone(CatalogTrack x)
        {
            return new CatalogTrack { Id=x.Id, DiscId=x.DiscId, Position=x.Position, Artist=x.Artist, Title=x.Title, Version=x.Version, ReleaseDate=x.ReleaseDate, Genre=x.Genre, LegacyGenre=x.LegacyGenre, Bpm=x.Bpm, DurationSeconds=x.DurationSeconds, LegacyArtistRaw=x.LegacyArtistRaw, LegacyJson=x.LegacyJson };
        }
    }

    internal static class FormHelpers
    {
        public static Grid CreateForm(params string[] labels)
        {
            Grid g = new Grid();
            g.Margin = new Thickness(12);
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i=0;i<labels.Length;i++)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                TextBlock l = new TextBlock { Text = labels[i], Margin = new Thickness(0,6,8,6), VerticalAlignment=VerticalAlignment.Center };
                Grid.SetRow(l,i); Grid.SetColumn(l,0); g.Children.Add(l);
            }
            return g;
        }

        public static TextBox AddBox(Grid g, int row, string text)
        {
            TextBox b = new TextBox { Text=text ?? "", Margin=new Thickness(0,4,0,4), MinWidth=220, Height=26, VerticalContentAlignment=VerticalAlignment.Center };
            Grid.SetRow(b,row); Grid.SetColumn(b,1); g.Children.Add(b); return b;
        }

        public static CheckBox AddCheckBox(Grid g, int row, bool value, string toolTip)
        {
            CheckBox b = new CheckBox { IsChecked=value, Margin=new Thickness(0,6,0,6), VerticalAlignment=VerticalAlignment.Center };
            if (!String.IsNullOrEmpty(toolTip)) b.ToolTip = toolTip;
            Grid.SetRow(b,row); Grid.SetColumn(b,1); g.Children.Add(b); return b;
        }

        public static bool Show(Window owner, string title, Grid form, Action<Button,Button> addButtons)
        {
            Window w = new Window { Owner=owner, Title=title, Width=480, SizeToContent=SizeToContent.Height, WindowStartupLocation=WindowStartupLocation.CenterOwner, ShowInTaskbar=false };
            DockPanel root = new DockPanel(); w.Content=root;
            StackPanel buttons = new StackPanel { Orientation=Orientation.Horizontal, HorizontalAlignment=HorizontalAlignment.Right, Margin=new Thickness(8) };
            Button ok = new Button { Content="OK", IsDefault=true, MinWidth=80, Margin=new Thickness(4), Padding=new Thickness(10,3,10,3) };
            Button cancel = new Button { Content="Cancel", IsCancel=true, MinWidth=80, Margin=new Thickness(4), Padding=new Thickness(10,3,10,3) };
            buttons.Children.Add(ok); buttons.Children.Add(cancel); DockPanel.SetDock(buttons,Dock.Bottom); root.Children.Add(buttons); root.Children.Add(form);
            bool accepted=false; ok.Click += delegate { accepted=true; w.DialogResult=true; }; if(addButtons!=null) addButtons(ok,cancel);
            w.ShowDialog(); return accepted;
        }
    }

    internal static class ReleaseEditorDialog
    {
        public static bool Edit(Window owner, string title, CatalogRelease x)
        {
            Grid g=FormHelpers.CreateForm("Album Artist","Album","Date/Year","Genre","Label","Catalog","Country","Number of Discs");
            TextBox a=FormHelpers.AddBox(g,0,x.AlbumArtist), b=FormHelpers.AddBox(g,1,x.Album), d=FormHelpers.AddBox(g,2,x.ReleaseDate), ge=FormHelpers.AddBox(g,3,x.Genre);
            TextBox l=FormHelpers.AddBox(g,4,x.Label), c=FormHelpers.AddBox(g,5,x.Catalog), co=FormHelpers.AddBox(g,6,x.Country), td=FormHelpers.AddBox(g,7,x.TotalDiscs.ToString());
            bool ok=FormHelpers.Show(owner,title,g,null); if(!ok) return false;
            int discs; if(!Int32.TryParse(td.Text,out discs) || discs<1) { MessageBox.Show(owner,"Number of Discs must be at least 1 sein."); return false; }
            x.AlbumArtist=a.Text.Trim(); x.Album=b.Text.Trim(); x.ReleaseDate=d.Text.Trim(); x.Genre=ge.Text.Trim(); x.Label=l.Text.Trim(); x.Catalog=c.Text.Trim(); x.Country=co.Text.Trim(); x.TotalDiscs=discs; return true;
        }
    }

    internal static class DiscEditorDialog
    {
        public static bool Edit(Window owner, string title, CatalogDisc x)
        {
            Grid g=FormHelpers.CreateForm("Disc Number","Medium","CD-TEXT Available");
            TextBox n=FormHelpers.AddBox(g,0,x.DiscNumber.ToString()), m=FormHelpers.AddBox(g,1,x.Medium);
            CheckBox cdText=FormHelpers.AddCheckBox(g,2,x.CdTextPresent,"Indicates whether DJ Library has verified CD-TEXT. Unchecked only means it is currently unavailable.");
            bool ok=FormHelpers.Show(owner,title,g,null); if(!ok) return false;
            int number; if(!Int32.TryParse(n.Text,out number) || number<1) { MessageBox.Show(owner,"Disc Number must be at least 1 sein."); return false; }
            bool newCdText = cdText.IsChecked == true;
            x.DiscNumber=number;
            x.Medium=m.Text.Trim();
            if (newCdText != x.CdTextPresent)
            {
                x.CdTextPresent = newCdText;
                x.CdTextStatus = newCdText ? "manual_present" : "manual_unknown";
            }
            return true;
        }
    }

    internal static class TrackEditorDialog
    {
        public static bool Edit(Window owner, string title, CatalogTrack x)
        {
            Grid g=FormHelpers.CreateForm("Position","Artist","Title","Mix/Version","Date/Year","Genre","BPM","Duration");
            TextBox p=FormHelpers.AddBox(g,0,x.Position.ToString()), a=FormHelpers.AddBox(g,1,x.Artist), t=FormHelpers.AddBox(g,2,x.Title), v=FormHelpers.AddBox(g,3,x.Version);
            p.IsReadOnly = true;
            p.ToolTip = "Track order is changed with the arrow buttons in Catalog Manager, keeping position changes collision-safe.";
            TextBox d=FormHelpers.AddBox(g,4,x.ReleaseDate), ge=FormHelpers.AddBox(g,5,x.Genre), bpm=FormHelpers.AddBox(g,6,x.Bpm>0?x.Bpm.ToString("0.##"):""), du=FormHelpers.AddBox(g,7,x.DurationSeconds>0?UiHelpers.FormatDuration(x.DurationSeconds):"");
            du.ToolTip = "For example 7:35 or 1:02:03; seconds are also accepted. Display is formatted compactly automatically.";
            bool ok=FormHelpers.Show(owner,title,g,null); if(!ok) return false;
            int pos=x.Position; double bp=0, dur=0;
            if(bpm.Text.Trim().Length>0 && !Double.TryParse(bpm.Text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.CurrentCulture,out bp)) { MessageBox.Show(owner,"BPM ist invalid."); return false; }
            if(!UiHelpers.TryParseDuration(du.Text,out dur)) { MessageBox.Show(owner,"Duration is invalid. Examples: 7:35, 1:02:03, or seconds."); return false; }
            x.Position=pos; x.Artist=a.Text.Trim(); x.Title=t.Text.Trim(); x.Version=v.Text.Trim(); x.ReleaseDate=d.Text.Trim(); x.Genre=ge.Text.Trim(); x.Bpm=Math.Max(0,bp); x.DurationSeconds=Math.Max(0,dur); return true;
        }
    }

    internal static class DrivePickerDialog
    {
        public static WindowsCdDrive Choose(Window owner, List<WindowsCdDrive> drives)
        {
            Window w=new Window { Owner=owner, Title="Select CD Drive", Width=360, Height=180, WindowStartupLocation=WindowStartupLocation.CenterOwner, ShowInTaskbar=false };
            DockPanel root=new DockPanel(); w.Content=root;
            ComboBox combo=new ComboBox { Margin=new Thickness(12), Height=28, DisplayMemberPath="DisplayName", ItemsSource=drives, SelectedIndex=0 };
            DockPanel.SetDock(combo,Dock.Top); root.Children.Add(combo);
            Button ok=new Button { Content="OK", IsDefault=true, Width=80, Margin=new Thickness(12), HorizontalAlignment=HorizontalAlignment.Right };
            ok.Click += delegate { w.DialogResult=true; }; root.Children.Add(ok);
            return w.ShowDialog()==true ? combo.SelectedItem as WindowsCdDrive : null;
        }
    }
}
