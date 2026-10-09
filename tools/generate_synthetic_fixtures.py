#!/usr/bin/env python3
"""Generate exclusively synthetic, reproducible DJ Library regression fixtures.

No repository/user music files are read. The generated dataset is NOT a
collection for new users and MUST NEVER be installed as a default catalog.
"""
import argparse
import csv
import gzip
import hashlib
import io
import tempfile
from pathlib import Path

from migrate_catalog import migrate, counts, release_size_distribution

TRACK_COLS = (
    "TrackId DiscId Artist Title Version DisplayTitle Date Genre Style Bpm "
    "DurationSeconds TrackNumber LegacyTrackNumber LegacyArtistRaw Album AlbumArtist "
    "Label Catalog Medium DiscNumber TotalDiscs DiscTrackCount DiscDurationSeconds "
    "DiscDurationSource DiscLayout IssueCode IssueDetail DigitalLevel DigitalCount "
    "StrongCount LikelyCount CandidateCount LegacyGenre GenreSource GenreConfidence "
    "GenreEvidenceCount"
).split()
CD_COLS = (
    "DiscId ReleaseId LegacyAlbumId AlbumArtist Album DiscNumber TotalDiscs Tracks "
    "DurationSeconds DurationSource Date Genre Label Catalog Country Medium Toc "
    "TocComplete CdTextPresent LegacySerial LegacyPackaging LegacyReleaseType "
    "LayoutKind PhysicalTrackCount LogicalTrackCount IssueCode IssueDetail "
    "StrongTracks LikelyTracks CandidateTracks NoMatchTracks LegacyGenre GenreSource "
    "DigitalGenres GenreCoverage GenreDominance ProjectedGenreTrackCount"
).split()
MATCH_COLS = (
    "TrackId DigitalItemId Level Method Confidence Artist Title OriginalTitle "
    "RemixedBy Album AlbumArtist DurationSeconds Bpm Label Catalog Codec Bitrate "
    "Path Subsong Genre Style"
).split()
assert len(TRACK_COLS) == 36 and len(CD_COLS) == 37 and len(MATCH_COLS) == 21

def write_tsv(path, headers, rows):
    with open(path, "wb") as raw:
        with gzip.GzipFile(filename="", mode="wb", fileobj=raw, mtime=0, compresslevel=9) as zipped:
            with io.TextIOWrapper(zipped, encoding="utf-8", newline="") as out:
                writer = csv.DictWriter(out, fieldnames=headers, delimiter="\t",
                                        lineterminator="\n", extrasaction="raise")
                writer.writeheader()
                for row in rows:
                    writer.writerow({key: row.get(key, "") for key in headers})

def gzip_database(path, dest):
    with open(path, "rb") as src, open(dest, "wb") as raw:
        with gzip.GzipFile(filename="", mode="wb", fileobj=raw, mtime=0, compresslevel=9) as out:
            while True:
                chunk = src.read(1024 * 1024)
                if not chunk:
                    break
                out.write(chunk)

