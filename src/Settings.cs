using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using System.Windows.Controls;
using System.Windows.Data;

namespace DJLibrary
{
    public sealed class AppSettings
    {
        public double WindowLeft = Double.NaN;
        public double WindowTop = Double.NaN;
        public double WindowWidth = 1380;
        public double WindowHeight = 850;
        public bool Maximized = false;
        public int SelectedTab = 0;
        public List<ColumnSetting> TrackColumns = new List<ColumnSetting>();
        public List<ColumnSetting> CdColumns = new List<ColumnSetting>();
        public List<SortSetting> TrackSorts = new List<SortSetting>();
        public List<SortSetting> CdSorts = new List<SortSetting>();
        public FilterState TrackFilter = new FilterState();
        public FilterState CdFilter = new FilterState();
        public string BridgeDirectory = "";
        public string LastManualBridgeDirectory = "";
        public List<string> KnownBridgeDirectories = new List<string>();
        public CdMetadataFetchOptions CdMetadataDefaults = new CdMetadataFetchOptions();
        public List<NamedGridLayout> GridLayouts = new List<NamedGridLayout>();
        public List<NamedWindowGeometry> WindowGeometries = new List<NamedWindowGeometry>();
    }

    public sealed class ColumnSetting
    {
        public string Key = "";
        public int DisplayIndex = 0;
        public double Width = 100;
        // Empty means a legacy pre-Candidate21 setting and is interpreted as Pixel.
        public string WidthUnit = "";
        public bool Visible = true;
    }

    public sealed class SortSetting
    {
        public string Property = "";
        public bool Descending = false;
    }

