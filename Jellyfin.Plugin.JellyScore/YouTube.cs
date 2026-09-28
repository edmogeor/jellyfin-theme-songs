using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Globalization;

namespace Jellyfin.Plugin.JellyScore;

public sealed record Work(string Title, string? OriginalTitle, int? Year, bool Series);
public sealed record Video(string Id, string Title, string Description, string Channel, int? Seconds,
    string? Album = null, string? Track = null, string? Artist = null, int? ReleaseYear = null, DateOnly? UploadDate = null);
public sealed record Choice(Video Video, string Recording, int Score, string Evidence);

public sealed class SearchFailure(string message) : Exception(message);
public sealed class DownloadFailure(string message) : IOException(message);
public sealed class SourceUnavailable(string message) : IOException(message);

public sealed class YouTube
{
    private readonly SemaphoreSlim _metadataGate = new(4);
    private static readonly SemaphoreSlim DownloaderGate = new(1);
    private static readonly HttpClient DownloaderClient = new() { Timeout = TimeSpan.FromMinutes(2) };
    private static string? _downloaderPath;
    public static string? DownloaderError { get; private set; }

    // ReSharper disable once MemberCanBePrivate.Global
    public static string DownloaderName(bool windows, bool macos, Architecture architecture, bool musl = false) => (windows, macos, architecture, musl) switch
    {
        (true, _, Architecture.X64, _) => "yt-dlp.exe",
        (true, _, Architecture.Arm64, _) => "yt-dlp_arm64.exe",
        (_, true, Architecture.X64 or Architecture.Arm64, _) => "yt-dlp_macos",
        (_, _, Architecture.X64, true) => "yt-dlp_musllinux",
        (_, _, Architecture.Arm64, true) => "yt-dlp_musllinux_aarch64",
        (_, _, Architecture.X64, _) => "yt-dlp_linux",
        (_, _, Architecture.Arm64, _) => "yt-dlp_linux_aarch64",
        _ => throw new PlatformNotSupportedException("This plugin package has no yt-dlp binary for this server architecture.")
    };

    public static bool DownloaderAvailable => File.Exists(Path.Combine(Path.GetDirectoryName(typeof(YouTube).Assembly.Location)!, "yt-dlp-version")) &&
        File.Exists(Path.Combine(Path.GetDirectoryName(typeof(YouTube).Assembly.Location)!, "SHA2-256SUMS"));

    private static async Task<string> Executable(CancellationToken ct)
    {
        if (_downloaderPath is not null) return _downloaderPath;
        await DownloaderGate.WaitAsync(ct);
        try
        {
            if (_downloaderPath is not null) return _downloaderPath;
            var directory = Path.GetDirectoryName(typeof(YouTube).Assembly.Location)!;
            var version = (await File.ReadAllTextAsync(Path.Combine(directory, "yt-dlp-version"), ct)).Trim();
            var asset = DownloaderName(OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(), RuntimeInformation.OSArchitecture,
                RuntimeInformation.RuntimeIdentifier.Contains("musl", StringComparison.OrdinalIgnoreCase));
            var checksum = File.ReadLines(Path.Combine(directory, "SHA2-256SUMS"))
                .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .FirstOrDefault(parts => parts.Length == 2 && parts[1] == asset)?[0];
            if (checksum is null || !Regex.IsMatch(version, "^[a-zA-Z0-9._-]+$") || !Regex.IsMatch(checksum, "^[a-fA-F0-9]{64}$"))
                throw new SearchFailure("Plugin package has no valid yt-dlp release or checksum for this platform.");
            var path = Path.Combine(Plugin.Instance.DownloaderFolder, version, asset);
            var url = new Uri($"https://github.com/yt-dlp/yt-dlp/releases/download/{version}/{asset}");
            _downloaderPath = await EnsureDownloader(path, checksum, token => DownloaderClient.GetStreamAsync(url, token), ct);
            DownloaderError = null;
            return _downloaderPath;
        }
        catch (Exception e) when (e is IOException or HttpRequestException or SearchFailure or UnauthorizedAccessException or PlatformNotSupportedException ||
            e is TaskCanceledException && !ct.IsCancellationRequested)
        {
            DownloaderError = $"Could not install yt-dlp for this server: {e.Message}";
            throw new SearchFailure(DownloaderError);
        }
        finally { DownloaderGate.Release(); }
    }

    public static Task<string> RetryDownloader(CancellationToken ct) => Executable(ct);

