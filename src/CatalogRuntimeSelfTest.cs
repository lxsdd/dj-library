using System;
using System.IO;
using System.IO.Compression;

namespace DJLibrary
{
    internal static class CatalogRuntimeSelfTest
    {
        public static string Run(string baseDir)
        {
            string temp=Path.Combine(Path.GetTempPath(),"DJLibrary-runtime-source-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                string seed=Path.Combine(baseDir,"data","catalog-seed-v1.sqlite.gz");
                string database=Path.Combine(temp,"catalog.sqlite");
                using(FileStream input=File.OpenRead(seed))
                using(GZipStream gzip=new GZipStream(input,CompressionMode.Decompress))
                using(FileStream output=new FileStream(database,FileMode.CreateNew,FileAccess.Write,FileShare.None)) gzip.CopyTo(output);
                using(CatalogService catalog=CatalogService.OpenForTesting(database))
                {
                    string projection=CatalogService.ValidateNativeProjectionContract(catalog);
                    string runtime=DataStore.ValidateCatalogSourceContract(catalog,Path.Combine(baseDir,"data","matches.tsv.gz"));
                    return projection+" + "+runtime;
                }
            }
            finally { try { Directory.Delete(temp,true); } catch { } }
        }
    }
}
