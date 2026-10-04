using System.IO;
using System.Text.Json;

namespace LowSizeMp4;

public sealed class UserSettings
{
    public string Theme { get; set; } = "Dark";
    public string AccentHex { get; set; } = "#0078D4";
    public string Backdrop { get; set; } = "Mica";
    public string LastPresetName { get; set; } = "Баланс H.264 (Рекомендуется)";
    public bool IsTargetSizeMode { get; set; } = false;
    public int TargetSizeMb { get; set; } = 25;
    public string Resolution { get; set; } = "Оригинал";
    public string Fps { get; set; } = "Оригинал";
    public int CustomFps { get; set; } = 120;
    public bool UseInterpolation { get; set; } = false;
    public bool IsMuteAudio { get; set; } = false;
    public bool UseGpuAcceleration { get; set; } = false;
    public bool PlaySoundOnComplete { get; set; } = true;
    public bool AutoOpenFolderOnComplete { get; set; } = false;
    public string OutputFileSuffix { get; set; } = "_compressed";
    public bool AutoClearCompleted { get; set; } = false;
    public string VideoCodec { get; set; } = "H.264 (AVC)";
    public string ExportFormat { get; set; } = "MP4 Видео";
    public string CompletionAction { get; set; } = "Ничего не делать";
    public string? CustomOutputFolder { get; set; }
    public string? CustomFfmpegPath { get; set; }
}

public static class SettingsService
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LowSizeMp4");

    private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static UserSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                var loaded = JsonSerializer.Deserialize<UserSettings>(json, JsonOptions);
                if (loaded != null)
                {
                    return loaded;
                }
            }
        }
        catch
        {
        }

        return new UserSettings();
    }

    public static void Save(UserSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SettingsFile, json);
        }
        catch
        {
        }
    }
}
