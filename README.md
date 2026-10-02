<div align="center">
  <img src="jellyscore.svg" width="600" alt="JellyScore" />
  <p>
    <a href="https://github.com/edmogeor/jellyscore/actions/workflows/ci.yml">
      <img src="https://github.com/edmogeor/jellyscore/actions/workflows/ci.yml/badge.svg?branch=main" alt="CI" />
    </a>
    <a href="https://github.com/edmogeor/jellyscore/releases">
      <img src="https://img.shields.io/github/v/release/edmogeor/jellyscore" alt="Latest release" />
    </a>
    <a href="LICENSE">
      <img src="https://img.shields.io/badge/License-GPL--3.0--or--later-blue.svg" alt="License: GPL-3.0-or-later" />
    </a>
  </p>
</div>

JellyScore automatically downloads theme music from YouTube for your movies and shows in Jellyfin 12. It skips theme music you added yourself and uncertain matches.

## Features

- Check new items automatically and search selected libraries after Jellyfin scans them. Run a scan yourself whenever you want.
- Check each recording's title, release year, soundtrack details, and length. Download the clearest match and skip uncertain results or existing theme music.
- Optionally prefer a franchise theme for movies in TMDb collections, even without a Jellyfin collection. Select the Collections library to add themes to matching Jellyfin collections independently of that movie preference.
- Follow scan progress and refresh or delete downloads from Jellyfin.
- Localized JellyScore configuration page: Danish, German, English (US), Spanish, Finnish, French, Italian, Japanese, Korean, Norwegian Bokmal, Dutch, Polish, Portuguese (Brazil), Russian, Swedish, and Chinese (Simplified).

## Get started

1. In Jellyfin, open **Dashboard > Plugins > Repositories** and add:

   ```text
   https://raw.githubusercontent.com/edmogeor/jellyscore/manifest-release/manifest.json
   ```

2. Install **JellyScore** from the plugin catalog and restart Jellyfin.
3. Open JellyScore. All libraries are selected by default, but you can choose which to include, independently control processing new items and scanning after Jellyfin library scans, or set a minimum match strength (0–100). Higher values skip weaker matches. The default is 50.
4. Select **Scan libraries** to search your existing items now. Installing the plugin does not start a scan.

**TMDb metadata:** Franchise themes for movies and themes for matching Jellyfin collections require a TMDb collection name saved in the movie's Jellyfin metadata. If that name is missing, movies use the normal film-specific search and collection themes cannot be matched. For short movie or TV titles, JellyScore may verify that there is no competing same-title work before accepting a theme without a year. This requires a saved TMDb ID and enabled TMDb metadata searches for both films and TV; otherwise, the year requirement remains. Previously saved collection names can still be used while they remain in Jellyfin metadata. The optional TV theme URL template uses a **TVDB ID**, not TMDb.

Jellyfin needs permission to write to your media folders and plugin configuration directory. Each movie needs its own folder. On first use JellyScore downloads and verifies [yt-dlp](https://github.com/yt-dlp/yt-dlp) for your server's platform from its official releases. YouTube extraction also needs a JavaScript runtime: JellyScore uses a supported Deno or Node.js installation if available, or downloads and verifies Deno on supported platforms. GitHub must be reachable for these downloads, but you do not need an API key. Later searches reuse the cached tools.

## Limitations

YouTube can rate-limit or challenge the server's guest session, account, or IP, particularly during large scans. JellyScore spaces yt-dlp requests and pauses for 30 minutes when it detects a rate-limit error. A stopped scan may need to be started again afterward. These measures cannot guarantee access, and YouTube changes may require a newer JellyScore release with an updated yt-dlp.

For TV series, administrators can optionally enter a **TV theme URL template**, such as `https://example.com/{tvdbId}.mp3`. JellyScore tries it first when the series has a TVDB ID, validates the audio, and falls back to YouTube if the source fails. There is no preconfigured URL. Movies still use YouTube.

**YouTube cookies are optional.** If YouTube requires a signed-in session, paste the contents of a [YouTube `cookies.txt` file](https://github.com/yt-dlp/yt-dlp/wiki/Extractors#exporting-youtube-cookies) into the **YouTube cookies (optional)** setting; leave it empty for anonymous requests. Cookies do not remove rate limits. They are stored in Jellyfin's plugin configuration and passed to yt-dlp, so treat them as account credentials. Using an account with yt-dlp may lead to temporary or permanent suspension.

## Manage your theme music

The JellyScore page shows your downloads and scan progress. You can **Refresh** a download to look for a different recording, or **Delete** it. Deleting pauses automatic downloads for that item until you run another full scan. JellyScore never deletes theme music you added yourself or files changed outside the plugin.

## Development

```sh
make setup
make check
make test-unit
```

| Command | Description |
| --- | --- |
| `make format` | Format C# with CSharpier and the admin page with Prettier. |
| `make check` | Verify C# and admin page formatting, lint inline JavaScript with oxlint, and build against Jellyfin 12.0 and 12.1. |
| `make test-unit` | Run matcher checks. |
| `make test-smoke` | Reset Docker and check the Jellyfin admin API without YouTube. |
| `make test-e2e` | Reset Docker and test downloading, refreshing, and deleting theme music. |
| `make up` | Keep a test server running at `http://127.0.0.1:18096`. |
| `make package` | Package the plugin with yt-dlp and Deno release identifiers and checksums, without executables. |

Run `make setup` to install the .NET tools and Node dependencies before `make check`. The end-to-end tests need Docker, Python 3, curl, and the .NET 10 SDK. The test server uses `user` / `password`. To install a local build, run `make package` and use `dist/JellyScore.zip`.

## Donations

Feel free to donate if you'd like to support development.

<a href="https://www.buymeacoffee.com/edmogeor" target="_blank"><img src="https://cdn.buymeacoffee.com/buttons/v2/default-yellow.png" alt="Buy Me A Coffee" style="height: 60px !important;width: 217px !important;" /></a>

## License

Copyright © 2026 edmogeor. Licensed under [GPL-3.0-or-later](LICENSE).

JellyScore uses [yt-dlp](https://github.com/yt-dlp/yt-dlp). Its Git repository is [Unlicense](https://github.com/yt-dlp/yt-dlp/blob/master/LICENSE), while its standalone executables bundle other components and are GPLv3+ as a combined work. See [yt-dlp's licensing details](https://github.com/yt-dlp/yt-dlp#licensing).

## AI disclosure

AI tools assist with development and testing. All code is reviewed by the maintainer before release.
