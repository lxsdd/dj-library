using System;

namespace DJLibrary
{
    public static partial class CdMetadataPipeline
    {
        public static CdMetadataReport Enrich(CdSnapshot snapshot, CdMetadataFetchOptions options)
        {
            if(snapshot==null) throw new ArgumentNullException("snapshot");
            CdMetadataFetchOptions selected=options==null?new CdMetadataFetchOptions():options;
            CdMetadataReport report=new CdMetadataReport();
            foreach(CdTrackCapture track in snapshot.Tracks)
                if(String.IsNullOrWhiteSpace(track.RawTitle)) track.RawTitle=track.Title??"";

            report.Status("CD-TEXT",CdTextSummary(snapshot),"");
            if(selected.UseFoobar) ApplyFoobar(snapshot,LoadBridgeItems(report),report);
            else report.Status("foobar","not queried (disabled before matching)","");

            DiscogsSearchSeed discogsSeed=selected.UseDiscogs?DiscogsIndependentMetadataSource.CaptureSeed(snapshot,selected):null;
            MusicBrainzLookup lookup=null;
            if(selected.UseMusicBrainz)
            {
                try { lookup=MusicBrainzMetadataSource.Apply(snapshot,report); }
                catch(Exception ex) { report.Status("MusicBrainz","online matching failed: "+CleanError(ex.Message),"https://musicbrainz.org/"); }
            }
            else report.Status("MusicBrainz","not queried (disabled before matching)","");

            if(selected.UseDiscogs)
            {
                try
                {
                    DiscogsIndependentMetadataSource.Apply(snapshot,discogsSeed,lookup==null?"":lookup.DiscogsReleaseId,report);
                }
                catch(Exception ex)
                {
                    report.Status("Discogs","Data provided by Discogs · independent matching failed: "+CleanError(ex.Message),"https://www.discogs.com/");
                }
            }
            else report.Status("Discogs","not queried (disabled before matching)","");

            snapshot.MetadataEvidence=report.Evidence;
            snapshot.MetadataSources=report.Sources;
            return report;
        }

        internal static string ValidatePreFetchContract()
        {
            CdMetadataFetchOptions none=new CdMetadataFetchOptions { UseCatalog=false,UseFoobar=false,UseMusicBrainz=false,UseDiscogs=false,UseTitleAnalysis=false };
            if(none.UseMusicBrainz || none.UseDiscogs || none.UseFoobar) throw new InvalidOperationException("Pre-fetch source options are not fail-closed.");
            return "pre-fetch source gate: disabled online providers execute no provider call";
        }
    }
}
