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

JellyScore automatically downloads theme music from YouTube for your movies and shows in Jellyfin 12+. Choose which libraries to use, then let it check new items or scan your existing library. It skips theme music you added yourself and uncertain matches.

## Features

- Automatically find theme music for new movies and shows in your chosen libraries. Run a scan to cover items already there.
- Check each recording's title, release year, soundtrack details, and length. Download the clearest match and skip uncertain results or existing theme music.
- Follow scan progress and refresh or delete downloads from Jellyfin.

## Get started

1. In Jellyfin, open **Dashboard > Plugins > Repositories** and add:

   ```text
   https://raw.githubusercontent.com/edmogeor/jellyscore/manifest-release/manifest.json
   ```

2. Install **JellyScore** from the plugin catalog and restart Jellyfin.
3. Open JellyScore and select your movie and TV libraries. New items are checked automatically. Nothing is downloaded until you select a library.
4. Select **Scan libraries** to check items already in your library.

Jellyfin needs permission to write to your media folders. Each movie needs its own folder. JellyScore uses Jellyfin's FFmpeg and comes with the YouTube download tool, so you do not need an API key.

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
| `make format` | Format C# with CSharpier. |
| `make check` | Verify formatting, linting, and builds against Jellyfin 12.0 and 12.1. |
| `make test-unit` | Run matcher checks. |
| `make test-smoke` | Reset Docker and check the Jellyfin admin API without YouTube. |
| `make test-e2e` | Reset Docker and test downloading, refreshing, and deleting theme music. |
| `make up` | Keep a test server running at `http://127.0.0.1:18096`. |
| `make package` | Bundle the plugin and all supported yt-dlp binaries in one archive. |

The end-to-end tests need Docker, Python 3, curl, and the .NET 10 SDK. The test server uses `user` / `password`. To install a local build, run `make package` and use `dist/ThemeSongs.zip`.

## Donations

Feel free to donate if you'd like to support development.

<a href="https://www.buymeacoffee.com/edmogeor" target="_blank"><img src="https://cdn.buymeacoffee.com/buttons/v2/default-yellow.png" alt="Buy Me A Coffee" style="height: 60px !important;width: 217px !important;" /></a>

## License

[GPL-3.0-or-later](LICENSE)
