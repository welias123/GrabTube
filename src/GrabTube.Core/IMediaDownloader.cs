namespace GrabTube.Core;

/// <summary>
/// The engine contract. Two calls: ask what a link is, then download it.
/// Implementations are expected to be safe to reuse across many downloads,
/// but not to be called concurrently on the same instance.
/// </summary>
public interface IMediaDownloader
{
    /// <summary>
    /// Resolves a link to its metadata without downloading any media. Useful for
    /// showing the user what they are about to get.
    /// </summary>
    Task<MediaInfo> InspectAsync(string url, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a video and, for MP3 requests, transcodes it.
    /// </summary>
    /// <param name="request">What to fetch and where to put it.</param>
    /// <param name="progress">
    /// Optional progress sink. Called often during the download, so keep the
    /// handler cheap if it touches a UI thread.
    /// </param>
    Task<DownloadResult> DownloadAsync(
        DownloadRequest request,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
