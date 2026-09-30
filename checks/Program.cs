using Jellyfin.Plugin.JellyScore;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

var work = new Work("Dune", null, 2021, false);
const string licensed = "Provided to YouTube by Warner Records\nDune Main Theme · Hans Zimmer\nAlbum: Dune 2021 (Original Motion Picture Soundtrack)";
Video video(string id, string title, string description, int? length = 120) => new(id, title, description, "Soundtrack", length);
void check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
using (var page = new StreamReader(typeof(YouTube).Assembly.GetManifestResourceStream("Jellyfin.Plugin.JellyScore.config.html")!))
{
    var html = page.ReadToEnd();
    check(html.Contains("Scan after Jellyfin scans the media library", StringComparison.Ordinal) && !html.Contains("{{", StringComparison.Ordinal),
        "the bundled admin page has English fallbacks from the translation dictionary");
}

var original = video("aaaaaaaaaaa", "Dune Main Theme", licensed);
check(Matcher.Evaluate(work, original) is { Score: > 0 }, "soundtrack theme accepted");
check(Matcher.Evaluate(work, original with { UploadDate = new DateOnly(2019, 12, 31) }) is null,
    "film uploads predating the previous calendar year are rejected");
check(Matcher.Evaluate(work, original with { UploadDate = new DateOnly(2020, 1, 1) }) is not null,
    "previous-year film promotion remains eligible");
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
var shawshank = new Work("The Shawshank Redemption", null, 1994, false);
var endTitle = new Video("Q2ctsooeJBU", "End Title", "End Title · Thomas Newman The Shawshank Redemption ℗ 1994 Epic Records", "Epic Soundtrax", 246,
    Album: "The Shawshank Redemption", Track: "End Title", Artist: "Thomas Newman", ReleaseYear: 1994);
check(Matcher.Promising(shawshank, endTitle) && Matcher.Select(shawshank, [endTitle], new HashSet<string>(), new HashSet<string>(), 50)?.Video.Id == endTitle.Id,
    "named soundtrack track linked by search description reaches metadata evaluation");
check(!Matcher.Promising(shawshank, endTitle with { Description = "End Title · Thomas Newman" }),
    "a generic track title without a work link is not shortlisted");
(string? Id, string? Title, int? Year) knownFilm = ("123", shawshank.Title, 1994);
(string? Id, string? Title, int? Year) otherFilm = ("456", "Another Film", 2020);
check(Matcher.NoCompetingEdition(shawshank, "123", [knownFilm, otherFilm], []),
    "unrelated search results do not make an identified film ambiguous");
check(!Matcher.NoCompetingEdition(shawshank, "123", [knownFilm, ("456", shawshank.Title, 2020)], []),
    "a same-title remake keeps the release-year requirement");
check(!Matcher.NoCompetingEdition(shawshank, "123", [knownFilm], [shawshank.Title]) &&
    !Matcher.NoCompetingEdition(shawshank, "123", [knownFilm], Enumerable.Repeat<string?>("Other Show", 20).ToArray()),
    "same-title TV edition or truncated TV results keep the release-year requirement");
check(!Matcher.NoCompetingEdition(shawshank, "123", [otherFilm], []) &&
    !Matcher.NoCompetingEdition(shawshank, "123", [], []) &&
    !Matcher.NoCompetingEdition(shawshank, "123", Enumerable.Repeat(knownFilm, 20).ToArray(), []),
    "missing target, failed search, and truncated pages cannot establish an unambiguous edition");
var yearlessTheme = video("bbbbbbbbbbb", "The Shawshank Redemption Main Theme", "");
check(Matcher.Evaluate(shawshank, yearlessTheme) is null &&
    Matcher.Evaluate(shawshank with { NoCompetingEdition = true }, yearlessTheme) is not null,
    "catalog evidence permits a yearless theme linked in its title");
check(Matcher.Select(shawshank, [endTitle, yearlessTheme], new HashSet<string>(), new HashSet<string>(), 50)?.Video.Id == endTitle.Id &&
    Matcher.Select(shawshank with { NoCompetingEdition = true }, [endTitle, yearlessTheme], new HashSet<string>(), new HashSet<string>(), 50)?.Video.Id == yearlessTheme.Id,
    "edition lookup can promote a yearless main theme over a soundtrack fallback");
