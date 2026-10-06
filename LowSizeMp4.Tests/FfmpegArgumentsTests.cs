using LowSizeMp4;
using Xunit;

namespace LowSizeMp4.Tests;

public class FfmpegArgumentsTests
{
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
}
