#!/usr/bin/env python3
import argparse
import csv
import gzip
import hashlib
import json
import os
import sqlite3
from collections import defaultdict

DISC_CORE = {
    "DiscId","ReleaseId","LegacyAlbumId","DiscNumber","Medium","Toc",
    "TocComplete","CdTextPresent","DurationSeconds"
}
TRACK_CORE = {
    "TrackId","DiscId","Artist","Title","Version","Date","Genre","Bpm",
    "DurationSeconds","TrackNumber","LegacyArtistRaw","LegacyGenre"
}

def read_tsv_gz(path):
    with gzip.open(path, "rt", encoding="utf-8-sig", newline="") as handle:
        return list(csv.DictReader(handle, delimiter="\t"))

def i(value, default=0):
    value = (value or "").strip()
    return default if value == "" else int(value)

def f(value, default=0.0):
    value = (value or "").strip()
    return default if value == "" else float(value)

def b(value):
    value = (value or "").strip().lower()
    return 1 if value in ("1","true","yes") else 0

def nz(value):
    return value or ""

def compact_json(row, header, excluded):
    payload = {}
    for key in header:
        if key not in excluded:
            payload[key] = nz(row.get(key))
    return json.dumps(payload, ensure_ascii=False, separators=(",",":"))

def first_nonempty(rows, field):
    for row in rows:
        value = nz(row.get(field))
        if value != "":
            return value
    return ""

def declared_total_discs(rows):
    declared = 1
    for row in rows:
        declared = max(declared, i(row.get("DiscNumber"), 1), i(row.get("TotalDiscs"), 1))
    return declared

def create_schema(db):
    db.executescript("""
PRAGMA foreign_keys=ON;
CREATE TABLE meta(key TEXT PRIMARY KEY,value TEXT NOT NULL);
CREATE TABLE release(
 id INTEGER PRIMARY KEY,
 album_artist TEXT NOT NULL DEFAULT '',
 album TEXT NOT NULL DEFAULT '',
 release_date TEXT NOT NULL DEFAULT '',
 genre TEXT NOT NULL DEFAULT '',
 label TEXT NOT NULL DEFAULT '',
 catalog TEXT NOT NULL DEFAULT '',
 country TEXT NOT NULL DEFAULT '',
 total_discs INTEGER NOT NULL DEFAULT 1 CHECK(total_discs>0)
);
CREATE TABLE disc(
 id INTEGER PRIMARY KEY,
 release_id INTEGER NOT NULL REFERENCES release(id) ON DELETE CASCADE,
 legacy_album_id INTEGER,
 disc_number INTEGER NOT NULL DEFAULT 1 CHECK(disc_number>0),
 medium TEXT NOT NULL DEFAULT '',
 toc TEXT NOT NULL DEFAULT '',
 toc_complete INTEGER NOT NULL DEFAULT 0 CHECK(toc_complete IN(0,1)),
 cd_text_present INTEGER NOT NULL DEFAULT 0 CHECK(cd_text_present IN(0,1)),
 duration_seconds REAL NOT NULL DEFAULT 0 CHECK(duration_seconds>=0),
 legacy_json TEXT NOT NULL DEFAULT '{}'
);
CREATE TABLE track(
 id INTEGER PRIMARY KEY,
 disc_id INTEGER NOT NULL REFERENCES disc(id) ON DELETE CASCADE,
 position INTEGER NOT NULL CHECK(position>0),
 artist TEXT NOT NULL DEFAULT '',
 title TEXT NOT NULL DEFAULT '',
 version TEXT NOT NULL DEFAULT '',
 release_date TEXT NOT NULL DEFAULT '',
 genre TEXT NOT NULL DEFAULT '',
 legacy_genre TEXT NOT NULL DEFAULT '',
 bpm REAL NOT NULL DEFAULT 0 CHECK(bpm>=0),
 duration_seconds REAL NOT NULL DEFAULT 0 CHECK(duration_seconds>=0),
 legacy_artist_raw TEXT NOT NULL DEFAULT '',
 legacy_json TEXT NOT NULL DEFAULT '{}',
 UNIQUE(disc_id,position)
);
CREATE TABLE change_log(
 id INTEGER PRIMARY KEY AUTOINCREMENT,
 changed_utc TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
 entity TEXT NOT NULL,
 entity_id INTEGER,
 operation TEXT NOT NULL,
 before_json TEXT,
 after_json TEXT
);
CREATE INDEX ix_disc_release ON disc(release_id);
CREATE INDEX ix_track_artist_title ON track(artist,title);
""")
    db.executemany("INSERT INTO meta(key,value) VALUES(?,?)", [
        ("schema_version","1"),
        ("product_target","0.4"),
        ("legacy_source_policy","read-only"),
    ])

