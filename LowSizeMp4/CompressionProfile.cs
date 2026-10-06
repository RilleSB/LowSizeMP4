namespace LowSizeMp4;

public sealed record CompressionProfile(
    string Id,
    string Name,
    string Badge,
    string Description,
    int H264Crf,
    int HevcCrf,
    int Av1Crf,
    string CpuPreset,
    string Av1CpuPreset,
    int GpuQualityLevel)
{
    public override string ToString() => Name;
}
