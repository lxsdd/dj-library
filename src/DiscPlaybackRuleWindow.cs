using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace DJLibrary
{
    // User profile only. No database or tag mutations.
    public sealed class DiscPlaybackRuleWindow : Window
    {
        private readonly CheckBox _enabled, _inclusive, _markCds, _markTracks;
        private readonly TextBox _label, _threshold;
        private readonly ComboBox _color;
        private readonly Action _changed;

        public DiscPlaybackRuleWindow(Window owner, Action changed)
        {
            Owner = owner;
            _changed = changed;
            Title = "Disc Playback Compatibility";
            GridRuntimeSupport.ApplyWindowIcon(this);
            Width = 570;
            Height = 390;
            MinWidth = 490;
            MinHeight = 370;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            WindowGeometrySettings.Attach(this, "playback.rule");

            DockPanel root = new DockPanel { Margin = new Thickness(14) };
            Content = root;
            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,12,0,0) };
            DockPanel.SetDock(buttons, Dock.Bottom);
            Button save = Button("Apply", delegate { Save(); });
            Button cancel = Button("Cancel", delegate { Close(); });
            buttons.Children.Add(save);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);

            Grid form = new Grid();
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(205) });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1,GridUnitType.Star) });
            for (int i=0;i<8;i++) form.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
            root.Children.Add(form);

            DiscPlaybackRuleSettings settings = DiscPlaybackRule.Current.Clone();
            _enabled = AddCheck(form, 0, "Enable disc playback rule", settings.Enabled);
            AddCaption(form, 1, "Player / rule name");
            _label = AddText(form, 1, settings.DeviceLabel);
            AddCaption(form, 2, "Physical disc limit (MM:SS:FF)");
            _threshold = AddText(form, 2, CdxCompatibility.FormatMsf(settings.LimitFrames));
            _threshold.ToolTip = "75 frames per second. 79:59:74 is the legacy Numark CDX cutoff.";
            _inclusive = AddCheck(form, 3, "Warn at the limit too (≥)", settings.Inclusive);
            _markCds = AddCheck(form, 4, "Highlight incompatible CD rows", settings.MarkCds);
            _markTracks = AddCheck(form, 5, "Highlight tracks of those CDs", settings.MarkTracks);
            AddCaption(form, 6, "Warning color");
            _color = new ComboBox { Width=165, Height=27, VerticalAlignment=VerticalAlignment.Center,
                HorizontalAlignment=HorizontalAlignment.Left, VerticalContentAlignment=VerticalAlignment.Center };
            _color.Items.Add("Red");
            _color.Items.Add("Orange");
            _color.SelectedItem=settings.WarningColor=="Orange"?"Orange":"Red";
            Grid.SetRow(_color,6); Grid.SetColumn(_color,1); form.Children.Add(_color);
            TextBlock note = new TextBlock { Text="Unknown stays unknown without a complete, valid physical TOC. The rule never edits a disc, track, or audio tag.",
                TextWrapping=TextWrapping.Wrap, VerticalAlignment=VerticalAlignment.Center };
            Grid.SetRow(note,7); Grid.SetColumnSpan(note,2); form.Children.Add(note);

            HorizontalScrollSupport.Enable(this);
        }

        private static Button Button(string text, RoutedEventHandler onClick)
        {
            Button button=new Button { Content=text, Padding=new Thickness(14,5,14,5),
                MinWidth=85, Margin=new Thickness(4,0,0,0), VerticalContentAlignment=VerticalAlignment.Center };
            button.Click+=onClick;
            return button;
        }

        private static void AddCaption(Grid parent,int row,string caption)
        {
            TextBlock label=new TextBlock { Text=caption, VerticalAlignment=VerticalAlignment.Center,
                TextAlignment=TextAlignment.Left, Margin=new Thickness(0,0,8,0) };
            Grid.SetRow(label,row); Grid.SetColumn(label,0); parent.Children.Add(label);
        }

        private static TextBox AddText(Grid parent,int row,string value)
        {
            TextBox text=new TextBox { Text=value??"", Height=27,
                VerticalContentAlignment=VerticalAlignment.Center, VerticalAlignment=VerticalAlignment.Center };
            Grid.SetRow(text,row); Grid.SetColumn(text,1); parent.Children.Add(text);
            return text;
        }

        private static CheckBox AddCheck(Grid parent,int row,string caption,bool value)
        {
            CheckBox checkbox=UiHelpers.AlignedCheckBox(caption);
            checkbox.IsChecked=value;
            Grid.SetRow(checkbox,row); Grid.SetColumn(checkbox,0); Grid.SetColumnSpan(checkbox,2);
            parent.Children.Add(checkbox);
            return checkbox;
        }

        private void Save()
        {
            int frames;
            if(!DiscPlaybackRule.TryParseMsf(_threshold.Text,out frames))
            {
                MessageBox.Show(this,"Enter a valid MM:SS:FF duration (for example 79:59:74, with FF from 00 to 74).",
                    "Invalid physical-disc limit",MessageBoxButton.OK,MessageBoxImage.Warning);
                _threshold.Focus(); _threshold.SelectAll(); return;
            }
            DiscPlaybackRuleSettings next = new DiscPlaybackRuleSettings {
                Enabled=_enabled.IsChecked==true,
                Inclusive=_inclusive.IsChecked==true,
                MarkCds=_markCds.IsChecked==true,
                MarkTracks=_markTracks.IsChecked==true,
                DeviceLabel=String.IsNullOrWhiteSpace(_label.Text)?"Disc player":_label.Text.Trim(),
                LimitFrames=frames,
                WarningColor=Convert.ToString(_color.SelectedItem,CultureInfo.InvariantCulture)=="Orange"?"Orange":"Red"
            };
            SettingsManager.Update(delegate(AppSettings settings) { settings.DiscPlayback=next.Clone(); });
            DiscPlaybackRule.Configure(next);
            if(_changed!=null) _changed();
            Close();
        }

        internal static string ValidateLayoutContract()
        {
            CheckBox cb=UiHelpers.AlignedCheckBox("Highlight tracks of incompatible CDs");
            if (!UiHelpers.IsAlignedCheckBox(cb))
                throw new InvalidOperationException("Playback-settings checkbox text has no common vertical baseline.");
            return "playback checkbox labels centered with glyph";
        }
    }
}
