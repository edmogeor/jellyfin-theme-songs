using Jellyfin.Plugin.JellyScore;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

var work = new Work("Dune", null, 2021, false);
const string licensed = "Provided to YouTube by Warner Records\nDune Main Theme · Hans Zimmer\nAlbum: Dune 2021 (Original Motion Picture Soundtrack)";
Video video(string id, string title, string description, int? length = 120) => new(id, title, description, "Soundtrack", length);
void check(bool condition, string reason) { if (!condition) throw new Exception(reason); }

var original = video("aaaaaaaaaaa", "Dune Main Theme", licensed);
check(Matcher.Evaluate(work, original) is { Score: > 0 }, "soundtrack theme accepted");
check(Matcher.Evaluate(work, video("bbbbbbbbbbb", "Dune Part Two Main Theme", licensed)) is null, "sequel rejected");
var partTwo = new Work("Dune: Part Two", null, 2024, false);
var partTwoVideo = video("COELrJTyosw", "Dune: Part Two Soundtrack | Only I Will Remain - Hans Zimmer | WaterTower", "Only I Will Remain, from the Official Soundtrack of Dune: Part Two", 404);
check(Matcher.Evaluate(work, partTwoVideo) is null, "Part Two soundtrack is not the first Dune film");
check(Matcher.Evaluate(partTwo, partTwoVideo) is { Score: > 0 }, "Part Two soundtrack can match Part Two without a year in the title");
check(Matcher.Evaluate(work, video("bbbbbbbbbbb", "Dune Main Theme", licensed.Replace("2021", "1984"))) is null, "adaptation rejected");
check(Matcher.Evaluate(work, video("bbbbbbbbbbb", "Dune Main Theme", licensed, 481)) is null, "overlong video rejected");
check(Matcher.Evaluate(work, video("bbbbbbbbbbb", "Dune Main Theme", licensed, null)) is null, "unknown duration rejected");
check(Matcher.Evaluate(work, video("bbbbbbbbbbb", "Dune 2021 Main Theme", "", 10)) is not null, "movie minimum duration included");
check(Matcher.Evaluate(work, video("bbbbbbbbbbb", "Dune 2021 Main Theme", "", 9)) is null, "movie below minimum duration rejected");
check(Matcher.Evaluate(work, video("bbbbbbbbbbb", "Dune 2021 Main Theme", "", 480)) is not null, "movie maximum duration included");
check(Matcher.Evaluate(work, video("bbbbbbbbbbb", "Dune Main Theme", "Dune 2021 theme")) is { Score: > 0 }, "description resolves edition");
check(Matcher.Evaluate(work, video("bbbbbbbbbbb", "Dune Main Theme", "Dune theme")) is null, "ambiguous edition rejected");
check(Matcher.Evaluate(work, video("bbbbbbbbbbb", "Dune 2021 Main Theme", "")) is { Score: > 0 }, "theme identified by title without distributor metadata");
check(Matcher.Evaluate(work, video("bbbbbbbbbbb", "Dune 2021 Original Soundtrack", "")) is { Score: > 0 }, "soundtrack title accepted without theme keyword");
check(Matcher.Evaluate(work, video("bbbbbbbbbbb", "Dune 2021 OST", "")) is { Score: > 0 }, "OST title accepted");
var dream = video("M-bWFbJlwXk", "Dream of Arrakis", "", 189) with
{ Album = "Dune (Original Motion Picture Soundtrack)", Track = "Dream of Arrakis", Artist = "Hans Zimmer", ReleaseYear = 2021 };
check(YouTube.FirstTrack("DUNE Official Soundtrack\nTracklist:\n1. Dream of Arrakis\n2. Herald of the Change") == "Dream of Arrakis",
    "album tracklist supplies a generic search hint");
check(Matcher.Promising(work, video("bbbbbbbbbbb", "Dune 2021 Main Theme", "", 120)), "flat theme is shortlisted");
check(!Matcher.Promising(work, video("bbbbbbbbbbb", "Dune 2021 scene", "", 120)), "scene clip avoids full metadata fetch");
check(!Matcher.Promising(work, video("bbbbbbbbbbb", "Dune 2021 Full Album", "", 4460)), "full album is not shortlisted for download");
check(Matcher.Select(work, [dream], new HashSet<string>(), new HashSet<string>())?.Video.Id == dream.Id,
    "single soundtrack track is eligible without theme in its title");
