"""Exercise plugin discovery and the admin boundary in an actual Jellyfin 12 server."""

import json
import os
import subprocess
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

BASE = "http://127.0.0.1:18096"
PLUGIN = "129e8a8b-87f1-48d3-802b-7dd151d72920"


def request(method, path, body=None, token=None, timeout=5):
    headers = {"Content-Type": "application/json"}
    headers["Authorization"] = (
        f'MediaBrowser Token="{token}"'
        if token
        else 'MediaBrowser Client="e2e", Device="e2e", DeviceId="e2e", Version="1"'
    )
    data = json.dumps(body).encode() if body is not None else None
    try:
        with urllib.request.urlopen(
            urllib.request.Request(BASE + path, data=data, headers=headers, method=method), timeout=timeout
        ) as response:
            return response.status, json.loads(response.read() or b"null")
    except urllib.error.HTTPError as error:
        try:
            return error.code, json.loads(error.read())
        except (ValueError, UnicodeDecodeError):
            return error.code, None
    except (urllib.error.URLError, ConnectionError):
        return 503, None


def field(data, name):
    return data.get(name, data.get(name[0].upper() + name[1:]))


def assert_settings(token, enabled, libraries):
    for path in ("/ThemeSongs/settings", f"/Plugins/{PLUGIN}/Configuration"):
        status, settings = request("GET", path, token=token)
        assert status == 200, f"read settings {path}: {status}"
        assert field(settings, "enabled") is enabled, f"saved enabled in {path}: {settings}"
        assert [uuid.UUID(str(value)) for value in field(settings, "libraries")] == [uuid.UUID(value) for value in libraries], (
            f"saved libraries in {path}: {settings}"
        )


def scan_until(token, expected=None, expect_current=False):
    _, before = request("GET", "/ThemeSongs/scan", token=token)
    status, _ = request("POST", "/ThemeSongs/scan", token=token)
    assert status == 202, f"start rescan: {status}"
    saw_current = False
    for attempt in range(300):
        status, progress = request("GET", "/ThemeSongs/scan", token=token)
        if status == 200 and field(progress, "running") and field(progress, "currentItem"):
            assert field(progress, "startedAt").startswith("20"), f"scan start time missing: {progress}"
            saw_current |= "Sorcerer" in field(progress, "currentItem")
        if status == 200 and not field(progress, "running") and field(progress, "runId") != field(before, "runId"):
            assert field(progress, "processed") == field(progress, "total") == 5, f"scan did not process all items: {progress}"
            counts = ("added", "alreadyThemed", "excluded", "noMatch", "unsupported", "failed")
            assert sum(field(progress, key) for key in counts) == 5, f"scan counts disagree: {progress}"
            if field(progress, "excluded") + field(progress, "noMatch"):
                assert field(progress, "rejections") and all(": " in item for item in field(progress, "rejections")), (
                    f"scan omitted rejection reasons: {progress}"
                )
            assert not expected or field(progress, expected) >= 1, f"Rescan finished without {expected}: {progress}"
            assert not expect_current or saw_current, f"scan never exposed current item: {progress}"
            return progress
        if attempt % 5 == 4:
            print(f"Waiting for scan ({expected or 'completion'}): {progress}", flush=True)
        time.sleep(2)
    raise SystemExit(f"Rescan did not finish with {expected}: {progress}")


for _ in range(60):
    status, info = request("GET", "/System/Info/Public")
    if status == 200:
        break
    time.sleep(2)
else:
    raise SystemExit("Jellyfin did not start")

if not info.get("StartupWizardCompleted", False):
    for method, endpoint, body in [
        ("POST", "/Startup/Configuration", {"UICulture": "en-US", "MetadataCountryCode": "US", "PreferredMetadataLanguage": "en"}),
        ("GET", "/Startup/User", None),
        ("POST", "/Startup/User", {"Name": "user", "Password": "password"}),
        ("POST", "/Startup/RemoteAccess", {"EnableRemoteAccess": True, "EnableAutomaticPortMapping": False}),
        ("POST", "/Startup/Complete", None),
    ]:
        for _ in range(30):
            status, _ = request(method, endpoint, body)
            if status in (200, 204):
                break
            time.sleep(2)
        print(f"{endpoint}: {status}", flush=True)
        assert status in (200, 204), f"wizard step {endpoint}: {status}"

