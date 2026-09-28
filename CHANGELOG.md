## [0.1.13.0]

### Improved

- Use the same default match strength of 50 for new and existing installations while retaining existing settings.
- Shorten the match-strength helper text in every supported language.

## [0.1.12.0]

### Added

- Set a minimum match strength from 0 to 100 in the admin page. New installations default to 50; existing installations retain their previous matching behavior at 0 until changed.
- Report when search results fall below the chosen minimum in all supported admin languages.

## [0.1.11.0]

### Improved

- Give verified work years and matching film or TV editions more weight when ranking eligible themes, including short series openings.
- Cap weak soundtrack labels and channel signals so promotional wording cannot outweigh stronger work evidence. Document the scoring weights and selection categories.

## [0.1.10.0]

### Fixed

- Reject TV theme uploads that borrow another work's named music, including an *Interview with the Vampire* fan edit uploaded before the 2022 series existed.
- Distinguish film and TV soundtrack editions using titles and identified albums without treating incidental words in descriptions as edition evidence.
- Keep named TV tracks eligible when their description explicitly links them to the correct series and year.

## [0.1.9.0]

### Improved

- Reduce the plugin archive from roughly 224 MB to under 100 KB by downloading and verifying only the server's yt-dlp binary on first use, then caching it.
- Show installation failures and a retry button in the admin page. Pause manual scans and individual theme refreshes until yt-dlp is available again.

## [0.1.8.0]

### Fixed

- Exclude episode character introductions and opening scenes from TV theme matches without rejecting openings merely labelled with an episode number.
- Let explicit TV themes compete with openings and intros, so an otherwise equal theme song ranks above a bare intro.

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
