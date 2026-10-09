using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace DJLibrary
{
    internal sealed class MetadataNormalizerPreviewWindow : Window
    {
        private const string ColumnLayoutKey = "metadata.normalizer.preview.columns";
        private static readonly double[] DefaultWidths = { 95, 125, 65, 240, 240, 250, 360 };
        internal MetadataNormalizerPreviewWindow(Window owner, DigitalItem item, MetadataNormalizerPreviewModel model)
        {
            if (item == null) throw new ArgumentNullException("item");
            if (model == null) throw new ArgumentNullException("model");

            Owner = owner;
            Title = "DJ Metadata Normalizer — Vorschau";
            GridRuntimeSupport.ApplyWindowIcon(this);
            Width = 1120;
            Height = 650;
            MinWidth = 820;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            WindowGeometrySettings.Attach(this, "metadata.normalizer.preview");

            DockPanel root = new DockPanel();
            root.Margin = new Thickness(8);
            Content = root;

            StackPanel actions = new StackPanel();
            actions.Orientation = Orientation.Horizontal;
            actions.HorizontalAlignment = HorizontalAlignment.Right;
            actions.Margin = new Thickness(0, 8, 0, 0);
            DockPanel.SetDock(actions, Dock.Bottom);
            root.Children.Add(actions);

            Button close = new Button();
            close.Content = "Schließen";
            close.Padding = new Thickness(14, 4, 14, 4);
            close.ToolTip = "Schließt die Vorschau. Es werden keine Tags geschrieben.";
            close.Click += delegate { Close(); };
            actions.Children.Add(close);

            StackPanel summary = new StackPanel();
            summary.Margin = new Thickness(0, 0, 0, 8);
            DockPanel.SetDock(summary, Dock.Top);
            root.Children.Add(summary);

            TextBlock heading = new TextBlock();
            heading.FontSize = 16;
            heading.FontWeight = FontWeights.SemiBold;
            heading.Text = "Read-only Normalisierungsvorschau";
            summary.Children.Add(heading);

            TextBlock notice = new TextBlock();
            notice.Margin = new Thickness(0, 3, 0, 6);
            notice.Text = "Diese Ansicht analysiert ausschließlich. Es gibt in dieser Stufe keinen Tag-Write-Pfad.";
            summary.Children.Add(notice);

            TextBlock source = new TextBlock();
            source.Text = "Datei: " + (item.Path ?? "") + (item.Subsong > 0 ? "  ·  Subsong " + item.Subsong.ToString() : "");
            source.TextWrapping = TextWrapping.Wrap;
            summary.Children.Add(source);

            TextBlock contract = new TextBlock();
            contract.Margin = new Thickness(0, 2, 0, 0);
            contract.Text = String.Format(
                "Ruleset {0} ({1})  ·  {2} Änderungen  ·  SAFE {3} / CONFIDENT {4} / REVIEW {5}  ·  Snapshot {6}",
                model.RulesetRevision,
                model.RulesSourceLabel,
                model.Proposals.Count,
                model.SafeCount,
                model.ConfidentCount,
                model.ReviewCount,
                model.IsCurrentFor(item) ? "aktuell" : "VERALTET");
            contract.ToolTip =
                "Ruleset path: " + model.RulesSourcePath +
                "\nBridge tag_fingerprint: " + model.BridgeTagFingerprint +
                "\nNative input_fingerprint: " + model.NativeInputFingerprint;
            summary.Children.Add(contract);

            DataGrid grid = UiHelpers.CreateReadOnlyGrid();
            grid.CanUserReorderColumns = true;
            root.Children.Add(grid);

            AddColumn(grid, "Safety", "Sicherheitsklasse aus der gemeinsamen Native Engine.", "Safety", 95);
            AddColumn(grid, "Feld", "Metadatenfeld.", "Field", 125);
            AddColumn(grid, "Wert #", "Nullbasierter Index bei Mehrfachwerten.", "ValueIndex", 65);
            AddColumn(grid, "Original", "Exakter Originalwert aus metadata_vectors_json.", "Original", 240);
            AddColumn(grid, "Vorschlag", "Kanonischer Vorschlag der gemeinsamen Native Engine.", "Proposed", 240);
            AddColumn(grid, "Regeln", "Stabile Rule-IDs, die zu diesem Vorschlag geführt haben.", "RuleIdsText", 250);
            AddColumn(grid, "Begründung", "Rationales der angewendeten Regeln.", "RationalesText", 360);
            grid.ItemsSource = model.Proposals;
            GridLayoutSettings.Apply(SettingsManager.Load(), ColumnLayoutKey, grid);

            Button configureColumns = new Button();
            configureColumns.Content = "Spalten…";
            configureColumns.Padding = new Thickness(14, 4, 14, 4);
            configureColumns.Margin = new Thickness(0, 0, 8, 0);
            configureColumns.ToolTip = "Spalten verschieben, ein- oder ausblenden und Breiten anpassen.";
            configureColumns.Click += delegate
            {
                ColumnsWindow columns = new ColumnsWindow(this, grid, "Normalizer-Vorschau");
                columns.ShowDialog();
            };
            actions.Children.Insert(0, configureColumns);

            Button resetColumns = new Button();
            resetColumns.Content = "Spalten zurücksetzen";
            resetColumns.Padding = new Thickness(14, 4, 14, 4);
            resetColumns.Margin = new Thickness(0, 0, 8, 0);
            resetColumns.ToolTip = "Standardbreiten, Reihenfolge, Sichtbarkeit und Sortierung wiederherstellen.";
            resetColumns.Click += delegate { ResetPreviewColumns(grid); };
            actions.Children.Insert(1, resetColumns);

            Closing += delegate
            {
                AppSettings latest = SettingsManager.Load();
                GridLayoutSettings.Capture(latest, ColumnLayoutKey, grid);
                SettingsManager.Save(latest);
            };

            PreviewKeyDown += delegate(object sender, System.Windows.Input.KeyEventArgs e)
            {
                if (e.Key == System.Windows.Input.Key.Escape)
                {
                    Close();
                    e.Handled = true;
                }
            };
            HorizontalScrollSupport.Enable(this);
        }

        internal static void ResetPreviewColumns(DataGrid grid)
        {
            if (grid == null || grid.Columns.Count != DefaultWidths.Length)
                throw new InvalidOperationException("Normalizer preview column schema differs from its defaults.");
            for (int i = 0; i < DefaultWidths.Length; ++i)
            {
                grid.Columns[i].Visibility = Visibility.Visible;
                grid.Columns[i].Width = new DataGridLength(DefaultWidths[i]);
                grid.Columns[i].SortDirection = null;
            }
            for (int i = 0; i < DefaultWidths.Length; ++i)
                grid.Columns[i].DisplayIndex = i;
            System.ComponentModel.ICollectionView view =
                CollectionViewSource.GetDefaultView(grid.ItemsSource);
            if (view != null) view.SortDescriptions.Clear();
        }

        private static void AddColumn(DataGrid grid, string header, string tip, string path, double width)
        {
            grid.Columns.Add(UiHelpers.TextColumn(new ColumnSpec(path, header, tip, path, width, true)));
        }
    }
}
