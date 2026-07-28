namespace GrabTube.Core;

/// <summary>Output container the engine should produce.</summary>
public enum MediaFormat
{
    /// <summary>Video and audio in a single MP4 file.</summary>
    Mp4,

    /// <summary>Audio only, transcoded to MP3. Requires ffmpeg.</summary>
    Mp3
}

/// <summary>
/// Upper bound for the video resolution. The engine picks the best stream that
/// stays at or below this, so asking for more than a video offers is harmless.
/// </summary>
public enum QualityPreference
{
    /// <summary>Whatever the best available stream is.</summary>
    Best,
    Hd720,
    Sd480,
    Sd360
}

/// <summary>Which part of the pipeline is currently running.</summary>
public enum DownloadStage
{
    Resolving,
    Downloading,
    Converting,
    Finished
}

/// <summary>Metadata about a video, resolved before anything is downloaded.</summary>
public sealed record MediaInfo(
    string Id,
    string Title,
    string Author,
    TimeSpan Duration,
    string ThumbnailUrl);

/// <summary>A single progress report. Speed and ETA are zero until enough bytes have moved to make a sensible estimate.</summary>
public sealed record DownloadProgress(
    DownloadStage Stage,
    double Fraction,
    long BytesReceived,
    long TotalBytes,
    double BytesPerSecond,
    TimeSpan Eta);

/// <summary>Everything the engine needs to perform one download.</summary>
public sealed record DownloadRequest
{
    /// <summary>A video URL, a share link, or a bare eleven character video id.</summary>
    public required string Url { get; init; }

    /// <summary>Directory the finished file is written to. Created if missing.</summary>
    public required string OutputDirectory { get; init; }

    public MediaFormat Format { get; init; } = MediaFormat.Mp4;

    public QualityPreference Quality { get; init; } = QualityPreference.Best;

    /// <summary>
    /// Overrides the file name, without extension. Leave null to derive it from
    /// the video title.
    /// </summary>
    public string? FileName { get; init; }

    /// <summary>
    /// When false, an existing file with the same name is left alone and its path
    /// is returned as is.
    /// </summary>
    public bool Overwrite { get; init; } = true;
}

/// <summary>The outcome of a successful download.</summary>
public sealed record DownloadResult(
    string FilePath,
    MediaInfo Media,
    long SizeInBytes,
    TimeSpan Elapsed);
