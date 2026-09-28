using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;

namespace Jellyfin.Plugin.JellyScore;

public sealed record Work(string Title, string? OriginalTitle, int? Year, bool Series);
public sealed record Video(string Id, string Title, string Description, string Channel, int? Seconds,
    string? Album = null, string? Track = null, string? Artist = null, int? ReleaseYear = null);
public sealed record Choice(Video Video, string Recording, int Score, string Evidence);

public sealed class SearchFailure(string message) : Exception(message);
public sealed class DownloadFailure(string message) : IOException(message);
public sealed class SourceUnavailable(string message) : IOException(message);

public sealed class YouTube
{
    private readonly SemaphoreSlim _metadataGate = new(4);

    // ReSharper disable once MemberCanBePrivate.Global
    public static string DownloaderName(bool windows, bool macos, Architecture architecture, bool musl = false) => (windows, macos, architecture, musl) switch
    {
        (true, _, Architecture.X64, _) => "yt-dlp-windows-x64.exe",
        (true, _, Architecture.Arm64, _) => "yt-dlp-windows-arm64.exe",
        (_, true, Architecture.X64 or Architecture.Arm64, _) => "yt-dlp-macos",
        (_, _, Architecture.X64, true) => "yt-dlp-linux-musl-x64",
        (_, _, Architecture.Arm64, true) => "yt-dlp-linux-musl-arm64",
        (_, _, Architecture.X64, _) => "yt-dlp-linux-x64",
        (_, _, Architecture.Arm64, _) => "yt-dlp-linux-arm64",
        _ => throw new PlatformNotSupportedException("This plugin package has no yt-dlp binary for this server architecture.")
    };

    public static string DownloaderPath => Path.Combine(Path.GetDirectoryName(typeof(YouTube).Assembly.Location)!,
        DownloaderName(OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(), RuntimeInformation.OSArchitecture,
            RuntimeInformation.RuntimeIdentifier.Contains("musl", StringComparison.OrdinalIgnoreCase)));

