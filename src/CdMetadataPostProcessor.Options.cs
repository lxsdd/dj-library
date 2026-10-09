using System;
using System.Collections.Generic;
using System.Globalization;

namespace DJLibrary
{
    internal static partial class CdMetadataPostProcessor
    {
        public static string Augment(CdSnapshot snapshot, IList<CatalogTocMatch> duplicates, CdMetadataReport report, CdMetadataFetchOptions options)
        {
            if(snapshot==null) throw new ArgumentNullException("snapshot");
            if(report==null) throw new ArgumentNullException("report");
            CdMetadataFetchOptions selected=options==null?new CdMetadataFetchOptions():options;

            AddCdTextEvidence(snapshot,report);
            if(selected.UseCatalog) AddCatalogEvidence(duplicates,report);
            else report.Status("Catalog","not used for this matching run","");
            if(selected.UseFoobar) AddFoobarEvidence(snapshot,report);
            if(selected.UseTitleAnalysis)
            {
                int parsed=AddSafeVersionEvidence(snapshot,report);
                report.Status("Title Analysis",parsed>0
                    ? parsed.ToString(CultureInfo.CurrentCulture)+" conservative Mix/Version suggestions from trailing version parentheses"
                    : "no additional safe Mix/Version suggestions","");
            }
            else report.Status("Title Analysis","not used for this matching run","");

            snapshot.MetadataEvidence=report.Evidence;
            snapshot.MetadataSources=report.Sources;
            return "physical CD-TEXT + preselected metadata evidence providers";
        }
    }
}