var soundtrackUpload = video("Phf-AC28SCY", "Dream of Arrakis | Dune OST",
    "Music from Dune (2021) distributed by Warner Bros.\nDune (Original Motion Picture Soundtrack) by Hans Zimmer.", 190);
check(Matcher.Evaluate(work, soundtrackUpload) is { Score: > 0 }, "description explicitly links film and year without structured album metadata");
check(Matcher.Evaluate(new Work("Dune", null, 1984, false), soundtrackUpload) is null, "description year rejects wrong Dune adaptation");
check(Matcher.Evaluate(new Work("Dune", null, 1984, false), dream) is null, "soundtrack release year rejects wrong adaptation");
check(Matcher.Evaluate(work, dream with { Album = null }) is null, "named soundtrack track still needs a work link");
var closing = video("bbbbbbbbbbb", "Dune 2021 Official Closing Credits Original Soundtrack", "");
check(Matcher.Select(work, [closing], new HashSet<string>(), new HashSet<string>())?.Video.Id == closing.Id,
    "closing credits are eligible when no main theme exists");
check(Matcher.Select(work, [closing, video("ccccccccccc", "Dune 2021 Main Theme", "")], new HashSet<string>(), new HashSet<string>())?.Video.Id == "ccccccccccc",
    "main theme wins over closing credits regardless of ranking signals");
check(Matcher.Select(work, [dream, video("ccccccccccc", "Dune 2021 Main Theme", "")], new HashSet<string>(), new HashSet<string>())?.Video.Id == "ccccccccccc",
    "main theme wins over soundtrack track fallback");
check(Matcher.Evaluate(work, video("bbbbbbbbbbb", "Dune 1984 Main Theme", "")) is null, "wrong edition without metadata rejected");
check(Matcher.Evaluate(work, video("bbbbbbbbbbb", "Dune 2021 Main Theme cover", "")) is null, "cover without metadata rejected");
check(Matcher.Evaluate(work, video("bbbbbbbbbbb", "Top 10 Dune 2021 Themes", "")) is null, "ranking video rejected");
check(Matcher.Select(work, [original, video("bbbbbbbbbbb", "Dune Main Theme", licensed.Replace("Hans Zimmer", "Other Artist"))],
    new HashSet<string>(), new HashSet<string>())?.Video.Id == original.Id, "first equally ranked recording selected");
var chosen = Matcher.Evaluate(work, original)!;
check(Matcher.Select(work, [original], new HashSet<string>(), new HashSet<string> { chosen.Recording }) is null, "refresh excludes installed recording");
check(Matcher.Select(work, [original, original with { Id = "bbbbbbbbbbb" }], new HashSet<string>(), new HashSet<string>()) is not null,
    "duplicate uploads of one recording do not create an ambiguous tie");
check(Matcher.Select(work, [original, original with { Id = "bbbbbbbbbbb" }], new HashSet<string> { original.Id }, new HashSet<string>())?.Video.Id == "bbbbbbbbbbb",
    "excluding one upload leaves another source of the same recording");
check(Matcher.RejectionReason(work, [original], new HashSet<string>(), new HashSet<string> { chosen.Recording }) ==
    "Only previously used recordings were found", "excluded recording has distinct reason");
check(Matcher.RejectionReason(work, [video("bbbbbbbbbbb", "Dune 1984 Main Theme", "")], new HashSet<string>(), new HashSet<string>()).Contains("Different release year"),
    "rejected edition reports why");
var oak = new Work("The End of Oak Street", null, 2026, false);
var tutorial = new Video("GneFZPhfN7o", "The End of Oak Street – Main Theme | Piano Tutorial (Synthesia)",
    "Learn how to play the Main Theme from The End of Oak Street (2026) on piano with this Synthesia tutorial.", "Noud van Harskamp", 123);