    private static string Executable()
    {
        var path = DownloaderPath;
        if (!File.Exists(path)) throw new IOException($"Bundled yt-dlp executable {Path.GetFileName(path)} is missing from the plugin directory.");
        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(path);
            if ((mode & UnixFileMode.UserExecute) == 0)
                File.SetUnixFileMode(path, mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }
        return path;
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
        var fullAlbum = flat.FirstOrDefault(video => video.Seconds > (work.Series ? 300 : 480) &&
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
            video.Seconds is > 0 and <= 480 && video.Title.Contains(firstTrack, StringComparison.OrdinalIgnoreCase)).Take(4), ct);
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
                await Audio.Run(Executable(), ["--no-playlist", "--no-progress", "--no-part", "--no-continue", "--retries", "2", "--socket-timeout", "20",
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
        return new Video(item.GetProperty("id").GetString()!, item.GetProperty("title").GetString()!,
            item.TryGetProperty("description", out value) ? value.GetString() ?? "" : "",
            item.TryGetProperty("channel", out value) ? value.GetString() ?? "" : "", duration > 0 ? (int)duration : null,
            Text(item, "album"), Text(item, "track"), Text(item, "artist"),
            item.TryGetProperty("release_year", out value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null);
    }

    private static string? Text(JsonElement item, string key) => item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static async Task<string> Tool(string[] args, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try { return await Audio.Run(Executable(), args, ct); }
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
    [GeneratedRegex(@"\b(cover|remix|fan.?edit|extended|reaction|trailer|review|full album|compilation|livestream|live stream|karaoke|piano cover|tutorials?|how to play|game|parody|tribute|ranked|top\s?10)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Reject();
    [GeneratedRegex(@"\b(part two|part 2|sequel)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Sequel();
    [GeneratedRegex(@"\b(opening|theme|main title|title sequence|credits|end title|intro|soundtrack|score|suite|overture|ost)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Theme();
    [GeneratedRegex(@"\b(closing|end)\s+(credits?|titles?|theme)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Closing();
    [GeneratedRegex(@"\b(theme|opening|main title|title sequence|intro|overture)\b", RegexOptions.IgnoreCase)]
    private static partial Regex MainTheme();
    [GeneratedRegex(@"(?im)^Album:\s*(.+)$")]
    private static partial Regex AlbumLine();
    [GeneratedRegex(@"\b(19\d{2}|20\d{2})\b")]
    private static partial Regex Years();

    private static string Normal(string value) => Regex.Replace(value.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();
    private static bool Contains(string text, string title) => (" " + Normal(text) + " ").Contains(" " + Normal(title) + " ", StringComparison.Ordinal);

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
        (video.Seconds is not { } seconds || seconds >= (work.Series ? 10 : 20) && seconds <= (work.Series ? 300 : 480)) &&
        !Reject().IsMatch(video.Title) && (!Sequel().IsMatch(video.Title) || Sequel().IsMatch(work.Title)) &&
        Theme().IsMatch(video.Title) &&
        (Contains(video.Title, work.Title) || work.OriginalTitle is not null && Contains(video.Title, work.OriginalTitle) || MainTheme().IsMatch(video.Title));

    private static Choice Rank(Work work, Video video, string recording, bool soundtrackMatch, bool hasArtist = false)
    {
        var title = video.Title.ToLowerInvariant();
        var channel = video.Channel.ToLowerInvariant();
        var evidence = new List<string>();
        var score = 0;
        void Add(int points, string source) { score += points; evidence.Add($"{source} {points:+#;-#;0}"); }

        if (new[] { work.Title, work.OriginalTitle }.Where(s => !string.IsNullOrWhiteSpace(s)).Any(s => Contains(video.Title, s!))) Add(30, "Work in title");
        else
        {
            var words = Normal(work.Title).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 3);
            var matchingWords = words.Count(w => Contains(video.Title, w));
            if (matchingWords > 0) Add(8 * matchingWords, "Partial title");
        }
        if (title.Contains("main theme")) Add(20, "Main theme");
        else if (title.Contains("theme")) Add(15, "Theme");
        if (title.Contains("official")) Add(10, "Official in title");
        if (title.Contains("soundtrack")) Add(12, "Soundtrack");
        if (Contains(title, "ost")) Add(12, "OST");
        if (title.Contains("original score")) Add(12, "Original score");
        if (title.Contains("score")) Add(8, "Score");
        if (title.Contains("original")) Add(5, "Original");
        var minutes = video.Seconds!.Value / 60d;
        if (minutes is >= 1 and <= 6) Add(15, "Duration");
        else if (minutes is >= 0.5 and <= 10) Add(8, "Duration");
        else Add(-20, "Duration");
        if (new[] { "music", "records", "soundtrack", "score", "film", "cinema" }.Any(channel.Contains)) Add(8, "Music channel");
        if (title.Contains("every ") || title.Contains("all ") && title.Contains("theme")) Add(-20, "Collection video");
        if (soundtrackMatch) { Add(40, "Matching soundtrack album"); if (hasArtist) Add(10, "Track and artist"); }
        return new Choice(video, recording, score, string.Join("; ", evidence));
    }

    // ReSharper disable once MemberCanBePrivate.Global
    public static Choice? Evaluate(Work work, Video video) => Evaluate(work, video, out _);

    // ReSharper disable once MemberCanBePrivate.Global
    public static Choice? Evaluate(Work work, Video video, out string reason)
    {
        reason = "";
        if (video.Seconds is not { } length || length < (work.Series ? 10 : 20) || length > (work.Series ? 300 : 480))
        { reason = "Duration is missing or outside the theme range"; return null; }
        var lines = video.Description.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var trackIndex = Array.FindIndex(lines, s => s.Contains('·'));
        var album = video.Album ?? AlbumLine().Match(video.Description).Groups[1].Value.Trim();
        if (album.Length == 0 && trackIndex >= 0 && lines.Length > trackIndex + 1)
            album = lines[trackIndex + 1];
        var title = video.Title;
        var identityText = title + " " + album;
        if (Reject().IsMatch(title) || Reject().IsMatch(album)) { reason = "Cover, remix, sequel, or other excluded format"; return null; }
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
        // Unknown edition/year is deliberately insufficient for ambiguous remakes.
        if (work.Year is not null && video.ReleaseYear is null && linkedYear is null && !Years().IsMatch(identityText) && Normal(work.Title).Split(' ').Length <= 3 &&
            !(work.Series && Contains(title, work.Title)) && !(Sequel().IsMatch(work.Title) && Contains(title, work.Title)))
        { reason = "Release year missing for an ambiguous title"; return null; }
        var albumMatches = Contains(album, work.Title) || work.OriginalTitle is not null && Contains(album, work.OriginalTitle);
        var parts = trackIndex >= 0 ? lines[trackIndex].Split('·', StringSplitOptions.TrimEntries) : [];
        var track = video.Track ?? (parts.Length >= 2 ? parts[0] : null);
        var artist = video.Artist ?? (parts.Length >= 2 ? parts[1] : null);
        var soundtrackTrack = albumMatches && track is not null && (Contains(title, track) || Contains(track, title));
        if (!Theme().IsMatch(title) && !soundtrackTrack)
        { reason = "Neither the title nor a matching soundtrack identifies this as music"; return null; }
        var candidate = soundtrackTrack
            ? Rank(work, video, Normal(track!) + "|" + Normal(artist ?? "") + "|" + Normal(album) + "|" + work.Year, true, artist is not null)
            : Rank(work, video, Normal(title) + "|" + work.Year, false);
        if (candidate.Score <= 0) { reason = "Ranking score is too low"; return null; }
        return candidate;
    }

    public static string RejectionReason(Work work, IEnumerable<Video> videos, IReadOnlySet<string> excludedVideos, IReadOnlySet<string> excludedRecordings)
    {
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
                return "Only previously used recordings were found";
            return "Eligible recordings were found";
        }
        if (reasons.Count == 0) return "No search results passed the title and duration shortlist";
        var counts = reasons.GroupBy(reason => reason).OrderByDescending(group => group.Count())
            .Take(3).Select(group => $"{group.Count()} {group.Key}");
        return $"Rejected {reasons.Count} candidates: {string.Join("; ", counts)}";
    }

    public static Choice? Select(Work work, IEnumerable<Video> videos, IReadOnlySet<string> excludedVideos, IReadOnlySet<string> excludedRecordings)
    {
        var choices = videos.Where(v => !excludedVideos.Contains(v.Id)).Select(v => Evaluate(work, v))
            .OfType<Choice>().Where(c => !excludedRecordings.Contains(c.Recording)).ToArray();
        if (!work.Series)
        {
            var mainThemes = choices.Where(c => !Closing().IsMatch(c.Video.Title) && MainTheme().IsMatch(c.Video.Title)).ToArray();
            var closingThemes = choices.Where(c => Closing().IsMatch(c.Video.Title)).ToArray();
            if (mainThemes.Length > 0) choices = mainThemes;
            else if (closingThemes.Length > 0) choices = closingThemes;
        }
        return choices.OrderByDescending(c => c.Score).FirstOrDefault();
    }
}
