.DEFAULT_GOAL := help
.PHONY: help setup format check test test-unit test-e2e test-smoke up package

help:
	@printf '%s\n' 'make setup      Restore .NET tooling.' 'make format     Format C#.' 'make check      Verify formatting, linting, and Jellyfin 12 builds.' 'make test-unit  Run matcher checks.' 'make test-e2e   Download a real theme in Jellyfin 12.' 'make test-smoke Run Jellyfin API checks without YouTube.' 'make test       Run unit and live e2e checks.' 'make up         Start Jellyfin 12 for manual testing.' 'make package    Bundle all supported yt-dlp binaries in one archive.'

setup:
	dotnet tool restore

format:
	dotnet tool restore
	dotnet csharpier format

check:
	dotnet tool restore
	dotnet build Jellyfin.Plugin.JellyScore/Jellyfin.Plugin.JellyScore.csproj -p:JellyfinVersion=12.0.0
	dotnet build Jellyfin.Plugin.JellyScore/Jellyfin.Plugin.JellyScore.csproj -p:JellyfinVersion=12.1.0
	dotnet csharpier check
	dotnet jb inspectcode --no-build --swea --format=Text --output=- --LogLevel=OFF --verbosity=OFF Jellyfin.Plugin.JellyScore/Jellyfin.Plugin.JellyScore.csproj

test-unit:
	dotnet run --project checks/checks.csproj

test-e2e:
	bash tests/e2e/test.sh

test-smoke:
	LIVE_YOUTUBE=0 bash tests/e2e/test.sh

test: test-unit test-e2e

package:
	bash package.sh

up:
	bash tests/e2e/up.sh
