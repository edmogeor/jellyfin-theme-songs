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

JellyScore automatically downloads theme music from YouTube for your movies and shows in Jellyfin 12+. It checks new items as they arrive and scans your libraries after Jellyfin scans them. You can also start a scan yourself. It skips theme music you added yourself and uncertain matches.

## Features

- Check new items automatically and search the whole library after Jellyfin scans it. Run a scan yourself whenever you want.
- Check each recording's title, release year, soundtrack details, and length. Download the clearest match and skip uncertain results or existing theme music.
- Follow scan progress and refresh or delete downloads from Jellyfin.
- Localized JellyScore configuration page: Danish, German, English (US), Spanish, Finnish, French, Italian, Japanese, Korean, Norwegian Bokmal, Dutch, Polish, Portuguese (Brazil), Russian, Swedish, and Chinese (Simplified).

## Get started

1. In Jellyfin, open **Dashboard > Plugins > Repositories** and add:

   ```text
   https://raw.githubusercontent.com/edmogeor/jellyscore/manifest-release/manifest.json
   ```

2. Install **JellyScore** from the plugin catalog and restart Jellyfin.
3. Open JellyScore. All libraries are selected by default, but you can choose which to include or turn off automatic scans.
4. JellyScore checks new items as they are added and runs after Jellyfin's next library scan. Select **Scan libraries** to search now. Installing the plugin does not start a scan.

Jellyfin needs permission to write to your media folders and plugin configuration directory. Each movie needs its own folder. On first use JellyScore downloads and verifies the YouTube download tool for your server's platform, so GitHub must be reachable, but you do not need an API key. Later searches reuse the cached tool.

## Manage your theme music

The JellyScore page shows your downloads and scan progress. You can **Refresh** a download to look for a different recording, or **Delete** it. Deleting pauses automatic downloads for that item until you run another full scan. JellyScore never deletes theme music you added yourself or files changed outside the plugin.

YouTube changes can interrupt searches. If that happens, you may need to update JellyScore to get a newer download tool.

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
| `make package` | Package the plugin with a yt-dlp release identifier and checksums, without its executable. |

Run `make setup` to install the .NET tools and Node dependencies before `make check`. The end-to-end tests need Docker, Python 3, curl, and the .NET 10 SDK. The test server uses `user` / `password`. To install a local build, run `make package` and use `dist/JellyScore.zip`.

## Donations

Feel free to donate if you'd like to support development.

<a href="https://www.buymeacoffee.com/edmogeor" target="_blank"><img src="https://cdn.buymeacoffee.com/buttons/v2/default-yellow.png" alt="Buy Me A Coffee" style="height: 60px !important;width: 217px !important;" /></a>

## License

Copyright © 2026 George Edmonds. Licensed under [GPL-3.0-or-later](LICENSE).
