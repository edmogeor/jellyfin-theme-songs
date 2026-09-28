using System.Threading.Channels;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyScore;

public sealed class NewItemWorker(ILibraryManager library, ThemeService themes, ILogger<NewItemWorker> logger) : BackgroundService
{
    private readonly Channel<Guid> _queue = Channel.CreateBounded<Guid>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    private readonly HashSet<Guid> _queued = [];
    private readonly Lock _gate = new();

    public override Task StartAsync(CancellationToken ct)
    {
        library.ItemAdded += Added;
        return base.StartAsync(ct);
    }

    private void Added(object? sender, ItemChangeEventArgs args)
    {
        if (!Plugin.Instance.Configuration.Enabled || args.Item is not (Movie or Series) || args.Item.ExtraType is not null || args.Item.IsVirtualItem) return;
        lock (_gate)
        {
            if (_queued.Add(args.Item.Id) && !_queue.Writer.TryWrite(args.Item.Id)) _queued.Remove(args.Item.Id);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await foreach (var id in _queue.Reader.ReadAllAsync(ct))
        {
            try
            {
                // Library item creation often precedes metadata and directory availability.
                await Task.Delay(TimeSpan.FromSeconds(20), ct);
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    try { await themes.Process(id, false, ct); break; }
                    catch (InvalidOperationException e) when (e.Message.Contains("not ready", StringComparison.Ordinal) && attempt < 2)
                    { await Task.Delay(TimeSpan.FromSeconds(30), ct); }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception e) { logger.LogWarning("Theme processing for {ItemId} failed: {Message}", id, e.Message); }
            finally { lock (_gate) _queued.Remove(id); }
        }
    }

    public override async Task StopAsync(CancellationToken ct)
    {
        library.ItemAdded -= Added;
        _queue.Writer.TryComplete();
        await base.StopAsync(ct);
    }
}

public sealed class LibraryScanWorker(ITaskManager tasks) : IHostedService
{
    public Task StartAsync(CancellationToken ct)
    {
        tasks.TaskCompleted += Completed;
        return Task.CompletedTask;
    }

    private void Completed(object? sender, TaskCompletionEventArgs args)
    {
        if (args.Task.ScheduledTask.Key == "RefreshLibrary" && args.Result.Status == TaskCompletionStatus.Completed && Plugin.Instance.Configuration.Enabled)
            tasks.QueueIfNotRunning<ThemeScan>();
    }

    public Task StopAsync(CancellationToken ct)
    {
        tasks.TaskCompleted -= Completed;
        return Task.CompletedTask;
    }
}

// ReSharper disable UnusedAutoPropertyAccessor.Global
public sealed class ScanStatus
{
    private const int PriorItems = 3;

    public Guid RunId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    internal DateTimeOffset LastCompletedAt { get; set; }
    internal double PriorSecondsPerItem { get; init; } = 20;
    public bool Running { get; set; }
    public bool Cancelled { get; set; }
    public string? CurrentItem { get; set; }
    public int Processed { get; set; }
    public int Total { get; set; }
    public int Added { get; set; }
    public int AlreadyThemed { get; set; }
    public int Excluded { get; set; }
    public int NoMatch { get; set; }
    public string[] Rejections { get; set; } = [];
    public int Unsupported { get; set; }
    public int Failed { get; set; }
    // ReSharper disable once UnusedMember.Global
    public double? RemainingSeconds
    {
        get
        {
            if (!Running || Total <= Processed || Total == 0) return null;
            var completedAt = LastCompletedAt > StartedAt ? LastCompletedAt : StartedAt;
            var secondsPerItem = ((completedAt - StartedAt).TotalSeconds + PriorItems * PriorSecondsPerItem) / (Processed + PriorItems);
            var stalledFor = Math.Max(0, (DateTimeOffset.UtcNow - completedAt).TotalSeconds - secondsPerItem);
            return (Total - Processed) * secondsPerItem + stalledFor;
        }
    }
}
// ReSharper restore UnusedAutoPropertyAccessor.Global

// ReSharper disable once ClassNeverInstantiated.Global
public sealed class ThemeScan(ILibraryManager library, ThemeService themes, Store store, ILogger<ThemeScan> logger) : IScheduledTask
{
    private static ScanStatus _status = new();
    public string Name => "Scan with JellyScore";
    public string Key => "ThemeSongsRescan";
    public string Description => "Find themes in selected movie and TV libraries.";
    public string Category => "Library";
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];
    public ScanStatus Status => _status;

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken ct)
    {
        var prior = store.Read(s => s.ScanSecondsPerItem);
        _status = new ScanStatus { RunId = Guid.NewGuid(), StartedAt = DateTimeOffset.UtcNow, Running = true,
            PriorSecondsPerItem = prior is > 0 and < 3600 ? prior.Value : 20 };
        var status = _status;
        var active = new Dictionary<Guid, string>();
        try
        {
            themes.ResetSuppression();
            var selected = Plugin.Instance.Configuration.SelectedLibraries(library);
            if (selected.Length == 0) { progress.Report(100); return; }
            var query = new InternalItemsQuery { IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series], AncestorIds = selected, Recursive = true };
            status.Total = library.GetCount(query);
            status.StartedAt = DateTimeOffset.UtcNow;
            status.LastCompletedAt = status.StartedAt;
            for (var offset = 0; ; offset += 100)
            {
                ct.ThrowIfCancellationRequested();
                query.StartIndex = offset;
                query.Limit = 100;
                var batch = library.GetItemList(query).ToArray();
                if (batch.Length == 0) break;
                await Parallel.ForEachAsync(batch, new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = ct }, async (item, token) =>
                {
                    lock (status) { active[item.Id] = item.Name; status.CurrentItem = string.Join(", ", active.Values); }
                    try
                    {
                        var result = await themes.Process(item.Id, false, token);
                        lock (status)
                        {
                            if (result == "Added") status.Added++;
                            else if (result == "Already themed") status.AlreadyThemed++;
                            else if (result == "Previously used recording excluded") status.Excluded++;
                            else status.NoMatch++;
                            if (result is "No match found" or "Previously used recording excluded")
                                status.Rejections = [.. status.Rejections.TakeLast(4), item.Name + ": " + themes.Outcome(item.Id)];
                        }
                    }
                    catch (InvalidOperationException) { lock (status) status.Unsupported++; }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (Exception e) { lock (status) status.Failed++; logger.LogWarning("Theme scan failed for {ItemId}: {Message}", item.Id, e.Message); }
                    finally { lock (status) { active.Remove(item.Id); status.CurrentItem = active.Count == 0 ? null : string.Join(", ", active.Values); } }
                    lock (status) { status.Processed++; status.LastCompletedAt = DateTimeOffset.UtcNow; progress.Report(100d * status.Processed / Math.Max(1, status.Total)); }
                });
                if (batch.Length < 100) break;
            }
            progress.Report(100);
            if (status.Processed > 0)
                store.Change(s => s.ScanSecondsPerItem = (status.LastCompletedAt - status.StartedAt).TotalSeconds / status.Processed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { status.Cancelled = true; throw; }
        finally { status.Running = false; status.CurrentItem = null; }
    }
}
