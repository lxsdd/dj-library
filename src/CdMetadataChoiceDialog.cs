using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace DJLibrary
{
    public sealed class CdMetadataChoiceDialog : Window
    {
        private readonly CdMetadataSelectionSession _session;
        private readonly AppSettings _settings;
        private readonly DataGrid _releaseGrid;
        private readonly DataGrid _trackGrid;
        private readonly TextBlock _fieldLabel;
        private readonly TextBlock _fieldDetail;
        private readonly ComboBox _fieldChoices;
        private readonly Button _useFieldButton;
        private readonly Button _bulkSourceButton;
        private readonly TextBlock _bulkResult;
        private readonly ComboBox _baseSourceCombo;
        private readonly Button _useBaseButton;
        private readonly TextBlock _baseDetail;
        private bool _updatingChoice;
        private bool _refreshing;
        private string _activeFieldKey = "";
        private ReleaseView _releaseView;
        private List<TrackView> _trackViews = new List<TrackView>();

        private CdMetadataChoiceDialog(Window owner, CdMetadataSelectionSession session)
        {
            Owner = owner;
            _session = session;
            _settings = SettingsManager.Load();
            Title = "Review Metadata";
            GridRuntimeSupport.ApplyWindowIcon(this);
            Width = 1180;
            Height = 780;
            MinWidth = 900;
            MinHeight = 600;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowGeometrySettings.Attach(this, "cd.metadata.review");

            DockPanel root = new DockPanel { Margin = new Thickness(12) };
            Content = root;

            StackPanel sourceArea = new StackPanel { Margin = new Thickness(0,0,0,8) };
            DockPanel.SetDock(sourceArea,Dock.Top); root.Children.Add(sourceArea);
            TextBlock sourceHeading = new TextBlock { Text="Review metadata result",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,3) };
            sourceHeading.ToolTip = "Review the effective metadata result. Physical TOC, track order and durations are never replaced by metadata matching.";
            sourceArea.Children.Add(sourceHeading);
            sourceArea.Children.Add(new TextBlock
            {
                Text="Choose starting values, then review only fields that are missing or have useful alternatives. Selecting a candidate below only previews it; changes are applied explicitly.",
                TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,6),
                ToolTip="Recommended values choose the strongest unambiguous evidence per field. Existing exact Catalog values remain the safe default for an already cataloged disc."
            });

            WrapPanel returned = new WrapPanel { Margin = new Thickness(0,0,0,6) };
            returned.Children.Add(new TextBlock { Text="Sources: ", FontWeight=FontWeights.SemiBold, VerticalAlignment=VerticalAlignment.Center, ToolTip="Sources already fetched during Match Metadata. No provider request is performed from this review window." });
            foreach(CdMetadataSourceToggle source in _session.Sources)
            {
                if(String.Equals(source.Source,"Manual",StringComparison.OrdinalIgnoreCase)) continue;
                int evidenceCount=_session.SourceEvidenceCount(source.Source);
                string status=source.Status??"";
                bool unavailable=evidenceCount==0;
                string glyph=unavailable?"—":"✓";
                TextBlock text=new TextBlock { Text=glyph+" "+source.Source,VerticalAlignment=VerticalAlignment.Center,ToolTip=(status.Length>0?status:(evidenceCount>0?(evidenceCount.ToString()+" returned metadata value(s)"):"No usable values returned for this disc.")) };
                Border chip=new Border { Background=SystemColors.ControlLightBrush,BorderBrush=SystemColors.ControlDarkBrush,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(2),Padding=new Thickness(6,2,6,2),Margin=new Thickness(0,0,6,3),Child=text };
                returned.Children.Add(chip);
            }
            sourceArea.Children.Add(returned);

            DockPanel basePanel = new DockPanel { Margin = new Thickness(0,2,0,0) };
            TextBlock baseLabel = new TextBlock { Text="Starting values:",FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0),ToolTip="Choose either a safe single-source starting point or Recommended values. This establishes the initial release/track result before explicit field changes." };
            DockPanel.SetDock(baseLabel,Dock.Left); basePanel.Children.Add(baseLabel);
            _useBaseButton = new Button { Content="Use Starting Values",MinWidth=135,Margin=new Thickness(7,0,0,0),Padding=new Thickness(8,3,8,3),ToolTip="Rebuild release/track metadata from the selected starting strategy and reset existing field changes. Physical TOC, track order and durations are preserved." };
            DockPanel.SetDock(_useBaseButton,Dock.Right); basePanel.Children.Add(_useBaseButton);
            IList<CdMetadataBaseSetChoice> baseChoices = _session.BaseSetChoices;
            _baseSourceCombo = new ComboBox { MinWidth=430,Height=27,VerticalAlignment=VerticalAlignment.Center,VerticalContentAlignment=VerticalAlignment.Center,ItemsSource=baseChoices,DisplayMemberPath="DisplayText",SelectedItem=baseChoices.FirstOrDefault(x => x != null && String.Equals(x.Source,_session.BaseSource,StringComparison.OrdinalIgnoreCase)),ToolTip="Select starting values. Choosing an item does not change metadata until Use Starting Values is pressed." };
            basePanel.Children.Add(_baseSourceCombo);
            sourceArea.Children.Add(basePanel);
            _baseDetail = new TextBlock { Margin=new Thickness(0,3,0,0),TextWrapping=TextWrapping.Wrap,ToolTip="Summarizes the currently effective metadata result and remaining review items." };
            sourceArea.Children.Add(_baseDetail);
            _baseSourceCombo.SelectionChanged += delegate { UpdateBaseDetail(); };
            _useBaseButton.Click += delegate { ApplySelectedBaseSource(); };
            UpdateBaseDetail();

            StackPanel buttons=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,10,0,0) };
            DockPanel.SetDock(buttons,Dock.Bottom);
            Button reset=new Button { Content="Reset Field Changes",MinWidth=150,Margin=new Thickness(4),Padding=new Thickness(8,3,8,3),ToolTip="Remove explicit field/bulk changes and return release/tracks to the selected starting values." };
            Button ok=new Button { Content="Apply",IsDefault=true,MinWidth=100,Margin=new Thickness(4),Padding=new Thickness(8,3,8,3),ToolTip="Apply the reviewed metadata result to the Audio CD preview." };
            Button cancel=new Button { Content="Cancel",IsCancel=true,MinWidth=100,Margin=new Thickness(4),Padding=new Thickness(8,3,8,3),ToolTip="Close this review without applying additional changes from this dialog." };
            reset.Click+=delegate { _session.ResetOverrides(); _bulkResult.Text="Field changes reset · starting values remain " + _session.BaseSource; ApplyAndRefresh(); };
            ok.Click+=delegate { _session.ApplyTo(_session.Snapshot); DialogResult=true; };
            buttons.Children.Add(reset); buttons.Children.Add(ok); buttons.Children.Add(cancel); root.Children.Add(buttons);

            GroupBox chooser=new GroupBox { Header="Selected field · value & source",Margin=new Thickness(0,8,0,0),ToolTip="Click a metadata cell above to review its current result and returned alternatives. Selecting an alternative is a preview until explicitly applied." };
            DockPanel.SetDock(chooser,Dock.Bottom);
            Grid chooserGrid=new Grid { Margin=new Thickness(10,6,10,8) };
            chooserGrid.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(210) });
            chooserGrid.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
            chooserGrid.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
            _fieldLabel=new TextBlock { Text="Select a field",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,10,0) };
            Grid.SetColumn(_fieldLabel,0); chooserGrid.Children.Add(_fieldLabel);
            StackPanel choiceStack=new StackPanel();
            _fieldChoices=new ComboBox { MinWidth=500,Height=27,VerticalAlignment=VerticalAlignment.Center,VerticalContentAlignment=VerticalAlignment.Center,DisplayMemberPath="DisplayText",ToolTip="Compare the current result with returned alternatives. Merely selecting an item does not change metadata." };
            _fieldChoices.SelectionChanged+=FieldChoiceChanged;
            _fieldDetail=new TextBlock { Text="",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(2,3,0,0) };
            _bulkResult=new TextBlock { Text="",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(2,3,0,0) };
            choiceStack.Children.Add(_fieldChoices); choiceStack.Children.Add(_fieldDetail); choiceStack.Children.Add(_bulkResult);
            Grid.SetColumn(choiceStack,1); chooserGrid.Children.Add(choiceStack);
            StackPanel fieldActions=new StackPanel { Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8,0,0,0) };
            _useFieldButton=new Button { Content="Use Selected Value",MinWidth=130,Margin=new Thickness(0,0,6,0),Padding=new Thickness(8,3,8,3),IsEnabled=false,ToolTip="Apply the selected candidate to this field only. Selecting a candidate in the list is only a preview." };
            ToolTipService.SetShowOnDisabled(_useFieldButton,true);
            _useFieldButton.Click+=delegate { ApplySelectedFieldValue(); };
            _bulkSourceButton=new Button { Content="Use Source For…",MinWidth=120,Margin=new Thickness(0,0,6,0),Padding=new Thickness(8,3,8,3),IsEnabled=false,ToolTip="Apply the selected candidate's concrete source to an explicit wider scope. Missing or ambiguous values from that source are left unchanged." };
            ToolTipService.SetShowOnDisabled(_bulkSourceButton,true);
            _bulkSourceButton.Click+=delegate { ShowBulkSourceMenu(); };
            Button edit=new Button { Content="F2 / Edit…",Padding=new Thickness(8,3,8,3),ToolTip="Set a manual value for the active metadata field only (F2 or double-click)." };
            edit.Click+=delegate { EditCurrentField(); };
            fieldActions.Children.Add(_useFieldButton); fieldActions.Children.Add(_bulkSourceButton); fieldActions.Children.Add(edit);
            Grid.SetColumn(fieldActions,2); chooserGrid.Children.Add(fieldActions);
            chooser.Content=chooserGrid; root.Children.Add(chooser);

            Grid matrices=new Grid();
            matrices.RowDefinitions.Add(new RowDefinition { Height=new GridLength(0.38,GridUnitType.Star),MinHeight=125 });
            matrices.RowDefinitions.Add(new RowDefinition { Height=new GridLength(5) });
            matrices.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star),MinHeight=250 });
            root.Children.Add(matrices);

            _releaseGrid=CreateReleaseGrid();
            _trackGrid=CreateTrackGrid();
            Border releaseBox=Section("Release",_releaseGrid);
            Border trackBox=Section("Tracks",_trackGrid);
            Grid.SetRow(releaseBox,0); Grid.SetRow(trackBox,2); matrices.Children.Add(releaseBox); matrices.Children.Add(trackBox);
            GridSplitter splitter=new GridSplitter { Height=5,HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Center,ResizeDirection=GridResizeDirection.Rows,ResizeBehavior=GridResizeBehavior.PreviousAndNext,Background=SystemColors.ControlLightBrush };
            Grid.SetRow(splitter,1); matrices.Children.Add(splitter);

            _releaseGrid.CurrentCellChanged+=GridCurrentCellChanged;
            _trackGrid.CurrentCellChanged+=GridCurrentCellChanged;
            _releaseGrid.PreviewMouseLeftButtonDown+=GridCellMouseDown;
            _trackGrid.PreviewMouseLeftButtonDown+=GridCellMouseDown;
            _releaseGrid.SelectionChanged+=delegate { if(!_refreshing && _releaseGrid.SelectedItem!=null) _trackGrid.SelectedItem=null; };
            _trackGrid.SelectionChanged+=delegate { if(!_refreshing && _trackGrid.SelectedItem!=null) _releaseGrid.SelectedItem=null; };
            _releaseGrid.MouseDoubleClick+=delegate { EditCurrentField(); };
            _trackGrid.MouseDoubleClick+=delegate { EditCurrentField(); };
            PreviewKeyDown+=delegate(object sender,KeyEventArgs e) { if(e.Key==Key.F2) { EditCurrentField(); e.Handled=true; } };
            Closing+=delegate
            {
                AppSettings latest=SettingsManager.Load();
                GridLayoutSettings.Capture(latest,"cd.metadata.release",_releaseGrid);
                GridLayoutSettings.Capture(latest,"cd.metadata.tracks",_trackGrid);
                SettingsManager.Save(latest);
            };

            ApplyAndRefresh();
            GridLayoutSettings.Apply(_settings,"cd.metadata.release",_releaseGrid);
            GridLayoutSettings.Apply(_settings,"cd.metadata.tracks",_trackGrid);
        }

        private static Border Section(string title,UIElement child)
        {
            Border border=new Border { BorderBrush=SystemColors.ControlDarkBrush,BorderThickness=new Thickness(1),Margin=new Thickness(0,3,0,3) };
            DockPanel panel=new DockPanel();
            TextBlock heading=new TextBlock { Text=title,FontWeight=FontWeights.SemiBold,Margin=new Thickness(7,4,7,4) };
            DockPanel.SetDock(heading,Dock.Top); panel.Children.Add(heading); panel.Children.Add(child); border.Child=panel; return border;
        }

        private static DataGrid CreateReleaseGrid()
        {
            DataGrid grid=UiHelpers.CreateReadOnlyGrid();
            foreach(FieldDefinition field in FieldSchema.ReleaseFields)
            {
                if(field.Key=="TotalDiscs") continue;
                grid.Columns.Add(UiHelpers.BoundTextColumn(field.Header,"Effective "+field.Header+" value. Click the cell to compare source alternatives.",field.BindingPath,field.Key,new DataGridLength(field.Width),Math.Max(75,field.Width*0.65),field.BindingPath+"ToolTip"));
            }
            grid.Columns.Add(UiHelpers.BoundTextColumn("Review","Shows the concrete changed, missing and alternative field names. Hover for source/value details.","SourceSummary","SourceSummary",new DataGridLength(260),180,"ReviewToolTip"));
            GridGovernance.Apply(grid); return grid;
        }

        private static DataGrid CreateTrackGrid()
        {
            DataGrid grid=UiHelpers.CreateReadOnlyGrid();
            grid.Columns.Add(UiHelpers.BoundTextColumn("#","Physical track order from the Audio CD TOC. Metadata sources do not change this position.","Position","Position",new DataGridLength(45),45,"PositionToolTip"));
            grid.Columns.Add(UiHelpers.BoundTextColumn("Artist","Effective track artist. Hover to see its current source and alternatives.","Artist","Artist",new DataGridLength(215),140,"ArtistToolTip"));
            grid.Columns.Add(UiHelpers.BoundTextColumn("Title","Effective track title. Hover to see its current source and alternatives.","Title","Title",new DataGridLength(1,DataGridLengthUnitType.Star),220,"TitleToolTip"));
            grid.Columns.Add(UiHelpers.BoundTextColumn("Mix/Version","Effective structured mix/version. Hover to see its current source and alternatives.","Version","Version",new DataGridLength(210),150,"VersionToolTip"));
            grid.Columns.Add(UiHelpers.BoundTextColumn("Genre","Effective track genre. Hover to see its current source and alternatives.","Genre","Genre",new DataGridLength(145),110,"GenreToolTip"));
            grid.Columns.Add(UiHelpers.BoundTextColumn("Review","Shows the concrete changed, missing and alternative field names. Hover for source/value details.","SourceSummary","SourceSummary",new DataGridLength(260),180,"ReviewToolTip"));
            GridGovernance.Apply(grid); return grid;
        }

        private void ApplyAndRefresh()
        {
            string preserveKey = _activeFieldKey;
            _refreshing = true;
            try
            {
                CdSnapshot snapshot=_session.Snapshot;
                _releaseView=new ReleaseView
                {
                    AlbumArtist=_session.SelectedValue("release.albumartist"),Album=_session.SelectedValue("release.album"),ReleaseDate=_session.SelectedValue("release.date"),Genre=_session.SelectedValue("release.genre"),Label=_session.SelectedValue("release.label"),Catalog=_session.SelectedValue("release.catalog"),Country=_session.SelectedValue("release.country"),SourceSummary=ReleaseSourceSummary(),ReviewToolTip=ReleaseReviewToolTip(),AlbumArtistToolTip=_session.FieldToolTip("release.albumartist"),AlbumToolTip=_session.FieldToolTip("release.album"),ReleaseDateToolTip=_session.FieldToolTip("release.date"),GenreToolTip=_session.FieldToolTip("release.genre"),LabelToolTip=_session.FieldToolTip("release.label"),CatalogToolTip=_session.FieldToolTip("release.catalog"),CountryToolTip=_session.FieldToolTip("release.country")
                };
                _releaseGrid.ItemsSource=new List<ReleaseView> { _releaseView };
                _trackViews=snapshot.Tracks.OrderBy(x=>x.Position).Select(x=>new TrackView(x.Position,_session)).ToList();
                _trackGrid.ItemsSource=_trackViews;
                if (String.IsNullOrWhiteSpace(preserveKey)) preserveKey = "release.albumartist";
                RestoreCurrentField(preserveKey);
            }
            finally { _refreshing = false; }
            UpdateFieldChooser();
        }

        private string ReleaseSourceSummary()
        {
            string[] keys={"release.albumartist","release.album","release.date","release.genre","release.label","release.catalog","release.country"};
            return _session.ReviewFieldSummary(keys);
        }


        private string ReleaseReviewToolTip()
        {
            string[] keys={"release.albumartist","release.album","release.date","release.genre","release.label","release.catalog","release.country"};
            return _session.ReviewToolTip(keys);
        }

        private void UpdateFieldChooser()
        {
            string key=_activeFieldKey;
            CdMetadataChoiceRow row=_session.FindRow(key);
            _updatingChoice=true;
            try
            {
                if(row==null)
                {
                    _fieldLabel.Text="Select a field"; _fieldChoices.ItemsSource=null; _fieldDetail.Text="Select a release or track metadata cell to inspect its current result and alternatives."; _useFieldButton.IsEnabled=false; _bulkSourceButton.IsEnabled=false; _bulkSourceButton.Content="Use Source For…"; UpdateBaseDetail(); return;
                }
                _fieldLabel.Text=(row.SelectedOption!=null && row.SelectedOption.IsBaseMissing && !_session.IsOverride(row.Key)?"⚠ ":"")+row.FieldLabel;
                _fieldLabel.ToolTip="Active semantic metadata field. The warning symbol means its starting values do not provide one unambiguous value.";
                IList<CdMetadataChoiceOption> visible=_session.VisibleOptions(row);
                _fieldChoices.ItemsSource=visible;
                _fieldChoices.SelectedItem=visible.FirstOrDefault(x=>Object.ReferenceEquals(x,row.SelectedOption)) ?? visible.FirstOrDefault();
                UpdateCandidateControls(row,_fieldChoices.SelectedItem as CdMetadataChoiceOption);
                UpdateBaseDetail();
            }
            finally { _updatingChoice=false; }
        }

        private void UpdateCandidateControls(CdMetadataChoiceRow row,CdMetadataChoiceOption option)
        {
            if(row==null || option==null)
            {
                _fieldDetail.Text="Select a value/source candidate.";
                _useFieldButton.IsEnabled=false;
                _bulkSourceButton.IsEnabled=false;
                _bulkSourceButton.Content="Use Source For…";
                return;
            }
            _fieldDetail.Text=_session.CandidateExplanation(row,option);
            _fieldDetail.ToolTip=_session.FieldToolTip(row.Key);
            _useFieldButton.IsEnabled=!Object.ReferenceEquals(option,row.SelectedOption);
            string source=_session.ConcreteSourceForOption(option);
            _bulkSourceButton.IsEnabled=source.Length>0;
            _bulkSourceButton.Content=source.Length==0?"Use Source For…":"Use "+source+" For…";
        }

        private string CurrentKey()
        {
            return _activeFieldKey ?? "";
        }

        private void GridCurrentCellChanged(object sender, EventArgs e)
        {
            if (_refreshing) return;
            DataGrid grid = sender as DataGrid;
            string key = KeyFromCurrentCell(grid);
            if (String.IsNullOrWhiteSpace(key)) return;
            SetActiveField(key, grid);
        }

        private void GridCellMouseDown(object sender, MouseButtonEventArgs e)
        {
            DataGrid grid = sender as DataGrid;
            if (grid == null) return;
            DataGridCell cell = FindAncestorCell(e.OriginalSource as DependencyObject);
            if (cell == null || cell.Column == null || cell.DataContext == null) return;
            string key = KeyFor(grid, cell.DataContext, cell.Column);
            if (String.IsNullOrWhiteSpace(key)) return;
            grid.CurrentCell = new DataGridCellInfo(cell.DataContext, cell.Column);
            grid.SelectedItem = cell.DataContext;
            SetActiveField(key, grid);
        }

        private static DataGridCell FindAncestorCell(DependencyObject origin)
        {
            DependencyObject current = origin;
            while (current != null)
            {
                DataGridCell cell = current as DataGridCell;
                if (cell != null) return cell;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private string KeyFromCurrentCell(DataGrid grid)
        {
            if (grid == null || grid.CurrentColumn == null || grid.CurrentCell.Item == null || grid.CurrentCell.Item == CollectionView.NewItemPlaceholder) return "";
            return KeyFor(grid, grid.CurrentCell.Item, grid.CurrentColumn);
        }

        private string KeyFor(DataGrid grid, object item, DataGridColumn column)
        {
            if (grid == null || item == null || column == null) return "";
            if (Object.ReferenceEquals(grid, _trackGrid))
            {
                TrackView track = item as TrackView;
                string suffix = TrackSuffix(column.SortMemberPath);
                return track == null || suffix.Length == 0 ? "" : "track." + track.Position.ToString() + "." + suffix;
            }
            if (Object.ReferenceEquals(grid, _releaseGrid)) return ReleaseKey(column.SortMemberPath);
            return "";
        }

        private void SetActiveField(string key, DataGrid sourceGrid)
        {
            if (String.IsNullOrWhiteSpace(key) || _session.FindRow(key) == null) return;
            if (!String.Equals(_activeFieldKey, key, StringComparison.OrdinalIgnoreCase)) _bulkResult.Text = "";
            _activeFieldKey = key;
            if (Object.ReferenceEquals(sourceGrid, _releaseGrid)) _trackGrid.SelectedItem = null;
            else if (Object.ReferenceEquals(sourceGrid, _trackGrid)) _releaseGrid.SelectedItem = null;
            UpdateFieldChooser();
        }

        private static string ReleaseKey(string path)
        {
            if(path=="AlbumArtist") return "release.albumartist"; if(path=="Album") return "release.album"; if(path=="ReleaseDate") return "release.date";
            if(path=="Genre") return "release.genre"; if(path=="Label") return "release.label"; if(path=="Catalog") return "release.catalog"; if(path=="Country") return "release.country";
            return "";
        }

        private static string TrackSuffix(string path)
        {
            if(path=="Artist") return "artist"; if(path=="Title") return "title"; if(path=="Version") return "version"; if(path=="Genre") return "genre"; return "";
        }

        private void FieldChoiceChanged(object sender,SelectionChangedEventArgs e)
        {
            if(_updatingChoice) return;
            CdMetadataChoiceRow row=_session.FindRow(_activeFieldKey);
            CdMetadataChoiceOption option=_fieldChoices.SelectedItem as CdMetadataChoiceOption;
            _bulkResult.Text="";
            UpdateCandidateControls(row,option);
        }

        private void ApplySelectedFieldValue()
        {
            string key=_activeFieldKey;
            CdMetadataChoiceRow row=_session.FindRow(key);
            CdMetadataChoiceOption option=_fieldChoices.SelectedItem as CdMetadataChoiceOption;
            if(row==null || option==null || Object.ReferenceEquals(option,row.SelectedOption)) return;
            string label=option.IsNone?"empty":(String.Equals(option.Source,"Current",StringComparison.OrdinalIgnoreCase)?"existing value":option.Source);
            _session.SelectOption(key,option);
            ApplyAndRefresh();
            RestoreCurrentField(key);
            _bulkResult.Text="Field updated from " + label + ".";
        }

        private void UpdateBulkSourceButton(CdMetadataChoiceOption option)
        {
            string source = _session.ConcreteSourceForOption(option);
            _bulkSourceButton.IsEnabled = source.Length > 0;
            _bulkSourceButton.Content = source.Length == 0 ? "Use Source For…" : "Use " + source + " For…";
        }

        private string CurrentBulkSource()
        {
            CdMetadataChoiceRow row = _session.FindRow(_activeFieldKey);
            CdMetadataChoiceOption option = _fieldChoices.SelectedItem as CdMetadataChoiceOption;
            if (option == null && row != null) option = row.SelectedOption;
            return _session.ConcreteSourceForOption(option);
        }

        private static int TrackPositionFromKey(string key)
        {
            if (String.IsNullOrWhiteSpace(key) || !key.StartsWith("track.", StringComparison.OrdinalIgnoreCase)) return 0;
            string[] parts = key.Split('.');
            int position;
            return parts.Length == 3 && Int32.TryParse(parts[1], out position) ? position : 0;
        }

        private void ShowBulkSourceMenu()
        {
            string source = CurrentBulkSource();
            if (source.Length == 0) return;
            int position = TrackPositionFromKey(_activeFieldKey);
            ContextMenu menu = new ContextMenu();
            MenuItem release = new MenuItem { Header = "Entire Release", ToolTip="Apply this source to every release field for which it provides one unique value. Missing/ambiguous source values leave the current result unchanged." };
            release.Click += delegate { ApplyBulkSource("release", source); };
            MenuItem currentTrack = new MenuItem { Header = "Current Track", IsEnabled = position > 0, ToolTip="Apply this source to Artist, Title, Mix/Version and Genre on the active track where it provides one unique value." };
            ToolTipService.SetShowOnDisabled(currentTrack,true);
            currentTrack.Click += delegate { ApplyBulkSource("track", source); };
            MenuItem allTracks = new MenuItem { Header = "All Tracks", ToolTip="Apply this source to matching fields across every track where it provides one unique value. Release fields are not changed." };
            allTracks.Click += delegate { ApplyBulkSource("alltracks", source); };
            menu.Items.Add(release);
            menu.Items.Add(currentTrack);
            menu.Items.Add(allTracks);
            menu.PlacementTarget = _bulkSourceButton;
            menu.IsOpen = true;
        }

        private CdMetadataBulkApplyResult ApplyBulkSource(string scope, string source)
        {
            string preserveKey = _activeFieldKey;
            CdMetadataBulkApplyResult result;
            if (String.Equals(scope, "release", StringComparison.OrdinalIgnoreCase))
                result = _session.ApplySourceToRelease(source);
            else if (String.Equals(scope, "track", StringComparison.OrdinalIgnoreCase))
            {
                int position = TrackPositionFromKey(_activeFieldKey);
                if (position <= 0) return new CdMetadataBulkApplyResult { Source = source };
                result = _session.ApplySourceToTrack(position, source);
            }
            else if (String.Equals(scope, "alltracks", StringComparison.OrdinalIgnoreCase))
                result = _session.ApplySourceToAllTracks(source);
            else
                throw new ArgumentOutOfRangeException("scope");

            ApplyAndRefresh();
            RestoreCurrentField(preserveKey);
            _bulkResult.Text = source + " applied · " + result.Summary;
            return result;
        }

        private void UpdateBaseDetail()
        {
            if (_baseDetail == null || _baseSourceCombo == null) return;
            CdMetadataBaseSetChoice choice=_baseSourceCombo.SelectedItem as CdMetadataBaseSetChoice;
            string selected=choice==null || String.IsNullOrWhiteSpace(choice.Source)?_session.BaseSource:choice.Source;
            string current=String.Equals(_session.BaseSource,"Recommended",StringComparison.OrdinalIgnoreCase)?"Recommended values":_session.BaseSource+" starting values";
            _baseDetail.Text="Current result: " + current + " · " + _session.OverallReviewSummary();
            _useBaseButton.IsEnabled=!String.Equals(selected,_session.BaseSource,StringComparison.OrdinalIgnoreCase);
            ToolTipService.SetShowOnDisabled(_useBaseButton,true);
        }

        private void ApplySelectedBaseSource()
        {
            CdMetadataBaseSetChoice choice = _baseSourceCombo.SelectedItem as CdMetadataBaseSetChoice;
            string source = choice == null ? "" : choice.Source;
            if (String.IsNullOrWhiteSpace(source) || String.Equals(source, _session.BaseSource, StringComparison.OrdinalIgnoreCase))
            {
                UpdateBaseDetail();
                return;
            }
            if (_session.OverrideCount > 0)
            {
                MessageBoxResult confirmation = MessageBox.Show(this,
                    "Changing the starting values resets all " + _session.OverrideCount.ToString() + " explicit field change(s).\n\nRelease/track metadata will be rebuilt using “" + source + "”. Physical TOC, track order and durations remain unchanged.\n\nContinue?",
                    "Change Starting Values", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                if (confirmation != MessageBoxResult.Yes) return;
            }
            _session.SetBaseSource(source);
            _bulkResult.Text = "Starting values changed to " + source + " · previous field changes reset";
            ApplyAndRefresh();
            IList<CdMetadataBaseSetChoice> refreshedBaseChoices = _session.BaseSetChoices;
            _baseSourceCombo.ItemsSource = refreshedBaseChoices;
            _baseSourceCombo.SelectedItem = refreshedBaseChoices.FirstOrDefault(x => x != null && String.Equals(x.Source,_session.BaseSource,StringComparison.OrdinalIgnoreCase));
            UpdateBaseDetail();
        }

        private void EditCurrentField()
        {
            string key = _activeFieldKey;
            CdMetadataChoiceRow row = _session.FindRow(key);
            if (row == null) return;
            string current = row.SelectedOption == null ? row.CurrentValue : row.SelectedOption.Value;
            string edited;
            if (!UnifiedEditors.EditSingleMetadataField(this, row.FieldLabel, current, out edited)) return;
            _session.SetManualValue(key, edited);
            ApplyAndRefresh();
            RestoreCurrentField(key);
        }

        private void RestoreCurrentField(string key)
        {
            if (String.IsNullOrWhiteSpace(key) || _session.FindRow(key) == null) return;
            _activeFieldKey = key;
            if (key.StartsWith("release.", StringComparison.OrdinalIgnoreCase))
            {
                string suffix = key.Substring("release.".Length);
                string member = suffix=="albumartist"?"AlbumArtist":suffix=="album"?"Album":suffix=="date"?"ReleaseDate":suffix=="genre"?"Genre":suffix=="label"?"Label":suffix=="catalog"?"Catalog":suffix=="country"?"Country":"";
                DataGridColumn column = _releaseGrid.Columns.FirstOrDefault(x => String.Equals(x.SortMemberPath, member, StringComparison.OrdinalIgnoreCase));
                if (column != null && _releaseView != null)
                {
                    _releaseGrid.SelectedItem = _releaseView;
                    _trackGrid.SelectedItem = null;
                    _releaseGrid.CurrentCell = new DataGridCellInfo(_releaseView, column);
                }
            }
            else if (key.StartsWith("track.", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = key.Split('.');
                int pos;
                if (parts.Length == 3 && Int32.TryParse(parts[1], out pos))
                {
                    TrackView track = _trackViews.FirstOrDefault(x => x.Position == pos);
                    string member = parts[2]=="artist"?"Artist":parts[2]=="title"?"Title":parts[2]=="version"?"Version":parts[2]=="genre"?"Genre":"";
                    DataGridColumn column = _trackGrid.Columns.FirstOrDefault(x => String.Equals(x.SortMemberPath, member, StringComparison.OrdinalIgnoreCase));
                    if (track != null && column != null)
                    {
                        _trackGrid.SelectedItem = track;
                        _releaseGrid.SelectedItem = null;
                        _trackGrid.CurrentCell = new DataGridCellInfo(track, column);
                    }
                }
            }
        }

        public static bool Edit(Window owner,CdMetadataSelectionSession session)
        {
            if(session==null) throw new ArgumentNullException("session");
            CdMetadataReviewState original = session.CaptureReviewState();
            bool applied = new CdMetadataChoiceDialog(owner,session).ShowDialog()==true;
            if(!applied) session.RestoreReviewState(original);
            return applied;
        }

        internal static string ValidateMatrixContract()
        {
            DataGrid release=CreateReleaseGrid(), tracks=CreateTrackGrid();
            GridGovernance.Validate(release,"Metadata Release Matrix"); GridGovernance.Validate(tracks,"Metadata Track Matrix");
            string[] expected={"Position","Artist","Title","Version","Genre"};
            for(int i=0;i<expected.Length;i++) if(tracks.Columns[i].SortMemberPath!=expected[i]) throw new InvalidOperationException("Metadata Track Matrix violates # / Artist / Title / Mix-Version / Genre order.");
            DataGridColumn review=tracks.Columns.FirstOrDefault(x=>x.SortMemberPath=="SourceSummary");
            FrameworkElement reviewHeader=review==null?null:review.Header as FrameworkElement;
            if(reviewHeader==null || reviewHeader.ToolTip==null) throw new InvalidOperationException("Metadata Review field-named Review column has no tooltip.");

            CdSnapshot snapshot=new CdSnapshot { Album="Physical Album",AlbumArtist="Physical Artist" };
            snapshot.Tracks.Add(new CdTrackCapture { Position=1,Artist="Artist A",Title="Track A",DurationSeconds=180 });
            CdMetadataReport report=new CdMetadataReport();
            report.Add("Album Artist","Various Artists","Catalog",1.00,"exact toc","");
            report.Add("Album","Catalog Album","Catalog",1.00,"exact toc","");
            report.Add("Track 1 Artist","Artist A","Catalog",1.00,"exact toc","");
            report.Add("Track 1 Title","Track A","Catalog",1.00,"exact toc","");
            report.Add("Track 1 Mix/Version","Extended Mix","Discogs",0.95,"release match","");
            report.Add("Album Artist","MB Artist","MusicBrainz",0.98,"disc id","");
            report.Add("Album","MusicBrainz Album","MusicBrainz",0.98,"disc id","");
            CdMetadataSelectionSession session=new CdMetadataSelectionSession(snapshot,report);
            if(session.BaseSource!="Catalog") throw new InvalidOperationException("Existing Catalog evidence must remain the safe initial starting values.");
            if(!session.BaseSources.Contains("Recommended")) throw new InvalidOperationException("Recommended starting values are not offered.");
            CdMetadataChoiceDialog dialog=new CdMetadataChoiceDialog(null,session);
            dialog.RestoreCurrentField("release.album"); dialog.UpdateFieldChooser();
            if(dialog._fieldChoices.Items.Cast<object>().OfType<CdMetadataChoiceOption>().Any(x=>!x.IsBaseValue && String.Equals(x.Source,"Current",StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Current pseudo-source leaked into normal field alternatives.");
            CdMetadataChoiceOption mb=session.VisibleOptions(session.FindRow("release.album")).FirstOrDefault(x=>String.Equals(x.Source,"MusicBrainz",StringComparison.OrdinalIgnoreCase));
            if(mb==null) throw new InvalidOperationException("MusicBrainz field alternative missing.");
            dialog._fieldChoices.SelectedItem=mb;
            dialog.FieldChoiceChanged(null,null);
            if(session.IsOverride("release.album")) throw new InvalidOperationException("Selecting a candidate must be preview-only.");
            if(!dialog._useFieldButton.IsEnabled || dialog._fieldDetail.Text.IndexOf("Preview:",StringComparison.OrdinalIgnoreCase)<0) throw new InvalidOperationException("Explicit candidate preview/apply UX is missing.");
            dialog.ApplySelectedFieldValue();
            if(!session.IsOverride("release.album") || session.SelectedSource("release.album")!="MusicBrainz") throw new InvalidOperationException("Use Selected Value did not apply the field change.");
            session.SetBaseSource("Recommended");
            if(session.SelectedValue("track.1.version")!="Extended Mix") throw new InvalidOperationException("Recommended starting values did not fill a missing field from strong returned evidence.");
            if(session.SelectedValue("release.album")!="Catalog Album") throw new InvalidOperationException("Recommended values did not keep stronger Catalog evidence.");
            dialog.ApplyAndRefresh();
            if(dialog.ReleaseSourceSummary().IndexOf("Base:",StringComparison.OrdinalIgnoreCase)>=0 || dialog.ReleaseSourceSummary().Length==0) throw new InvalidOperationException("Review column still exposes internal Base/Overrides prose.");
            TrackView trackView=new TrackView(1,session);
            if(trackView.SourceSummary.IndexOf("Missing: Genre",StringComparison.OrdinalIgnoreCase)<0)
                throw new InvalidOperationException("Track Review does not name the concrete missing Genre field.");
            if(trackView.SourceSummary.IndexOf("2 missing",StringComparison.OrdinalIgnoreCase)>=0)
                throw new InvalidOperationException("Track Review still exposes opaque count-only missing status.");
            if(dialog._fieldChoices.ToolTip==null || dialog._useFieldButton.ToolTip==null || dialog._bulkSourceButton.ToolTip==null || dialog._useBaseButton.ToolTip==null) throw new InvalidOperationException("Metadata Review discoverability tooltips are incomplete.");
            return "metadata review UX v3: safe starting values + Recommended composition + preview-before-apply + field-named Review state + full field tooltips";
        }

        private sealed class ReleaseView
        {
            public string AlbumArtist { get; set; }
            public string Album { get; set; }
            public string ReleaseDate { get; set; }
            public string Genre { get; set; }
            public string Label { get; set; }
            public string Catalog { get; set; }
            public string Country { get; set; }
            public string SourceSummary { get; set; }
            public string ReviewToolTip { get; set; }
            public string AlbumArtistToolTip { get; set; }
            public string AlbumToolTip { get; set; }
            public string ReleaseDateToolTip { get; set; }
            public string GenreToolTip { get; set; }
            public string LabelToolTip { get; set; }
            public string CatalogToolTip { get; set; }
            public string CountryToolTip { get; set; }
        }

        private sealed class TrackView
        {
            public int Position { get; private set; }
            public string Artist { get; private set; }
            public string Title { get; private set; }
            public string Version { get; private set; }
            public string Genre { get; private set; }
            public string SourceSummary { get; private set; }
            public string ReviewToolTip { get; private set; }
            public string PositionToolTip { get { return "Physical track position " + Position.ToString() + ". Metadata matching does not change track order."; } }
            public string ArtistToolTip { get; private set; }
            public string TitleToolTip { get; private set; }
            public string VersionToolTip { get; private set; }
            public string GenreToolTip { get; private set; }
            public TrackView(int position,CdMetadataSelectionSession session)
            {
                Position=position;
                string p="track."+Position.ToString()+".";
                Artist=session.SelectedValue(p+"artist"); Title=session.SelectedValue(p+"title"); Version=session.SelectedValue(p+"version"); Genre=session.SelectedValue(p+"genre");
                string[] keys={p+"artist",p+"title",p+"version",p+"genre"};
                SourceSummary=session.ReviewFieldSummary(keys);
                ReviewToolTip=session.ReviewToolTip(keys);
                ArtistToolTip=session.FieldToolTip(p+"artist"); TitleToolTip=session.FieldToolTip(p+"title"); VersionToolTip=session.FieldToolTip(p+"version"); GenreToolTip=session.FieldToolTip(p+"genre");
            }
        }

    }
}
