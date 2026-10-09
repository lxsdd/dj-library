using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace DJLibrary
{
    internal static class UnifiedEditors
    {
        public static bool EditRelease(Window owner, string title, CatalogRelease x)
        {
            Grid g = CreateForm("Album Artist", "Album", "Date/Year", "Genre/Style", "Label", "Catalog Number", "Country", "Number of Discs");
            TextBox artist = AddBox(g,0,x.AlbumArtist), album = AddBox(g,1,x.Album), date = AddBox(g,2,x.ReleaseDate), genre = AddBox(g,3,x.Genre);
            TextBox label = AddBox(g,4,x.Label), catalog = AddBox(g,5,x.Catalog), country = AddBox(g,6,x.Country), discs = AddBox(g,7,x.TotalDiscs.ToString(CultureInfo.CurrentCulture));
            if (!Show(owner,title,g)) return false;
            int total; if (!Int32.TryParse(discs.Text, out total) || total < 1) { MessageBox.Show(owner,"Number of Discs must be at least 1 sein."); return false; }
            x.AlbumArtist=artist.Text.Trim(); x.Album=album.Text.Trim(); x.ReleaseDate=date.Text.Trim(); x.Genre=genre.Text.Trim();
            x.Label=label.Text.Trim(); x.Catalog=catalog.Text.Trim(); x.Country=country.Text.Trim(); x.TotalDiscs=total;
            return true;
        }

        public static bool EditMetadataRelease(Window owner, CatalogRelease x)
        {
            Grid g = CreateForm("Album Artist", "Album", "Date/Year", "Genre/Style", "Label", "Catalog Number", "Country");
            TextBox artist = AddBox(g,0,x.AlbumArtist), album = AddBox(g,1,x.Album), date = AddBox(g,2,x.ReleaseDate), genre = AddBox(g,3,x.Genre);
            TextBox label = AddBox(g,4,x.Label), catalog = AddBox(g,5,x.Catalog), country = AddBox(g,6,x.Country);
            if(!Show(owner,"Edit Release Metadata",g)) return false;
            x.AlbumArtist=artist.Text.Trim(); x.Album=album.Text.Trim(); x.ReleaseDate=date.Text.Trim(); x.Genre=genre.Text.Trim(); x.Label=label.Text.Trim(); x.Catalog=catalog.Text.Trim(); x.Country=country.Text.Trim();
            return true;
        }

        public static bool EditDisc(Window owner, string title, CatalogDisc x)
        {
            Grid g=CreateForm("Disc Number","Medium","CD-TEXT Available");
            TextBox number=AddBox(g,0,x.DiscNumber.ToString(CultureInfo.CurrentCulture)), medium=AddBox(g,1,x.Medium);
            CheckBox cdText=AddCheck(g,2,x.CdTextPresent,"Indicates whether DJ Library has verified CD-TEXT. Unchecked only means it is currently unavailable.");
            if(!Show(owner,title,g)) return false;
            int n; if(!Int32.TryParse(number.Text,out n) || n<1) { MessageBox.Show(owner,"Disc Number must be at least 1 sein."); return false; }
            bool present=cdText.IsChecked==true; x.DiscNumber=n; x.Medium=medium.Text.Trim();
            if(present!=x.CdTextPresent) { x.CdTextPresent=present; x.CdTextStatus=present?"manual_present":"manual_unknown"; }
            return true;
        }

        public static bool EditTrack(Window owner, string title, CatalogTrack x)
        {
            Grid g=CreateForm("Position","Artist","Title","Mix/Version","Date/Year","Genre","BPM","Duration");
            TextBox position=AddBox(g,0,x.Position.ToString(CultureInfo.CurrentCulture)), artist=AddBox(g,1,x.Artist), name=AddBox(g,2,x.Title), version=AddBox(g,3,x.Version);
            position.IsReadOnly=true; position.ToolTip="Track order is changed safely in Catalog Manager.";
            TextBox date=AddBox(g,4,x.ReleaseDate), genre=AddBox(g,5,x.Genre), bpm=AddBox(g,6,x.Bpm>0?x.Bpm.ToString("0.##",CultureInfo.CurrentCulture):""), duration=AddBox(g,7,x.DurationSeconds>0?UiHelpers.FormatDuration(x.DurationSeconds):"");
            duration.ToolTip="For example 7:35 or 1:02:03; seconds are also accepted.";
            if(!Show(owner,title,g)) return false;
            double bp=0,dur=0;
            if(bpm.Text.Trim().Length>0 && !Double.TryParse(bpm.Text,NumberStyles.Float,CultureInfo.CurrentCulture,out bp)) { MessageBox.Show(owner,"BPM is invalid."); return false; }
            if(!UiHelpers.TryParseDuration(duration.Text,out dur)) { MessageBox.Show(owner,"Duration is invalid. Examples: 7:35, 1:02:03, or seconds."); return false; }
            x.Artist=artist.Text.Trim(); x.Title=name.Text.Trim(); x.Version=version.Text.Trim(); x.ReleaseDate=date.Text.Trim(); x.Genre=genre.Text.Trim(); x.Bpm=Math.Max(0,bp); x.DurationSeconds=Math.Max(0,dur);
            return true;
        }

        public static bool EditMetadataTrack(Window owner, CdTrackCapture x)
        {
            Grid g=CreateForm("Artist","Title","Mix/Version","Genre","Duration (TOC)");
            TextBox artist=AddBox(g,0,x.Artist), title=AddBox(g,1,x.Title), version=AddBox(g,2,x.Version), genre=AddBox(g,3,x.Genre), duration=AddBox(g,4,UiHelpers.FormatDuration(x.DurationSeconds));
            duration.IsReadOnly=true; duration.ToolTip="Duration comes from the physical TOC and cannot be overwritten manually during Metadata Review.";
            if(!Show(owner,"Edit Track Metadata",g)) return false;
            x.Artist=artist.Text.Trim(); x.Title=title.Text.Trim(); x.Version=version.Text.Trim(); x.Genre=genre.Text.Trim();
            return true;
        }


        public static bool EditSingleMetadataField(Window owner, string label, string current, out string value)
        {
            Grid g = CreateForm(label);
            TextBox box = AddBox(g,0,current ?? "");
            if (!Show(owner,"Edit Metadata Field",g)) { value = current ?? ""; return false; }
            value = box.Text.Trim();
            return true;
        }

        private static Grid CreateForm(params string[] labels)
        {
            Grid g=new Grid { Margin=new Thickness(12) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(135) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
            for(int i=0;i<labels.Length;i++)
            {
                g.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
                TextBlock l=new TextBlock { Text=labels[i],Margin=new Thickness(0,6,8,6),VerticalAlignment=VerticalAlignment.Center };
                Grid.SetRow(l,i); Grid.SetColumn(l,0); g.Children.Add(l);
            }
            return g;
        }

        private static TextBox AddBox(Grid g,int row,string text)
        {
            TextBox b=new TextBox { Text=text??"",Margin=new Thickness(0,4,0,4),MinWidth=260,Height=26,VerticalContentAlignment=VerticalAlignment.Center };
            Grid.SetRow(b,row); Grid.SetColumn(b,1); g.Children.Add(b); return b;
        }

        private static CheckBox AddCheck(Grid g,int row,bool value,string tip)
        {
            CheckBox b=new CheckBox { IsChecked=value,Margin=new Thickness(0,6,0,6),VerticalAlignment=VerticalAlignment.Center,ToolTip=tip };
            Grid.SetRow(b,row); Grid.SetColumn(b,1); g.Children.Add(b); return b;
        }

        private static bool Show(Window owner,string title,Grid form)
        {
            Window w=new Window { Owner=owner,Title=title,Width=520,SizeToContent=SizeToContent.Height,WindowStartupLocation=WindowStartupLocation.CenterOwner,ShowInTaskbar=false };
            GridRuntimeSupport.ApplyWindowIcon(w);
            DockPanel root=new DockPanel(); w.Content=root;
            StackPanel buttons=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(8) };
            Button ok=new Button { Content="OK",IsDefault=true,MinWidth=85,Margin=new Thickness(4),Padding=new Thickness(10,3,10,3) };
            Button cancel=new Button { Content="Cancel",IsCancel=true,MinWidth=85,Margin=new Thickness(4),Padding=new Thickness(10,3,10,3) };
            buttons.Children.Add(ok); buttons.Children.Add(cancel); DockPanel.SetDock(buttons,Dock.Bottom); root.Children.Add(buttons); root.Children.Add(form);
            bool accepted=false; ok.Click+=delegate { accepted=true; w.DialogResult=true; }; w.ShowDialog(); return accepted;
        }

        internal static string ValidateContract()
        {
            return "shared release/disc/track editor controls; metadata review reuses the same controls with physically-derived fields locked/omitted where required";
        }
    }
}
