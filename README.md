# GrabTube

A small Windows desktop app that turns a YouTube link into an MP4 or an MP3. Paste,
pick a format, press Download. That is the whole flow.

The download logic lives in a separate library, `GrabTube.Core`, so you can drop the
engine into your own project and skip the interface entirely.

## Requirements

- Windows 10 or 11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- ffmpeg, only if you want MP3 output

## Build and run

```bash
dotnet build GrabTube.slnx -c Release
dotnet run --project src/GrabTube.App
```

## Using the engine

Add a reference to `GrabTube.Core` and you are two calls away from a file on disk.

```csharp
using GrabTube.Core;

var downloader = new YouTubeDownloader();

var info = await downloader.InspectAsync("https://www.youtube.com/watch?v=jNQXAC9IVRw");
Console.WriteLine($"{info.Title} by {info.Author}, {info.Duration}");

var result = await downloader.DownloadAsync(new DownloadRequest
{
    Url = info.Id,
    OutputDirectory = @"C:\Downloads",
    Format = MediaFormat.Mp4,
    Quality = QualityPreference.Hd720
});

Console.WriteLine($"Saved to {result.FilePath}");
```

### Progress and cancellation

`DownloadAsync` takes an optional `IProgress<DownloadProgress>` and a `CancellationToken`.
The progress record carries the current stage, a fraction between 0 and 1, byte counts,
transfer speed and an ETA, so a UI can render a full status line without doing any math
of its own.

```csharp
using var cancellation = new CancellationTokenSource();

var progress = new Progress<DownloadProgress>(p =>
    Console.WriteLine($"{p.Stage} {p.Fraction:P0} at {p.BytesPerSecond / 1024 / 1024:0.#} MB/s"));

await downloader.DownloadAsync(request, progress, cancellation.Token);
```

### Public surface

| Type | Purpose |
| --- | --- |
| `IMediaDownloader` | The contract, if you want to mock it in tests |
| `YouTubeDownloader` | The implementation, backed by YoutubeExplode |
| `DownloadRequest` | Url, output directory, format, quality, overwrite behaviour |
| `DownloadProgress` | Stage, fraction, bytes, speed, ETA |
| `DownloadResult` | Final path, metadata, size, elapsed time |
| `MediaInfo` | Id, title, author, duration, thumbnail |
| `Ffmpeg.Locate()` | Finds ffmpeg so you can tell the user whether MP3 is available |

Everything the engine throws on purpose derives from `GrabTubeException`, so a single
catch clause is enough to separate expected failures from real bugs.

## About video quality

MP4 output uses muxed streams, which already carry video and audio in one file. Nothing
has to be merged afterwards, which is why the app runs without any external binary.
The tradeoff is resolution: YouTube caps muxed streams at 720p, and for many videos the
best one on offer is 360p. The quality selector sets an upper bound, so picking Best
gives you the highest muxed stream that exists rather than a guaranteed 720p.

MP3 output takes the best audio only stream, which is not affected by that cap, and hands
it to ffmpeg at the highest VBR quality setting.

## Where ffmpeg is picked up

`Ffmpeg.Locate()` checks, in order:

1. `ffmpeg.exe` next to the application
2. the winget package folder under `%LOCALAPPDATA%\Microsoft\WinGet\Packages`
3. every directory in `PATH`

Set `YouTubeDownloader.FfmpegPath` yourself if you ship your own build.

## Layout

```
src/GrabTube.Core   the engine, no UI dependencies, targets net8.0
src/GrabTube.App    the WPF front end, targets net8.0-windows
```

## License

MIT. See [LICENSE](LICENSE).