def migrate(tracks_path, cds_path, output):
    tracks = read_tsv_gz(tracks_path)
    cds = read_tsv_gz(cds_path)
    if len(tracks) != 15440:
        raise RuntimeError("expected 15440 tracks, got %d" % len(tracks))
    if len(cds) != 1315:
        raise RuntimeError("expected 1315 CDs, got %d" % len(cds))
    track_header = list(tracks[0].keys())
    disc_header = list(cds[0].keys())

    releases = defaultdict(list)
    for row in cds:
        releases[i(row["ReleaseId"])].append(row)
    if len(releases) != 1119:
        raise RuntimeError("expected 1119 releases, got %d" % len(releases))

    if os.path.exists(output):
        os.remove(output)
    db = sqlite3.connect(output)
    try:
        create_schema(db)
        with db:
            for rid in sorted(releases):
                rows = releases[rid]
                total_discs = declared_total_discs(rows)
                db.execute(
                    "INSERT INTO release(id,album_artist,album,release_date,genre,label,catalog,country,total_discs) VALUES(?,?,?,?,?,?,?,?,?)",
                    (
                        rid,
                        first_nonempty(rows,"AlbumArtist"),
                        first_nonempty(rows,"Album"),
                        first_nonempty(rows,"Date"),
                        first_nonempty(rows,"LegacyGenre"),
                        first_nonempty(rows,"Label"),
                        first_nonempty(rows,"Catalog"),
                        first_nonempty(rows,"Country"),
                        total_discs,
                    ),
                )

            for row in sorted(cds, key=lambda x: i(x["DiscId"])):
                legacy_album = nz(row["LegacyAlbumId"]).strip()
                db.execute(
                    "INSERT INTO disc(id,release_id,legacy_album_id,disc_number,medium,toc,toc_complete,cd_text_present,duration_seconds,legacy_json) VALUES(?,?,?,?,?,?,?,?,?,?)",
                    (
                        i(row["DiscId"]), i(row["ReleaseId"]),
                        None if legacy_album == "" else int(legacy_album),
                        i(row["DiscNumber"],1), nz(row["Medium"]), nz(row["Toc"]),
                        b(row["TocComplete"]), b(row["CdTextPresent"]),
                        f(row["DurationSeconds"]),
                        compact_json(row, disc_header, DISC_CORE),
                    ),
                )

            for row in sorted(tracks, key=lambda x: i(x["TrackId"])):
                db.execute(
                    "INSERT INTO track(id,disc_id,position,artist,title,version,release_date,genre,legacy_genre,bpm,duration_seconds,legacy_artist_raw,legacy_json) VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?)",
                    (
                        i(row["TrackId"]), i(row["DiscId"]), i(row["TrackNumber"],1),
                        nz(row["Artist"]), nz(row["Title"]), nz(row["Version"]),
                        nz(row["Date"]), nz(row["Genre"]), nz(row["LegacyGenre"]),
                        f(row["Bpm"]), f(row["DurationSeconds"]), nz(row["LegacyArtistRaw"]),
                        compact_json(row, track_header, TRACK_CORE),
                    ),
                )
        quick = db.execute("PRAGMA quick_check").fetchone()[0]
        if quick != "ok":
            raise RuntimeError("quick_check failed: %s" % quick)
        fk = list(db.execute("PRAGMA foreign_key_check"))
        if fk:
            raise RuntimeError("foreign_key_check returned %d rows" % len(fk))
    finally:
        db.close()

