using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DJLibrary
{
    public sealed partial class CatalogService
    {
        public bool HasUserChanges
        {
            get
            {
                long changeLog = ToLong(_db.Scalar("SELECT COUNT(*) FROM change_log"));
                long undo = ToLong(_db.Scalar("SELECT COUNT(*) FROM undo_entry"));
                return changeLog > 0 || undo > 0;
            }
        }

        public List<CatalogNativeDisc> GetNativeDiscs()
        {
            string sql =
                "SELECT d.id,d.release_id,d.legacy_album_id,d.disc_number,d.medium,d.toc,d.toc_complete,d.cd_text_present,d.duration_seconds,d.legacy_json," +
                "r.album_artist,r.album,r.release_date,r.genre,r.label,r.catalog,r.country,r.total_discs,COUNT(t.id) AS track_count " +
                "FROM disc d JOIN release r ON r.id=d.release_id LEFT JOIN track t ON t.disc_id=d.id " +
                "GROUP BY d.id,d.release_id,d.legacy_album_id,d.disc_number,d.medium,d.toc,d.toc_complete,d.cd_text_present,d.duration_seconds,d.legacy_json," +
                "r.album_artist,r.album,r.release_date,r.genre,r.label,r.catalog,r.country,r.total_discs " +
                "ORDER BY r.album_artist COLLATE NOCASE,r.album COLLATE NOCASE,d.disc_number,d.id";
            return _db.Query(sql).Select(delegate(Dictionary<string, object> row)
            {
                CatalogNativeDisc x = new CatalogNativeDisc();
                x.Id=ToLong(row["id"]); x.ReleaseId=ToLong(row["release_id"]); x.LegacyAlbumId=ToLong(row["legacy_album_id"]);
                x.AlbumArtist=ToStringValue(row["album_artist"]); x.Album=ToStringValue(row["album"]); x.DiscNumber=(int)ToLong(row["disc_number"]);
                x.TotalDiscs=(int)ToLong(row["total_discs"]); x.TrackCount=(int)ToLong(row["track_count"]); x.DurationSeconds=ToDouble(row["duration_seconds"]);
                x.ReleaseDate=ToStringValue(row["release_date"]); x.Genre=ToStringValue(row["genre"]); x.Label=ToStringValue(row["label"]);
                x.Catalog=ToStringValue(row["catalog"]); x.Country=ToStringValue(row["country"]); x.Medium=ToStringValue(row["medium"]);
                x.Toc=ToStringValue(row["toc"]); x.TocComplete=ToLong(row["toc_complete"])!=0; x.CdTextPresent=ToLong(row["cd_text_present"])!=0;
                x.LegacyJson=ToStringValue(row["legacy_json"]); return x;
            }).ToList();
        }

        public List<CatalogNativeTrack> GetNativeTracks()
        {
            string sql =
                "SELECT t.id,t.disc_id,t.position,t.artist,t.title,t.version,t.release_date,t.genre,t.legacy_genre,t.bpm,t.duration_seconds,t.legacy_artist_raw,t.legacy_json," +
                "d.disc_number,d.medium,d.duration_seconds AS disc_duration,d.toc,d.toc_complete," +
                "r.album,r.album_artist,r.label,r.catalog,r.total_discs " +
                "FROM track t JOIN disc d ON d.id=t.disc_id JOIN release r ON r.id=d.release_id " +
                "ORDER BY t.id";
            return _db.Query(sql).Select(delegate(Dictionary<string, object> row)
            {
                CatalogNativeTrack x = new CatalogNativeTrack();
                x.Id=ToLong(row["id"]); x.DiscId=ToLong(row["disc_id"]); x.Position=(int)ToLong(row["position"]);
                x.Artist=ToStringValue(row["artist"]); x.Title=ToStringValue(row["title"]); x.Version=ToStringValue(row["version"]);
                x.ReleaseDate=ToStringValue(row["release_date"]); x.Genre=ToStringValue(row["genre"]); x.LegacyGenre=ToStringValue(row["legacy_genre"]);
                x.Bpm=ToDouble(row["bpm"]); x.DurationSeconds=ToDouble(row["duration_seconds"]); x.LegacyArtistRaw=ToStringValue(row["legacy_artist_raw"]);
                x.LegacyJson=ToStringValue(row["legacy_json"]); x.Album=ToStringValue(row["album"]); x.AlbumArtist=ToStringValue(row["album_artist"]);
                x.Label=ToStringValue(row["label"]); x.Catalog=ToStringValue(row["catalog"]); x.Medium=ToStringValue(row["medium"]);
                x.DiscNumber=(int)ToLong(row["disc_number"]); x.TotalDiscs=(int)ToLong(row["total_discs"]); x.DiscDurationSeconds=ToDouble(row["disc_duration"]);
                x.DiscToc=ToStringValue(row["toc"]); x.DiscTocComplete=ToLong(row["toc_complete"])!=0; return x;
            }).ToList();
        }

        internal static string ValidateNativeProjectionContract(CatalogService catalog)
        {
            if (catalog == null) throw new ArgumentNullException("catalog");
            List<CatalogNativeDisc> discs = catalog.GetNativeDiscs();
            List<CatalogNativeTrack> tracks = catalog.GetNativeTracks();
            CatalogCounts counts = catalog.GetCounts();
            if (discs.Count != counts.Discs || tracks.Count != counts.Tracks)
                throw new InvalidOperationException("SQLite-Native-Projektion verliert physische Datensätze.");
            if (discs.Any(x => x.Id <= 0 || x.ReleaseId <= 0) || tracks.Any(x => x.Id <= 0 || x.DiscId <= 0 || x.Position <= 0))
                throw new InvalidOperationException("SQLite-Native-Projektion enthält invalide stabile Identitäten.");
            return "SQLite single-source native projection " + counts.Releases.ToString(CultureInfo.InvariantCulture) + "/" + counts.Discs.ToString(CultureInfo.InvariantCulture) + "/" + counts.Tracks.ToString(CultureInfo.InvariantCulture);
        }
    }
}
