namespace GrabTube.Core;

/// <summary>Base type for every failure the engine raises on purpose.</summary>
public class GrabTubeException : Exception
{
    public GrabTubeException(string message) : base(message) { }

    public GrabTubeException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>The link was not something we could turn into a video id.</summary>
public sealed class InvalidUrlException : GrabTubeException
{
    public InvalidUrlException(string url)
        : base($"'{url}' is not a YouTube video link or video id.") { }
}

/// <summary>
/// The video exists as a page but gave us nothing we can download. Private,
/// deleted, region locked, age gated, or YouTube simply having a bad day.
/// </summary>
public sealed class MediaUnavailableException : GrabTubeException
{
    public MediaUnavailableException(string message) : base(message) { }

    public MediaUnavailableException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>MP3 output was requested but no ffmpeg binary could be located.</summary>
public sealed class FfmpegMissingException : GrabTubeException
{
    public FfmpegMissingException()
        : base("MP3 output needs ffmpeg, and none was found. Install it, put it next "
               + "to the application, or set FfmpegPath on the downloader.") { }
}

/// <summary>ffmpeg started but exited unhappy.</summary>
public sealed class ConversionFailedException : GrabTubeException
{
    public ConversionFailedException(int exitCode, string details)
        : base($"ffmpeg exited with code {exitCode}. {details}") { }
}
