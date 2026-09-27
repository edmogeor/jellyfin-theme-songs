using System.Collections.Concurrent;
using System.Security.Cryptography;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;

namespace Jellyfin.Plugin.JellyScore;

public sealed class ThemeService(ILibraryManager library, IProviderManager providers, IFileSystem fileSystem, IMediaEncoder encoder, Store store, YouTube youtube)
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".m4a", ".flac", ".ogg", ".opus", ".wav", ".wma", ".aac" };
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase) { ".mkv", ".mp4", ".avi", ".mov", ".wmv", ".m4v", ".ts", ".webm" };

    public IReadOnlyList<ManagedTheme> List()
    {
        var rows = store.Read(s => s.Themes.Values.OrderByDescending(t => t.Date).ToArray());
        var stale = new List<(ManagedTheme Theme, bool MissingItem)>();
        foreach (var record in rows)
        {
            var gate = _locks.GetOrAdd(record.ItemId, _ => new SemaphoreSlim(1));
            if (!gate.Wait(0)) continue;
            try
            {
                var item = library.GetItemById(record.ItemId);
                if (item is null) { stale.Add((record, true)); continue; }
                var currentFolder = item is Series ? item.Path : Path.GetDirectoryName(item.Path);
                if (!Directory.Exists(record.Folder) && !Directory.Exists(currentFolder)) continue;
                try
                {
                    var (folder, _, libraryId) = Location(item);
                    if (libraryId != record.LibraryId || !Owned(record, item, folder)) stale.Add((record, false));
                }
                catch (InvalidOperationException) { stale.Add((record, false)); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
            finally { gate.Release(); }
        }
        if (stale.Count == 0) return rows;
        store.Change(s =>
        {
            foreach (var (record, missingItem) in stale)
            {
                if (!s.Themes.TryGetValue(record.ItemId, out var current) || !ReferenceEquals(current, record)) continue;
                s.Themes.Remove(record.ItemId);
                if (missingItem)
                {
                    s.ExcludedVideos.Remove(record.ItemId);
                    s.ExcludedRecordings.Remove(record.ItemId);
                    s.Suppressed.Remove(record.ItemId);
                    s.Outcomes.Remove(record.ItemId);
                }
                else s.Outcomes[record.ItemId] = "No longer managed; theme or library item changed outside plugin";
            }
        });
        var removed = stale.Select(s => s.Theme.ItemId).ToHashSet();
        return rows.Where(r => !removed.Contains(r.ItemId)).ToArray();
    }
    public string? Outcome(Guid id) => store.Read(s => s.Outcomes.GetValueOrDefault(id));
    public static string Status(ManagedTheme record)
    {
        try
        {
            if (!File.Exists(record.Path) || File.GetAttributes(record.Path).HasFlag(FileAttributes.ReparsePoint) ||
                !string.Equals(Canonical(record.Path), Path.Combine(Canonical(record.Folder), "theme.mp3"), StringComparison.Ordinal))
                return "Missing or externally modified";
            using var file = File.OpenRead(record.Path);
            return Convert.ToHexString(SHA256.HashData(file)) == record.Hash ? "Active" : "Missing or externally modified";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return "Missing or externally modified"; }
    }

    private static string Canonical(string path)
    {
        path = Path.GetFullPath(path);
        if (Directory.Exists(path)) return (new DirectoryInfo(path).ResolveLinkTarget(true)?.FullName ?? path);
        if (File.Exists(path)) return (new FileInfo(path).ResolveLinkTarget(true)?.FullName ?? path);
        return path;
    }

    private (string Folder, string Library, Guid LibraryId) Location(BaseItem item)
    {
        if (item is not (Movie or Series) || item.IsVirtualItem || item.ExtraType is not null || item.SourceType != SourceType.Library)
            throw new InvalidOperationException("Unsupported item.");
        var folders = library.GetCollectionFolders(item);
        var selectedLibraries = Plugin.Instance.Configuration.Libraries;
        var selected = folders.FirstOrDefault(f => selectedLibraries is null || selectedLibraries.Contains(f.Id));
        if (selected is null) throw new InvalidOperationException("Item is not in a selected library.");
        var folder = Canonical(item is Series ? item.Path : Path.GetDirectoryName(item.Path)!);
        var roots = selected.PhysicalLocations.Append(selected.Path).Where(Directory.Exists).Select(Canonical).ToArray();
        if (!Directory.Exists(folder)) throw new InvalidOperationException("Item folder is missing or unwritable.");
        if (item is Movie && (!File.Exists(item.Path) || item.IsInMixedFolder || roots.Contains(folder, StringComparer.Ordinal) ||
            Directory.EnumerateFiles(folder).Count(p => VideoExtensions.Contains(Path.GetExtension(p))) > 1))
            throw new InvalidOperationException("Movie needs a dedicated physical folder.");
        if (!roots.Any(root => folder.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) ||
            item is Series && !string.Equals(Canonical(item.Path), folder, StringComparison.Ordinal))
            throw new InvalidOperationException("Item needs a physical folder inside its library.");
        return (folder, selected.Name, selected.Id);
    }

    private static bool Owned(ManagedTheme entry, BaseItem item, string folder)
    {
        var path = Path.Combine(folder, "theme.mp3");
        if (entry.ItemId != item.Id || !string.Equals(entry.Folder, folder, StringComparison.Ordinal) ||
            !string.Equals(entry.Path, path, StringComparison.Ordinal) || !File.Exists(path) ||
            File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) return false;
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)) == entry.Hash;
    }

    private static bool OtherTheme(BaseItem item, string folder, string? owned)
    {
        if (item.GetThemeSongs().Any(song => song.Path is not null &&
            string.Equals(Canonical(Path.GetDirectoryName(song.Path)!), folder, StringComparison.Ordinal) &&
            !string.Equals(Canonical(song.Path), owned, StringComparison.Ordinal))) return true;
        if (Directory.EnumerateFiles(folder).Any(p => Path.GetFileNameWithoutExtension(p).Equals("theme", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(p, owned, StringComparison.Ordinal))) return true;
        var subfolder = Path.Combine(folder, "theme-music");
        return Directory.Exists(subfolder) && Directory.EnumerateFiles(subfolder, "*", SearchOption.AllDirectories).Any(p => AudioExtensions.Contains(Path.GetExtension(p)));
    }

    private void Refresh(BaseItem item) => providers.QueueRefresh(item.Id, new MetadataRefreshOptions(new DirectoryService(fileSystem)), RefreshPriority.High);

    public async Task<string> Process(Guid id, bool replacement, CancellationToken ct)
    {
        var gate = _locks.GetOrAdd(id, _ => new SemaphoreSlim(1));
        await gate.WaitAsync(ct);
        try
        {
            var item = library.GetItemById(id) ?? throw new InvalidOperationException("Item no longer exists.");
            var (folder, libraryName, libraryId) = Location(item);
            var existing = store.Read(s => s.Themes.GetValueOrDefault(id));
            if (replacement && existing is null) throw new InvalidOperationException("No managed theme to refresh.");
            if (existing is not null && !Owned(existing, item, folder)) throw new InvalidOperationException("Theme changed elsewhere. The file was left untouched.");
            if (!replacement && existing is not null) return "Already themed";
            if (!replacement && store.Read(s => s.Suppressed.Contains(id))) return "Suppressed until rescan";
            if (OtherTheme(item, folder, existing?.Path)) return "Already themed";
            if (string.IsNullOrWhiteSpace(item.Name)) throw new InvalidOperationException("Item title is not ready; retry after metadata refresh.");
            var work = new Work(item.Name, item.OriginalTitle, item.ProductionYear, item is Series);
            var excludedIds = store.Read(s => new HashSet<string>(s.ExcludedVideos.GetValueOrDefault(id) ?? []));
            var excludedRecordings = store.Read(s => new HashSet<string>(s.ExcludedRecordings.GetValueOrDefault(id) ?? []));
            if (replacement)
            {
                excludedIds.Add(existing!.VideoId);
                excludedRecordings.Add(existing.Recording);
                store.Change(s =>
                {
                    if (!s.ExcludedVideos.TryGetValue(id, out var ids)) s.ExcludedVideos[id] = ids = [];
                    ids.Add(existing.VideoId);
                    if (!s.ExcludedRecordings.TryGetValue(id, out var recordings)) s.ExcludedRecordings[id] = recordings = [];
                    recordings.Add(existing.Recording);
                });
            }
            var videos = await youtube.Search(work, ct);
            var choice = Matcher.Select(work, videos, excludedIds, excludedRecordings);
            if (choice is null)
            {
                videos = videos.Concat(await youtube.Search(work, ct, nextPage: true)).DistinctBy(video => video.Id).ToArray();
                choice = Matcher.Select(work, videos, excludedIds, excludedRecordings);
            }
            if (choice is null)
            {
                videos = videos.Concat(await youtube.SearchAlbumTrack(work, ct)).DistinctBy(video => video.Id).ToArray();
                choice = Matcher.Select(work, videos, excludedIds, excludedRecordings);
            }
            if (choice is null)
            {
                var reason = Matcher.RejectionReason(work, videos, excludedIds, excludedRecordings);
                var excluded = reason == "Only previously used recordings were found";
                var result = replacement ? "No replacement found" : excluded ? "Previously used recording excluded" : "No match found";
                store.Change(s => s.Outcomes[id] = excluded ? result : result + ": " + reason);
                return result;
            }
            var path = Path.Combine(folder, "theme.mp3");
            for (var sourceAttempt = 0; sourceAttempt < 2; sourceAttempt++)
            {
                var temporary = Path.Combine(folder, ".theme-" + Guid.NewGuid().ToString("N") + ".mp3");
                try
                {
                    try { await Audio.Convert(choice, temporary, encoder, ct); }
                    catch (DownloadFailure) when (sourceAttempt == 0)
                    {
                        excludedIds.Add(choice.Video.Id);
                        choice = Matcher.Select(work, videos, excludedIds, excludedRecordings);
                        if (choice is null) throw;
                        continue;
                    }
                    ct.ThrowIfCancellationRequested();
                    if (OtherTheme(item, folder, existing?.Path)) throw new IOException("Another theme appeared. The file was left untouched.");
                    if (existing is not null)
                    {
                        if (!Owned(existing, item, folder)) throw new IOException("Theme changed during download. The file was left untouched.");
                        File.Move(temporary, path, true);
                    }
                    else File.Move(temporary, path);
                    await using var stream = File.OpenRead(path);
                    // Complete ownership recording after the file is in place, even if cancellation arrives.
                    var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, CancellationToken.None));
                    var result = replacement ? "Replaced" : "Added";
                    store.Change(s =>
                    {
                        s.Themes[id] = new ManagedTheme { ItemId = id, Folder = folder, Path = path, LibraryId = libraryId, Library = libraryName,
                            Kind = item is Movie ? "Movie" : "Series", Name = item.Name, Year = item.ProductionYear,
                            VideoId = choice.Video.Id, VideoTitle = choice.Video.Title, Recording = choice.Recording,
                            Hash = hash, Score = choice.Score, Evidence = choice.Evidence, Date = DateTimeOffset.UtcNow };
                        s.Outcomes[id] = result;
                    });
                    Refresh(item);
                    return result;
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            throw new InvalidOperationException("Download failed. No other match was available.");
        }
        finally { gate.Release(); }
    }

    public async Task Delete(Guid id, CancellationToken ct)
    {
        var gate = _locks.GetOrAdd(id, _ => new SemaphoreSlim(1));
        await gate.WaitAsync(ct);
        try
        {
            var item = library.GetItemById(id) ?? throw new InvalidOperationException("Item no longer exists.");
            var (folder, _, _) = Location(item);
            var record = store.Read(s => s.Themes.GetValueOrDefault(id)) ?? throw new InvalidOperationException("No managed theme.");
            if (!Owned(record, item, folder)) throw new InvalidOperationException("Theme changed elsewhere. It was not deleted.");
            File.Delete(record.Path);
            store.Change(s => { s.Themes.Remove(id); s.Suppressed.Add(id); s.Outcomes[id] = "Deleted; automatic downloads paused until full rescan"; });
            Refresh(item);
        }
        finally { gate.Release(); }
    }

    public void ResetSuppression() => store.Change(s => s.Suppressed.Clear());
}