def reconcile_release_totals(path):
    db = sqlite3.connect(path)
    changed = 0
    try:
        with db:
            releases = list(db.execute("SELECT id,total_discs FROM release ORDER BY id"))
            for rid, current in releases:
                desired = max(1, int(current or 1))
                max_disc = db.execute("SELECT MAX(disc_number) FROM disc WHERE release_id=?", (rid,)).fetchone()[0] or 0
                desired = max(desired, int(max_disc))
                for (legacy_json,) in db.execute("SELECT legacy_json FROM disc WHERE release_id=?", (rid,)):
                    try:
                        raw = json.loads(legacy_json or "{}")
                        desired = max(desired, int(raw.get("TotalDiscs") or 0))
                    except (TypeError, ValueError, json.JSONDecodeError):
                        pass
                if desired > int(current or 1):
                    db.execute("UPDATE release SET total_discs=? WHERE id=?", (desired, rid))
                    changed += 1
        quick = db.execute("PRAGMA quick_check").fetchone()[0]
        if quick != "ok":
            raise RuntimeError("reconciled quick_check failed: %s" % quick)
        fk = list(db.execute("PRAGMA foreign_key_check"))
        if fk:
            raise RuntimeError("reconciled foreign_key_check returned %d rows" % len(fk))
        return changed
    finally:
        db.close()

def canonical_digest(path):
    db = sqlite3.connect(path)
    db.row_factory = sqlite3.Row
    h = hashlib.sha256()
    try:
        for table, order in [
            ("meta","key"),
            ("release","id"),
            ("disc","id"),
            ("track","id"),
        ]:
            cols = [x[1] for x in db.execute("PRAGMA table_info(%s)" % table)]
            h.update((table+"\n").encode("utf-8"))
            for row in db.execute("SELECT * FROM %s ORDER BY %s" % (table,order)):
                values = []
                for col in cols:
                    value = row[col]
                    if isinstance(value, float):
                        value = format(value, ".17g")
                    values.append(value)
                h.update(json.dumps(values, ensure_ascii=False, separators=(",",":"), default=str).encode("utf-8"))
                h.update(b"\n")
        return h.hexdigest()
    finally:
        db.close()

def decompress_gzip(source, destination):
    with gzip.open(source,"rb") as src, open(destination,"wb") as dst:
        while True:
            block = src.read(1024*1024)
            if not block:
                break
            dst.write(block)

def counts(path):
    db=sqlite3.connect(path)
    try:
        return tuple(db.execute("SELECT (SELECT COUNT(*) FROM release),(SELECT COUNT(*) FROM disc),(SELECT COUNT(*) FROM track)").fetchone())
    finally:
        db.close()

def release_size_distribution(path):
    db=sqlite3.connect(path)
    try:
        return dict(db.execute("SELECT total_discs,COUNT(*) FROM release GROUP BY total_discs ORDER BY total_discs"))
    finally:
        db.close()

def main():
    parser=argparse.ArgumentParser(description="Rebuild DJ Library v0.4 schema-v1 seed from immutable RC13 TSV fixtures.")
    parser.add_argument("--tracks",required=True)
    parser.add_argument("--cds",required=True)
    parser.add_argument("--output",required=True)
    parser.add_argument("--compare-gzip")
    args=parser.parse_args()

    migrate(args.tracks,args.cds,args.output)
    rebuilt_digest=canonical_digest(args.output)
    rebuilt_counts=counts(args.output)
    distribution=release_size_distribution(args.output)
    expected_distribution={1:839,2:221,3:45,4:8,5:5,8:1}
    if distribution != expected_distribution:
        raise RuntimeError("unexpected release total_discs distribution: %r" % distribution)
    print("rebuilt counts=%d/%d/%d digest=%s multidisc=%s" % (rebuilt_counts[0],rebuilt_counts[1],rebuilt_counts[2],rebuilt_digest,distribution))

    if args.compare_gzip:
        reference=args.output+".reference.sqlite"
        try:
            decompress_gzip(args.compare_gzip,reference)
            corrected = reconcile_release_totals(reference)
            if corrected not in (0,69):
                raise RuntimeError("unexpected reference multi-disc reconciliation count: %d" % corrected)
            ref_counts=counts(reference)
            ref_digest=canonical_digest(reference)
            ref_distribution=release_size_distribution(reference)
            print("reference counts=%d/%d/%d digest=%s multidisc=%s corrected_release_totals=%d" % (ref_counts[0],ref_counts[1],ref_counts[2],ref_digest,ref_distribution,corrected))
            if rebuilt_counts != ref_counts:
                raise RuntimeError("catalog counts differ")
            if distribution != ref_distribution:
                raise RuntimeError("multi-disc distribution differs")
            if rebuilt_digest != ref_digest:
                raise RuntimeError("semantic catalog digest differs")
            print("PASS: lossless deterministic catalog migration matches qualified seed after safe multi-disc reconciliation")
        finally:
            if os.path.exists(reference):
                os.remove(reference)

if __name__=="__main__":
    main()