for _ in range(30):
    status, login = request("POST", "/Users/AuthenticateByName", {"Username": "user", "Pw": "password"})
    if status == 200:
        break
    time.sleep(2)
assert status == 200, f"admin login: {status}"
assert login["User"]["Name"] == "user", f"test admin was not renamed: {login['User']['Name']}"
token = login["AccessToken"]
status, config = request("GET", f"/Plugins/{PLUGIN}/Configuration", token=token)
assert status == 200, f"plugin not loaded: {status}"
assert config["Enabled"] is True, "automatic processing must default to on"
assert config.get("Libraries") is None, "new installs must default to all libraries"
status, settings = request("GET", "/ThemeSongs/settings", token=token)
assert status == 200, f"admin settings: {status}"
assert settings.get("downloaderAvailable", settings.get("DownloaderAvailable")) is True, "bundled downloader missing"
status, strings = request("GET", "/ThemeSongs/strings/en-us", token=token)
assert status == 200 and strings["scanLibraries"] == "Scan libraries", f"English translations: {status} {strings}"
status, _ = request("GET", "/ThemeSongs/strings/fr", token=token)
assert status == 404, f"unsupported translation should fall back to English: {status}"
status, downloads = request("GET", "/ThemeSongs/downloads", token=token)
assert status == 200 and downloads.get("total", downloads.get("Total")) == 0, f"empty managed list: {status}"
status, _ = request("POST", "/ThemeSongs/settings", {"enabled": False, "libraries": []}, token)
assert status == 204, f"save settings: {status}"
selected = ["00000000-0000-0000-0000-000000000001"]
status, _ = request("POST", "/ThemeSongs/settings", {"enabled": True, "libraries": selected}, token)
assert status == 204, f"enable automatic processing: {status}"
assert_settings(token, True, selected)
subprocess.run(["docker", "compose", "-f", "tests/e2e/compose.yaml", "restart", "jellyfin"], check=True)
for _ in range(60):
    status, _ = request("GET", "/ThemeSongs/settings", token=token)
    if status == 200:
        break
    time.sleep(2)
assert status == 200, f"plugin did not restart: {status}"
assert_settings(token, True, selected)
status, before = request("GET", "/ThemeSongs/scan", token=token)
assert status == 200, f"read scan status: {status}"
status, _ = request("POST", "/ThemeSongs/settings", {"enabled": True, "libraries": []}, token)
assert status == 204, f"clear selected libraries: {status}"
status, _ = request("POST", "/Library/Refresh", token=token, timeout=120)
assert status in (200, 204), f"start Jellyfin library scan: {status}"
for _ in range(60):
    status, progress = request("GET", "/ThemeSongs/scan", token=token)
    if status == 200 and field(progress, "runId") != field(before, "runId"):
        break
    time.sleep(1)
else:
    raise AssertionError("JellyScore did not run after Jellyfin's library scan")
status, _ = request("POST", "/ThemeSongs/settings", {"enabled": False, "libraries": []}, token)
assert status == 204, f"disable automatic processing: {status}"
assert_settings(token, False, [])
for method, path in [("GET", "/ThemeSongs/downloads"), ("GET", "/ThemeSongs/strings/en-us"), ("POST", "/ThemeSongs/scan"), ("POST", "/ThemeSongs/settings")]:
    status, _ = request(method, path)
    assert status in (401, 403), f"unauthorized {path}: {status}"
print("Jellyfin 12 plugin smoke checks passed")
if os.environ.get("LIVE_YOUTUBE") == "0":
    raise SystemExit(0)
print("Creating movie and TV libraries...", flush=True)
status, existing = request("GET", "/Library/VirtualFolders", token=token)
for name, kind, media_path in (("Films", "movies", "/media/movies"), ("Shows", "tvshows", "/media/shows"),
                               ("Other films", "movies", "/media/unused")):
    if not any(folder["Name"] == name for folder in existing):
        path = "/Library/VirtualFolders?" + urllib.parse.urlencode(
            {"name": name, "collectionType": kind, "paths": media_path, "refreshLibrary": "false"}
        )
        status, _ = request("POST", path, {}, token)
        assert status == 204, f"create {name} library: {status}"
