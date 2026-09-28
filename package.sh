#!/usr/bin/env bash
set -euo pipefail

version=${YT_DLP_VERSION:-$(curl --fail --location --retry 3 --silent --show-error https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest | python3 -c 'import json,sys; print(json.load(sys.stdin)["tag_name"])')}
output=dist/universal
mkdir -p "$output"
dotnet publish Jellyfin.Plugin.JellyScore/Jellyfin.Plugin.JellyScore.csproj -c Release -o "$output" --no-self-contained
base="https://github.com/yt-dlp/yt-dlp/releases/download/$version"
curl --fail --location --retry 3 --silent --show-error "$base/SHA2-256SUMS" -o "$output/checksums"

assets=(yt-dlp_linux yt-dlp_linux_aarch64 yt-dlp_musllinux yt-dlp_musllinux_aarch64 yt-dlp.exe yt-dlp_arm64.exe yt-dlp_macos)
for asset in "${assets[@]}"; do
  test -n "$(awk -v asset="$asset" '$2 == asset {print $1}' "$output/checksums")"
done
mv "$output/checksums" "$output/SHA2-256SUMS"
printf '%s\n' "$version" > "$output/yt-dlp-version"
(
  cd "$output"
  python3 -m zipfile -c ../JellyScore.zip Jellyfin.Plugin.JellyScore.dll yt-dlp-version SHA2-256SUMS
)
