<div align="center">
  <img src="jellyscore.svg" width="800" alt="JellyScore" />
  <h1>JellyScore for Jellyfin</h1>
  <p>
    <a href="https://github.com/edmogeor/jellyfin-theme-songs/actions/workflows/ci.yml">
      <img src="https://github.com/edmogeor/jellyfin-theme-songs/actions/workflows/ci.yml/badge.svg?branch=main" alt="CI" />
    </a>
    <a href="https://github.com/edmogeor/jellyfin-theme-songs/releases">
      <img src="https://img.shields.io/github/v/release/edmogeor/jellyfin-theme-songs" alt="Latest release" />
    </a>
  </p>
</div>

JellyScore downloads theme music from YouTube for your movies and shows in Jellyfin 12+. Choose which libraries to use, then let it find themes for new items or scan your existing library. It skips themes you added yourself and uncertain matches.

## Features

- Find themes for new movies and shows, or scan your existing library.
- Compare the title, release year, soundtrack details, and length of each recording. Download only when one match stands out; skip uncertain matches and existing themes.
- See scan progress and manage your downloads in Jellyfin.

## Get started

1. In Jellyfin, open **Dashboard > Plugins > Repositories** and add:

   ```text
   https://raw.githubusercontent.com/edmogeor/jellyfin-theme-songs/manifest-release/manifest.json
   ```

2. Install **JellyScore** from the plugin catalog and restart Jellyfin.
3. Open JellyScore and select your movie and TV libraries. New items are checked automatically. Nothing is downloaded until you select a library.
4. Select **Scan libraries** to look for themes for items already in your library.

Jellyfin needs permission to write to your media folders. Each movie needs its own folder. JellyScore uses Jellyfin's FFmpeg and comes with the YouTube download tool, so you do not need an API key.

## Manage your themes

The JellyScore page shows your downloads and scan progress. You can **Refresh** a theme to look for a different recording, or **Delete** it. Deleting pauses automatic downloads for that item until you run another full scan. JellyScore never deletes themes you added yourself or files changed outside the plugin.

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
| `make test-e2e` | Reset Docker and check a real theme download, refresh, and deletion. |
| `make up` | Keep a test server running at `http://127.0.0.1:18096`. |
| `make package` | Bundle the plugin and all supported yt-dlp binaries in one archive. |

The end-to-end tests need Docker, Python 3, curl, and the .NET 10 SDK. The test server uses `user` / `password`. To install a local build, run `make package` and use `dist/ThemeSongs.zip`.

## Donations

Feel free to donate if you'd like to support development.

<a href="https://www.buymeacoffee.com/edmogeor" target="_blank"><img src="https://cdn.buymeacoffee.com/buttons/v2/default-yellow.png" alt="Buy Me A Coffee" style="height: 60px !important;width: 217px !important;" /></a>
