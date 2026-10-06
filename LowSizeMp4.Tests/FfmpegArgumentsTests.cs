using LowSizeMp4;
using Xunit;

namespace LowSizeMp4.Tests;

public class FfmpegArgumentsTests
{
    [Fact]
    public void ValidateSymbols()
    {
        var symbols = new[] { "ArrowUpload24", "Checkmark24", "Code24", "Gauge24", "Image24", "MusicNote224", "Settings24", "Target24", "TaskListSquareLtr24", "Video24" };
        var invalid = symbols.Where(s => !Enum.IsDefined(typeof(Wpf.Ui.Controls.SymbolRegular), s)).ToList();
        Assert.Empty(invalid);
    }
    private readonly FfmpegService _service = new();
    private readonly FfmpegService.GpuEncoderInfo _cpuOnlyGpu = new(false, false, false, "libx264", "libx265", null);

    private readonly CompressionProfile _balanceProfile = new(
        "balance",
        "Оптимальный (Баланс)",
        "Рекомендуется",
        "Оптимальный баланс размера и качества.",
        H264Crf: 23,
        HevcCrf: 26,
        Av1Crf: 28,
        CpuPreset: "medium",
        Av1CpuPreset: "6",
        GpuQualityLevel: 23);

    private readonly CompressionProfile _maxCompressionProfile = new(
        "max_compression",
        "Максимальное сжатие",
        "Мин. размер",
        "Максимальная экономия места.",
        H264Crf: 28,
        HevcCrf: 30,
        Av1Crf: 32,
        CpuPreset: "slow",
        Av1CpuPreset: "5",
        GpuQualityLevel: 28);

    private readonly CompressionProfile _highQualityProfile = new(
        "high_quality",
        "Высокое качество",
        "Без потерь",
        "Минимум визуальных потерь.",
        H264Crf: 19,
        HevcCrf: 22,
        Av1Crf: 24,
        CpuPreset: "slow",
        Av1CpuPreset: "5",
        GpuQualityLevel: 19);

    [Fact]
    public void H264_With_BalanceProfile_Produces_Libx264_Crf23_PresetMedium()
    {
        var args = _service.BuildCompressionArguments(
            profile: _balanceProfile,
            isTargetSizeMode: false,
            targetSizeMb: 25,
            durationSeconds: 60,
            resolution: "Оригинал",
            fps: "Оригинал",
            useInterpolation: false,
            isMuteAudio: false,
            useGpu: false,
            gpuInfo: _cpuOnlyGpu,
            videoCodec: "H.264 (AVC)",
            exportFormat: "MP4 Видео");

        Assert.Contains("-c:v libx264", args);
        Assert.Contains("-crf 23", args);
        Assert.Contains("-preset medium", args);
        Assert.Contains("-c:a aac -b:a 128k", args);
        Assert.Contains("-pix_fmt yuv420p -movflags +faststart", args);
    }

    [Fact]
    public void H265_With_MaxCompressionProfile_Produces_Libx265_Crf30_PresetSlow_Hvc1Tag()
    {
        var args = _service.BuildCompressionArguments(
            profile: _maxCompressionProfile,
            isTargetSizeMode: false,
            targetSizeMb: 25,
            durationSeconds: 60,
            resolution: "Оригинал",
            fps: "Оригинал",
            useInterpolation: false,
            isMuteAudio: false,
            useGpu: false,
            gpuInfo: _cpuOnlyGpu,
            videoCodec: "H.265 (HEVC)",
            exportFormat: "MP4 Видео");

        Assert.Contains("-c:v libx265", args);
        Assert.Contains("-crf 30", args);
        Assert.Contains("-preset slow", args);
        Assert.Contains("-tag:v hvc1", args);
    }

