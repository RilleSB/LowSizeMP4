namespace LowSizeMp4;

public sealed class FfmpegProgressEventArgs : EventArgs
{
    public double? Percent { get; init; }
    public string? CurrentTime { get; init; }
    public string? Speed { get; init; }
    public string? Fps { get; init; }
    public string? RawLine { get; init; }
}
