using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace DJLibrary
{
    // WPF treats `AlternatingRowBackground = null` as a LOCAL dependency-property value,
    // not as "property cleared". DataGridRow then coerces that local value onto odd rows
    // and it outranks a RowStyle background. For our code-built read-only grids, callers
    // use null to mean "disable alternating background"; make that intent explicit and
    // remove the local value instead. This keeps the production CDX RowStyle authoritative.
    public sealed class ReadOnlyDataGrid : DataGrid
    {
        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property == AlternatingRowBackgroundProperty && e.NewValue == null &&
                !Object.ReferenceEquals(ReadLocalValue(AlternatingRowBackgroundProperty), DependencyProperty.UnsetValue))
            {
                ClearValue(AlternatingRowBackgroundProperty);
            }
        }
    }

    public static class UiHelpers
    {
        public static string FormatDuration(double seconds)
        {
            if (seconds <= 0) return "—";
            int total = (int)Math.Round(seconds);
            int h = total / 3600;
            int m = (total % 3600) / 60;
            int s = total % 60;
            if (h > 0) return h.ToString() + ":" + m.ToString("00") + ":" + s.ToString("00");
            return m.ToString() + ":" + s.ToString("00");
        }

        public static bool TryParseDuration(string text, out double seconds)
        {
            seconds = 0;
            if (String.IsNullOrWhiteSpace(text)) return true;
            string value = text.Trim();
            string[] parts = value.Split(':');
            int h = 0, m = 0, s = 0;
            if (parts.Length == 3)
            {
                if (!Int32.TryParse(parts[0], out h) || !Int32.TryParse(parts[1], out m) || !Int32.TryParse(parts[2], out s)) return false;
                if (h < 0 || m < 0 || m > 59 || s < 0 || s > 59) return false;
                seconds = h * 3600 + m * 60 + s;
                return true;
            }
            if (parts.Length == 2)
            {
                if (!Int32.TryParse(parts[0], out m) || !Int32.TryParse(parts[1], out s)) return false;
                if (m < 0 || s < 0 || s > 59) return false;
                seconds = m * 60 + s;
                return true;
            }

            double raw;
            if (!Double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out raw) &&
                !Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out raw)) return false;
            if (raw < 0) return false;
            seconds = raw;
            return true;
        }

        public static string DigitalLevelLabel(string level)
        {
            if (String.Equals(level, "strong", StringComparison.OrdinalIgnoreCase)) return "Strong";
            if (String.Equals(level, "likely", StringComparison.OrdinalIgnoreCase)) return "Likely";
            if (String.Equals(level, "candidate", StringComparison.OrdinalIgnoreCase)) return "Candidate";
            return "No Match";
        }

        public static string DigitalText(string level, int strong, int likely, int candidate)
        {
            if (String.Equals(level, "strong", StringComparison.OrdinalIgnoreCase))
            {
                int n = strong > 0 ? strong : 1;
                return n == 1 ? "Strong · unambiguous" : "Strong · " + n.ToString() + " candidates";
            }
            if (String.Equals(level, "likely", StringComparison.OrdinalIgnoreCase))
            {
                int n = likely > 0 ? likely : 1;
                return n == 1 ? "Likely" : "Likely · " + n.ToString();
            }
            if (String.Equals(level, "candidate", StringComparison.OrdinalIgnoreCase))
            {
                int n = candidate > 0 ? candidate : 1;
                return n == 1 ? "Candidate" : "Candidates · " + n.ToString();
            }
            return "—";
        }

        public static string LayoutText(string layout)
        {
            if (String.IsNullOrEmpty(layout) || String.Equals(layout, "standard", StringComparison.OrdinalIgnoreCase)) return "";
            if (String.Equals(layout, "single_track_continuous_mix", StringComparison.OrdinalIgnoreCase)) return "Continuous Mix";
            return layout.Replace('_', ' ');
        }

        // Shared baseline contract: text and the interactive checkbox glyph
        // must occupy the same vertically centered row across WPF dialogs.
        public static CheckBox AlignedCheckBox(string caption)
        {
            CheckBox cb = new CheckBox();
            cb.Content = new TextBlock {
                Text = caption ?? "",
                // The glyph has its own template spacing; reserve a visible,
                // DPI-independent gap before the caption in all checkable dialogs.
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Left,
                TextWrapping = TextWrapping.NoWrap
            };
            cb.MinHeight = 26;
            cb.Padding = new Thickness(0);
            cb.Margin = new Thickness(0);
            cb.VerticalAlignment = VerticalAlignment.Center;
            cb.VerticalContentAlignment = VerticalAlignment.Center;
            cb.HorizontalAlignment = HorizontalAlignment.Stretch;
            return cb;
        }

        public static bool IsAlignedCheckBox(CheckBox cb)
        {
            TextBlock text = cb == null ? null : cb.Content as TextBlock;
            return cb != null && text != null &&
                cb.VerticalAlignment == VerticalAlignment.Center &&
                cb.VerticalContentAlignment == VerticalAlignment.Center &&
                text.VerticalAlignment == VerticalAlignment.Center &&
                text.Margin.Left >= 7 && text.Margin.Top == 0 && text.Margin.Bottom == 0 &&
                cb.Padding.Top == 0 && cb.Padding.Bottom == 0;
        }

        public static TextBlock Header(string text, string toolTip)
        {
            TextBlock tb = new TextBlock();
            tb.Text = text;
            tb.ToolTip = toolTip;
            tb.HorizontalAlignment = HorizontalAlignment.Stretch;
            tb.VerticalAlignment = VerticalAlignment.Center;
            tb.Background = Brushes.Transparent;
            return tb;
        }

        public static double ReadableMinimumWidth(string member)
        {
            string key=(member??"").ToLowerInvariant();
            if(key=="position" || key=="tracknumber" || key=="discnumber" || key=="tracks") return 45;
            if(key.Contains("duration") || key=="bpm" || key.Contains("date") || key.Contains("year")) return 75;
            if(key.Contains("country")) return 75;
            if(key.Contains("catalog")) return 95;
            if(key.Contains("genre")) return 105;
            if(key.Contains("version") || key.Contains("mix")) return 135;
            if(key.Contains("artist")) return 135;
            if(key.Contains("title") || key.Contains("album")) return 180;
            if(key.Contains("source") || key.Contains("review")) return 115;
            if(key.Contains("label")) return 110;
            return 70;
        }

        public static bool HasCellToolTip(DataGridTextColumn column)
        {
            if(column==null || column.ElementStyle==null) return false;
            foreach(SetterBase setterBase in column.ElementStyle.Setters)
            {
                Setter setter=setterBase as Setter;
                if(setter!=null && setter.Property==FrameworkElement.ToolTipProperty) return true;
            }
            return false;
        }

        public static void EnsureTextColumnReadability(DataGridTextColumn column)
        {
            if(column==null) return;
            string member=column.SortMemberPath;
            Binding binding=column.Binding as Binding;
            string path=binding!=null && binding.Path!=null ? binding.Path.Path : member;
            column.MinWidth=Math.Max(column.MinWidth,ReadableMinimumWidth(member));
            if(HasCellToolTip(column) || String.IsNullOrWhiteSpace(path) || path==".") return;

            Style style=column.ElementStyle;
            if(style==null)
            {
                style=new Style(typeof(TextBlock),DataGridTextColumn.DefaultElementStyle);
                column.ElementStyle=style;
            }

            // Preserve a caller's direct BasedOn relationship to the native WPF
            // DefaultElementStyle whenever the style is still mutable. Creating a
            // second derived style here used to hide that relationship and broke the
            // Candidate21 selection/layout contract even though the visual setters were
            // otherwise correct.
            if(!style.IsSealed)
            {
                style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty,new Binding(path)));
                return;
            }

            Style derived=new Style(typeof(TextBlock),style);
            derived.Setters.Add(new Setter(FrameworkElement.ToolTipProperty,new Binding(path)));
            column.ElementStyle=derived;
        }

        public static DataGridTextColumn BoundTextColumn(string header,string headerToolTip,string bindingPath,string sortPath,DataGridLength width,double minWidth,string cellToolTipPath)
        {
            DataGridTextColumn column=new DataGridTextColumn();
            column.Header=Header(header,headerToolTip);
            column.Binding=new Binding(bindingPath);
            column.SortMemberPath=String.IsNullOrWhiteSpace(sortPath)?bindingPath:sortPath;
            column.Width=width;
            column.MinWidth=Math.Max(minWidth,ReadableMinimumWidth(column.SortMemberPath));
            column.IsReadOnly=true;
            Style style=new Style(typeof(TextBlock),DataGridTextColumn.DefaultElementStyle);
            style.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty,VerticalAlignment.Center));
            string tipPath=String.IsNullOrWhiteSpace(cellToolTipPath)?bindingPath:cellToolTipPath;
            if(!String.IsNullOrWhiteSpace(tipPath)) style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty,new Binding(tipPath)));
            column.ElementStyle=style;
            return column;
        }

        public static DataGridTextColumn TextColumn(ColumnSpec spec)
        {
            DataGridTextColumn c = new DataGridTextColumn();
            c.Header = Header(spec.Header, spec.ToolTip);
            c.Binding = new Binding(spec.BindingPath);
            c.SortMemberPath = spec.Key;
            c.Width = new DataGridLength(spec.Width);
            c.Visibility = spec.Visible ? Visibility.Visible : Visibility.Collapsed;
            c.IsReadOnly = true;

            // Keep the stock WPF DataGridCell selection visuals intact.  The only
            // per-column visual customization is layout: the generated TextBlock is
            // centered vertically.  Foreground is deliberately NOT set here so it
            // continues to inherit the cell's active/inactive selection foreground.
            Style elementStyle = new Style(typeof(TextBlock), DataGridTextColumn.DefaultElementStyle);
            elementStyle.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
            c.ElementStyle = elementStyle;
            EnsureTextColumnReadability(c);
            return c;
        }

        public static TextBlock FieldLabel(string text, string toolTip)
        {
            TextBlock t = new TextBlock();
            t.Text = text;
            t.FontWeight = FontWeights.SemiBold;
            t.TextWrapping = TextWrapping.Wrap;
            t.Margin = new Thickness(0, 4, 10, 4);
            t.VerticalAlignment = VerticalAlignment.Center;
            if (!String.IsNullOrEmpty(toolTip)) t.ToolTip = toolTip;
            return t;
        }

        public static TextBlock FieldValue(string text, string toolTip)
        {
            TextBlock t = new TextBlock();
            t.Text = String.IsNullOrEmpty(text) ? "—" : text;
            t.TextWrapping = TextWrapping.Wrap;
            t.Margin = new Thickness(0, 4, 18, 4);
            t.VerticalAlignment = VerticalAlignment.Center;
            if (!String.IsNullOrEmpty(toolTip)) t.ToolTip = toolTip;
            return t;
        }

        public static TextBox SelectableFieldValue(string text, string toolTip)
        {
            TextBox t = new TextBox();
            t.Text = String.IsNullOrEmpty(text) ? "—" : text;
            t.IsReadOnly = true;
            t.BorderThickness = new Thickness(0);
            t.Background = Brushes.Transparent;
            t.Padding = new Thickness(0);
            t.TextWrapping = TextWrapping.Wrap;
            t.Margin = new Thickness(0, 4, 18, 4);
            t.VerticalAlignment = VerticalAlignment.Center;
            t.Cursor = Cursors.IBeam;
            if (!String.IsNullOrEmpty(toolTip)) t.ToolTip = toolTip;
            return t;
        }

        public static void AddField(Grid grid, int row, int pair, string label, string value, string toolTip)
        {
            int col = pair * 2;
            if (grid.RowDefinitions.Count <= row)
            {
                RowDefinition rd = new RowDefinition();
                rd.Height = GridLength.Auto;
                grid.RowDefinitions.Add(rd);
            }
            TextBlock l = FieldLabel(label, toolTip);
            TextBox v = SelectableFieldValue(value, toolTip);
            Grid.SetRow(l, row);
            Grid.SetColumn(l, col);
            Grid.SetRow(v, row);
            Grid.SetColumn(v, col + 1);
            grid.Children.Add(l);
            grid.Children.Add(v);
        }

        public static void AddBooleanField(Grid grid, int row, int pair, string label, bool value, string toolTip)
        {
            int col = pair * 2;
            if (grid.RowDefinitions.Count <= row)
            {
                RowDefinition rd = new RowDefinition();
                rd.Height = GridLength.Auto;
                grid.RowDefinitions.Add(rd);
            }
            TextBlock l = FieldLabel(label, toolTip);
            CheckBox v = new CheckBox();
            v.IsChecked = value;
            v.IsHitTestVisible = false;
            v.Focusable = false;
            v.VerticalAlignment = VerticalAlignment.Center;
            v.VerticalContentAlignment = VerticalAlignment.Center;
            v.Margin = new Thickness(0, 5, 18, 4);
            if (!String.IsNullOrEmpty(toolTip)) v.ToolTip = toolTip;
            Grid.SetRow(l, row);
            Grid.SetColumn(l, col);
            Grid.SetRow(v, row);
            Grid.SetColumn(v, col + 1);
            grid.Children.Add(l);
            grid.Children.Add(v);
        }

        public static Grid CreateFourColumnFieldGrid()
        {
            Grid g = new Grid();
            g.Margin = new Thickness(12);
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return g;
        }

        public static Grid CreateTwoColumnFieldGrid()
        {
            Grid g = new Grid();
            g.Margin = new Thickness(10);
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return g;
        }

        public static DataGrid CreateReadOnlyGrid()
        {
            DataGrid grid = new ReadOnlyDataGrid();
            grid.AutoGenerateColumns = false;
            grid.CanUserAddRows = false;
            grid.CanUserDeleteRows = false;
            grid.CanUserReorderColumns = true;
            grid.CanUserResizeColumns = true;
            grid.CanUserSortColumns = true;
            grid.IsReadOnly = true;
            grid.SelectionMode = DataGridSelectionMode.Single;
            grid.SelectionUnit = DataGridSelectionUnit.FullRow;

            grid.Resources[DataGrid.FocusBorderBrushKey] = SystemColors.HighlightBrush;

            grid.HeadersVisibility = DataGridHeadersVisibility.Column;
            grid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
            grid.HorizontalGridLinesBrush = SystemColors.ControlLightBrush;
            grid.BorderBrush = SystemColors.ControlDarkBrush;
            grid.BorderThickness = new Thickness(1);
            grid.RowHeight = 25;
            grid.ColumnHeaderHeight = 27;
            grid.AlternationCount = 2;
            grid.AlternatingRowBackground = SystemColors.ControlLightLightBrush;
            grid.Background = SystemColors.WindowBrush;
            grid.EnableRowVirtualization = true;
            grid.EnableColumnVirtualization = false;
            VirtualizingStackPanel.SetIsVirtualizing(grid, true);
            VirtualizingStackPanel.SetVirtualizationMode(grid, VirtualizationMode.Recycling);
            ScrollViewer.SetCanContentScroll(grid, true);
            ScrollViewer.SetHorizontalScrollBarVisibility(grid, ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(grid, ScrollBarVisibility.Auto);
            GridGovernance.Apply(grid);
            return grid;
        }

        public static string ValidateReadOnlyGridVisualContract()
        {
            DataGrid grid = CreateReadOnlyGrid();
            GridGovernance.Validate(grid, "UiHelpers.CreateReadOnlyGrid");
            if (grid.SelectionUnit != DataGridSelectionUnit.FullRow)
                throw new InvalidOperationException("UI-Selbsttest: DataGrid SelectionUnit ist nicht FullRow.");
            if (grid.SelectionMode != DataGridSelectionMode.Single)
                throw new InvalidOperationException("UI-Selbsttest: DataGrid SelectionMode ist nicht Single.");
            if (grid.RowStyle != null)
                throw new InvalidOperationException("UI-Selbsttest: RowStyle darf die native WPF-Auswahl nicht ueberschreiben.");
            if (grid.CellStyle != null)
                throw new InvalidOperationException("UI-Selbsttest: CellStyle darf die native WPF-Auswahl nicht ueberschreiben.");
            if (grid.EnableColumnVirtualization)
                throw new InvalidOperationException("UI-Selbsttest: Spaltenvirtualisierung muss fuer deterministische FullRow-Auswahl deaktiviert sein.");
            if (!grid.EnableRowVirtualization)
                throw new InvalidOperationException("UI-Selbsttest: Zeilenvirtualisierung muss fuer den 15k-Track-Bestand aktiv bleiben.");
            if (!GridRuntimeSupport.HasClipboardMenu(grid))
                throw new InvalidOperationException("UI-Selbsttest: explizite Clipboard-Konfiguration fehlt am produktiven Grid-Factory-Pfad.");

            ReadOnlyDataGrid nullSemantics = new ReadOnlyDataGrid();
            nullSemantics.AlternatingRowBackground = SystemColors.ControlLightLightBrush;
            nullSemantics.AlternatingRowBackground = null;
            if (!Object.ReferenceEquals(nullSemantics.ReadLocalValue(DataGrid.AlternatingRowBackgroundProperty), DependencyProperty.UnsetValue))
                throw new InvalidOperationException("UI-Selbsttest: AlternatingRowBackground=null muss den lokalen WPF-Wert vollständig entfernen.");

            SolidColorBrush focusBrush = grid.Resources[DataGrid.FocusBorderBrushKey] as SolidColorBrush;
            SolidColorBrush highlightBrush = SystemColors.HighlightBrush as SolidColorBrush;
            if (focusBrush == null || highlightBrush == null || focusBrush.Color != highlightBrush.Color)
                throw new InvalidOperationException("UI-Selbsttest: Current-Cell-Fokusrahmen ist nicht mit der aktiven Auswahlfarbe verschmolzen.");

            DataGridTextColumn column = TextColumn(new ColumnSpec("SelfTest", "SelfTest", "", "SelfTest", 100, true));
            if (column.ElementStyle == null)
                throw new InvalidOperationException("UI-Selbsttest: TextColumn besitzt keinen Layout-Style.");
            if (!Object.ReferenceEquals(column.ElementStyle.BasedOn, DataGridTextColumn.DefaultElementStyle))
                throw new InvalidOperationException("UI-Selbsttest: TextColumn-Layout muss auf dem nativen DefaultElementStyle basieren.");

            bool centered = false;
            foreach (SetterBase setterBase in column.ElementStyle.Setters)
            {
                Setter setter = setterBase as Setter;
                if (setter == null) continue;
                if (setter.Property == TextBlock.VerticalAlignmentProperty && Object.Equals(setter.Value, VerticalAlignment.Center)) centered = true;
                if (setter.Property == TextBlock.ForegroundProperty)
                    throw new InvalidOperationException("UI-Selbsttest: TextColumn darf die Foreground-Farbe nicht lokal setzen.");
                if (setter.Property == TextBlock.RenderTransformProperty)
                    throw new InvalidOperationException("UI-Selbsttest: TextColumn darf keinen RenderTransform zur Vertikalausrichtung verwenden.");
            }
            if (!centered)
                throw new InvalidOperationException("UI-Selftest: TextColumn is not vertically centered.");
            if (column.MinWidth < 40 || !HasCellToolTip(column))
                throw new InvalidOperationException("UI-Selftest: TextColumn readability floor/full-value tooltip governance is missing.");

            TextBox selectable = SelectableFieldValue("Album #310", "");
            if (!selectable.IsReadOnly || selectable.BorderThickness != new Thickness(0) || selectable.Text != "Album #310")
                throw new InvalidOperationException("UI-Selbsttest: auswählbares Read-only-Metadatenfeld verletzt den Copy-Vertrag.");

            return "UI contract: native FullRow selection + explicit cell/row clipboard + no column virtualization + row virtualization + selection-safe focus + vertically centered text + selectable read-only metadata + deterministic alternating-row semantics + readable cell tooltips";
        }

        public static string MatchMethodDescription(string method)
        {
            if (method == "live_exact_artist_title_version_duration") return "Live: Artist, Basistitel und Mix/Version stimmen strukturiert; Laufzeit liegt innerhalb der Toleranz.";
            if (method == "live_exact_display_title_duration") return "Live: Artist und vollständiger Anzeigetitel stimmen; Laufzeit liegt innerhalb der Toleranz.";
            if (method == "exact_artist_title_version_duration") return "Artist, Basistitel und Mix/Version stimmen; Laufzeit liegt innerhalb der Toleranz.";
            if (method == "exact_artist_title_version") return "Artist, Basistitel und Mix/Version stimmen. Die Laufzeit ist nicht stark genug zur eindeutigen Bestätigung.";
            if (method == "exact_artist_title_duration") return "Artist, Basistitel und Laufzeit stimmen; Mix/Version ist nicht exakt bestätigt.";
            if (method == "exact_artist_title") return "Artist und Basistitel stimmen. Weitere Merkmale reichen noch nicht für einen starken Treffer.";
            return method;
        }

        public static bool ContainsAllTerms(string haystack, string search)
        {
            if (String.IsNullOrWhiteSpace(search)) return true;
            if (haystack == null) haystack = "";
            string[] terms = search.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            int i;
            for (i = 0; i < terms.Length; i++)
            {
                if (haystack.IndexOf(terms[i], StringComparison.OrdinalIgnoreCase) < 0) return false;
            }
            return true;
        }
    }
}
