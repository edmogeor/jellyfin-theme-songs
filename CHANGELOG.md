## [0.1.2.0]

### Fixed

- Skip piano tutorials and other how-to-play videos when finding theme music.

## [0.1.1.0]

### Fixed

- Preserve theme music dynamics by applying one fixed gain per track, capped by true peak, instead of dynamic loudness normalization.
- Select the first eligible recording when top-scoring soundtrack tracks tie instead of leaving the item without a theme.

## [0.1.0.0]

### Added

- Theme song discovery for selected movie and TV libraries, with conservative matching and two-pass loudness normalization.
- Admin dashboard for settings, rescan, managed downloads, refresh, and delete.
- One plugin package containing yt-dlp binaries for supported server platforms.