print("Waiting for movie and series indexing...", flush=True)
status, folders = request("GET", "/Library/VirtualFolders", token=token)
assert status == 200, f"list libraries: {status}"
library_ids = [next(folder["ItemId"] for folder in folders if folder["Name"] == name) for name in ("Films", "Shows")]
status, _ = request("POST", f"/Plugins/{PLUGIN}/Configuration", {"Enabled": False, "Libraries": None}, token)
assert status == 204, f"reset library selection to default: {status}"
status, settings = request("GET", "/ThemeSongs/settings", token=token)
assert status == 200 and {uuid.UUID(str(value)) for value in field(settings, "libraries")} == {
    uuid.UUID(folder["ItemId"]) for folder in folders
}, f"all libraries should be selected by default: {settings}"
status, _ = request("POST", "/ThemeSongs/settings", {"enabled": False, "libraries": library_ids}, token)
assert status == 204, f"select libraries: {status}"
assert_settings(token, False, library_ids)
status, _ = request("POST", "/Library/Refresh", token=token, timeout=120)
assert status in (200, 204), f"scan media libraries: {status}"

for attempt in range(30):
    status, items = request("GET", "/Items?Recursive=true&IncludeItemTypes=Movie,Series", token=token)
    indexed = {(item["Type"], item["Name"], item.get("ProductionYear")) for item in (items or {}).get("Items", [])}
    if status == 200 and {
        ("Movie", "Harry Potter and the Sorcerer's Stone", 2001),
        ("Movie", "Dune", 2021),
        ("Movie", "User Theme", 2000),
        ("Series", "The Office (US)", 2005),
        ("Series", "Breaking Bad", 2008),
        ("Movie", "Unselected Example", 1999),
    }.issubset(indexed):
        break
    if attempt % 5 == 0:
        print(f"Waiting for test items: {status}, indexed={indexed}", flush=True)
    time.sleep(2)
else:
    raise SystemExit(f"Test media was not indexed: {status} {items}")
movie = next(item for item in items["Items"] if "Sorcerer" in item["Name"])
user_theme = next(item for item in items["Items"] if item["Name"] == "User Theme")
first_scan = scan_until(token, "added", expect_current=True)
status, downloads = request("GET", "/ThemeSongs/downloads", token=token)
assert status == 200, f"list themes: {status}"
assert all(field(item, "itemId") != user_theme["Id"] for item in field(downloads, "items")), (
    f"user-provided theme became managed: {downloads}"
)
status, _ = request("DELETE", f"/ThemeSongs/{user_theme['Id']}", token=token)
assert status == 409, f"delete user-provided theme: {status}"
subprocess.run(["docker", "compose", "-f", "tests/e2e/compose.yaml", "exec", "-T", "jellyfin", "cmp", "-s",
                "/tmp/user-theme-original", "/media/movies/User Theme (2000)/theme.mp3"], check=True)
dune = next((item for item in field(downloads, "items") if field(item, "name") == "Dune"), None)
assert dune is not None and field(dune, "status") == "Active", f"Dune soundtrack track was not discovered: {downloads}"
theme = next((item for item in field(downloads, "items") if uuid.UUID(field(item, "itemId")) == uuid.UUID(movie["Id"])), None)
if theme is None and field(first_scan, "failed"):
    scan_until(token)
    status, downloads = request("GET", "/ThemeSongs/downloads", token=token)
    theme = next((item for item in field(downloads, "items") if uuid.UUID(field(item, "itemId")) == uuid.UUID(movie["Id"])), None)