    [Fact]
    public void AV1_With_HighQualityProfile_Produces_Libsvtav1_Crf24_Preset5()
    {
        var args = _service.BuildCompressionArguments(
            profile: _highQualityProfile,
            isTargetSizeMode: false,
            targetSizeMb: 25,
            durationSeconds: 60,
            resolution: "Оригинал",
            fps: "Оригинал",
            useInterpolation: false,
            isMuteAudio: false,
            useGpu: false,
            gpuInfo: _cpuOnlyGpu,
            videoCodec: "AV1",
            exportFormat: "MP4 Видео");

        Assert.Contains("-c:v libsvtav1", args);
        Assert.Contains("-crf 24", args);
        Assert.Contains("-preset 5", args);
    }

    [Fact]
    public void MP3Export_Produces_Vn_Libmp3lame_AndCorrectBitrate()
    {
        var args = _service.BuildCompressionArguments(
            profile: _balanceProfile,
            isTargetSizeMode: false,
            targetSizeMb: 25,
            durationSeconds: 60,
            resolution: "Оригинал",
            fps: "Оригинал",
            useInterpolation: false,
            isMuteAudio: false,
            useGpu: false,
            gpuInfo: _cpuOnlyGpu,
            videoCodec: "H.264 (AVC)",
            exportFormat: "MP3 Аудио",
            audioBitrate: "192 kbps");

        Assert.Equal("-vn -c:a libmp3lame -b:a 192k", args);
    }

    [Fact]
    public void GIFExport_Produces_PaletteFilter_AndLoop0()
    {
        var args = _service.BuildCompressionArguments(
            profile: _balanceProfile,
            isTargetSizeMode: false,
            targetSizeMb: 25,
            durationSeconds: 60,
            resolution: "720p",
            fps: "15 FPS",
            useInterpolation: false,
            isMuteAudio: false,
            useGpu: false,
            gpuInfo: _cpuOnlyGpu,
            videoCodec: "H.264 (AVC)",
            exportFormat: "GIF Анимация");

        Assert.Contains("palettegen", args);
        Assert.Contains("paletteuse", args);
        Assert.Contains("-loop 0", args);
        Assert.DoesNotContain("-c:v", args);
    }

    [Fact]
    public void TargetSizeMode_CalculatesVideoBitrate_Correctly()
    {
        // 25 MB, 100 sec => (25 * 1024 * 1024 * 8 * 0.94) / 100000 = 1971 kbps total.
        // With audio (-96k) => 1875k video bitrate, maxrate 2625k, bufsize 3750k.
        var args = _service.BuildCompressionArguments(
            profile: _balanceProfile,
            isTargetSizeMode: true,
            targetSizeMb: 25,
            durationSeconds: 100,
            resolution: "Оригинал",
            fps: "Оригинал",
            useInterpolation: false,
            isMuteAudio: false,
            useGpu: false,
            gpuInfo: _cpuOnlyGpu,
            videoCodec: "H.264 (AVC)",
            exportFormat: "MP4 Видео");

        Assert.Contains("-b:v 1875k", args);
        Assert.Contains("-maxrate 2625k", args);
        Assert.Contains("-bufsize 3750k", args);
        Assert.Contains("-c:a aac -b:a 128k", args);
    }

    [Fact]
    public void TargetSizeMode_WithMuteAudio_UsesFullBitrateAndAn()
    {
        // 25 MB, 100 sec with mute => 1971k video bitrate, -an flag
        var args = _service.BuildCompressionArguments(
            profile: _balanceProfile,
            isTargetSizeMode: true,
            targetSizeMb: 25,
            durationSeconds: 100,
            resolution: "Оригинал",
            fps: "Оригинал",
            useInterpolation: false,
            isMuteAudio: true,
            useGpu: false,
            gpuInfo: _cpuOnlyGpu,
            videoCodec: "H.264 (AVC)",
            exportFormat: "MP4 Видео");

        Assert.Contains("-b:v 1971k", args);
        Assert.Contains("-an", args);
        Assert.DoesNotContain("-c:a", args);
    }

