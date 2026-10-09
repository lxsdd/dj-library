# DJ Library

Native Windows/WPF catalog for personal physical CDs and read-only foobar2000 Bridge snapshots. The application uses a per-user SQLite catalog, supports CDX compatibility checks, MusicBrainz/Discogs metadata review and a read-only metadata-normalizer preview.

## Personal data stays local

The source distribution does not contain music tracks, personal listening data, real user media paths, user library exports or a preinstalled collection. The self-test corpus is deterministic and entirely synthetic. The application never installs that corpus as a personal catalog.

Existing user catalogs are reused from the local profile at `catalog-v0.4.sqlite`. New users can create an **empty catalog** with no music entries or explicitly import a compatible existing DJ Library SQLite catalog/backup. An import is staged and validated without overwriting a previous catalog; neither option ships or installs synthetic music data. Additional import formats may be added separately.

The `foo_dj_library_bridge` is read-only. Metadata normalization in the app is preview-only. Nothing writes to MP3 tags or foobar metadata in the background.

## Configurable physical-disc playback check

Open **Tools → Disc Playback Rule…** to enable or disable the playback rule, choose a descriptive player name, enter an exact 75 fps limit such as `79:59:74`, choose `>=` or `>`, and control red/amber CD and track row highlighting independently. The default preserves the previous Numark CDX limit and red marking. **Disc playable** columns in Tracks and CDs, plus the status filter, use one physical TOC-based calculation; every track inherits its containing physical CD's status.

A disc with no **complete, valid TOC** is always `Unknown`: the app never classifies it from guessed per-track durations. Disabling the rule hides the related main-list columns and filter and stops row highlighting. This is a presentation preference; it never modifies the physical catalog or audio tags.

## Development and CI

Run the Windows CI workflow in GitHub Actions. The CI generates synthetic physical/digital data with `tools/generate_synthetic_fixtures.py` and exercises the full application self-test, SQLite migration, CDX edge cases, native metadata normalizer and GUI production-path contracts. It produces SHA-bound qualification manifests and packages only when the full gate is authorized.

**The portable program ZIP contains no synthetic or personal test catalog at all**. Synthetic fixtures are temporary CI-only test inputs, never end-user content. Public release requires `fixture_source=synthetic_v1`, `portable_data_policy=no_catalog_no_fixtures` and `privacy_gate=PASS` in the qualified CI manifest, plus real-user runtime acceptance when applicable. The ZIP is inspected again at release time.

## Publishing safety

The original historical private repository is NOT safe to publish directly: its old Git objects, refs, logs and artifacts have included real catalog data. A sanitized working tree or current commit alone is insufficient. Publication is allowed only from an independently created **new clean Git history** after source/license/privacy review and secure archival of the existing private history.

Do not switch the old private repository visibility to Public.
