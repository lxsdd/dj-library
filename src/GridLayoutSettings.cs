using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Data;

namespace DJLibrary
{
    public sealed class NamedGridLayout
    {
        public string Key = "";
        public List<ColumnSetting> Columns = new List<ColumnSetting>();
        public List<SortSetting> Sorts = new List<SortSetting>();
    }

    internal static class GridLayoutSettings
    {
        public static NamedGridLayout Find(AppSettings settings, string key)
        {
            if (settings == null || settings.GridLayouts == null || String.IsNullOrWhiteSpace(key)) return null;
            return settings.GridLayouts.FirstOrDefault(x => String.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
        }

        public static void Apply(AppSettings settings, string key, DataGrid grid)
        {
            NamedGridLayout layout = Find(settings,key);
            if(layout==null) return;
            SettingsManager.ApplyColumns(grid,layout.Columns);
            ICollectionView view=CollectionViewSource.GetDefaultView(grid.ItemsSource);
            if(view!=null) SettingsManager.ApplySorts(view,layout.Sorts);
        }

        public static void Capture(AppSettings settings, string key, DataGrid grid)
        {
            if(settings==null || grid==null || String.IsNullOrWhiteSpace(key)) return;
            if(settings.GridLayouts==null) settings.GridLayouts=new List<NamedGridLayout>();
            NamedGridLayout layout=Find(settings,key);
            if(layout==null) { layout=new NamedGridLayout { Key=key }; settings.GridLayouts.Add(layout); }
            layout.Columns=SettingsManager.CaptureColumns(grid);
            ICollectionView view=CollectionViewSource.GetDefaultView(grid.ItemsSource);
            layout.Sorts=SettingsManager.CaptureSorts(view);
        }

        internal static string ValidateContract()
        {
            AppSettings settings=new AppSettings();
            DataGrid grid=UiHelpers.CreateReadOnlyGrid();
            DataGridTextColumn artist=new DataGridTextColumn { Header="Artist",SortMemberPath="Artist",Width=new DataGridLength(200,DataGridLengthUnitType.Pixel) };
            DataGridTextColumn title=new DataGridTextColumn { Header="Title",SortMemberPath="Title",Width=new DataGridLength(2,DataGridLengthUnitType.Star) };
            grid.Columns.Add(artist); grid.Columns.Add(title);
            artist.DisplayIndex=1; title.DisplayIndex=0;
            Capture(settings,"selftest",grid);
            artist.DisplayIndex=0; title.DisplayIndex=1; title.Width=new DataGridLength(333,DataGridLengthUnitType.Pixel);
            Apply(settings,"selftest",grid);
            if(title.DisplayIndex!=0 || artist.DisplayIndex!=1) throw new InvalidOperationException("Persistent grid order was not restored.");
            if(title.Width.UnitType!=DataGridLengthUnitType.Star || Math.Abs(title.Width.Value-2)>0.001) throw new InvalidOperationException("Persistent grid layout lost Star sizing semantics.");
            NamedGridLayout saved=Find(settings,"selftest");
            ColumnSetting savedTitle=saved==null?null:saved.Columns.FirstOrDefault(x=>x.Key=="Title");
            if(savedTitle==null || !String.Equals(savedTitle.WidthUnit,"Star",StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Persistent grid layout did not serialize the width unit.");
            return "named persistent grid layouts + lossless DataGridLength units";
        }
    }
}
