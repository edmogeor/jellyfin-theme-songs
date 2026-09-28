using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyScore;

[ApiController]
[Route("ThemeSongs")]
[Authorize(Policy = "RequiresElevation")]
public sealed class ThemeController(ThemeService themes, ThemeScan scan, ITaskManager tasks, ILibraryManager library) : ControllerBase
{
    [HttpGet("settings")]
    public object Settings() => new { Plugin.Instance.Configuration.Enabled, Libraries = Plugin.Instance.Configuration.SelectedLibraries(library),
        DownloaderAvailable = System.IO.File.Exists(YouTube.DownloaderPath),
        LibrariesAvailable = library.GetVirtualFolders().Select(f => new { f.Name, f.ItemId }) };

    [HttpGet("strings/{locale}")]
    public IActionResult Strings(string locale)
    {
        var stream = typeof(ThemeController).Assembly.GetManifestResourceStream($"Jellyfin.Plugin.JellyScore.Strings.{locale}.json");
        return stream is null ? NotFound() : File(stream, "application/json");
    }

    public sealed record SettingsRequest(bool Enabled, Guid[]? Libraries);

    [HttpPost("settings")]
    public IActionResult Save([FromBody] SettingsRequest request)
    {
        var config = Plugin.Instance.Configuration;
        config.Enabled = request.Enabled;
        config.Libraries = request.Libraries ?? [];
        Plugin.Instance.UpdateConfiguration(config);
        return NoContent();
    }

    [HttpGet("downloads")]
    public object Downloads([FromQuery] string? search = null, [FromQuery] int page = 1)
    {
        var all = themes.List();
        var rows = all.Where(r => string.IsNullOrEmpty(search) || r.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            r.Library.Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
        return new { Total = rows.Length, AllTotal = all.Count, Items = rows.Skip((Math.Max(1, page) - 1) * 25).Take(25).Select(r => new {
            r.ItemId, r.Name, r.Kind, r.Year, r.Library, r.Path, r.VideoTitle, r.Score, r.Evidence, r.Date,
            Source = "https://www.youtube.com/watch?v=" + r.VideoId,
            Status = ThemeService.Status(r) }) };
    }

    [HttpDelete("downloads")]
    public async Task<object> DeleteAll(CancellationToken ct)
    {
        var result = await themes.DeleteAll(ct);
        return new { result.Deleted, result.Skipped };
    }

    [HttpGet("scan")]
    public object Progress() => scan.Status;

    [HttpPost("scan")]
    public IActionResult StartScan()
    {
        tasks.QueueIfNotRunning<ThemeScan>();
        return Accepted();
    }

    [HttpPost("scan/cancel")]
    public IActionResult CancelScan() { tasks.CancelIfRunning<ThemeScan>(); return Accepted(); }

    [HttpPost("{id:guid}/refresh")]
    public async Task<IActionResult> Refresh(Guid id, CancellationToken ct)
    {
        try { return Ok(new { (await themes.Process(id, true, ct)).Result }); }
        catch (InvalidOperationException e) { return Conflict(new { Error = e.Message, Code = ErrorCode(e, "refreshFailed") }); }
        catch (Exception e) when (e is IOException or SearchFailure) { return UnprocessableEntity(new { Error = e.Message, Code = ErrorCode(e, "refreshFailed") }); }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try { await themes.Delete(id, ct); return NoContent(); }
        catch (InvalidOperationException e) { return Conflict(new { Error = e.Message, Code = ErrorCode(e, "deleteFailed") }); }
        catch (IOException e) { return UnprocessableEntity(new { Error = e.Message, Code = ErrorCode(e, "deleteFailed") }); }
    }

    private static string ErrorCode(Exception e, string fallback) => e.Message switch
    {
        "Theme changed elsewhere. The file was left untouched." or "Theme changed elsewhere. It was not deleted." or
            "Theme changed during download. The file was left untouched." => "themeChanged",
        "Another theme appeared. The file was left untouched." => "anotherTheme",
        "No managed theme to refresh." or "No managed theme." or "Item no longer exists." => "themeUnavailable",
        "Item is not in a selected library." or "Unsupported item." or "Movie needs a dedicated physical folder." or
            "Item needs a physical folder inside its library." or "Item folder is missing or unwritable." => "themeLocationUnavailable",
        "Item title is not ready; retry after metadata refresh." => "itemNotReady",
        _ when e is SearchFailure => "searchFailed",
        _ when e is DownloadFailure => "downloadFailed",
        _ => fallback
    };
}
