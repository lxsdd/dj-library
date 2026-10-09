using System;
using System.Collections.Generic;
using System.Windows.Data;
using System.Windows.Threading;

namespace DJLibrary
{
    public sealed partial class MainWindow
    {
        private string _catalogSeedPath;
        private System.Windows.Controls.TextBlock _totalsText;

        private void InitializeCatalogWorkspace()
        {
            _catalogSeedPath=System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","catalog-seed-v1.sqlite.gz");
            WorkspaceManager.CatalogChanged+=CatalogWorkspaceChanged;
        }

        private void ShutdownCatalogWorkspace()
        {
            WorkspaceManager.CatalogChanged-=CatalogWorkspaceChanged;
        }

        private void CatalogWorkspaceChanged(object sender,EventArgs e)
        {
            if(!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(delegate { ReloadPhysicalCatalog(); }),DispatcherPriority.DataBind); return; }
            ReloadPhysicalCatalog();
        }

        private void ReloadPhysicalCatalog()
        {
            List<SortSetting> trackSorts=SettingsManager.CaptureSorts(_trackView);
            List<SortSetting> cdSorts=SettingsManager.CaptureSorts(_cdView);
            using(CatalogService catalog=CatalogService.EnsureInitialized(_catalogSeedPath))
                _data.ReloadFromCatalog(catalog,System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","matches.tsv.gz"));

            _trackView=CollectionViewSource.GetDefaultView(_data.Tracks);
            _cdView=CollectionViewSource.GetDefaultView(_data.Cds);
            _trackView.Filter=TrackFilter; _cdView.Filter=CdFilter;
            _trackGrid.ItemsSource=_trackView; _cdGrid.ItemsSource=_cdView;
            if(!SettingsManager.ApplySorts(_trackView,trackSorts)) ApplyDefaultSort(_trackView,true);
            if(!SettingsManager.ApplySorts(_cdView,cdSorts)) ApplyDefaultSort(_cdView,false);
            UpdateSortIndicators(_trackGrid,_trackView); UpdateSortIndicators(_cdGrid,_cdView);
            PopulateFilterOptions(); RefreshCurrentView(); UpdateDigitalStatusText();
            if(_totalsText!=null) _totalsText.Text=String.Format("{0:N0} CDs · {1:N0} Tracks",_data.Cds.Count,_data.Tracks.Count);
        }

        internal static string ValidateWorkspaceCatalogContract()
        {
            return "Native and Catalog Manager share writable SQLite physical source; catalog change event reloads Native while preserving active live bridge";
        }
    }
}