check(!Matcher.Promising(oak, tutorial), "piano tutorial is not shortlisted");
check(Matcher.Evaluate(oak, tutorial) is null, "piano tutorial cannot be downloaded even when full metadata is available");
Video oakTrack(string id, string title, int seconds) => new(id, title, "Composed by Michael Giacchino.", "OfficialMovieSoundtrack", seconds);
var endOfOakSuite = oakTrack("48Jx-37AFVY", "27. The End of Oak Suite (The End of Oak Street Soundtrack)", 286);
var mainOnEndOfDays = oakTrack("KzScQcXFImg", "26. Main on End of Days (The End of Oak Street Soundtrack)", 105);
check(Matcher.Promising(oak, endOfOakSuite) && Matcher.Promising(oak, mainOnEndOfDays),
    "End of Oak Street soundtrack tracks pass the search shortlist");
check(Matcher.Evaluate(oak, mainOnEndOfDays)?.Score == Matcher.Evaluate(oak, endOfOakSuite)?.Score,
    "End of Oak Street soundtrack recordings tie on ranking evidence");
check(Matcher.Select(oak, [endOfOakSuite, mainOnEndOfDays], new HashSet<string>(), new HashSet<string>())?.Video.Id == endOfOakSuite.Id,
    "End of Oak Street tie selects the first eligible recording");
check(Matcher.Select(oak, [endOfOakSuite, mainOnEndOfDays], new HashSet<string> { endOfOakSuite.Id }, new HashSet<string>())?.Video.Id == mainOnEndOfDays.Id,
    "excluded source leaves the other soundtrack recording eligible");
var harry = new Work("Harry Potter and the Sorcerer's Stone", null, 2001, false);
var named = video("wtHra9tFISY", "Hedwig's Theme", "Provided to YouTube by Atlantic Records\n\nHedwig's Theme · John Williams\n\nHarry Potter and The Sorcerer's Stone Original Motion Picture Soundtrack\n\n℗ 2001 Warner Records Inc.", 309);
check(Matcher.Promising(harry, named), "named themes remain in the cheap shortlist");
check(Matcher.Evaluate(harry, named) is { Score: > 0 }, "named theme linked via soundtrack album");
var alternate = video("bbbbbbbbbbb", "Harry Potter and the Sorcerer's Stone 2001 Main Theme", "");
check(Matcher.Select(harry, [alternate, named],
    new HashSet<string>(), new HashSet<string>())?.Video.Id == named.Id, "unique higher-scoring soundtrack beats title-only candidate");
check(Matcher.Select(harry, [alternate, named], new HashSet<string> { named.Id }, new HashSet<string>())?.Video.Id == alternate.Id,
    "failed source can fall back to a different eligible recording");
check(Matcher.Evaluate(harry, named with { Description = named.Description.Replace("Provided to YouTube by Atlantic Records\n\n", "") }) is { Score: > 0 },
    "named theme linked via soundtrack album without distributor metadata");
check(Matcher.Evaluate(harry, video("ccccccccccc", "Hedwig's Theme", "Theme from Harry Potter and the Sorcerer's Stone", 309)) is { Score: > 0 },
    "named theme linked by description without album or artist");
check(Matcher.Evaluate(harry, video("ccccccccccc", "Hedwig's Theme", "", 309)) is null,
    "search term alone does not establish which film a named track belongs to");
var office = new Work("The Office (US)", null, 2005, true);
check(Matcher.Evaluate(office, video("bbbbbbbbbbb", "The Office (US) Opening Credits", "", 70)) is { Score: > 0 },
    "series with an explicit regional qualifier need not repeat the premiere year");
check(Matcher.Evaluate(office, video("bbbbbbbbbbb", "The Office UK Opening Credits", "", 70)) is null,
    "different regional version rejected");
check(Matcher.Evaluate(office, video("bbbbbbbbbbb", "The Office (US) Opening Credits", "", 10)) is not null,
    "series minimum duration included");
check(Matcher.Promising(office, video("bbbbbbbbbbb", "The Office (US) Opening Credits", "", 480)) &&
    Matcher.Evaluate(office, video("bbbbbbbbbbb", "The Office (US) Opening Credits", "", 480)) is not null,
    "series shares the movie maximum duration");
