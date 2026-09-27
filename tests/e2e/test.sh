#!/usr/bin/env bash
set -euo pipefail
docker compose -f tests/e2e/compose.yaml down -v
bash tests/e2e/up.sh
docker compose -f tests/e2e/compose.yaml exec -T jellyfin cp /config/plugins/theme-songs/yt-dlp-linux-x64 /config/plugins/theme-songs/yt-dlp-linux-x64.real
docker compose -f tests/e2e/compose.yaml cp tests/e2e/yt-dlp-wrapper.sh jellyfin:/config/plugins/theme-songs/yt-dlp-linux-x64
docker compose -f tests/e2e/compose.yaml exec -T jellyfin chmod +x /config/plugins/theme-songs/yt-dlp-linux-x64
docker compose -f tests/e2e/compose.yaml exec -T jellyfin sh <<'SH'
set -eu
movie() {
  folder="${4:-/media/movies}/$1 ($3)"
  mkdir -p "$folder"
  /usr/lib/jellyfin-ffmpeg/ffmpeg -loglevel error -f lavfi -i color=c=black:s=320x240:r=1 -t 1 -c:v mpeg4 -y "$folder/$1.mp4"
  printf '<movie><title>%s</title><originaltitle>%s</originaltitle><year>%s</year><lockdata>true</lockdata></movie>\n' "$2" "$2" "$3" > "$folder/movie.nfo"
}
movie "Harry Potter and the Sorcerer's Stone" 'Harry Potter and the Sorcerer&apos;s Stone' 2001
movie Dune Dune 2021
movie 'Unselected Example' 'Unselected Example' 1999 /media/unused
series() {
  folder="/media/shows/$1 ($2)"
  mkdir -p "$folder/Season 01"
  /usr/lib/jellyfin-ffmpeg/ffmpeg -loglevel error -f lavfi -i color=c=black:s=320x240:r=1 -t 1 -c:v mpeg4 -y "$folder/Season 01/S01E01 Pilot.mp4"
  printf '<tvshow><title>%s</title><year>%s</year><lockdata>true</lockdata></tvshow>\n' "$1" "$2" > "$folder/tvshow.nfo"
}
series 'The Office (US)' 2005
series 'Breaking Bad' 2008
SH
python3 -u tests/e2e/smoke.py