def fixture_rows():
    # 1,119 releases with the same multi-disc stress distribution as before.
    totals = [1] * 839 + [2] * 221 + [3] * 45 + [4] * 8 + [5] * 5 + [8]
    assert len(totals) == 1119
    disc_to_release = {disc_id: (disc_id, 1) for disc_id in range(1, 1120)}
    for disc_id in range(1120, 1316):
        release_id = 840 + disc_id - 1120
        disc_to_release[disc_id] = (release_id, 2)

    # 38 ordinary incompatible discs plus boundary disc 310. Two unknown.
    incompatible = set(range(10, 49)) - {41}
    incompatible.add(310)
    unknown = {41, 1104}
    assert len(incompatible) == 39
    marked = set(sorted(incompatible)[:21]) | {310}
    assert len(marked) == 22

    track_rows = []
    cd_rows = []
    match_rows = []
    track_id = 0
    item_id = 0
    for disc_id in range(1, 1316):
        release_id, disc_number = disc_to_release[disc_id]
        declared = totals[release_id - 1]
        album = ("Fixture Boundary #310" if disc_id == 310 else
                 "Fixture Album %04d" % release_id)
        # Legacy marker exists only for a generated incompatible single-disc release.
        if disc_id in marked:
            album += "°"
        artist = "Fixture Artist %03d" % (release_id % 75)
        medium = "Double CD" if 1061 <= release_id <= 1117 else "CD"
        genre = ["House", "Downtempo", "Techno", "Disco"][disc_id % 4]
        ntracks = 12 if disc_id <= 975 else 11
        if disc_id == 41:
            toc, complete = "150 360150 2965", "1"  # non-monotonic
        elif disc_id == 1104:
            toc, complete = "150 360150", "0"  # incomplete
        elif disc_id == 310:
            toc, complete = "150 361175", "1"  # exactly 361,025 physical frames
        elif disc_id in incompatible:
            toc, complete = "150 360150", "1"
        else:
            toc, complete = "150 %d" % (300150 + disc_id), "1"
        if disc_id in incompatible:
            assert complete == "1"
        statuses = {"strong": 0, "likely": 0, "candidate": 0, "none": 0}
        for pos in range(1, ntracks + 1):
            track_id += 1
            version = "Extended Mix" if pos == 1 else ""
            title = "Fixture Track %05d" % track_id
            display = title + (" (" + version + ")" if version else "")
            level = ("strong" if track_id <= 2518 else
                     "likely" if track_id <= 4266 else
                     "candidate" if track_id <= 6425 else "none")
            statuses[level] += 1
            # The first 169 strong tracks have a second independent digital item.
            multiplicity = 2 if track_id <= 169 else (1 if level != "none" else 0)
            strong = multiplicity if level == "strong" else 0
            likely = multiplicity if level == "likely" else 0
            candidate = multiplicity if level == "candidate" else 0
            track_rows.append(dict(
                TrackId=track_id, DiscId=disc_id, Artist=artist, Title=title,
                Version=version, DisplayTitle=display, Date="2026", Genre=genre,
                Style="Synthetic", Bpm=120 + track_id % 20, DurationSeconds=210,
                TrackNumber=pos, LegacyTrackNumber=pos, LegacyArtistRaw=artist,
                Album=album, AlbumArtist=artist, Label="Fixture Label",
                Catalog="FIX-%05d" % release_id, Medium=medium,
                DiscNumber=disc_number, TotalDiscs=declared, DiscTrackCount=ntracks,
                DiscDurationSeconds=ntracks * 210, DiscDurationSource="toc_leadout",
                DiscLayout="single", DigitalLevel=level, DigitalCount=multiplicity,
                StrongCount=strong, LikelyCount=likely, CandidateCount=candidate,
                LegacyGenre=genre, GenreSource="legacy"))
            for copy in range(multiplicity):
                item_id += 1
                match_rows.append(dict(
                    TrackId=track_id, DigitalItemId=item_id, Level=level,
                    Method="synthetic_exact" if level == "strong" else "synthetic_weak",
                    Confidence="1" if level == "strong" else "0.5",
                    Artist=artist, Title=display, OriginalTitle=title, RemixedBy=version,
                    Album=album, AlbumArtist=artist, DurationSeconds=210, Bpm=120,
                    Label="Fixture Label", Catalog="FIX-%05d" % release_id,
                    Codec="FLAC", Bitrate="1000",
                    Path="C:\\\\Fixtures\\\\Audio\\\\fixture-%06d.flac" % item_id,
                    Subsong=0, Genre=genre, Style="Synthetic"))
        cd_rows.append(dict(
            DiscId=disc_id, ReleaseId=release_id, LegacyAlbumId=release_id,
            AlbumArtist=artist, Album=album, DiscNumber=disc_number, TotalDiscs=declared,
            Tracks=ntracks, DurationSeconds=ntracks * 210, DurationSource="toc_leadout",
            Date="2026", Genre=genre, Label="Fixture Label",
            Catalog="FIX-%05d" % release_id, Country="XX", Medium=medium,
            Toc=toc, TocComplete=complete, CdTextPresent=0,
            LegacySerial="SYNTHETIC", LegacyPackaging="Fixture",
            LegacyReleaseType="Synthetic", LayoutKind="single",
            PhysicalTrackCount=ntracks, LogicalTrackCount=ntracks,
            StrongTracks=statuses["strong"], LikelyTracks=statuses["likely"],
            CandidateTracks=statuses["candidate"], NoMatchTracks=statuses["none"],
            LegacyGenre=genre, GenreSource="legacy"))

    assert len(track_rows) == 15440
    assert len(cd_rows) == 1315
    assert len(match_rows) == 6594
    assert [sum(row["DigitalLevel"] == level for row in track_rows)
            for level in ("strong", "likely", "candidate", "none")] == [2518, 1748, 2159, 9015]
    return track_rows, cd_rows, match_rows

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    output = Path(args.output)
    output.mkdir(parents=True, exist_ok=True)
    tracks, cds, matches = fixture_rows()
    write_tsv(output / "tracks.tsv.gz", TRACK_COLS, tracks)
    write_tsv(output / "cds.tsv.gz", CD_COLS, cds)
    write_tsv(output / "matches.tsv.gz", MATCH_COLS, matches)
    with tempfile.TemporaryDirectory(prefix="dj-library-synthetic-") as tmp:
        seed = Path(tmp) / "synthetic-v1.sqlite"
        migrate(str(output / "tracks.tsv.gz"), str(output / "cds.tsv.gz"), str(seed))
        assert counts(str(seed)) == (1119, 1315, 15440)
        assert release_size_distribution(str(seed)) == {1: 839, 2: 221, 3: 45, 4: 8, 5: 5, 8: 1}
        gzip_database(seed, output / "catalog-seed-v1.sqlite.gz")
    for name in ("tracks.tsv.gz", "cds.tsv.gz", "matches.tsv.gz",
                 "catalog-seed-v1.sqlite.gz"):
        path = output / name
        print("%s sha256=%s" % (name, hashlib.sha256(path.read_bytes()).hexdigest()))
    print("PASS synthetic only: releases=1119 discs=1315 tracks=15440 matches=6594")

if __name__ == "__main__":
    main()
