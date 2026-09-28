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
        if (process.ExitCode != 0) throw new IOException($"Audio tool '{Path.GetFileName(executable)}' failed (exit {process.ExitCode}): {error[..Math.Min(error.Length, JellyScoreConstants.ToolErrorMessageMaximumLength)]}");
        return output;
    }

    private static JsonElement Stats(string output)
    {
        var match = Regex.Match(output, @"\{\s*""input_i"".+?\}", RegexOptions.Singleline);
        if (!match.Success) throw new IOException("FFmpeg loudness analysis returned no measurements.");
        return JsonDocument.Parse(match.Value).RootElement.Clone();
    }

    // ReSharper disable once MemberCanBePrivate.Global
    public static double FixedGain(double loudness, double truePeak) => Math.Min(JellyScoreConstants.TargetLufs - loudness,
        JellyScoreConstants.MaximumTruePeakDbtp - truePeak);

    public static async Task Convert(Choice choice, string destination, IMediaEncoder encoder, CancellationToken ct)
    {
        var raw = destination + ".source";
        try
        {
            await YouTube.Download(choice.Video.Id, raw, ct);
            if (!File.Exists(raw) || new FileInfo(raw).Length is 0 or > JellyScoreConstants.RawAudioMaximumBytes) throw new DownloadFailure("Downloaded audio is missing or too large.");
            // Leave headroom for MP3 encoding without compressing the recording's dynamics.
            var measured = Stats(await Run(encoder.EncoderPath, ["-hide_banner", "-nostats", "-i", raw, "-af",
                $"loudnorm=I={JellyScoreConstants.TargetLufs}:TP={JellyScoreConstants.MaximumTruePeakDbtp}:LRA={JellyScoreConstants.TargetLoudnessRange}:print_format=json", "-f", "null", "-"], ct));
            if (!double.TryParse(measured.GetProperty("input_i").GetString(), CultureInfo.InvariantCulture, out var loudness) || !double.IsFinite(loudness) ||
                !double.TryParse(measured.GetProperty("input_tp").GetString(), CultureInfo.InvariantCulture, out var peak) || !double.IsFinite(peak))
                throw new IOException("FFmpeg loudness analysis returned invalid measurements.");
            var gain = FixedGain(loudness, peak).ToString("R", CultureInfo.InvariantCulture);
            await Run(encoder.EncoderPath, ["-hide_banner", "-nostdin", "-y", "-i", raw, "-vn", "-af", $"volume={gain}dB", "-c:a", "libmp3lame", "-b:a", $"{JellyScoreConstants.Mp3BitrateKbps}k", "-f", "mp3", destination], ct);
            if (!File.Exists(destination) || new FileInfo(destination).Length is 0 or > JellyScoreConstants.ConvertedAudioMaximumBytes) throw new IOException("Converted audio is empty or too large.");
            var probe = await Run(encoder.ProbePath, ["-v", "error", "-select_streams", "a:0", "-show_entries", "stream=codec_name:format=duration", "-of", "json", destination], ct);
            using var info = JsonDocument.Parse(probe);
            if (info.RootElement.GetProperty("streams").GetArrayLength() == 0 ||
                !double.TryParse(info.RootElement.GetProperty("format").GetProperty("duration").GetString(), CultureInfo.InvariantCulture, out var duration) ||
                duration < 1 || duration > choice.Video.Seconds + JellyScoreConstants.DurationToleranceSeconds) throw new IOException("Converted audio could not be validated.");
        }
        finally { if (File.Exists(raw)) File.Delete(raw); }
    }
}