    public static class SettingsManager
    {
        public static string SettingsDirectory
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DJ Library");
            }
        }

        public static string SettingsPath
        {
            get { return Path.Combine(SettingsDirectory, "settings.xml"); }
        }

        public static AppSettings Load()
        {
            AppSettings loaded;
            if (TryLoad(SettingsPath, out loaded)) return loaded;
            string backup = SettingsPath + ".bak";
            if (TryLoad(backup, out loaded))
            {
                WriteDiagnostic("settings.xml was unreadable; settings.xml.bak was used.");
                return loaded;
            }
            if (File.Exists(SettingsPath)) WriteDiagnostic("settings.xml and its backup could not be read; safe defaults are used.");
            return new AppSettings();
        }

        public static void Update(Action<AppSettings> mutate)
        {
            if (mutate == null) throw new ArgumentNullException("mutate");
            AppSettings latest = Load();
            mutate(latest);
            Save(latest);
        }

        internal static void CopyMainOwnedState(AppSettings source, AppSettings target)
        {
            if (source == null || target == null) return;
            target.WindowLeft = source.WindowLeft;
            target.WindowTop = source.WindowTop;
            target.WindowWidth = source.WindowWidth;
            target.WindowHeight = source.WindowHeight;
            target.Maximized = source.Maximized;
            target.SelectedTab = source.SelectedTab;
            target.TrackColumns = source.TrackColumns ?? new List<ColumnSetting>();
            target.CdColumns = source.CdColumns ?? new List<ColumnSetting>();
            target.TrackSorts = source.TrackSorts ?? new List<SortSetting>();
            target.CdSorts = source.CdSorts ?? new List<SortSetting>();
            target.TrackFilter = source.TrackFilter ?? new FilterState();
            target.CdFilter = source.CdFilter ?? new FilterState();
            target.BridgeDirectory = source.BridgeDirectory ?? "";
            target.LastManualBridgeDirectory = source.LastManualBridgeDirectory ?? "";
            target.KnownBridgeDirectories = source.KnownBridgeDirectories ?? new List<string>();
            // Deliberately do NOT copy CdMetadataDefaults, GridLayouts or
            // WindowGeometries. Those are owned by other windows/workflows and
            // may have been written after MainWindow loaded its settings snapshot.
        }

        internal static string ValidateStaleSnapshotMergeContract()
        {
            AppSettings staleMain = new AppSettings();
            staleMain.WindowLeft = 44;
            staleMain.WindowTop = 55;
            staleMain.BridgeDirectory = @"C:\bridge";
            staleMain.WindowGeometries.Clear();

            AppSettings latest = new AppSettings();
            latest.WindowGeometries.Add(new NamedWindowGeometry
            {
                Key = "catalog.manager", Left = 321, Top = 222, Width = 1110, Height = 720
            });
            latest.CdMetadataDefaults.UseDiscogs = false;

            CopyMainOwnedState(staleMain, latest);
            NamedWindowGeometry geometry = latest.WindowGeometries.FirstOrDefault(x => x != null && x.Key == "catalog.manager");
            if (geometry == null || Math.Abs(geometry.Left - 321) > 0.1 || Math.Abs(geometry.Top - 222) > 0.1)
                throw new InvalidDataException("Stale MainWindow settings erased a window geometry written by another window.");
            if (latest.CdMetadataDefaults == null || latest.CdMetadataDefaults.UseDiscogs != false)
                throw new InvalidDataException("Stale MainWindow settings erased metadata defaults written by another workflow.");
            if (latest.WindowLeft != 44 || latest.WindowTop != 55 || latest.BridgeDirectory != @"C:\bridge")
                throw new InvalidDataException("MainWindow-owned settings were not merged into the latest settings snapshot.");
            return "stale-snapshot merge preserves window geometry and metadata defaults";
        }

        internal static AppSettings LoadForTesting(string primaryPath, string backupPath)
        {
            AppSettings loaded;
            if (TryLoad(primaryPath, out loaded)) return loaded;
            if (TryLoad(backupPath, out loaded)) return loaded;
            return new AppSettings();
        }

        private static bool TryLoad(string path, out AppSettings settings)
        {
            settings = null;
            try
            {
                if (!File.Exists(path)) return false;
                XmlSerializer xs = new XmlSerializer(typeof(AppSettings));
                using (FileStream fs = File.OpenRead(path)) settings = xs.Deserialize(fs) as AppSettings;
                return settings != null;
            }
            catch (Exception ex)
            {
                WriteDiagnostic("Settings could not be read (" + Path.GetFileName(path) + "): " + ex.Message);
                return false;
            }
        }

        private static void WriteDiagnostic(string message)
        {
            try
            {
                Directory.CreateDirectory(SettingsDirectory);
                File.AppendAllText(Path.Combine(SettingsDirectory, "diagnostics.log"), DateTime.Now.ToString("s") + " " + message + Environment.NewLine);
            }
            catch { }
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                Directory.CreateDirectory(SettingsDirectory);
                XmlSerializer xs = new XmlSerializer(typeof(AppSettings));
                string temp = SettingsPath + ".tmp";
                string backup = SettingsPath + ".bak";
                using (FileStream fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    xs.Serialize(fs, settings);
                    fs.Flush(true);
                }
                if (File.Exists(SettingsPath))
                {
                    File.Replace(temp, SettingsPath, backup, true);
                }
                else
                {
                    File.Move(temp, SettingsPath);
                }
            }
            catch (Exception ex)
            {
                WriteDiagnostic("Settings could not be saved: " + ex.Message);
            }
        }

        internal static DataGridLength RestoreColumnWidth(ColumnSetting setting)
        {
            if (setting == null) return new DataGridLength(100);
            string unit = setting.WidthUnit ?? "";
            if (String.Equals(unit, "Star", StringComparison.OrdinalIgnoreCase))
                return new DataGridLength(Math.Max(0.1, setting.Width), DataGridLengthUnitType.Star);
            if (String.Equals(unit, "Auto", StringComparison.OrdinalIgnoreCase)) return DataGridLength.Auto;
            if (String.Equals(unit, "SizeToCells", StringComparison.OrdinalIgnoreCase)) return DataGridLength.SizeToCells;
            if (String.Equals(unit, "SizeToHeader", StringComparison.OrdinalIgnoreCase)) return DataGridLength.SizeToHeader;
            // Backward compatibility: old layouts stored ActualWidth only.
            return new DataGridLength(Math.Max(30, setting.Width), DataGridLengthUnitType.Pixel);
        }

        internal static void CaptureColumnWidth(DataGridColumn column, ColumnSetting setting)
        {
            if (column == null || setting == null) return;
            DataGridLength width = column.Width;
            setting.WidthUnit = width.UnitType.ToString();
            if (width.UnitType == DataGridLengthUnitType.Pixel)
                setting.Width = Math.Max(30, column.ActualWidth > 0 ? column.ActualWidth : width.Value);
            else if (width.UnitType == DataGridLengthUnitType.Star)
                setting.Width = Math.Max(0.1, width.Value);
            else
                setting.Width = width.Value;
        }

        public static List<ColumnSetting> CaptureColumns(DataGrid grid)
        {
            List<ColumnSetting> result = new List<ColumnSetting>();
            foreach (DataGridColumn column in grid.Columns)
            {
                string key = column.SortMemberPath;
                if (String.IsNullOrEmpty(key)) continue;
                ColumnSetting s = new ColumnSetting();
                s.Key = key;
                s.DisplayIndex = column.DisplayIndex;
                CaptureColumnWidth(column, s);
                s.Visible = column.Visibility == System.Windows.Visibility.Visible;
                result.Add(s);
            }
            return result;
        }

        public static void ApplyColumns(DataGrid grid, List<ColumnSetting> settings)
        {
            if (settings == null || settings.Count == 0) return;

            Dictionary<string, ColumnSetting> map = new Dictionary<string, ColumnSetting>(StringComparer.OrdinalIgnoreCase);
            foreach (ColumnSetting s in settings)
            {
                if (!String.IsNullOrEmpty(s.Key)) map[s.Key] = s;
            }

            foreach (DataGridColumn c in grid.Columns)
            {
                ColumnSetting s;
                if (map.TryGetValue(c.SortMemberPath, out s))
                {
                    c.Width = RestoreColumnWidth(s);
                    c.Visibility = s.Visible ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
                }
            }

            List<DataGridColumn> ordered = new List<DataGridColumn>();
            foreach (DataGridColumn c in grid.Columns) ordered.Add(c);
            ordered.Sort(delegate(DataGridColumn a, DataGridColumn b)
            {
                ColumnSetting sa;
                ColumnSetting sb;
                int ia = map.TryGetValue(a.SortMemberPath, out sa) ? sa.DisplayIndex : a.DisplayIndex;
                int ib = map.TryGetValue(b.SortMemberPath, out sb) ? sb.DisplayIndex : b.DisplayIndex;
                return ia.CompareTo(ib);
            });

            int i;
            for (i = 0; i < ordered.Count; i++)
            {
                try { ordered[i].DisplayIndex = i; }
                catch { }
            }
        }

        public static List<SortSetting> CaptureSorts(ICollectionView view)
        {
            List<SortSetting> result = new List<SortSetting>();
            if (view == null) return result;
            foreach (SortDescription d in view.SortDescriptions)
            {
                SortSetting s = new SortSetting();
                s.Property = d.PropertyName;
                s.Descending = d.Direction == ListSortDirection.Descending;
                result.Add(s);
            }
            return result;
        }

        public static bool ApplySorts(ICollectionView view, List<SortSetting> settings)
        {
            if (view == null || settings == null || settings.Count == 0) return false;
            view.SortDescriptions.Clear();
            foreach (SortSetting s in settings)
            {
                if (String.IsNullOrEmpty(s.Property)) continue;
                view.SortDescriptions.Add(new SortDescription(
                    s.Property,
                    s.Descending ? ListSortDirection.Descending : ListSortDirection.Ascending));
            }
            return view.SortDescriptions.Count > 0;
        }
    }
}