check(Matcher.Evaluate(shawshank with { NoCompetingEdition = true }, video("bbbbbbbbbbb", "Main Theme", shawshank.Title)) is null &&
    Matcher.Evaluate(shawshank with { NoCompetingEdition = true }, yearlessTheme with { Title = "The Shawshank Redemption 2026 Main Theme" }) is null,
    "catalog evidence never replaces a work link or overrides a conflicting year");
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
Matcher.RejectionReason(work, [video("bbbbbbbbbbb", "Dune 1984 Main Theme", "")], new HashSet<string>(), new HashSet<string>(), out var rejectionCode);
check(rejectionCode == "reasonEdition", "rejection also has a localized UI code");
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
var supernatural = new Work("Supernatural", null, 2005, true);
var deathScene = video("5EcsBgxXDqc", "Death's Intro... Supernatural S5E21", "", 120);
var supernaturalOpening = video("bbbbbbbbbbb", "Supernatural S5E21 Opening Theme", "", 20);
var titleCardMontage = new Video("wg0yCihdKio", "Supernatural Seasons 1-15 Main Title Cards", "Property of Warner Bros and the CW\nI don't own anything", "Tye Judy", 87,
    UploadDate: new DateOnly(2019, 10, 11));
check(Matcher.MatchStrength(-10) == 0 && Matcher.MatchStrength(150) == 100,
    "displayed match strength stays within 0-100");
check(Matcher.MatchStrength(Matcher.Evaluate(supernatural, titleCardMontage)!.Score) == 42,
    "multi-season collection has a bounded match strength of 42");
check(Matcher.Select(supernatural, [titleCardMontage], new HashSet<string>(), new HashSet<string>(), 50) is null,
    "a higher minimum skips the weak collection even when it is the only source");
Matcher.RejectionReason(supernatural, [titleCardMontage], new HashSet<string>(), new HashSet<string>(), out var strengthCode, 50);
check(strengthCode == "reasonBelowStrength", "below-minimum results have their own admin reason");
var waywardSon = new Video("DJcX6Tpv9RI", "Carry on Wayward Son - Kansas (Supernatural Main Theme)",
    "Carry On Wayward Son by Kansas, officially released in December 1976, and now the main theme of TV-show Supernatural", "Anna Lovén", 321,
    UploadDate: new DateOnly(2014, 11, 27));
var genericSupernaturalTheme = new Video("FtYRMGnj-_A", "Supernatural Theme Song With Lyrics", "Supernatural Theme Song With Lyrics", "Theme Lyric", 316,
    UploadDate: new DateOnly(2012, 6, 16));
check(Matcher.Promising(supernatural, titleCardMontage) && Matcher.Evaluate(supernatural, titleCardMontage) is { Score: > 0 },
    "multi-season title-card montages remain eligible as a fallback");
check(Matcher.Select(supernatural, [titleCardMontage], new HashSet<string>(), new HashSet<string>())?.Video.Id == titleCardMontage.Id,
    "the title-card collection can still be selected when it is the only candidate");
check(Matcher.Evaluate(supernatural, video("ccccccccccc", "Supernatural Season 15 Main Title", "", 20)) is not null,
    "one season's short main title is not mistaken for a compilation");
check(Matcher.Evaluate(supernatural, waywardSon) is not null,
    "named music explicitly identified as the TV show's theme remains eligible");
check(Matcher.Evaluate(supernatural, waywardSon with { Description = "Carry On Wayward Son by Kansas" }) is null,
    "a named recording still needs evidence that it belongs to the series");
check(Matcher.Select(supernatural, [titleCardMontage, genericSupernaturalTheme, waywardSon], new HashSet<string>(), new HashSet<string>())?.Video.Id == waywardSon.Id,
    "named series song beats a title-card montage and generic theme upload");
