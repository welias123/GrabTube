using System.Diagnostics;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Exceptions;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace GrabTube.Core;

/// <summary>
/// Downloads YouTube videos as MP4 or MP3.
/// </summary>
/// <remarks>
/// MP4 output uses muxed streams, which already carry video and audio in one
/// file. That keeps the happy path free of any external dependency, at the cost
/// of resolution: YouTube caps muxed streams at 720p. MP3 output grabs the best
/// audio only stream and hands it to ffmpeg.
/// </remarks>
public sealed class YouTubeDownloader : IMediaDownloader
{
    private readonly YoutubeClient _client;

    public YouTubeDownloader() : this(new HttpClient()) { }

    /// <param name="httpClient">
    /// Share your own client if the host application already pools connections.
    /// </param>
    public YouTubeDownloader(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _client = new YoutubeClient(httpClient);
    }

    /// <summary>
    /// Path to ffmpeg. Resolved automatically on first use, but you can set it
    /// yourself if you ship your own build.
    /// </summary>
    public string? FfmpegPath { get; set; }

    /// <summary>True when MP3 output is currently possible.</summary>
    public bool CanConvertToMp3 => (FfmpegPath ??= Ffmpeg.Locate()) is not null;

    public async Task<MediaInfo> InspectAsync(string url, CancellationToken cancellationToken = default)
    {
        var id = ParseId(url);

        Video video;
        try
        {
            video = await _client.Videos.GetAsync(id, cancellationToken).ConfigureAwait(false);
        }
        // VideoUnavailableException derives from VideoUnplayableException here, so
        // the narrower case has to come first.
        catch (VideoUnavailableException ex)
        {
            throw new MediaUnavailableException("This video is private, deleted, or blocked in your region.", ex);
        }
        catch (VideoUnplayableException ex)
        {
            throw new MediaUnavailableException("This video cannot be played outside YouTube.", ex);
        }

        return new MediaInfo(
            video.Id.Value,
            video.Title,
            video.Author.ChannelTitle,
            video.Duration ?? TimeSpan.Zero,
            video.Thumbnails.Count > 0 ? video.Thumbnails.GetWithHighestResolution().Url : string.Empty);
    }

    public async Task<DownloadResult> DownloadAsync(
        DownloadRequest request,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Format == MediaFormat.Mp3 && !CanConvertToMp3)
            throw new FfmpegMissingException();

        var started = Stopwatch.StartNew();
        progress?.Report(new DownloadProgress(DownloadStage.Resolving, 0, 0, 0, 0, TimeSpan.Zero));

        var info = await InspectAsync(request.Url, cancellationToken).ConfigureAwait(false);
        var manifest = await GetManifestAsync(info.Id, cancellationToken).ConfigureAwait(false);

        Directory.CreateDirectory(request.OutputDirectory);

        var baseName = FileNames.Sanitize(request.FileName ?? info.Title);
        var extension = request.Format == MediaFormat.Mp3 ? ".mp3" : ".mp4";
        var target = Path.Combine(request.OutputDirectory, baseName + extension);

        if (File.Exists(target) && !request.Overwrite)
            return new DownloadResult(target, info, new FileInfo(target).Length, started.Elapsed);

        target = FileNames.MakeUnique(target);

        var filePath = request.Format == MediaFormat.Mp3
            ? await DownloadAsMp3Async(manifest, info, target, progress, cancellationToken).ConfigureAwait(false)
            : await DownloadAsMp4Async(manifest, request.Quality, target, progress, cancellationToken).ConfigureAwait(false);

        started.Stop();
        var size = new FileInfo(filePath).Length;
        progress?.Report(new DownloadProgress(DownloadStage.Finished, 1, size, size, 0, TimeSpan.Zero));

        return new DownloadResult(filePath, info, size, started.Elapsed);
    }

    private async Task<string> DownloadAsMp4Async(
        StreamManifest manifest,
        QualityPreference quality,
        string target,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var muxed = manifest.GetMuxedStreams().ToList();
        if (muxed.Count == 0)
        {
            throw new MediaUnavailableException(
                "YouTube offered no combined video and audio stream for this video.");
        }

        var ceiling = MaxHeightFor(quality);
        var stream = muxed
            .Where(s => s.VideoQuality.MaxHeight <= ceiling)
            .OrderByDescending(s => s.VideoQuality.MaxHeight)
            .ThenByDescending(s => s.Bitrate.BitsPerSecond)
            .FirstOrDefault()
            ?? muxed.OrderBy(s => s.VideoQuality.MaxHeight).First();

        await DownloadStreamAsync(stream, target, DownloadStage.Downloading, progress, cancellationToken)
            .ConfigureAwait(false);

        return target;
    }

    private async Task<string> DownloadAsMp3Async(
        StreamManifest manifest,
        MediaInfo info,
        string target,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stream = manifest.GetAudioOnlyStreams()
            .OrderByDescending(s => s.Bitrate.BitsPerSecond)
            .FirstOrDefault();

        if (stream is null)
            throw new MediaUnavailableException("YouTube offered no audio stream for this video.");

        var temp = Path.Combine(Path.GetTempPath(), $"grabtube-{Guid.NewGuid():N}.{stream.Container.Name}");

        try
        {
            await DownloadStreamAsync(stream, temp, DownloadStage.Downloading, progress, cancellationToken)
                .ConfigureAwait(false);

            var size = new FileInfo(temp).Length;
            var relay = progress is null
                ? null
                : new Progress<double>(fraction => progress.Report(new DownloadProgress(
                    DownloadStage.Converting, fraction, size, size, 0, TimeSpan.Zero)));

            await Ffmpeg.ToMp3Async(FfmpegPath!, temp, target, info.Duration, relay, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            TryDelete(temp);
        }

        return target;
    }

    private async Task DownloadStreamAsync(
        IStreamInfo stream,
        string path,
        DownloadStage stage,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var total = stream.Size.Bytes;
        var meter = new SpeedMeter();

        var relay = progress is null
            ? null
            : new Progress<double>(fraction =>
            {
                var received = (long)(total * fraction);
                var (speed, eta) = meter.Sample(received, total);
                progress.Report(new DownloadProgress(stage, fraction, received, total, speed, eta));
            });

        try
        {
            await _client.Videos.Streams
                .DownloadAsync(stream, path, relay, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not GrabTubeException)
        {
            TryDelete(path);
            throw new MediaUnavailableException("The download failed before it finished.", ex);
        }
    }

    private async Task<StreamManifest> GetManifestAsync(string videoId, CancellationToken cancellationToken)
    {
        try
        {
            return await _client.Videos.Streams
                .GetManifestAsync(videoId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (VideoUnplayableException ex)
        {
            throw new MediaUnavailableException(
                "YouTube refused to hand out the streams for this video.", ex);
        }
    }

    private static VideoId ParseId(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidUrlException(url ?? string.Empty);

        return VideoId.TryParse(url.Trim()) ?? throw new InvalidUrlException(url.Trim());
    }

    private static int MaxHeightFor(QualityPreference quality) => quality switch
    {
        QualityPreference.Hd720 => 720,
        QualityPreference.Sd480 => 480,
        QualityPreference.Sd360 => 360,
        _ => int.MaxValue
    };

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // A leftover temp file is not worth failing an otherwise fine download over.
        }
    }
}
