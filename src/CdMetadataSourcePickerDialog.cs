using System;
using System.Windows;
using System.Windows.Controls;

namespace DJLibrary
{
    public sealed class CdMetadataSourcePickerResult
    {
        public CdMetadataFetchOptions Options { get; set; }
        public bool RememberAsDefault { get; set; }
    }

    internal sealed class CdMetadataSourcePickerDialog : Window
    {
        private readonly CheckBox _catalog;
        private readonly CheckBox _foobar;
        private readonly CheckBox _musicBrainz;
        private readonly CheckBox _discogs;
        private readonly CheckBox _titleAnalysis;
        private readonly CheckBox _remember;
        private readonly TextBox _albumArtist;
        private readonly TextBox _album;
        private readonly TextBox _year;
        private readonly TextBox _label;
        private readonly TextBox _catalogNumber;
        private readonly TextBox _country;

        public CdMetadataSourcePickerResult Result { get; private set; }

        private CdMetadataSourcePickerDialog(Window owner, CdMetadataFetchOptions defaults)
        {
            Owner = owner;
            Title = "Match Metadata";
            GridRuntimeSupport.ApplyWindowIcon(this);
            Width = 650;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowGeometrySettings.Attach(this, "cd.metadata.match");
            ShowInTaskbar = false;

            CdMetadataFetchOptions value = defaults == null ? new CdMetadataFetchOptions() : defaults.Clone();
            DockPanel root = new DockPanel { Margin = new Thickness(14) };
            Content = root;

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            DockPanel.SetDock(buttons, Dock.Bottom);
            Button start = new Button { Content = "Start Matching", IsDefault = true, MinWidth = 120, Margin = new Thickness(4), Padding = new Thickness(9, 3, 9, 3), ToolTip="Contact/use only the checked additional sources. Physical TOC/CD-TEXT have already been read and are not re-fetched here." };
            Button cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90, Margin = new Thickness(4), Padding = new Thickness(9, 3, 9, 3), ToolTip="Close without starting an additional metadata lookup." };
            buttons.Children.Add(start); buttons.Children.Add(cancel); root.Children.Add(buttons);

            StackPanel body = new StackPanel();
            root.Children.Add(body);
            body.Children.Add(new TextBlock
            {
                Text = "The disc has already been read locally from TOC and, when available, CD-TEXT. Select only the additional sources that may be used or contacted for this matching run.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });

            body.Children.Add(Heading("Local sources"));
            _catalog = Choice("Catalog (exact full TOC)", value.UseCatalog, "Compare with the writable DJ Library catalog. No network access.");
            _foobar = Choice("foobar Bridge", value.UseFoobar, "Compare with the active local foobar Bridge snapshot. No network access.");
            _titleAnalysis = Choice("Title Analysis", value.UseTitleAnalysis, "Conservative local recognition of trailing mix/version parentheses. No network access.");
            body.Children.Add(_catalog); body.Children.Add(_foobar); body.Children.Add(_titleAnalysis);

            body.Children.Add(Heading("Online sources"));
            _musicBrainz = Choice("MusicBrainz", value.UseMusicBrainz, "Disc ID / TOC lookup via musicbrainz.org. Disabled means no MusicBrainz request.");
            _discogs = Choice("Discogs", value.UseDiscogs, "Independent Discogs release search. Disabled means no Discogs request.");
            body.Children.Add(_musicBrainz); body.Children.Add(_discogs);

            body.Children.Add(Heading("Independent Discogs search anchors (editable)"));
            body.Children.Add(new TextBlock
            {
                Text = "These values are used only as search anchors. Artist + Album are enough to start a Discogs search; the physical TOC, track count and durations are still used to score the returned releases. Add Year, Label or Catalog Number to narrow ambiguous pressings.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0,0,0,6)
            });
            Grid anchors = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            anchors.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            anchors.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            string[] labels = { "Album Artist", "Album", "Year", "Label", "Catalog Number", "Country" };
            string[] values = { value.SearchAlbumArtist, value.SearchAlbum, value.SearchYear, value.SearchLabel, value.SearchCatalog, value.SearchCountry };
            TextBox[] boxes = new TextBox[labels.Length];
            for (int i=0;i<labels.Length;i++)
            {
                anchors.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                TextBlock l = new TextBlock { Text = labels[i], Margin = new Thickness(0,4,8,4), VerticalAlignment = VerticalAlignment.Center, ToolTip="Editable Discogs search anchor. It helps find/rank a release but is not written to the catalog merely because it is entered here." };
                TextBox b = new TextBox { Text = values[i] ?? "", Height = 26, Margin = new Thickness(0,2,0,2), VerticalContentAlignment = VerticalAlignment.Center, ToolTip="Search anchor only. Artist + Album are sufficient to start independent Discogs search; TOC/track count/durations score returned releases." };
                Grid.SetRow(l,i); Grid.SetColumn(l,0); anchors.Children.Add(l);
                Grid.SetRow(b,i); Grid.SetColumn(b,1); anchors.Children.Add(b); boxes[i]=b;
            }
            _albumArtist=boxes[0]; _album=boxes[1]; _year=boxes[2]; _label=boxes[3]; _catalogNumber=boxes[4]; _country=boxes[5];
            body.Children.Add(anchors);

            _remember = new CheckBox { Content = "Remember source selection as default for future Audio CD matching", Margin = new Thickness(0, 10, 0, 0), ToolTip="Remember only which additional sources are enabled by default. Editable search-anchor values are not stored as global defaults." };
            body.Children.Add(_remember);

            start.Click += delegate
            {
                Result = new CdMetadataSourcePickerResult
                {
                    Options = new CdMetadataFetchOptions
                    {
                        UseCatalog = _catalog.IsChecked == true,
                        UseFoobar = _foobar.IsChecked == true,
                        UseMusicBrainz = _musicBrainz.IsChecked == true,
                        UseDiscogs = _discogs.IsChecked == true,
                        UseTitleAnalysis = _titleAnalysis.IsChecked == true,
                        SearchAlbumArtist = _albumArtist.Text.Trim(),
                        SearchAlbum = _album.Text.Trim(),
                        SearchYear = _year.Text.Trim(),
                        SearchLabel = _label.Text.Trim(),
                        SearchCatalog = _catalogNumber.Text.Trim(),
                        SearchCountry = _country.Text.Trim()
                    },
                    RememberAsDefault = _remember.IsChecked == true
                };
                DialogResult = true;
            };
        }

        private static TextBlock Heading(string text)
        {
            return new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) };
        }

        private static CheckBox Choice(string text, bool value, string tip)
        {
            return new CheckBox { Content = text, IsChecked = value, ToolTip = tip, Margin = new Thickness(0, 3, 0, 3) };
        }

        public static bool Choose(Window owner, CdMetadataFetchOptions defaults, out CdMetadataSourcePickerResult result)
        {
            CdMetadataSourcePickerDialog dialog = new CdMetadataSourcePickerDialog(owner, defaults);
            bool ok = dialog.ShowDialog() == true;
            result = ok ? dialog.Result : null;
            return ok;
        }
    }
}