check(Matcher.Select(supernatural, [titleCardMontage, genericSupernaturalTheme, waywardSon], new HashSet<string>(), new HashSet<string>(), 60)?.Video.Id == waywardSon.Id,
    "thresholds filter before ranking series themes");
check(Matcher.Select(supernatural, [titleCardMontage, genericSupernaturalTheme], new HashSet<string>(), new HashSet<string>())?.Video.Id == genericSupernaturalTheme.Id,
    "a standalone theme upload beats a multi-season title-card collection");
check(!Matcher.Promising(supernatural, deathScene) && Matcher.Evaluate(supernatural, deathScene) is null,
    "episode character intro is neither shortlisted nor eligible as the series theme");
check(Matcher.Promising(supernatural, supernaturalOpening) && Matcher.Evaluate(supernatural, supernaturalOpening) is not null,
    "episode number alone does not exclude a genuine series opening");
check(Matcher.Evaluate(supernatural, video("ccccccccccc", "Supernatural Opening Scene", "", 120)) is null,
    "opening scene is not theme music");
check(Matcher.Select(supernatural, [deathScene, supernaturalOpening], new HashSet<string>(), new HashSet<string>())?.Video.Id == supernaturalOpening.Id,
    "series opening wins instead of episode scene");
var plainIntro = video("ddddddddddd", "Supernatural Intro", "", 70);
var explicitTheme = video("eeeeeeeeeee", "Supernatural Theme Song", "", 70);
check(Matcher.Select(supernatural, [plainIntro, explicitTheme], new HashSet<string>(), new HashSet<string>())?.Video.Id == explicitTheme.Id,
    "explicit series theme scores above a bare intro");
check(Matcher.Evaluate(supernatural, explicitTheme with { Title = "Supernatural Original Soundtrack OST Official Theme Song" })!.Score -
    Matcher.Evaluate(supernatural, explicitTheme)!.Score == 15,
    "weak soundtrack labels count once and official claims contribute only a small bonus");
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
var vampireFilm = new Work("Interview with the Vampire", null, 1994, false);
var filmSoundtrack = video("bbbbbbbbbbb", "Interview with the Vampire Theme", "Album: Interview with the Vampire (Original Motion Picture Soundtrack)");
check(Matcher.Evaluate(vampire, filmSoundtrack) is null, "1994 film soundtrack album cannot qualify for the TV series without a year");
check(Matcher.Evaluate(vampire, video("bbbbbbbbbbb", "Interview with the Vampire 2022 Official Main Theme Original Motion Picture Soundtrack", "")) is null,
    "a matching year and high-scoring words cannot override a conflicting film edition");
check(Matcher.Evaluate(vampireFilm, filmSoundtrack) is not null, "film soundtrack remains eligible for the film");
check(Matcher.Evaluate(vampireFilm, vampireSoundtrack) is null, "TV soundtrack cannot qualify for the film without a year");
check(Matcher.Evaluate(vampire, vampireSoundtrack) is not null, "TV soundtrack remains eligible for the series");
var genericVampireTheme = new Video("bbbbbbbbbbb", "Interview with the Vampire Official Original Soundtrack OST Main Theme", "", "Music Channel", 159);
check(Matcher.Evaluate(vampire, genericVampireTheme) is not null && Matcher.Evaluate(vampire, vampireOpening)!.Score > Matcher.Evaluate(vampire, genericVampireTheme)!.Score,
    "the verified series opening ranks above an eligible title padded with promotional words");
check(Matcher.Select(vampire, [genericVampireTheme, vampireOpening], new HashSet<string>(), new HashSet<string>())?.Video.Id == vampireOpening.Id,
    "an edition-specific short opening beats a longer generic theme with stacked promotional words");
check(Matcher.Evaluate(vampire, video("bbbbbbbbbbb", "Interview with the Vampire 2022 Opening", "My first movie edit")) is not null,
    "an incidental movie mention in a description does not override the TV edition");
var namedTvTheme = video("bbbbbbbbbbb", "Come to Me Theme | Interview with the Vampire",
    "Music from Interview with the Vampire 2022 Original Television Series Soundtrack");
check(Matcher.Evaluate(vampire, namedTvTheme) is not null,
    "a named track linked to the correct TV edition in its description remains eligible");
