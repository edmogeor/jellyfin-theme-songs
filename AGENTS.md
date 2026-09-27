# Agent Instructions

## Commands

- `make setup` restores local .NET tools.
- `make format` formats C# with CSharpier.
- `make check` verifies formatting, linting, and Jellyfin 12.0/12.1 builds.
- `make test-unit` runs matcher checks.
- `make test-smoke` resets Docker and checks the Jellyfin admin API.
- `make test-e2e` resets Docker and downloads a real theme for a test film.
- `make test` runs unit and live end-to-end tests.

Run `make check` after code or configuration changes. Run the relevant test target for behavior changes.

## Releases

- Update `build.yaml`, the project version, and `CHANGELOG.md` together.
- Use `X.Y.Z.W` versions and push a matching `vX.Y.Z.W` tag after the checks pass.
- The release workflow bundles all supported yt-dlp binaries in one archive and updates the manifest-only `manifest-release` branch.

## Changes

- Never overwrite or delete an unmodified user's theme. Keep per-item ownership and hash checks intact.
- Do not commit builds, downloaded executables, media fixtures, or the Jellyfin source reference checkout.
- Use `chore:`, `feat:`, `fix:`, `test:`, or `docs:` in commit subjects.
