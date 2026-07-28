using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace GrabTube.Core;

/// <summary>
/// Thin wrapper around the ffmpeg binary. We only ever ask it to do one thing,
/// so this stays deliberately small.
/// </summary>
public static class Ffmpeg
{
    /// <summary>
    /// Looks for ffmpeg in the places it actually tends to live on Windows,
    /// in order of how much we trust the result.
    /// </summary>
    /// <returns>Full path to ffmpeg.exe, or null if the search came up empty.</returns>
    public static string? Locate()
    {
        var beside = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        if (File.Exists(beside))
            return beside;

        foreach (var candidate in WingetCandidates())
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return FromPathVariable();
    }

    // winget keeps ffmpeg under a versioned folder, so we glob and take the newest.
    private static IEnumerable<string> WingetCandidates()
    {
        var packages = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WinGet", "Packages");

        if (!Directory.Exists(packages))
            yield break;

        string[] matches;
        try
        {
            matches = Directory.GetFiles(packages, "ffmpeg.exe", SearchOption.AllDirectories);
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

        Array.Sort(matches, StringComparer.OrdinalIgnoreCase);
        for (var i = matches.Length - 1; i >= 0; i--)
            yield return matches[i];
    }

    private static string? FromPathVariable()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
            return null;

        foreach (var dir in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir))
                continue;

            string full;
            try
            {
                full = Path.Combine(dir.Trim(), "ffmpeg.exe");
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (File.Exists(full))
                return full;
        }

        return null;
    }

    /// <summary>
    /// Transcodes an audio file to MP3 at the highest VBR quality setting.
    /// </summary>
    /// <param name="totalDuration">
    /// Used to turn ffmpeg's timestamps into a percentage. Pass the video duration.
    /// </param>
    internal static async Task ToMp3Async(
        string ffmpegPath,
        string inputPath,
        string outputPath,
        TimeSpan totalDuration,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var args = new[]
        {
            "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
            "-i", inputPath,
            "-vn",
            "-codec:a", "libmp3lame",
            "-qscale:a", "0",
            "-progress", "pipe:1",
            outputPath
        };

        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = startInfo };
        var errors = new StringBuilder();

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                errors.AppendLine(e.Data);
        };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null || progress is null || totalDuration <= TimeSpan.Zero)
                return;

            // The progress stream is key=value lines. out_time_ms is the only one we care about.
            if (!e.Data.StartsWith("out_time_ms=", StringComparison.Ordinal))
                return;

            var raw = e.Data.AsSpan("out_time_ms=".Length);
            if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var microseconds))
                return;

            var done = microseconds / 1_000_000d / totalDuration.TotalSeconds;
            progress.Report(Math.Clamp(done, 0d, 1d));
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        if (process.ExitCode != 0)
            throw new ConversionFailedException(process.ExitCode, errors.ToString().Trim());
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Process finished between the check and the kill. Nothing to clean up.
        }
    }
}