check(Matcher.Evaluate(vampire, namedTvTheme with { Description = "Music from Interview with the Vampire" }) is null,
    "a named theme without edition evidence remains uncertain");
var vampireFanEdit = new Video("vldqvBpuACY", "Interview With The Vampire Requiem For A Dream Theme Song",
    "Interview With The Vampire Requiem For A Dream Theme. My first movie :). I put Lestat at the end not because I don't like him, but because I wanted him to appear when the powerful music starts.", "summerrainnnn", 364,
    UploadDate: new DateOnly(2010, 2, 27));
check(Matcher.Evaluate(vampire, vampireFanEdit with { UploadDate = null }) is null,
    "another work's named theme cannot pass as the TV series theme even without an upload date");
check(Matcher.Evaluate(vampire, vampireFanEdit) is null, "the 2010 fan edit cannot be the 2022 TV series theme");
check(Matcher.Evaluate(vampire, video("bbbbbbbbbbb", "Interview with the Vampire Theme Song", "") with { UploadDate = new DateOnly(2010, 2, 27) }) is null,
    "an otherwise plausible upload from before the TV series was made is rejected");
check(Matcher.Evaluate(vampire, video("bbbbbbbbbbb", "Interview with the Vampire Theme Song", "") with { UploadDate = new DateOnly(2020, 12, 31) }) is null,
    "uploads older than the previous calendar year are too early");
check(Matcher.Evaluate(vampire, video("bbbbbbbbbbb", "Interview with the Vampire Theme Song", "") with { UploadDate = new DateOnly(2021, 1, 1) }) is not null,
    "previous-year promotional uploads remain eligible without a precise premiere date");
check(Matcher.Evaluate(vampire, video("bbbbbbbbbbb", "Interview with the Vampire Theme Song", "")) is not null,
    "missing upload dates are not treated as negative evidence");
check(Matcher.Select(vampire, [vampireFanEdit, vampireOpening], new HashSet<string>(), new HashSet<string>())?.Video.Id == vampireOpening.Id,
    "TV opening wins when a high-scoring fan edit borrows music from another work");
check(Matcher.Evaluate(new Work("Breaking Bad", null, 2008, true), video("bbbbbbbbbbb", "Breaking Bad Better Call Saul Theme Song", "")) is null,
    "other named themes are rejected independently of the series title");
check(Matcher.Evaluate(supernatural, explicitTheme) is not null, "a plain series theme without another named work remains eligible");
check(Matcher.Promising(vampire, vampireOpening), "the short 2022 opening reaches full metadata evaluation");
check(Matcher.Select(vampire, [vampireSoundtrack, vampireOpening], new HashSet<string>(), new HashSet<string>())?.Video.Id == vampireOpening.Id,
    "series opening wins over a soundtrack track");
var reliableSoundtrack = vampireSoundtrack with { Album = "Interview with the Vampire (Original Television Series Soundtrack)", Track = "Come to Me", Artist = "Daniel Hart", ReleaseYear = 2022 };
check(Matcher.Select(vampire, [vampireOpening, reliableSoundtrack], new HashSet<string>(), new HashSet<string>(), 90)?.Video.Id == reliableSoundtrack.Id,
    "a strict minimum can fall back to a verified soundtrack when a short opening is below the threshold");
check(Matcher.Select(vampire, [vampireSoundtrack], new HashSet<string>(), new HashSet<string>())?.Video.Id == vampireSoundtrack.Id,
    "soundtrack track remains a fallback when no opening is found");
check(Matcher.Evaluate(vampire, video("7jLOWfP3Lmc", "Interview with the Vampire - Opening",
    "A clip from Interview with the Vampire (1994) of the opening scene.", 164)) is null,
    "1994 film opening is not eligible for the 2022 series");
