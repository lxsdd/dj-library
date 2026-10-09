using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DJLibrary
{
    public static class GridRuntimeSupport
    {
        private static readonly DependencyProperty ClipboardConfiguredProperty = DependencyProperty.RegisterAttached(
            "ClipboardConfigured", typeof(bool), typeof(GridRuntimeSupport), new PropertyMetadata(false));

        public static bool ApplyWindowIcon(Window window)
        {
            if (window == null) return false;
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DJLibrary.ico");
                if (!File.Exists(path)) return false;
                BitmapFrame frame = BitmapFrame.Create(
                    new Uri(path, UriKind.Absolute),
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);
                window.Icon = frame;
                return window.Icon != null;
            }
            catch { return false; }
        }

        public static void ConfigureClipboard(DataGrid grid)
        {
            if (grid == null || (bool)grid.GetValue(ClipboardConfiguredProperty)) return;
            grid.SetValue(ClipboardConfiguredProperty, true);

            grid.ClipboardCopyMode = DataGridClipboardCopyMode.None;
            grid.PreviewKeyDown += GridPreviewKeyDown;
            grid.PreviewMouseRightButtonDown += GridPreviewMouseRightButtonDown;

            ContextMenu menu = new ContextMenu();
            MenuItem value = new MenuItem { Header = "Copy Value" };
            value.Click += delegate { TryCopyCurrentValue(grid); };
            menu.Items.Add(value);
            MenuItem row = new MenuItem { Header = "Copy Row" };
            row.Click += delegate { TryCopyRow(grid); };
            menu.Items.Add(row);
            grid.ContextMenu = menu;
        }

        public static bool HasClipboardMenu(DataGrid grid)
        {
            if (grid == null || grid.ContextMenu == null || grid.ClipboardCopyMode != DataGridClipboardCopyMode.None) return false;
            bool value = false, row = false;
            foreach (object item in grid.ContextMenu.Items)
            {
                MenuItem m = item as MenuItem;
                if (m == null) continue;
                string header = Convert.ToString(m.Header, CultureInfo.InvariantCulture);
                if (header == "Copy Value") value = true;
                if (header == "Copy Row") row = true;
            }
            return value && row;
        }

        private static void GridPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || e.Key != Key.C) return;
            DataGrid grid = sender as DataGrid;
            if (grid != null && TryCopyCurrentValue(grid)) e.Handled = true;
        }

        private static void GridPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            DataGrid grid = sender as DataGrid;
            if (grid == null) return;
            DependencyObject current = e.OriginalSource as DependencyObject;
            while (current != null && !(current is DataGridCell))
            {
                DependencyObject parent = null;
                try { parent = VisualTreeHelper.GetParent(current); }
                catch { try { parent = LogicalTreeHelper.GetParent(current); } catch { } }
                current = parent;
            }
            DataGridCell cell = current as DataGridCell;
            if (cell == null) return;
            object item = cell.DataContext;
            if (item == null || item == CollectionView.NewItemPlaceholder) return;
            grid.SelectedItem = item;
            grid.CurrentCell = new DataGridCellInfo(item, cell.Column);
        }

        public static bool TryCopyCurrentValue(DataGrid grid)
        {
            try
            {
                if (grid == null) return false;
                object item = grid.CurrentCell.Item;
                if (item == null || item == CollectionView.NewItemPlaceholder) item = grid.SelectedItem;
                if (item == null) return false;
                DataGridColumn column = grid.CurrentCell.Column;
                if (column == null)
                    column = grid.Columns.Where(delegate(DataGridColumn c) { return c.Visibility == Visibility.Visible; })
                        .OrderBy(delegate(DataGridColumn c) { return c.DisplayIndex; }).FirstOrDefault();
                if (column == null) return false;
                SetClipboardText(GetColumnText(column, item));
                return true;
            }
            catch { return false; }
        }

        public static bool TryCopyRow(DataGrid grid)
        {
            try
            {
                if (grid == null) return false;
                object item = grid.SelectedItem;
                if (item == null || item == CollectionView.NewItemPlaceholder) item = grid.CurrentCell.Item;
                if (item == null) return false;
                string line = String.Join("\t", grid.Columns
                    .Where(delegate(DataGridColumn c) { return c.Visibility == Visibility.Visible; })
                    .OrderBy(delegate(DataGridColumn c) { return c.DisplayIndex; })
                    .Select(delegate(DataGridColumn c) { return GetColumnText(c, item); }).ToArray());
                SetClipboardText(line);
                return true;
            }
            catch { return false; }
        }

        private static void SetClipboardText(string text)
        {
            if (String.IsNullOrEmpty(text)) Clipboard.Clear();
            else Clipboard.SetText(text);
        }

        internal static string GetColumnText(DataGridColumn column, object item)
        {
            if (column == null || item == null) return "";
            if (String.Equals(column.SortMemberPath, "CdxCompatibilitySortKey", StringComparison.Ordinal))
                return PropertyText(item, "CdxCompatibilityText");

            string path = null;
            DataGridBoundColumn bound = column as DataGridBoundColumn;
            if (bound != null)
            {
                Binding binding = bound.Binding as Binding;
                if (binding != null && binding.Path != null) path = binding.Path.Path;
            }
            if (String.IsNullOrWhiteSpace(path) || path == ".") path = column.SortMemberPath;
            if (String.IsNullOrWhiteSpace(path)) return "";

            object current = item;
            foreach (string part in path.Split('.'))
            {
                if (current == null) return "";
                PropertyInfo property = current.GetType().GetProperty(part, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (property == null) return "";
                current = property.GetValue(current, null);
            }
            return FormatClipboardValue(current);
        }

        private static string PropertyText(object item, string name)
        {
            PropertyInfo p = item.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            return p == null ? "" : FormatClipboardValue(p.GetValue(item, null));
        }

        internal static string FormatClipboardValue(object value)
        {
            if (value == null) return "";
            if (value is bool) return (bool)value ? "Yes" : "No";
            return Convert.ToString(value, CultureInfo.CurrentCulture) ?? "";
        }

        public static DataGridTextColumn CreateCdxColumn(ColumnSpec spec)
        {
            DataGridTextColumn column = UiHelpers.TextColumn(spec);
            Style style = new Style(typeof(TextBlock), column.ElementStyle);
            style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding("CdxCompatibilityToolTip")));
            column.ElementStyle = style;
            return column;
        }

        private sealed class CdxBackgroundConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                return String.Equals(value as string, "no", StringComparison.Ordinal)
                    ? (Brush)new SolidColorBrush(Color.FromRgb(255, 236, 236))
                    : SystemColors.WindowBrush;
            }
            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        public static Style CreateCdxRowStyle()
        {
            Style style = new Style(typeof(DataGridRow));
            Binding background = new Binding("CdxCompatibilityCode");
            background.Converter = new CdxBackgroundConverter();
            style.Setters.Add(new Setter(Control.BackgroundProperty, background));

            Trigger selected = new Trigger();
            selected.Property = DataGridRow.IsSelectedProperty;
            selected.Value = true;
            selected.Setters.Add(new Setter(Control.BackgroundProperty, SystemColors.HighlightBrush));
            selected.Setters.Add(new Setter(Control.ForegroundProperty, SystemColors.HighlightTextBrush));
            style.Triggers.Add(selected);
            return style;
        }

        public static bool HasDeterministicCdxRowStyle(DataGrid grid)
        {
            if (grid == null || grid.RowStyle == null) return false;
            // Only an actually alternating grid can hit DataGridRow's odd-row transfer path.
            // For AlternationCount > 1, a LOCAL AlternatingRowBackground value (even null)
            // can outrank the RowStyle Background. Non-alternating grids never use that path.
            if (grid.AlternationCount > 1 &&
                !Object.ReferenceEquals(grid.ReadLocalValue(DataGrid.AlternatingRowBackgroundProperty), DependencyProperty.UnsetValue)) return false;
            bool boundBackground = false, selected = false;
            foreach (Setter setter in grid.RowStyle.Setters)
            {
                if (setter.Property != Control.BackgroundProperty) continue;
                Binding b = setter.Value as Binding;
                if (b != null && b.Path != null && b.Path.Path == "CdxCompatibilityCode" && b.Converter != null) boundBackground = true;
            }
            foreach (TriggerBase trigger in grid.RowStyle.Triggers)
            {
                Trigger t = trigger as Trigger;
                if (t != null && t.Property == DataGridRow.IsSelectedProperty && Object.Equals(t.Value, true)) selected = true;
            }
            return boundBackground && selected;
        }

        public static bool HasCdxColumn(DataGrid grid)
        {
            if (grid == null) return false;
            return grid.Columns.Any(delegate(DataGridColumn c)
            {
                DataGridTextColumn text = c as DataGridTextColumn;
                if (!String.Equals(c.SortMemberPath, "CdxCompatibilitySortKey", StringComparison.Ordinal)) return false;
                if (text == null) return false;
                Binding binding = text.Binding as Binding;
                return binding != null && binding.Path != null && binding.Path.Path == "CdxCompatibilityText";
            });
        }

        public static string ReadableMatchMethod(string method)
        {
            if (String.IsNullOrWhiteSpace(method)) return "—";
            if (method == "live_exact_artist_title_version_duration") return "Artist + Title/Mix + Duration";
            if (method == "live_exact_display_title_duration") return "Artist + Full Title + Duration";
            if (method == "live_exact_identity_duration_conflict") return "Exact title identity · duration differs by >3 s";
            if (method == "live_exact_artist_base_title_duration") return "Artist + Base Title + Duration";
            if (method == "live_partial_identity") return "Partial title/version identity";
            if (method == "live_exact_artist_base_title") return "Artist + Base Title";
            if (method == "exact_artist_title_version_duration") return "Artist + Title/Mix + Duration";
            if (method == "exact_artist_title_version") return "Artist + Title/Mix";
            if (method == "exact_artist_title_duration") return "Artist + Base Title + Duration";
            if (method == "exact_artist_title") return "Artist + Base Title";
            return method.Replace('_', ' ');
        }
    }
}
