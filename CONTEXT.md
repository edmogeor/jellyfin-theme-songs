# JellyScore

JellyScore is a Jellyfin 12 plugin that finds likely theme music on YouTube for movies and TV series, saves it where Jellyfin recognizes it, and lets administrators manage plugin-downloaded themes. A missed theme is preferable to a wrong match or a change to someone's own files.

## Terms and scope

- **Theme**: audio Jellyfin recognizes in an item's folder, including `theme.*` and audio in `theme-music/`.
- **Managed theme**: a `theme.mp3` downloaded by this plugin whose item, folder, path, and file hash still match its ownership record. An existing, manually added, or externally modified file is not managed.
- **Recording**: the track identity used to distinguish a replacement from another upload of the same music. A YouTube video ID identifies a source upload, not necessarily a different recording.
- Only physical movies in dedicated folders and physical series roots inside selected, writable libraries are eligible. Episodes, seasons, extras, virtual items, and movies in mixed or shared folders are not.
- The control surface is a Jellyfin administrator-only page and API. There is no manual candidate-review queue.

## Discovery and selection

- Automatic processing of new items is enabled by default. An unset library selection means all libraries; an explicitly empty selection means none. Installation does not itself backfill existing items.
- Administrators can set a minimum match strength from 0 to 100. The default is 50, including existing settings without a saved value. Match strength is the candidate's bounded ranking score (0 to 100), not a probability. Apply the minimum after hard eligibility checks and before category preference, including refresh and failed-source fallback. A changed minimum affects future searches, not files already installed.
- New item events queue processing after metadata and the physical folder become available. A completed Jellyfin library refresh can also queue a scan while automatic processing is enabled. Administrators can start or cancel a full scan of selected libraries and see counts, recent rejection reasons, progress, and an approximate time remaining seeded from the last completed scan (or a first-run default) and adjusted as items finish.
- Search uses the Jellyfin display title, original title when present, production year, and theme or soundtrack terms. Titles broaden retrieval; a search query alone never proves a candidate belongs to that work. Search results are shortlisted before full video metadata is fetched. A full-album tracklist can supply a search hint, but the album itself is not a theme candidate.
- Reject mismatched works, years, adaptations, sequels, regional versions, covers, remixes, fan edits, tutorials, trailers, reviews, compilations, and full albums. Film-versus-TV soundtrack labels in the title or identified album indicate the wrong edition; incidental mentions of "movie" or "TV" in free-form descriptions do not. A series title attached to another named theme does not establish that the music belongs to the series; an explicit description linking that recording as the TV show's theme can. A named track can qualify when its title, soundtrack album, or description ties it to the correct work and edition. Missing edition evidence for an ambiguous title means no match. An upload older than the year before the work's production year cannot be its theme; a newer upload does not prove a match or establish the work's release year.
- For example, do not use a UK Office opening for *The Office (US)* or a 1984 or Part Two track for *Dune* (2021). A recording titled "Hedwig's Theme" can still qualify for *Harry Potter and the Sorcerer's Stone* when its soundtrack metadata links it to that film.
- Require a known duration of 10 seconds to 8 minutes for both series and movies. First reject ineligible works, editions, dates, and formats; ranking cannot rescue a contradiction. Score the remaining candidates with additive evidence, not a probability:

  | Evidence | Points |
  | --- | ---: |
  | Full work title in video title, or partial title words | +30, or up to +16 |
  | Matching work year in title/album, or linked description/track metadata | +30, or +20 |
  | Matching film/TV edition label in title or identified album | +20 |
  | Main theme/title, theme, opening/intro | +20, +15, +12 (opening and theme can combine) |
  | Identified track on matching soundtrack album, with named artist | +40, plus +15 |
  | Soundtrack/OST/score label, official claim, music channel | +10 maximum, +5, +3 |
  | Duration 30 seconds to 6 minutes, short opening, 6 to 8 minutes, other short recording | +10, +10, +5, -10 |
  | Multi-season opening/title-card collection, other collection-style title | -30, or -20 |

  For series, prefer the highest-scoring explicit theme, opening, or intro over other soundtrack tracks; an otherwise equal theme scores slightly above a bare intro. Multi-season collections can remain fallback candidates but rank below comparable standalone themes. For movies, prefer main themes, then closing credits, then soundtrack tracks. Accept the highest-scoring eligible recording meeting the administrator's minimum in the preferred category; when recordings tie, choose the first search result. Equivalent uploads of one recording remain interchangeable sources.
- Incomplete searches, unavailable tools, and failed downloads are failures, not proof that no match exists. A complete search without an eligible recording reports a reason. Retry transient failures with bounds; a failed download may try one other eligible source without permanently blacklisting the failed upload.

## Audio and file ownership

- The plugin packages a yt-dlp release identifier and its checksums, not executables. On first use it downloads only the server's platform-specific binary, verifies its checksum, and caches it in Jellyfin's plugin configuration directory; a failed or unverifiable download appears in the admin page with a retry action. It uses Jellyfin's FFmpeg installation. It downloads only the selected audio, measures its integrated loudness and true peak, then converts it to `theme.mp3` with a single fixed gain targeting -18 LUFS without exceeding -3 dBTP. Peak headroom takes priority over the loudness target; never compress or dynamically normalize the recording. It never processes manually added themes.
- Place a movie's theme beside its movie file in a dedicated folder, or in a series' physical root. Check Jellyfin's recognized themes and physical theme files before downloading and again before committing. Do not overwrite a non-plugin theme, even if a stale record claims ownership.
- Convert to a temporary file in the destination folder, validate it, then move it into place. A failed conversion or replacement must leave the existing theme untouched. Refresh Jellyfin's item metadata after a successful write or deletion.
- Persist managed item IDs, canonical paths, library IDs, source video IDs, recording identities, hashes, scores, and dates in plugin configuration data, not in media folders. Serialize work per item. Before replacing or deleting a file, confirm that its location and hash still match the record. Treat a changed or moved file as user-owned and leave it alone. When listing themes, remove stale records for changed or deleted items without deleting their files; defer reconciliation when a media folder is temporarily unavailable.

## Admin actions

- The admin page controls automatic processing and selected libraries, shows scan progress, and lists searchable, paginated managed downloads with source links and status. Administrator-visible rejection reasons and action errors are translated in the page from stable codes; diagnostic text in logs and stored outcomes remains unchanged. Only administrators can read the management API or change settings, start scans, refresh, or delete.
- **Refresh** excludes the current source video and recording, then seeks a different eligible recording. If none qualifies or the replacement fails, the existing file stays in place. Source and recording exclusions persist across restarts and scans; transient download failures do not create permanent exclusions.
- **Delete** removes only an unchanged managed file, then suppresses automatic re-download for that item. **Delete all** applies the same ownership checks to every managed download, across all search results and pages, and reports files it could not safely delete. A subsequent full scan clears deletion suppression, including a scan queued after a Jellyfin library refresh. Neither action removes another theme file or a `theme-music/` directory.