check(YouTube.FirstTrack("No tracklist here") is null, "album without a tracklist has no search hint");
check(YouTube.FirstTrack("Tracklist:\n1) First Track\n2) Next Track") == "First Track", "parenthesized track number parsed");
check(YouTube.DownloaderName(false, false, Architecture.X64) == "yt-dlp_linux", "Linux x64 binary");
check(YouTube.DownloaderName(false, false, Architecture.Arm64, true) == "yt-dlp_musllinux_aarch64", "Alpine arm64 binary");
check(YouTube.DownloaderName(true, false, Architecture.Arm64) == "yt-dlp_arm64.exe", "Windows arm64 binary");
check(YouTube.DownloaderName(false, true, Architecture.Arm64) == "yt-dlp_macos", "macOS universal binary");
check(Audio.FixedGain(-30, -9, -26) == 4, "fixed gain brings a quiet track to the default target");
check(Audio.FixedGain(-30, -1, -26) == -2, "true peak caps gain even when average loudness stays below target");
check(Audio.FixedGain(-30, -9, -20) == 6, "chosen volume changes gain while peak headroom still wins");
var measured = Audio.Stats("Integrated loudness:\n    I:         -19.3 LUFS\n    Threshold: -29.5 LUFS\nTrue peak:\n    Peak:       -3.1 dBFS");
check(measured.Loudness == -19.3 && Math.Abs(measured.TruePeak - -3.0) < 0.0001 &&
    Math.Abs(Audio.FixedGain(measured.Loudness, measured.TruePeak, -26) - -6.7) < 0.0001 &&
    Audio.FixedGain(-30, measured.TruePeak, -26) == 0,
    "fast EBU R128 analysis measures loudness and conservatively caps true peak");
check(Audio.Filter(4, 10) == "volume=4dB,afade=t=in:d=1,afade=t=out:st=9:d=1", "short track fades at both ends");
check(Audio.Filter(-2, 120) == "volume=-2dB,afade=t=in:d=1,afade=t=out:st=119:d=1", "fade-out follows actual track duration");
var scanEstimate = new ScanStatus { Running = true, Total = 12, StartedAt = DateTimeOffset.UtcNow };
check(scanEstimate.RemainingSeconds is > 239 and < 241, "first scan has an ETA before any item completes");
scanEstimate.StartedAt = DateTimeOffset.UtcNow.AddSeconds(-60);
check(scanEstimate.RemainingSeconds is > 279 and < 282, "a slow item adds one overdue interval rather than inflating every remaining item");
scanEstimate.Running = false;
check(scanEstimate.RemainingSeconds is null, "completed scans do not show an ETA");
check(!File.Exists("dist/JellyScore.zip") || System.IO.Compression.ZipFile.OpenRead("dist/JellyScore.zip").Entries.Count == 3,
    "plugin archive contains DLL, pinned version, and checksums only");
var folder = Path.Combine(Path.GetTempPath(), "theme-songs-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    var binary = Path.Combine(folder, "yt-dlp", "test-binary");
    var bytes = System.Text.Encoding.UTF8.GetBytes("verified downloader");
    var hash = Convert.ToHexString(SHA256.HashData(bytes));
    var downloads = 0;
    Task<Stream> fetch(CancellationToken _) { downloads++; return Task.FromResult<Stream>(new MemoryStream(bytes)); }
    check(await YouTube.EnsureDownloader(binary, hash, fetch, CancellationToken.None) == binary && downloads == 1,
        "first use downloads the selected binary");
    check(await YouTube.EnsureDownloader(binary, hash, fetch, CancellationToken.None) == binary && downloads == 1,
        "valid cached binary avoids another download");
    File.WriteAllText(binary, "corrupt");
    try { await YouTube.EnsureDownloader(binary, hash, _ => Task.FromResult<Stream>(new MemoryStream([1, 2, 3])), CancellationToken.None); }
    catch (IOException) { }
    check(File.ReadAllText(binary) == "corrupt" && Directory.GetFiles(Path.GetDirectoryName(binary)!, "*.tmp").Length == 0,
        "failed verification never installs an executable or leaves temporary files");
    check(await YouTube.EnsureDownloader(binary, hash, fetch, CancellationToken.None) == binary && downloads == 2 && File.ReadAllBytes(binary).SequenceEqual(bytes),
        "corrupt cached executable is replaced by a verified copy");
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