    // ReSharper disable once MemberCanBePrivate.Global
    public static async Task<string> EnsureDownloader(string path, string checksum, Func<CancellationToken, Task<Stream>> download, CancellationToken ct)
    {
        if (File.Exists(path))
        {
            await using var cached = File.OpenRead(path);
            if (string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(cached, ct)), checksum, StringComparison.OrdinalIgnoreCase))
                return path;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var input = await download(ct))
            await using (var output = File.Create(temporary)) await input.CopyToAsync(output, ct);
            await using (var file = File.OpenRead(temporary))
                if (!string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(file, ct)), checksum, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Downloaded yt-dlp checksum did not match the pinned release.");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.Move(temporary, path, true);
            return path;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task<IReadOnlyList<Video>> Search(Work work, CancellationToken ct, bool nextPage = false)
    {
        var titles = new[] { work.Title, work.OriginalTitle }.Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var videos = new Dictionary<string, Video>();
        foreach (var title in titles)
        {
            var query = work.Series ? $"{title} theme song" : $"{title} {work.Year} main theme soundtrack";
            var flat = await Flat(query, 30, ct);
            var shortlist = flat.Where(video => Matcher.Promising(work, video)).Skip(nextPage ? 8 : 0).Take(8);
            foreach (var video in await Details(shortlist, ct)) videos[video.Id] = video;
        }
        return videos.Values.ToArray();
    }

    public async Task<IReadOnlyList<Video>> SearchAlbumTrack(Work work, CancellationToken ct)
    {
        var query = $"{work.Title} {work.Year} soundtrack album";
        var flat = await Flat(query, 20, ct);
        var fullAlbum = flat.FirstOrDefault(video => video.Seconds > Matcher.MaxSeconds &&
            video.Title.Contains("album", StringComparison.OrdinalIgnoreCase) &&
            video.Title.Contains(work.Title, StringComparison.OrdinalIgnoreCase));
        if (fullAlbum is null) return [];
        var album = await Recheck(fullAlbum.Id, ct);
        // ponytail: first album track is only a search hint; expand the tracklist if measured misses warrant it.
        var firstTrack = FirstTrack(album.Description);
        if (firstTrack is null) return [];
        var soundtrack = work.Series ? "TV soundtrack" : "Original Motion Picture Soundtrack";
        var tracks = await Flat($"{firstTrack} {work.Title} {soundtrack}", 10, ct);
        return await Details(tracks.Where(video => Matcher.Promising(work, video) ||
            video.Seconds is > 0 and <= Matcher.MaxSeconds && video.Title.Contains(firstTrack, StringComparison.OrdinalIgnoreCase)).Take(4), ct);
    }

    // ReSharper disable once MemberCanBePrivate.Global
    public static string? FirstTrack(string description)
    {
        var match = Regex.Match(description, @"(?im)\btracklist:?\s*\n\s*1[.)]\s*(.+)$");
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static async Task<IReadOnlyList<Video>> Flat(string query, int count, CancellationToken ct)
    {
        var output = await Tool(["--no-warnings", "--flat-playlist", "--dump-json", "--socket-timeout", "20", "--retries", "2", $"ytsearch{count}:" + query], ct);
        var videos = new List<Video>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.StartsWith('{')) continue;
            using var doc = JsonDocument.Parse(line);
            videos.Add(Parse(doc.RootElement));
        }
        if (videos.Count == 0) throw new SearchFailure("Search returned no video details; retry when YouTube is available.");
        return videos;
    }

    private async Task<IReadOnlyList<Video>> Details(IEnumerable<Video> candidates, CancellationToken ct)
    {
        var shortlist = candidates.ToArray();
        var videos = new Video?[shortlist.Length];
        await Parallel.ForEachAsync(Enumerable.Range(0, shortlist.Length), new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = ct }, async (index, token) =>
        {
            try { videos[index] = await Recheck(shortlist[index].Id, token); }
            catch (SourceUnavailable) { }
        });
        return videos.OfType<Video>().ToArray();
    }

    private async Task<Video> Recheck(string id, CancellationToken ct)
    {
        if (!Regex.IsMatch(id, "^[a-zA-Z0-9_-]{11}$")) throw new SearchFailure("Invalid source video ID.");
        await _metadataGate.WaitAsync(ct);
        try
        {
            var output = await Tool(["--no-warnings", "--skip-download", "--dump-json", "--no-playlist", "https://www.youtube.com/watch?v=" + id], ct);
            using var doc = JsonDocument.Parse(output);
            return Parse(doc.RootElement);
        }
        finally { _metadataGate.Release(); }
    }

    public static async Task Download(string id, string path, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await Audio.Run(await Executable(ct), ["--no-playlist", "--no-progress", "--no-part", "--no-continue", "--retries", "2", "--socket-timeout", "20",
                    "-f", "bestaudio", "--max-filesize", "30M", "-o", path, "https://www.youtube.com/watch?v=" + id], ct);
                return;
            }
            catch (IOException) when (attempt < 2)
            {
                if (File.Exists(path)) File.Delete(path);
                await Task.Delay(TimeSpan.FromSeconds(2 << attempt), ct);
            }
            catch (IOException e) { throw new DownloadFailure(e.Message); }
        }
    }

    private static Video Parse(JsonElement item)
    {
        var duration = item.TryGetProperty("duration", out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0;
        var uploaded = DateOnly.TryParseExact(Text(item, "upload_date"), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date : (DateOnly?)null;
        return new Video(item.GetProperty("id").GetString()!, item.GetProperty("title").GetString()!,
            item.TryGetProperty("description", out value) ? value.GetString() ?? "" : "",
            item.TryGetProperty("channel", out value) ? value.GetString() ?? "" : "", duration > 0 ? (int)duration : null,
            Text(item, "album"), Text(item, "track"), Text(item, "artist"),
            item.TryGetProperty("release_year", out value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null, uploaded);
    }

    private static string? Text(JsonElement item, string key) => item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static async Task<string> Tool(string[] args, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try { return await Audio.Run(await Executable(ct), args, ct); }
            catch (IOException e) when (e.Message.Contains("This video is not available", StringComparison.OrdinalIgnoreCase) ||
                e.Message.Contains("Video unavailable", StringComparison.OrdinalIgnoreCase))
            { throw new SourceUnavailable(e.Message); }
            catch (IOException) when (attempt < 2) { await Task.Delay(TimeSpan.FromSeconds(2 << attempt), ct); }
        }
        throw new SearchFailure("Bundled yt-dlp failed. Check network access and update the plugin package.");
    }
}

public static partial class Matcher
{
    private const int MinSeconds = 10;
    internal const int MaxSeconds = 480;

    [GeneratedRegex(@"\b(cover|remix|fan.?edit|extended|reaction|trailer|review|full album|compilation|livestream|live stream|karaoke|piano cover|tutorials?|how to play|game|parody|tribute|ranked|top\s?10)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Reject();
    [GeneratedRegex(@"\b(?:s\d{1,2}\s*e\d{1,3}|\d{1,2}x\d{1,3}|season\s+\d+\s+episode\s+\d+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex EpisodeNumber();
    [GeneratedRegex(@"\b(?:opening|intro(?:duction)?)\s+scene\b", RegexOptions.IgnoreCase)]
    private static partial Regex IntroScene();
    [GeneratedRegex(@"\b[\p{L}]+['’]s\s+intro(?:duction)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex CharacterIntro();
    [GeneratedRegex(@"\bseasons?\s+\d+\s*(?:[-–—]|to|through|&|and)\s*\d+\b|\b(?:all|every)\s+(?:\w+\s+){0,3}(?:seasons?|title cards?|openings?|intros?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SeriesCollection();
    [GeneratedRegex(@"\b(part two|part 2|sequel)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Sequel();
    [GeneratedRegex(@"\b(opening|theme|main title|title sequence|credits|end title|intro|soundtrack|score|suite|overture|ost)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Theme();
    [GeneratedRegex(@"\b(closing|end)\s+(credits?|titles?|theme)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Closing();
    [GeneratedRegex(@"\b(opening(?!\s+scene\b)|intro|main titles?|title sequence)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SeriesOpening();
    [GeneratedRegex(@"\b(theme|opening|main title|title sequence|intro|overture)\b", RegexOptions.IgnoreCase)]
    private static partial Regex MainTheme();
    [GeneratedRegex(@"(?im)^Album:\s*(.+)$")]
    private static partial Regex AlbumLine();
    [GeneratedRegex(@"\b(19\d{2}|20\d{2})\b")]
    private static partial Regex Years();
    [GeneratedRegex(@"\b(?:motion picture|(?:movie|film)\s+(?:soundtrack|score|theme|opening|ost))\b", RegexOptions.IgnoreCase)]
    private static partial Regex FilmEdition();
    [GeneratedRegex(@"\b(?:(?:tv|television)\s+series|(?:tv|television)\s+(?:soundtrack|score|theme|opening|ost))\b", RegexOptions.IgnoreCase)]
    private static partial Regex SeriesEdition();

    private static string Normal(string value) => Regex.Replace(value.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();
    private static bool Contains(string text, string title) => (" " + Normal(text) + " ").Contains(" " + Normal(title) + " ", StringComparison.Ordinal);
    private static bool EpisodeClip(string title) => IntroScene().IsMatch(title) ||
        EpisodeNumber().IsMatch(title) && CharacterIntro().IsMatch(title);
    private static bool OtherNamedTheme(Work work, Video video)
    {
        if (!work.Series || !Regex.IsMatch(video.Title, @"\btheme\b", RegexOptions.IgnoreCase)) return false;
        var remainder = " " + Normal(video.Title) + " ";
        foreach (var title in new[] { work.Title, work.OriginalTitle }.Where(s => !string.IsNullOrWhiteSpace(s)))
            remainder = remainder.Replace(" " + Normal(title!) + " ", " ", StringComparison.Ordinal);
        remainder = Regex.Replace(remainder, @"\b(?:\d+|official|original|main|theme|song|opening|intro|title|credits|soundtrack|score|ost|tv|television|series|season)\b", " ");
        return remainder.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 3;
    }

    private static bool LinkedSeriesTheme(Work work, string description)
    {
        var text = Normal(description);
        return new[] { work.Title, work.OriginalTitle }.Where(s => !string.IsNullOrWhiteSpace(s)).Any(title =>
            Regex.IsMatch(text, $@"\b(?:main )?theme (?:of|for) (?:the )?(?:tv show|tv series|television series) {Regex.Escape(Normal(title!))}\b"));
    }

    private static int? LinkedYear(Work work, string description)
    {
        var text = Normal(description);
        foreach (var title in new[] { work.Title, work.OriginalTitle }.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            var year = Regex.Match(text, $@"(?:^| ){Regex.Escape(Normal(title!))} (19\d{{2}}|20\d{{2}})(?: |$)");
            if (year.Success) return int.Parse(year.Groups[1].Value);
        }
        return null;
    }

    public static bool Promising(Work work, Video video) =>
        (video.Seconds is not { } seconds || seconds is >= MinSeconds and <= MaxSeconds) &&
        !Reject().IsMatch(video.Title) && !EpisodeClip(video.Title) &&
        (!Sequel().IsMatch(video.Title) || Sequel().IsMatch(work.Title)) &&
        Theme().IsMatch(video.Title) &&
        (Contains(video.Title, work.Title) || work.OriginalTitle is not null && Contains(video.Title, work.OriginalTitle) || MainTheme().IsMatch(video.Title));

    private static Choice Rank(Work work, Video video, string recording, string album, int? linkedYear, bool soundtrackMatch, bool hasArtist = false)
    {
        var title = video.Title;
        var identity = title + " " + album;
        var evidence = new List<string>();
        var score = 0;
        void Add(int points, string source) { score += points; evidence.Add($"{source} {points:+#;-#;0}"); }

        if (new[] { work.Title, work.OriginalTitle }.Where(s => !string.IsNullOrWhiteSpace(s)).Any(s => Contains(video.Title, s!))) Add(30, "Work in title");
        else
        {
            var words = Normal(work.Title).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 3);
            var matchingWords = words.Count(w => Contains(video.Title, w));
            if (matchingWords > 0) Add(Math.Min(16, 8 * matchingWords), "Partial title");
        }
        if (work.Year is { } year)
        {
            if (Contains(identity, year.ToString(CultureInfo.InvariantCulture))) Add(30, "Edition year in title or album");
            else if (video.ReleaseYear == year || linkedYear == year) Add(20, "Edition year in metadata");
        }
        if (work.Series ? SeriesEdition().IsMatch(identity) : FilmEdition().IsMatch(identity)) Add(20, "Matching film/TV edition");
        if (Contains(title, "main theme") || Contains(title, "main title")) Add(20, "Main theme or title");
        else if (Contains(title, "theme")) Add(15, "Theme");
        if (!Closing().IsMatch(title) && SeriesOpening().IsMatch(title)) Add(12, "Opening or intro");
        if (Contains(title, "soundtrack") || Contains(title, "ost") || Contains(title, "score")) Add(10, "Soundtrack label");
        if (Contains(title, "official")) Add(5, "Official label");
        if (soundtrackMatch) { Add(40, "Matching soundtrack track"); if (hasArtist) Add(15, "Identified artist"); }
        if (video.Seconds is >= 30 and <= 360) Add(10, "Typical music duration");
        else if (video.Seconds is < 30 && SeriesOpening().IsMatch(title)) Add(10, "Short opening");
        else if (video.Seconds is < 30) Add(-10, "Very short recording");
        else Add(5, "Long recording");
        if (new[] { "music", "records", "soundtrack", "score", "film", "cinema" }.Any(s => video.Channel.Contains(s, StringComparison.OrdinalIgnoreCase))) Add(3, "Music channel");
        if (SeriesCollection().IsMatch(title)) Add(-30, "Multi-season collection");
        else if (title.Contains("every ", StringComparison.OrdinalIgnoreCase) || title.Contains("all ", StringComparison.OrdinalIgnoreCase) && Contains(title, "theme")) Add(-20, "Collection video");
        return new Choice(video, recording, score, string.Join("; ", evidence));
    }

    // ReSharper disable once MemberCanBePrivate.Global
    public static int MatchStrength(int score) => Math.Clamp(score, 0, 100);

    // ReSharper disable once MemberCanBePrivate.Global
    public static Choice? Evaluate(Work work, Video video) => Evaluate(work, video, out _);

    // ReSharper disable once MemberCanBePrivate.Global
    public static Choice? Evaluate(Work work, Video video, out string reason)
    {
        reason = "";
        if (video.Seconds is not { } length || length is < MinSeconds or > MaxSeconds)
        { reason = "Duration is missing or outside the theme range"; return null; }
        if (work.Year is { } workYear && video.UploadDate is { Year: var uploadYear } && uploadYear < workYear - 1)
        { reason = "Upload predates this work"; return null; }
        var lines = video.Description.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var trackIndex = Array.FindIndex(lines, s => s.Contains('·'));
        var album = video.Album ?? AlbumLine().Match(video.Description).Groups[1].Value.Trim();
        if (album.Length == 0 && trackIndex >= 0 && lines.Length > trackIndex + 1)
            album = lines[trackIndex + 1];
        var title = video.Title;
        var identityText = title + " " + album;
        if (Reject().IsMatch(title) || Reject().IsMatch(album) || EpisodeClip(title))
        { reason = "Cover, remix, sequel, or other excluded format"; return null; }
        if (Sequel().IsMatch(identityText) && !Sequel().IsMatch(work.Title))
        { reason = "Soundtrack belongs to a different sequel"; return null; }
        var workTitles = new[] { work.Title, work.OriginalTitle }.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        if (!workTitles.Any(s => Contains(identityText, s!) || Contains(video.Description, s!)))
        { reason = "Title or description does not identify this work"; return null; }
        var linkedYear = LinkedYear(work, video.Description);
        if (work.Year is { } year && (video.ReleaseYear is { } releaseYear && releaseYear != year ||
            linkedYear is { } descriptionYear && descriptionYear != year ||
            Years().Matches(identityText).Select(m => int.Parse(m.Value)).Any(y => y != year)))
        { reason = "Different release year or adaptation"; return null; }
        if (work.Series ? FilmEdition().IsMatch(identityText) : SeriesEdition().IsMatch(identityText))
        { reason = "Soundtrack belongs to a different film or series edition"; return null; }
        // Unknown edition/year is deliberately insufficient for ambiguous remakes.
        if (work.Year is not null && video.ReleaseYear is null && linkedYear is null && !Years().IsMatch(identityText) && Normal(work.Title).Split(' ').Length <= 3 &&
            !(work.Series && Contains(title, work.Title)) && !(Sequel().IsMatch(work.Title) && Contains(title, work.Title)))
        { reason = "Release year missing for an ambiguous title"; return null; }
        var albumMatches = Contains(album, work.Title) || work.OriginalTitle is not null && Contains(album, work.OriginalTitle);
        var parts = trackIndex >= 0 ? lines[trackIndex].Split('·', StringSplitOptions.TrimEntries) : [];
        var track = video.Track ?? (parts.Length >= 2 ? parts[0] : null);
        var artist = video.Artist ?? (parts.Length >= 2 ? parts[1] : null);
        var soundtrackTrack = albumMatches && track is not null && (Contains(title, track) || Contains(track, title));
        if (!soundtrackTrack && OtherNamedTheme(work, video) && !(albumMatches && SeriesEdition().IsMatch(album)) &&
            !(work.Year is { } seriesYear && linkedYear == seriesYear && SeriesEdition().IsMatch(video.Description)) &&
            !LinkedSeriesTheme(work, video.Description))
        { reason = "Title names a different theme"; return null; }
        if (!Theme().IsMatch(title) && !soundtrackTrack)
        { reason = "Neither the title nor a matching soundtrack identifies this as music"; return null; }
        var candidate = soundtrackTrack
            ? Rank(work, video, Normal(track!) + "|" + Normal(artist ?? "") + "|" + Normal(album) + "|" + work.Year, album, linkedYear, true, artist is not null)
            : Rank(work, video, Normal(title) + "|" + work.Year, album, linkedYear, false);
        if (candidate.Score <= 0) { reason = "Ranking score is too low"; return null; }
        return candidate;
    }

    // ReSharper disable once UnusedMember.Global
    public static string RejectionReason(Work work, IEnumerable<Video> videos, IReadOnlySet<string> excludedVideos, IReadOnlySet<string> excludedRecordings) =>
        RejectionReason(work, videos, excludedVideos, excludedRecordings, out _);

    public static string RejectionReason(Work work, IEnumerable<Video> videos, IReadOnlySet<string> excludedVideos, IReadOnlySet<string> excludedRecordings, out string code, int minimumMatchStrength = 0)
    {
        code = "reasonNoMatch";
        var reasons = new List<string>();
        var eligible = new List<Choice>();
        foreach (var video in videos)
        {
            var choice = Evaluate(work, video, out var reason);
            if (choice is null) reasons.Add(reason);
            else eligible.Add(choice);
        }
        if (eligible.Count > 0)
        {
            if (eligible.All(c => excludedVideos.Contains(c.Video.Id) || excludedRecordings.Contains(c.Recording)))
            {
                code = "reasonPreviouslyUsed";
                return "Only previously used recordings were found";
            }
            if (eligible.Where(c => !excludedVideos.Contains(c.Video.Id) && !excludedRecordings.Contains(c.Recording))
                .All(c => MatchStrength(c.Score) < minimumMatchStrength))
            {
                code = "reasonBelowStrength";
                return "Only recordings below the minimum match strength were found";
            }
            return "Eligible recordings were found";
        }
        if (reasons.Count == 0) { code = "reasonNoResults"; return "No search results passed the title and duration shortlist"; }
        var groups = reasons.GroupBy(reason => reason).OrderByDescending(group => group.Count()).ToArray();
        code = groups[0].Key switch
        {
            "Duration is missing or outside the theme range" => "reasonDuration",
            "Cover, remix, sequel, or other excluded format" => "reasonExcludedFormat",
            "Soundtrack belongs to a different sequel" or "Different release year or adaptation" or "Upload predates this work" or
                "Soundtrack belongs to a different film or series edition" or
                "Release year missing for an ambiguous title" => "reasonEdition",
            "Title or description does not identify this work" or "Title names a different theme" => "reasonWrongWork",
            "Neither the title nor a matching soundtrack identifies this as music" => "reasonNotMusic",
            "Ranking score is too low" => "reasonLowScore",
            _ => "reasonNoMatch"
        };
        var counts = groups.Take(3).Select(group => $"{group.Count()} {group.Key}");
        return $"Rejected {reasons.Count} candidates: {string.Join("; ", counts)}";
    }

    public static Choice? Select(Work work, IEnumerable<Video> videos, IReadOnlySet<string> excludedVideos, IReadOnlySet<string> excludedRecordings, int minimumMatchStrength = 0)
    {
        var choices = videos.Where(v => !excludedVideos.Contains(v.Id)).Select(v => Evaluate(work, v))
            .OfType<Choice>().Where(c => !excludedRecordings.Contains(c.Recording) && MatchStrength(c.Score) >= minimumMatchStrength).ToArray();
        if (work.Series)
        {
            var openings = choices.Where(c => !Closing().IsMatch(c.Video.Title) &&
                (SeriesOpening().IsMatch(c.Video.Title) || Contains(c.Video.Title, "theme"))).ToArray();
            if (openings.Length > 0) choices = openings;
        }
        else
        {
            var mainThemes = choices.Where(c => !Closing().IsMatch(c.Video.Title) && MainTheme().IsMatch(c.Video.Title)).ToArray();
            var closingThemes = choices.Where(c => Closing().IsMatch(c.Video.Title)).ToArray();
            if (mainThemes.Length > 0) choices = mainThemes;
            else if (closingThemes.Length > 0) choices = closingThemes;
        }
        return choices.OrderByDescending(c => c.Score).FirstOrDefault();
    }
}