    [Fact]
    public void GpuEncoders_Nvenc_Qsv_Amf_ProduceCorrectFlags()
    {
        var nvenc = new FfmpegService.GpuEncoderInfo(true, false, false, "h264_nvenc", "hevc_nvenc", "av1_nvenc");
        var qsv = new FfmpegService.GpuEncoderInfo(false, true, false, "h264_qsv", "hevc_qsv", "av1_qsv");
        var amf = new FfmpegService.GpuEncoderInfo(false, false, true, "h264_amf", "hevc_amf", "av1_amf");

        var nvencArgs = _service.BuildCompressionArguments(
            profile: _balanceProfile,
            isTargetSizeMode: false,
            targetSizeMb: 25,
            durationSeconds: 60,
            resolution: "Оригинал",
            fps: "Оригинал",
            useInterpolation: false,
            isMuteAudio: false,
            useGpu: true,
            gpuInfo: nvenc,
            videoCodec: "H.264 (AVC)",
            exportFormat: "MP4 Видео");

        Assert.Contains("-c:v h264_nvenc", nvencArgs);
        Assert.Contains("-rc:v vbr -cq:v 23", nvencArgs);

        var qsvArgs = _service.BuildCompressionArguments(
            profile: _balanceProfile,
            isTargetSizeMode: false,
            targetSizeMb: 25,
            durationSeconds: 60,
            resolution: "Оригинал",
            fps: "Оригинал",
            useInterpolation: false,
            isMuteAudio: false,
            useGpu: true,
            gpuInfo: qsv,
            videoCodec: "H.264 (AVC)",
            exportFormat: "MP4 Видео");

        Assert.Contains("-c:v h264_qsv", qsvArgs);
        Assert.Contains("-global_quality 23", qsvArgs);

        var amfArgs = _service.BuildCompressionArguments(
            profile: _balanceProfile,
            isTargetSizeMode: false,
            targetSizeMb: 25,
            durationSeconds: 60,
            resolution: "Оригинал",
            fps: "Оригинал",
            useInterpolation: false,
            isMuteAudio: false,
            useGpu: true,
            gpuInfo: amf,
            videoCodec: "H.264 (AVC)",
            exportFormat: "MP4 Видео");

        Assert.Contains("-c:v h264_amf", amfArgs);
        Assert.Contains("-rc cqp -qp_p 23", amfArgs);
    }

    [Fact]
    public void Settings_RoundTripSerialization_And_LegacyProfileResolution()
    {
        var settings = new UserSettings
        {
            ExportFormat = "MP3 Аудио",
            AudioBitrate = "256 kbps",
            LastPresetName = "Максимальное сжатие",
            VideoCodec = "AV1"
        };

        var json = System.Text.Json.JsonSerializer.Serialize(settings);
        var restored = System.Text.Json.JsonSerializer.Deserialize<UserSettings>(json);

        Assert.NotNull(restored);
        Assert.Equal("MP3 Аудио", restored.ExportFormat);
        Assert.Equal("256 kbps", restored.AudioBitrate);
        Assert.Equal("Максимальное сжатие", restored.LastPresetName);
        Assert.Equal("AV1", restored.VideoCodec);

        var fastProfile = new CompressionProfile("fast", "Быстро", "", "", 28, 30, 32, "fast", "7", 28);
        var profiles = new List<CompressionProfile> { _balanceProfile, _maxCompressionProfile, _highQualityProfile, fastProfile };

        Assert.Equal("max_compression", SettingsService.ResolveProfile("HEVC", profiles).Id);
        Assert.Equal("high_quality", SettingsService.ResolveProfile("Высокое качество", profiles).Id);
        Assert.Equal("fast", SettingsService.ResolveProfile("Discord (8 MB)", profiles).Id);
        Assert.Equal("balance", SettingsService.ResolveProfile("balance", profiles).Id);
        Assert.Equal("balance", SettingsService.ResolveProfile(null, profiles).Id);
    }
}
