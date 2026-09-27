#!/usr/bin/env bash
set -euo pipefail

version=${YT_DLP_VERSION:-$(curl --fail --location --retry 3 --silent --show-error https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest | python3 -c 'import json,sys; print(json.load(sys.stdin)["tag_name"])')}
output=dist/universal
mkdir -p "$output"
dotnet publish Jellyfin.Plugin.ThemeSongs/Jellyfin.Plugin.ThemeSongs.csproj -c Release -o "$output" --no-self-contained
base="https://github.com/yt-dlp/yt-dlp/releases/download/$version"
curl --fail --location --retry 3 --silent --show-error "$base/SHA2-256SUMS" -o "$output/checksums"

assets=(yt-dlp_linux yt-dlp_linux_aarch64 yt-dlp_musllinux yt-dlp_musllinux_aarch64 yt-dlp.exe yt-dlp_arm64.exe yt-dlp_macos)
names=(yt-dlp-linux-x64 yt-dlp-linux-arm64 yt-dlp-linux-musl-x64 yt-dlp-linux-musl-arm64 yt-dlp-windows-x64.exe yt-dlp-windows-arm64.exe yt-dlp-macos)
for index in "${!assets[@]}"; do
  asset=${assets[$index]}
  name=${names[$index]}
  expected=$(awk -v asset="$asset" '$2 == asset {print $1}' "$output/checksums")
  test -n "$expected"
  actual=$(sha256sum "$output/$name" 2>/dev/null | cut -d' ' -f1 || true)
  if [[ $actual != "$expected" ]]; then
    curl --fail --location --retry 3 --silent --show-error "$base/$asset" -o "$output/$name"
    actual=$(sha256sum "$output/$name" | cut -d' ' -f1)
  fi
  test "$actual" = "$expected"
  [[ $name == *.exe ]] || chmod +x "$output/$name"
done
rm "$output/checksums"
(
  cd "$output"
  python3 -m zipfile -c ../ThemeSongs.zip Jellyfin.Plugin.ThemeSongs.dll "${names[@]}"
)
