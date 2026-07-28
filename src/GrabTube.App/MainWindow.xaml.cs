using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using GrabTube.Core;
using Microsoft.Win32;

namespace GrabTube.App;

public partial class MainWindow : Window
{
    private readonly YouTubeDownloader _downloader = new();
    private readonly AppSettings _settings = AppSettings.Load();

    private CancellationTokenSource? _download;
    private CancellationTokenSource? _preview;
    private string? _lastFolder;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        FolderText.Text = Shorten(_settings.OutputDirectory);
        UrlBox.Focus();
    }

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnClose(object sender, RoutedEventArgs e)
    {
        _download?.Cancel();
        Close();
    }

    private void OnFormatChanged(object sender, RoutedEventArgs e)
    {
        if (QualityPanel is null)
            return;

        var isVideo = Mp4Option.IsChecked == true;
        QualityPanel.IsEnabled = isVideo;
        QualityPanel.Opacity = isVideo ? 1.0 : 0.45;
        QualityLabel.Opacity = isVideo ? 1.0 : 0.45;
    }

    private async void OnUrlChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _preview?.Cancel();
        PreviewText.Visibility = Visibility.Collapsed;

        var url = UrlBox.Text.Trim();
        if (url.Length < 11)
            return;

        _preview = new CancellationTokenSource();
        var token = _preview.Token;

        try
        {
            await Task.Delay(450, token);
            var info = await _downloader.InspectAsync(url, token);

            PreviewText.Text = $"{info.Title}  ·  {Format(info.Duration)}";
            PreviewText.Foreground = (Brush)FindResource("Muted");
            PreviewText.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException)
        {
        }
        catch (GrabTubeException)
        {
        }
        catch (HttpRequestException)
        {
        }
    }

    private void OnChangeFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose a download folder",
            InitialDirectory = Directory.Exists(_settings.OutputDirectory)
                ? _settings.OutputDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
        };

        if (dialog.ShowDialog(this) != true)
            return;

        _settings.OutputDirectory = dialog.FolderName;
        _settings.Save();
        FolderText.Text = Shorten(dialog.FolderName);
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_lastFolder) || !Directory.Exists(_lastFolder))
            return;

        Process.Start(new ProcessStartInfo(_lastFolder) { UseShellExecute = true });
    }

    private async void OnAction(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            _download?.Cancel();
            return;
        }

        var url = UrlBox.Text.Trim();
        if (url.Length == 0)
        {
            ShowError("Paste a YouTube link first.");
            return;
        }

        var wantsMp3 = Mp3Option.IsChecked == true;
        if (wantsMp3 && !_downloader.CanConvertToMp3)
        {
            ShowError("MP3 needs ffmpeg. Put ffmpeg.exe next to GrabTube.exe.");
            return;
        }

        var request = new DownloadRequest
        {
            Url = url,
            OutputDirectory = _settings.OutputDirectory,
            Format = wantsMp3 ? MediaFormat.Mp3 : MediaFormat.Mp4,
            Quality = SelectedQuality()
        };

        SetBusy(true);
        _download = new CancellationTokenSource();

        var progress = new Progress<DownloadProgress>(Report);

        try
        {
            var result = await _downloader.DownloadAsync(request, progress, _download.Token);

            _lastFolder = Path.GetDirectoryName(result.FilePath);
            Meter.Value = 1;
            StatusText.Foreground = (Brush)FindResource("Muted");
            StatusText.Text = $"Saved {Path.GetFileName(result.FilePath)}  ·  {Size(result.SizeInBytes)}";
            OpenFolderButton.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException)
        {
            Meter.Value = 0;
            StatusText.Foreground = (Brush)FindResource("Muted");
            StatusText.Text = "Cancelled.";
        }
        catch (GrabTubeException ex)
        {
            ShowError(ex.Message);
        }
        catch (HttpRequestException)
        {
            ShowError("No connection to YouTube.");
        }
        finally
        {
            _download?.Dispose();
            _download = null;
            SetBusy(false);
        }
    }

    private void Report(DownloadProgress p)
    {
        StatusPanel.Visibility = Visibility.Visible;
        StatusText.Foreground = (Brush)FindResource("Muted");
        Meter.Value = p.Fraction;

        StatusText.Text = p.Stage switch
        {
            DownloadStage.Resolving => "Looking up the video...",
            DownloadStage.Converting => $"Converting to MP3  ·  {p.Fraction:P0}",
            DownloadStage.Downloading => Describe(p),
            _ => StatusText.Text
        };
    }

    private static string Describe(DownloadProgress p)
    {
        var line = $"{p.Fraction:P0}  ·  {Size(p.BytesReceived)} of {Size(p.TotalBytes)}";

        if (p.BytesPerSecond > 0)
            line += $"  ·  {Size((long)p.BytesPerSecond)}/s";

        if (p.Eta > TimeSpan.Zero)
            line += $"  ·  {Format(p.Eta)} left";

        return line;
    }

    private QualityPreference SelectedQuality()
    {
        if (HdOption.IsChecked == true)
            return QualityPreference.Hd720;

        return SdOption.IsChecked == true ? QualityPreference.Sd360 : QualityPreference.Best;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ActionButton.Content = busy ? "Cancel" : "Download";
        UrlBox.IsEnabled = !busy;
        Mp4Option.IsEnabled = !busy;
        Mp3Option.IsEnabled = !busy;
        ChangeFolderButton.IsEnabled = !busy;
        QualityPanel.IsEnabled = !busy && Mp4Option.IsChecked == true;

        if (busy)
        {
            StatusPanel.Visibility = Visibility.Visible;
            OpenFolderButton.Visibility = Visibility.Collapsed;
            Meter.Value = 0;
        }
    }

    private void ShowError(string message)
    {
        StatusPanel.Visibility = Visibility.Visible;
        Meter.Value = 0;
        StatusText.Foreground = (Brush)FindResource("Danger");
        StatusText.Text = message;
    }

    private static string Shorten(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path.StartsWith(home, StringComparison.OrdinalIgnoreCase)
            ? "~" + path[home.Length..]
            : path;
    }

    private static string Size(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
    }

    private static string Format(TimeSpan span) =>
        span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss") : span.ToString(@"m\:ss");
}
