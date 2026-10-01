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
deno_release="https://api.github.com/repos/denoland/deno/releases/${DENO_VERSION:+tags/$DENO_VERSION}"
if [ -z "${DENO_VERSION:-}" ]; then deno_release="${deno_release}latest"; fi
curl --fail --location --retry 3 --silent --show-error "$deno_release" | python3 -c '
import json, re, sys
from pathlib import Path

release = json.load(sys.stdin)
names = [f"deno-{arch}-{platform}.zip" for arch, platform in (
    ("x86_64", "unknown-linux-gnu"), ("aarch64", "unknown-linux-gnu"),
    ("x86_64", "apple-darwin"), ("aarch64", "apple-darwin"),
    ("x86_64", "pc-windows-msvc"), ("aarch64", "pc-windows-msvc"))]
assets = {asset["name"]: asset.get("digest", "") for asset in release["assets"]}
checksums = [assets[name].removeprefix("sha256:") + "  " + name for name in names]
if not re.fullmatch(r"v[0-9.]+", release["tag_name"]) or any(
    not re.fullmatch(r"[a-fA-F0-9]{64}", checksum.split()[0]) for checksum in checksums
):
    sys.exit("Deno release metadata has missing or invalid checksums")
output = Path(sys.argv[1])
(output / "deno-checksums").write_text("\n".join(checksums) + "\n")
(output / "deno-version").write_text(release["tag_name"] + "\n")
' "$output"
(
  cd "$output"
  python3 -m zipfile -c ../JellyScore.zip Jellyfin.Plugin.JellyScore.dll yt-dlp-version SHA2-256SUMS deno-version deno-checksums
)
