using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using MediaBrowser.Controller.MediaEncoding;

namespace Jellyfin.Plugin.JellyScore;

public static class Audio
{
    internal static async Task<string> Run(string executable, string[] args, CancellationToken ct)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo(executable) {
            RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
        try { process.Start(); }
        catch (System.ComponentModel.Win32Exception) { throw new IOException($"Executable '{executable}' is unavailable or not executable. Check the plugin package and Jellyfin's FFmpeg installation."); }
        await using var registration = ct.Register(static state =>
        {
            var running = (Process)state!;
            try { if (!running.HasExited) running.Kill(true); }
            catch (InvalidOperationException) { }
        }, process);
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var error = (await stderr).Trim();
        var output = await stdout + "\n" + error;
        if (process.ExitCode != 0) throw new IOException($"Audio tool '{Path.GetFileName(executable)}' failed (exit {process.ExitCode}): {error[..Math.Min(error.Length, 400)]}");
        return output;
    }

    private static JsonElement Stats(string output)
    {
        var match = Regex.Match(output, @"\{\s*""input_i"".+?\}", RegexOptions.Singleline);
        if (!match.Success) throw new IOException("FFmpeg loudness analysis returned no measurements.");
        return JsonDocument.Parse(match.Value).RootElement.Clone();
    }

    public static async Task Convert(Choice choice, string destination, IMediaEncoder encoder, CancellationToken ct)
    {
        var raw = destination + ".source";
        try
        {
            await YouTube.Download(choice.Video.Id, raw, ct);
            if (!File.Exists(raw) || new FileInfo(raw).Length is 0 or > 30_000_000) throw new DownloadFailure("Downloaded audio is missing or too large.");
            // Leave headroom for MP3 encoding while verifying the final file against the -2 dBTP ceiling.
            var measured = Stats(await Run(encoder.EncoderPath, ["-hide_banner", "-nostats", "-i", raw, "-af", "loudnorm=I=-18:TP=-3:LRA=11:print_format=json", "-f", "null", "-"], ct));
            var filter = string.Join(':', new[] { "loudnorm=I=-18", "TP=-3", "LRA=11",
                "measured_I=" + measured.GetProperty("input_i").GetString(), "measured_TP=" + measured.GetProperty("input_tp").GetString(),
                "measured_LRA=" + measured.GetProperty("input_lra").GetString(), "measured_thresh=" + measured.GetProperty("input_thresh").GetString(),
                "offset=" + measured.GetProperty("target_offset").GetString(), "linear=true", "print_format=json" });
            await Run(encoder.EncoderPath, ["-hide_banner", "-nostdin", "-y", "-i", raw, "-vn", "-af", filter, "-c:a", "libmp3lame", "-b:a", "192k", "-f", "mp3", destination], ct);
            if (!File.Exists(destination) || new FileInfo(destination).Length is 0 or > 15_000_000) throw new IOException("Converted audio is empty or too large.");
            var probe = await Run(encoder.ProbePath, ["-v", "error", "-select_streams", "a:0", "-show_entries", "stream=codec_name:format=duration", "-of", "json", destination], ct);
            using var info = JsonDocument.Parse(probe);
            if (info.RootElement.GetProperty("streams").GetArrayLength() == 0 ||
                !double.TryParse(info.RootElement.GetProperty("format").GetProperty("duration").GetString(), CultureInfo.InvariantCulture, out var duration) ||
                duration < 1 || duration > choice.Video.Seconds + 2) throw new IOException("Converted audio could not be validated.");
        }
        finally { if (File.Exists(raw)) File.Delete(raw); }
    }
}
