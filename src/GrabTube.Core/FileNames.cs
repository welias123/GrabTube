using System.Text;

namespace GrabTube.Core;

internal static class FileNames
{
    private const int MaxLength = 120;

    /// <summary>
    /// Turns a video title into something Windows will accept. Video titles are
    /// user generated, which means they contain everything a file system hates.
    /// </summary>
    public static string Sanitize(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return "video";

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(title.Length);

        foreach (var c in title)
            builder.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);

        var cleaned = builder.ToString().Trim();

        // Trailing dots and spaces look harmless and then quietly break File.Move.
        cleaned = cleaned.TrimEnd('.', ' ');

        if (cleaned.Length > MaxLength)
            cleaned = cleaned[..MaxLength].TrimEnd('.', ' ');

        return cleaned.Length == 0 ? "video" : cleaned;
    }

    /// <summary>Appends " (2)", " (3)" and so on until the path is free.</summary>
    public static string MakeUnique(string path)
    {
        if (!File.Exists(path))
            return path;

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var i = 2; i < 10_000; i++)
        {
            var candidate = Path.Combine(directory, $"{name} ({i}){extension}");
            if (!File.Exists(candidate))
                return candidate;
        }

        // Ten thousand copies of the same video is a you problem, but we still return something.
        return Path.Combine(directory, $"{name} ({Guid.NewGuid():N}){extension}");
    }
}
