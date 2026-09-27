using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.ThemeSongs;

[ApiController]
[Route("ThemeSongs")]
[Authorize(Policy = "RequiresElevation")]
public sealed class ThemeController(ThemeService themes, ThemeScan scan, ITaskManager tasks, ILibraryManager library) : ControllerBase
{
    [HttpGet("settings")]
    public object Settings() => new { Plugin.Instance.Configuration.Enabled, Plugin.Instance.Configuration.Libraries,
        DownloaderAvailable = System.IO.File.Exists(YouTube.DownloaderPath),
        LibrariesAvailable = library.GetVirtualFolders().Select(f => new { f.Name, f.ItemId }) };

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
        var rows = themes.List().Where(r => string.IsNullOrEmpty(search) || r.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            r.Library.Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
        return new { Total = rows.Length, Items = rows.Skip((Math.Max(1, page) - 1) * 25).Take(25).Select(r => new {
            r.ItemId, r.Name, r.Kind, r.Year, r.Library, r.Path, r.VideoTitle, r.Score, r.Evidence, r.Date,
            Source = "https://www.youtube.com/watch?v=" + r.VideoId,
            Status = ThemeService.Status(r) }) };
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
        try { return Ok(new { Result = await themes.Process(id, true, ct) }); }
        catch (InvalidOperationException e) { return Conflict(new { Error = e.Message }); }
        catch (Exception e) when (e is IOException or SearchFailure) { return UnprocessableEntity(new { Error = e.Message }); }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try { await themes.Delete(id, ct); return NoContent(); }
        catch (InvalidOperationException e) { return Conflict(new { Error = e.Message }); }
        catch (IOException e) { return UnprocessableEntity(new { Error = e.Message }); }
    }
}
