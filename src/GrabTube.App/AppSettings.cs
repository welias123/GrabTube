using System.IO;
using System.Text.Json;

namespace GrabTube.App;

public sealed class AppSettings
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GrabTube");

    private static readonly string File = Path.Combine(Folder, "settings.json");

    public string OutputDirectory { get; set; } = DefaultFolder();

    public static AppSettings Load()
    {
        try
        {
            if (System.IO.File.Exists(File))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(System.IO.File.ReadAllText(File));
                if (loaded is not null && !string.IsNullOrWhiteSpace(loaded.OutputDirectory))
                    return loaded;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            System.IO.File.WriteAllText(File, JsonSerializer.Serialize(this));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string DefaultFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "GrabTube");
}
