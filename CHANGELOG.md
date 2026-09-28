## [0.1.7.0]

### Fixed

- Retry loading administrator translations after an initial failure so localized settings and diagnostics appear when the translations become available.

## [0.1.6.0]

### Improved

- Translate administrator-visible scan rejection reasons and refresh/delete errors in all supported languages while retaining detailed diagnostics for troubleshooting.
- Display scan estimates with singular and plural minute labels, including "Under 1 min" for shorter estimates.

## [0.1.5.0]

### Improved

- Show a scan time estimate as soon as the item count is known, using prior scan throughput and adjusting it as items finish.
- Keep the estimate on the server to avoid client clock differences and large jumps during slow searches.

## [0.1.4.0]

### Improved

- Prefer an eligible TV opening or intro over a higher-scoring soundtrack track, as seen with *Interview with the Vampire* (2022).
- Use the same 10-second to 8-minute theme duration range for movies and series.

## [0.1.3.0]

### Added

- Delete all managed themes across pages and search results while preserving files changed outside the plugin.

### Improved

- Confirm individual and bulk deletion with Jellyfin dialogs featuring a centered title and red Delete button.

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