check(Matcher.Evaluate(office, video("bbbbbbbbbbb", "The Office (US) Opening Credits", "", 481)) is null,
    "series above maximum duration rejected");
var vampire = new Work("Interview with the Vampire", null, 2022, true);
var vampireOpening = new Video("JPeuE8uh9FY", "Interview with the Vampire (1 season) | 2022 | Opening", "", "Илья Якуба", 22, ReleaseYear: 2022);
var vampireSoundtrack = new Video("NWTRlUYij6M", "Come to Me | Interview with the Vampire (Original Television Series Soundtrack)",
    "Music video by Daniel Hart performing Come to Me. (C) 2022 AMC Film Holdings LLC", "SonySoundtracksVEVO", 159);
check(Matcher.Promising(vampire, vampireOpening), "the short 2022 opening reaches full metadata evaluation");
check(Matcher.Evaluate(vampire, vampireOpening)?.Score < Matcher.Evaluate(vampire, vampireSoundtrack)?.Score,
    "the short opening scores below a full soundtrack track");
check(Matcher.Select(vampire, [vampireSoundtrack, vampireOpening], new HashSet<string>(), new HashSet<string>())?.Video.Id == vampireOpening.Id,
    "series opening wins over a higher-scoring soundtrack track");
check(Matcher.Select(vampire, [vampireSoundtrack], new HashSet<string>(), new HashSet<string>())?.Video.Id == vampireSoundtrack.Id,
    "soundtrack track remains a fallback when no opening is found");
check(Matcher.Evaluate(vampire, video("7jLOWfP3Lmc", "Interview with the Vampire - Opening",
    "A clip from Interview with the Vampire (1994) of the opening scene.", 164)) is null,
    "1994 film opening is not eligible for the 2022 series");
check(YouTube.FirstTrack("No tracklist here") is null, "album without a tracklist has no search hint");
check(YouTube.FirstTrack("Tracklist:\n1) First Track\n2) Next Track") == "First Track", "parenthesized track number parsed");
check(YouTube.DownloaderName(false, false, Architecture.X64) == "yt-dlp-linux-x64", "Linux x64 binary");
check(YouTube.DownloaderName(false, false, Architecture.Arm64, true) == "yt-dlp-linux-musl-arm64", "Alpine arm64 binary");
check(YouTube.DownloaderName(true, false, Architecture.Arm64) == "yt-dlp-windows-arm64.exe", "Windows arm64 binary");
check(YouTube.DownloaderName(false, true, Architecture.Arm64) == "yt-dlp-macos", "macOS universal binary");
check(Audio.FixedGain(-24, -9) == 6, "fixed gain brings a quiet track to -18 LUFS and -3 dBTP");
check(Audio.FixedGain(-24, -1) == -2, "true peak caps gain even when average loudness stays below target");
check(!File.Exists("dist/JellyScore.zip") || System.IO.Compression.ZipFile.OpenRead("dist/JellyScore.zip").Entries.Count == 8, "one archive contains DLL and seven executables");
var folder = Path.Combine(Path.GetTempPath(), "theme-songs-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    var path = Path.Combine(folder, "theme.mp3");
    File.WriteAllText(path, "plugin theme");
    var managed = new ManagedTheme { Folder = folder, Path = path, Hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) };
    check(ThemeService.Status(managed) == "Active", "unchanged theme is active");
    var other = Path.Combine(folder, "other.mp3");
    File.Copy(path, other);
    check(ThemeService.Status(new ManagedTheme { Folder = folder, Path = other, Hash = managed.Hash }) == "Missing or externally modified",
        "identical audio at another path is not managed");
    File.WriteAllText(path, "user edited theme");
    check(ThemeService.Status(managed) == "Missing or externally modified", "edited theme is not active");
    File.Delete(path);
    check(ThemeService.Status(managed) == "Missing or externally modified", "deleted theme is not active");
}
finally { Directory.Delete(folder, true); }
Console.WriteLine("Theme checks passed");
