using System;

namespace DJLibrary
{
    internal static class BuildInfo
    {
        public const string ProductVersion = "0.4.0";
        public const string Stage = "";

        static BuildInfo()
        {
            CdMetadataNetwork.EnsureModernTls();

            string[] args = Environment.GetCommandLineArgs();
            bool selfTest = Array.Exists(args, delegate(string x)
            {
                return String.Equals(x, "--self-test", StringComparison.OrdinalIgnoreCase) ||
                       String.Equals(x, "--self-test-ci", StringComparison.OrdinalIgnoreCase);
            });

            if (selfTest)
            {
                DiscPlaybackRule.ValidateContract();
                DiscPlaybackRuleWindow.ValidateLayoutContract();
                ColumnsWindow.ValidateAlignmentContract();
                CatalogMultiDiscCompatibility.ValidateSelfTest(AppDomain.CurrentDomain.BaseDirectory);
                CdMetadataNetwork.RunSelfTest();
                CdMetadataPostProcessor.RunSelfTest();
                CdMetadataSelectionSession.RunSelfTest();
                CdMetadataPipeline.ValidatePreFetchContract();
                FieldSchema.ValidateContract();
                BridgeSnapshotReader.ValidateSchemaV3Contract();
                MetadataNormalizerAnalysis.ValidateSelfTest(AppDomain.CurrentDomain.BaseDirectory);
                MetadataNormalizerPreview.ValidateSelfTest(AppDomain.CurrentDomain.BaseDirectory);
                GridGovernance.ValidateContract();
                GridLayoutSettings.ValidateContract();
                WorkspaceManager.ValidateContract();
                UnifiedEditors.ValidateContract();
                CdCapturePreviewDialog.ValidateManualMetadataSelectionContract();
                CdMetadataChoiceDialog.ValidateMatrixContract();
                CatalogWindow.ValidateWorkspaceContract();
                MainWindow.ValidateWorkspaceCatalogContract();
            }
            else CatalogMultiDiscCompatibility.PrepareOwnedCatalog();
        }

        public static string DisplayVersion
        {
            get { return String.IsNullOrWhiteSpace(Stage) ? "v" + ProductVersion : "v" + ProductVersion + " " + Stage; }
        }
        public static string WindowTitle { get { return "DJ Library — Native " + DisplayVersion; } }
    }
}
