using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace DJLibrary
{
    public sealed class ColumnsWindow : Window
    {
        private readonly DataGrid _grid;
        private readonly ListBox _list;

        public ColumnsWindow(Window owner, DataGrid grid, string title)
        {
            Owner = owner;
            _grid = grid;
            Title = title;
            Width = 500;
            Height = 560;
            MinWidth = 430;
            MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            WindowGeometrySettings.Attach(this, "columns.selector");

            DockPanel root = new DockPanel();
            Content = root;

            TextBlock info = new TextBlock();
            info.Text = "Show/hide columns and set their order. Move them with Up/Down, Alt+↑/↓, or drag and drop directly in the column header.";
            info.TextWrapping = TextWrapping.Wrap;
            info.Margin = new Thickness(10);
            info.ToolTip = "Column widths and order are saved automatically when the app exits.";
            DockPanel.SetDock(info, Dock.Top);
            root.Children.Add(info);

            StackPanel buttons = new StackPanel();
            buttons.Orientation = Orientation.Horizontal;
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            buttons.Margin = new Thickness(8);
            DockPanel.SetDock(buttons, Dock.Bottom);

            Button up = Button("↑ Nach oben", "Move the selected column one position left.", delegate { Move(-1); });
            Button down = Button("↓ Nach unten", "Move the selected column one position right.", delegate { Move(1); });
            Button apply = Button("Apply", "Apply visibility and order.", delegate { Apply(); });
            Button close = Button("Close", "Close column selection.", delegate { Close(); });

            buttons.Children.Add(up);
            buttons.Children.Add(down);
            buttons.Children.Add(apply);
            buttons.Children.Add(close);
            root.Children.Add(buttons);

            _list = new ListBox();
            _list.Margin = new Thickness(10, 0, 10, 0);
            ScrollViewer.SetVerticalScrollBarVisibility(_list, ScrollBarVisibility.Auto);
            ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Auto);
            root.Children.Add(_list);

            Populate();
            PreviewKeyDown += delegate(object sender, System.Windows.Input.KeyEventArgs e)
            {
                if (e.Key == System.Windows.Input.Key.Escape) { Close(); e.Handled = true; }
                else if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Alt) != 0 && EffectiveKey(e) == System.Windows.Input.Key.Up) { Move(-1); e.Handled = true; }
                else if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Alt) != 0 && EffectiveKey(e) == System.Windows.Input.Key.Down) { Move(1); e.Handled = true; }
            };
            HorizontalScrollSupport.Enable(this);
        }


        private static System.Windows.Input.Key EffectiveKey(System.Windows.Input.KeyEventArgs e)
        {
            return e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        }

        private static Button Button(string text, string toolTip, RoutedEventHandler handler)
        {
            Button b = new Button();
            b.Content = text;
            b.Margin = new Thickness(4);
            b.Padding = new Thickness(12, 4, 12, 4);
            b.ToolTip = toolTip;
            b.Click += handler;
            return b;
        }

        private void Populate()
        {
            List<DataGridColumn> cols = new List<DataGridColumn>();
            foreach (DataGridColumn c in _grid.Columns) cols.Add(c);
            cols.Sort(delegate(DataGridColumn a, DataGridColumn b) { return a.DisplayIndex.CompareTo(b.DisplayIndex); });

            foreach (DataGridColumn c in cols)
            {
                string label = c.SortMemberPath;
                TextBlock header = c.Header as TextBlock;
                if (header != null) label = header.Text;

                CheckBox cb = UiHelpers.AlignedCheckBox(label);
                cb.IsChecked = c.Visibility == Visibility.Visible;
                cb.Tag = c;
                cb.ToolTip = header != null ? header.ToolTip : null;

                ListBoxItem item = new ListBoxItem();
                item.Padding = new Thickness(4, 0, 4, 0);
                item.MinHeight = 30;
                item.VerticalContentAlignment = VerticalAlignment.Center;
                item.Content = cb;
                item.Tag = c;
                _list.Items.Add(item);
            }
        }

        internal static string ValidateAlignmentContract()
        {
            DataGrid grid = new DataGrid();
            grid.Columns.Add(new DataGridTextColumn {
                Header = UiHelpers.Header("Album / CD", "Sample"),
                SortMemberPath = "Album",
                Visibility = Visibility.Visible
            });
            ColumnsWindow dialog = new ColumnsWindow(null, grid, "Columns layout test");
            ListBoxItem item = dialog._list.Items[0] as ListBoxItem;
            CheckBox checkbox = item == null ? null : item.Content as CheckBox;
            if (item == null || item.MinHeight < 28 ||
                item.VerticalContentAlignment != VerticalAlignment.Center ||
                !UiHelpers.IsAlignedCheckBox(checkbox))
                throw new InvalidOperationException("Configure Columns checkbox and caption have mismatched vertical alignment.");
            return "Configure Columns checkbox + caption share a centered row baseline";
        }

        private void Move(int delta)
        {
            int index = _list.SelectedIndex;
            if (index < 0) return;
            int target = index + delta;
            if (target < 0 || target >= _list.Items.Count) return;
            object item = _list.Items[index];
            _list.Items.RemoveAt(index);
            _list.Items.Insert(target, item);
            _list.SelectedIndex = target;
            _list.ScrollIntoView(item);
        }

        private void Apply()
        {
            int i;
            for (i = 0; i < _list.Items.Count; i++)
            {
                ListBoxItem item = _list.Items[i] as ListBoxItem;
                if (item == null) continue;
                CheckBox cb = item.Content as CheckBox;
                DataGridColumn col = item.Tag as DataGridColumn;
                if (cb == null || col == null) continue;
                col.Visibility = cb.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }

            for (i = 0; i < _list.Items.Count; i++)
            {
                ListBoxItem item = _list.Items[i] as ListBoxItem;
                DataGridColumn col = item != null ? item.Tag as DataGridColumn : null;
                if (col == null) continue;
                try { col.DisplayIndex = i; }
                catch { }
            }
        }
    }
}
