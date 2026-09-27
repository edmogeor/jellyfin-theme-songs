#!/bin/sh
set -eu
for arg do
  if [ "$arg" = '-o' ] && [ ! -e /tmp/theme-songs-download-retried ]; then
    touch /tmp/theme-songs-download-retried
    printf '%s\n' 'Simulated transient download failure' >&2
    exit 1
  fi
done
exec /config/plugins/theme-songs/yt-dlp-linux-x64.real "$@"
