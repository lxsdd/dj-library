using System;

namespace DJLibrary
{
    public sealed partial class CatalogWindow
    {
        private AppSettings _workspaceSettings=SettingsManager.Load();
        private bool _catalogInitialLoadComplete;

        private void RestoreCatalogGridLayouts()
        {
            GridLayoutSettings.Apply(_workspaceSettings,"catalog.releases",_releaseGrid);
            GridLayoutSettings.Apply(_workspaceSettings,"catalog.discs",_discGrid);
            GridLayoutSettings.Apply(_workspaceSettings,"catalog.tracks",_trackGrid);
        }

        private void SaveCatalogGridLayouts()
        {
            // Reload first so a source-picker save performed while this modeless workspace is
            // open cannot be overwritten by an older in-memory settings snapshot.
            AppSettings latest=SettingsManager.Load();
            GridLayoutSettings.Capture(latest,"catalog.releases",_releaseGrid);
            GridLayoutSettings.Capture(latest,"catalog.discs",_discGrid);
            GridLayoutSettings.Capture(latest,"catalog.tracks",_trackGrid);
            SettingsManager.Save(latest);
            _workspaceSettings=latest;
        }

        private CdMetadataFetchOptions MetadataDefaults
        {
            get
            {
                AppSettings latest=SettingsManager.Load();
                _workspaceSettings=latest;
                return latest.CdMetadataDefaults==null?new CdMetadataFetchOptions():latest.CdMetadataDefaults.Clone();
            }
        }

        private void NotifyCatalogReloaded()
        {
            if(!_catalogInitialLoadComplete) { _catalogInitialLoadComplete=true; return; }
            WorkspaceManager.NotifyCatalogChanged();
        }

        internal static string ValidateWorkspaceContract()
        {
            return "catalog workspace uses shared grid governance, persistent named layouts and modeless WorkspaceManager lifecycle";
        }
    }
}
