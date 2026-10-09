using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace DJLibrary
{
    public static class UiRuntimeSelfTest
    {
        public static string Run(DataStore data)
        {
            if (data == null) throw new ArgumentNullException("data");

            MainWindow main = new MainWindow(data);
            string mainContract = main.ValidateRuntimeUiContract();
            if (main.Icon == null) throw new InvalidOperationException("UI-Produktionspfad: Hauptfenster besitzt kein explizites Window/Icon.");

            CdRow alex = data.Cds.FirstOrDefault(delegate(CdRow x) { return x.Album == "Fixture Boundary #310"; });
            if (alex == null || alex.CdxCompatibilityText != "No")
                throw new InvalidOperationException("UI-Produktionspfad: Fixture Boundary #310 ist nicht als CDX=Nein verfügbar.");

            CdDetailWindow cdDetail = new CdDetailWindow(null, alex, data);
            if (cdDetail.Icon == null) throw new InvalidOperationException("UI-Produktionspfad: CD-Detailfenster besitzt kein explizites Icon.");
            if (!ContainsTextBlock(cdDetail, "CDX Compatible") || !ContainsTextBox(cdDetail, "No"))
                throw new InvalidOperationException("UI-Produktionspfad: CD-Details enthalten kein sichtbares CDX Compatible=Nein-Feld.");

            // Inspect the exact private DataGrid field constructed by MainWindow. Reflection is
            // used only by the test; production delivery remains explicit in MainWindow.
            FieldInfo cdxField = typeof(MainWindow).GetField("_cdGrid", BindingFlags.Instance | BindingFlags.NonPublic);
            DataGrid productionCdxGrid = cdxField == null ? null : cdxField.GetValue(main) as DataGrid;
            if (productionCdxGrid == null || !GridRuntimeSupport.HasCdxColumn(productionCdxGrid))
                throw new InvalidOperationException("UI-Produktionspfad: reales _cdGrid mit CDX-Spalte fehlt.");
            if (!Object.ReferenceEquals(productionCdxGrid.ReadLocalValue(DataGrid.AlternatingRowBackgroundProperty), DependencyProperty.UnsetValue))
                throw new InvalidOperationException("UI-Produktionspfad: reales _cdGrid besitzt noch einen lokalen AlternatingRowBackground-Wert.");
            if (!GridRuntimeSupport.HasDeterministicCdxRowStyle(productionCdxGrid))
                throw new InvalidOperationException("UI-Produktionspfad: reales _cdGrid besitzt keinen deterministischen CDX-RowStyle.");

            TrackRow sampleTrack = data.Tracks.FirstOrDefault();
            if (sampleTrack == null) throw new InvalidOperationException("UI-Produktionspfad: kein Track für Detailtest present.");
            TrackDetailWindow trackDetail = new TrackDetailWindow(null, sampleTrack, data);
            if (trackDetail.Icon == null) throw new InvalidOperationException("UI-Produktionspfad: Track-Detailfenster besitzt kein explizites Icon.");
            DataGrid matchGrid = FindDataGrids(trackDetail).FirstOrDefault(delegate(DataGrid g)
            {
                return g.Columns.Any(delegate(DataGridColumn c) { return c.SortMemberPath == "MethodText"; });
            });
            if (matchGrid == null) throw new InvalidOperationException("UI-Produktionspfad: Match-Grund bindet nicht direkt an MethodText.");
            if (!GridRuntimeSupport.HasClipboardMenu(matchGrid))
                throw new InvalidOperationException("UI-Produktionspfad: Match-Grid besitzt keine explizite Clipboard-Konfiguration.");

            MatchRow match = new MatchRow { Method = "live_exact_artist_title_version_duration" };
            if (match.MethodText != "Artist + Title/Mix + Duration")
                throw new InvalidOperationException("UI-Produktionspfad: sprechender Match-Grund fehlt.");

            DataGrid cdxGrid = UiHelpers.CreateReadOnlyGrid();
            cdxGrid.AlternatingRowBackground = null;
            cdxGrid.RowStyle = GridRuntimeSupport.CreateCdxRowStyle();
            if (!GridRuntimeSupport.HasDeterministicCdxRowStyle(cdxGrid))
                throw new InvalidOperationException("UI-Produktionspfad: CDX=Nein darf nicht durch einen lokalen AlternatingRowBackground-Wert verdeckt werden.");

            string catalogContract = CatalogWindow.ValidateDiscGridRuntimeContract();
            string renderedCdx = ValidateRenderedCdxRows(data);
            string metadata = CdMetadataPipeline.RunSelfTest();
            string windowGeometry = WindowGeometrySettings.ValidateContract();
            string metadataReview = CdMetadataChoiceDialog.ValidateMatrixContract();
            string normalizerGrid = ValidateNormalizerPreviewGrid();
            return "UI production path: " + mainContract + " + deterministic CDX rendering + CD details + MethodText match reason + " + catalogContract + " + " + renderedCdx + " + " + metadata + " + " + windowGeometry + " + " + metadataReview + " + " + normalizerGrid;
        }

        private static string ValidateRenderedCdxRows(DataStore data)
        {
            CdRow noRow = data.Cds.FirstOrDefault(delegate(CdRow x) { return x.CdxCompatibilityCode == "no"; });
            CdRow yesRow = data.Cds.FirstOrDefault(delegate(CdRow x) { return x.CdxCompatibilityCode == "yes"; });
            if (noRow == null || yesRow == null)
                throw new InvalidOperationException("UI-Produktionspfad: Fixture benötigt mindestens eine reale CDX=Nein- und eine CDX=Ja-CD.");

            // Separate object, same qualified physical TOC: prevents WPF SelectedItem from
            // resolving a duplicate reference to the first row during the selection test.
            CdRow noRowOdd = new CdRow
            {
                Album = "CDX self-test odd",
                Toc = noRow.Toc,
                TocComplete = noRow.TocComplete
            };
            if (noRowOdd.CdxCompatibilityCode != "no")
                throw new InvalidOperationException("UI-Produktionspfad: kopierte reale CDX=Nein-TOC klassifiziert nicht als no.");

            CdRow unknownRow = new CdRow { Album = "CDX self-test unknown", Toc = "", TocComplete = false };
            if (unknownRow.CdxCompatibilityCode != "unknown")
                throw new InvalidOperationException("UI-Produktionspfad: synthetische Unknown-CD klassifiziert nicht als unknown.");

            DataGrid grid = UiHelpers.CreateReadOnlyGrid();
            grid.AlternatingRowBackground = null;
            if (!Object.ReferenceEquals(grid.ReadLocalValue(DataGrid.AlternatingRowBackgroundProperty), DependencyProperty.UnsetValue))
                throw new InvalidOperationException("UI-Produktionspfad: CDX-Rendergrid hat nach null noch einen lokalen AlternatingRowBackground-Wert.");
            grid.RowStyle = GridRuntimeSupport.CreateCdxRowStyle();
            grid.Width = 420;
            grid.Height = 180;
            grid.Columns.Add(new DataGridTextColumn { Header = "CDX", Binding = new Binding("CdxCompatibilityText"), Width = 100 });

            // Critical regression layout: CDX=Nein is rendered at alternation index 0 and 1.
            // The previous broken test used no/yes/no/unknown and therefore never exercised
            // the failing odd-row state.
            List<CdRow> values = new List<CdRow> { noRow, noRowOdd, yesRow, unknownRow };
            grid.ItemsSource = values;

            HwndSourceParameters parameters = new HwndSourceParameters("DJLibrary-CdxRenderSelfTest");
            parameters.Width = 440;
            parameters.Height = 220;
            parameters.WindowStyle = unchecked((int)0x80000000); // WS_POPUP: real presentation source without a visible app window.
            HwndSource host = new HwndSource(parameters);
            try
            {
                host.RootVisual = grid;
                grid.ApplyTemplate();
                grid.Measure(new Size(420, 180));
                grid.Arrange(new Rect(0, 0, 420, 180));
                grid.UpdateLayout();

                for (int i = 0; i < values.Count; i++)
                {
                    DataGridRow row = grid.ItemContainerGenerator.ContainerFromIndex(i) as DataGridRow;
                    if (row == null)
                    {
                        grid.ScrollIntoView(values[i]);
                        grid.UpdateLayout();
                        row = grid.ItemContainerGenerator.ContainerFromIndex(i) as DataGridRow;
                    }
                    if (row == null) throw new InvalidOperationException("UI-Produktionspfad: gerenderte CDX-Testzeile " + i + " wurde nicht realisiert.");
                    int alternation = ItemsControl.GetAlternationIndex(row);
                    if (alternation != (i % 2))
                        throw new InvalidOperationException("UI-Produktionspfad: unerwarteter AlternationIndex in CDX-Testzeile " + i + ": " + alternation + ".");
                    SolidColorBrush brush = row.Background as SolidColorBrush;
                    bool pink = brush != null && brush.Color == Color.FromRgb(255, 236, 236);
                    if ((i == 0 || i == 1) && !pink)
                        throw new InvalidOperationException("UI-Produktionspfad: CDX=Nein ist in gerenderter Zeile " + i + " (AlternationIndex " + alternation + ") nicht #FFECEC.");
                    if (i >= 2 && pink)
                        throw new InvalidOperationException("UI-Produktionspfad: kompatible/unbekannte gerenderte CDX-Zeile ist fälschlich #FFECEC.");
                }

                grid.SelectedItem = noRowOdd;
                grid.UpdateLayout();
                DataGridRow selectedRow = grid.ItemContainerGenerator.ContainerFromIndex(1) as DataGridRow;
                SolidColorBrush selectedBrush = selectedRow == null ? null : selectedRow.Background as SolidColorBrush;
                SolidColorBrush expectedSelection = SystemColors.HighlightBrush as SolidColorBrush;
                if (selectedBrush == null || expectedSelection == null || selectedBrush.Color != expectedSelection.Color)
                    throw new InvalidOperationException("UI-Produktionspfad: Auswahlfarbe gewinnt nicht deterministisch über CDX-Warnfarbe.");
            }
            finally
            {
                host.RootVisual = null;
                host.Dispose();
            }
            return "real CdRow CDX=Nein rendered on alternation 0+1 deterministic + selection precedence";
        }

        private static string ValidateNormalizerPreviewGrid()
        {
            // Construct the actual production Normalizer preview, with a
            // deterministic model. The normalizer engine is not invoked.
            DigitalItem item = new DigitalItem { Path = "fixture.mp3", Fingerprint = "test-fingerprint" };
            List<MetadataNormalizerProposalRow> proposals = new List<MetadataNormalizerProposalRow>();
            proposals.Add(new MetadataNormalizerProposalRow(0, "TITLE", 0, " Old ", "Old",
                "SAFE", "trim", "Remove surrounding whitespace"));
            MetadataNormalizerPreviewModel model = new MetadataNormalizerPreviewModel(
                "test-fingerprint", "native-test", "v1", "test", "rules/default-rules.json", proposals);
            MetadataNormalizerPreviewWindow preview = new MetadataNormalizerPreviewWindow(null, item, model);
            DataGrid grid = FindDataGrids(preview).FirstOrDefault();
            if (grid == null || grid.Columns.Count != 7 ||
                grid.ItemsSource != model.Proposals)
                throw new InvalidOperationException("Normalizer-Vorschau hat keine reale gebundene Vorschlagstabelle.");

            Button reset = Descendants(preview).OfType<Button>().FirstOrDefault(
                delegate(Button b) { return String.Equals(b.Content as string, "Spalten zurücksetzen", StringComparison.Ordinal); });
            Button configure = Descendants(preview).OfType<Button>().FirstOrDefault(
                delegate(Button b) { return String.Equals(b.Content as string, "Spalten…", StringComparison.Ordinal); });
            if (reset == null || configure == null)
                throw new InvalidOperationException("Normalizer-Vorschau besitzt keine Spaltenaktionen.");

            MetadataNormalizerPreviewWindow.ResetPreviewColumns(grid);
            grid.Columns[0].DisplayIndex = 2;
            grid.Columns[1].Visibility = Visibility.Collapsed;
            grid.Columns[2].Width = new DataGridLength(170, DataGridLengthUnitType.Pixel);
            AppSettings transient = new AppSettings();
            GridLayoutSettings.Capture(transient, "normalizer.runtime.fixture", grid);
            MetadataNormalizerPreviewWindow.ResetPreviewColumns(grid);
            GridLayoutSettings.Apply(transient, "normalizer.runtime.fixture", grid);
            if (grid.Columns[0].DisplayIndex != 2 ||
                grid.Columns[1].Visibility != Visibility.Collapsed ||
                Math.Abs(grid.Columns[2].Width.Value - 170) > 0.1)
                throw new InvalidOperationException("Normalizer-Vorschau verlor ihre Tabellenlayout-Daten.");

            // Dispatch the real WPF button event, not a duplicate reset algorithm.
            reset.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            if (grid.Columns[0].DisplayIndex != 0 ||
                grid.Columns[1].Visibility != Visibility.Visible ||
                Math.Abs(grid.Columns[2].Width.Value - 65) > 0.1)
                throw new InvalidOperationException("Normalizer-Reset ist nicht mit dem echten Button verbunden.");
            return "normalizer preview production DataGrid + persisted columns + real reset button event";
        }

        private static bool ContainsTextBlock(DependencyObject root, string text)
        {
            foreach (DependencyObject item in Descendants(root))
            {
                TextBlock block = item as TextBlock;
                if (block != null && block.Text == text) return true;
            }
            return false;
        }

        private static bool ContainsTextBox(DependencyObject root, string text)
        {
            foreach (DependencyObject item in Descendants(root))
            {
                TextBox box = item as TextBox;
                if (box != null && box.Text == text && box.IsReadOnly) return true;
            }
            return false;
        }

        private static IEnumerable<DataGrid> FindDataGrids(DependencyObject root)
        {
            foreach (DependencyObject item in Descendants(root))
            {
                DataGrid grid = item as DataGrid;
                if (grid != null) yield return grid;
            }
        }

        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            if (root == null) yield break;
            Stack<DependencyObject> stack = new Stack<DependencyObject>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                DependencyObject current = stack.Pop();
                yield return current;
                IEnumerable children = LogicalTreeHelper.GetChildren(current);
                foreach (object child in children)
                {
                    DependencyObject dependency = child as DependencyObject;
                    if (dependency != null) stack.Push(dependency);
                }
            }
        }
    }
}