assert theme is not None, f"test movie theme not downloaded: {downloads}"
assert field(theme, "source").startswith("https://www.youtube.com/watch?v="), theme
assert field(theme, "score") > 0, theme
assert field(theme, "status") == "Active", theme
subprocess.run(["docker", "compose", "-f", "tests/e2e/compose.yaml", "exec", "-T", "jellyfin", "test", "-f", "/tmp/theme-songs-download-retried"], check=True)
status, songs = request("GET", f"/Items/{movie['Id']}/ThemeSongs", token=token)
assert status == 200 and songs.get("TotalRecordCount", 0) > 0, f"Jellyfin cannot see downloaded theme: {status} {songs}"
scan_until(token, "alreadyThemed")
status, refreshed = request("POST", f"/ThemeSongs/{movie['Id']}/refresh", token=token, timeout=300)
assert status == 200 and field(refreshed, "result") in ("Replaced", "No replacement found"), f"refresh theme: {status} {refreshed}"
status, downloads = request("GET", "/ThemeSongs/downloads", token=token)
theme = next((item for item in field(downloads, "items") if uuid.UUID(field(item, "itemId")) == uuid.UUID(movie["Id"])), None)
assert status == 200 and theme is not None, f"refresh lost managed theme: {downloads}"
assert field(theme, "status") == "Active", downloads
status, _ = request("DELETE", f"/ThemeSongs/{movie['Id']}", token=token)
assert status == 204, f"delete managed theme: {status}"
status, downloads = request("GET", "/ThemeSongs/downloads", token=token)
assert status == 200 and all(uuid.UUID(field(item, "itemId")) != uuid.UUID(movie["Id"]) for item in field(downloads, "items")), (
    f"deleted theme still managed: {downloads}"
)
for _ in range(30):
    status, songs = request("GET", f"/Items/{movie['Id']}/ThemeSongs", token=token)
    if status == 200 and songs.get("TotalRecordCount") == 0:
        break
    time.sleep(2)
assert status == 200 and songs.get("TotalRecordCount") == 0, f"Jellyfin still sees deleted theme: {status} {songs}"
status, _ = request("POST", f"/ThemeSongs/{movie['Id']}/refresh", token=token)
assert status == 409, f"refresh deleted theme: {status}"
scan_until(token)
status, downloads = request("GET", "/ThemeSongs/downloads", token=token)
theme = next((item for item in field(downloads, "items") if uuid.UUID(field(item, "itemId")) == uuid.UUID(movie["Id"])), None)
assert status == 200 and (theme is None or field(theme, "status") == "Active"), f"rescan produced stale theme: {downloads}"

subprocess.run(["docker", "compose", "-f", "tests/e2e/compose.yaml", "exec", "-T", "jellyfin", "sh", "-c",
                'printf "%s" external-edit >> "$1"', "sh", field(dune, "path")], check=True)
subprocess.run(["docker", "compose", "-f", "tests/e2e/compose.yaml", "exec", "-T", "jellyfin", "cp",
                field(dune, "path"), "/tmp/edited-theme-original"], check=True)
status, _ = request("POST", f"/ThemeSongs/{field(dune, 'itemId')}/refresh", token=token)
assert status == 409, f"refresh externally edited theme: {status}"
status, _ = request("DELETE", f"/ThemeSongs/{field(dune, 'itemId')}", token=token)
assert status == 409, f"delete externally edited theme: {status}"
subprocess.run(["docker", "compose", "-f", "tests/e2e/compose.yaml", "exec", "-T", "jellyfin", "cmp", "-s",
                "/tmp/edited-theme-original", field(dune, "path")], check=True)
status, downloads = request("GET", "/ThemeSongs/downloads", token=token)
assert status == 200 and all(field(item, "name") != "Dune" for item in field(downloads, "items")), (
    f"externally edited theme remains managed: {downloads}"
)
subprocess.run(["docker", "compose", "-f", "tests/e2e/compose.yaml", "exec", "-T", "jellyfin", "test", "-f", field(dune, "path")], check=True)

removed = next((item for item in field(downloads, "items") if field(item, "kind") == "Series"), None)
assert removed is not None, f"no managed series to remove: {downloads}"
status, _ = request("DELETE", "/Items/" + field(removed, "itemId"), token=token)
assert status == 204, f"remove series from Jellyfin: {status}"
for _ in range(30):
    status, downloads = request("GET", "/ThemeSongs/downloads", token=token)
    if status == 200 and all(field(item, "itemId") != field(removed, "itemId") for item in field(downloads, "items")):
        break
    time.sleep(1)
assert status == 200 and all(field(item, "itemId") != field(removed, "itemId") for item in field(downloads, "items")), (
    f"removed series remains managed: {downloads}"
)
print("Movie and series scan, refresh, delete, and stale-record cleanup passed")
