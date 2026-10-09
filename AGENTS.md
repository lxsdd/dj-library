# DJ Library public-source development contract

- GitHub source and hosted CI are canonical. Do not require permanent local repositories.
- Use only deterministic synthetic test data in source, CI, packages and pull-request artifacts. Never commit user-owned music catalogs, track files, digital match paths, bridge snapshots from real profiles, API tokens or unredacted logs.
- Personal SQLite remains local and authoritative; existing catalogs must be preserved. No silent replacement, implicit data seed, background tagging or destructive writes.
- CI must validate the real WPF production UI path, not only isolated helpers. Verify persisted windows, column order/visibility/width, horizontal scroll, keyboard selection, provider provenance and per-field Review status.
- Keep Candidate #22 real-runtime regressions and Bridge schema v1/v2/v3 compatible.
- External provider values must have per-field provenance. Track Artist/Title and derived Mix/Version must distinguish provider-native from interpreted values; preserve Various Artists track-level artist evidence.
- Use a single controlled integration branch and FULL Windows CI before qualification. Do not merge or release failed/unexecuted CI. A job with zero executed steps is infrastructure-blocked, not a software PASS.
- Release only an exact, already qualified SHA-bound artifact with synthetic fixture manifest and user runtime PASS where needed. Never rebuild on promotion.
- Public fork PRs receive minimum read-only permissions. No trusted credentials and no long-retention packaging of untrusted PR outputs.
- Do not expose historical private Git refs, commit objects, Actions archives or branches by changing the original private repository visibility.
