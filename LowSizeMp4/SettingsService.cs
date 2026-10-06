using System.IO;
using System.Text.Json;

namespace LowSizeMp4;

public sealed class UserSettings
{
    public string Theme { get; set; } = "Dark";
    public string AccentHex { get; set; } = "#0078D4";
    public string Backdrop { get; set; } = "Mica";
    public string LastPresetName { get; set; } = "Оптимальный (Баланс)";
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
    public string AudioBitrate { get; set; } = "192 kbps";
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

    public static CompressionProfile ResolveProfile(string? savedNameOrId, IReadOnlyList<CompressionProfile> profiles)
    {
        if (profiles.Count == 0)
        {
            throw new InvalidOperationException("No profiles available");
        }

        if (string.IsNullOrWhiteSpace(savedNameOrId))
        {
            return profiles[0];
        }

        // Direct ID or Name match
        var match = profiles.FirstOrDefault(p =>
            string.Equals(p.Id, savedNameOrId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.Name, savedNameOrId, StringComparison.OrdinalIgnoreCase));

        if (match != null) return match;

        // Legacy name mapping
        if (savedNameOrId.Contains("265", StringComparison.OrdinalIgnoreCase) ||
            savedNameOrId.Contains("HEVC", StringComparison.OrdinalIgnoreCase) ||
            savedNameOrId.Contains("сжатие", StringComparison.OrdinalIgnoreCase))
        {
            return profiles.FirstOrDefault(p => p.Id == "max_compression") ?? profiles[0];
        }
        if (savedNameOrId.Contains("Высокое", StringComparison.OrdinalIgnoreCase))
        {
            return profiles.FirstOrDefault(p => p.Id == "high_quality") ?? profiles[0];
        }
        if (savedNameOrId.Contains("Discord", StringComparison.OrdinalIgnoreCase) ||
            savedNameOrId.Contains("Telegram", StringComparison.OrdinalIgnoreCase) ||
            savedNameOrId.Contains("Быстро", StringComparison.OrdinalIgnoreCase))
        {
            return profiles.FirstOrDefault(p => p.Id == "fast") ?? profiles[0];
        }

        return profiles[0];
    }

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
