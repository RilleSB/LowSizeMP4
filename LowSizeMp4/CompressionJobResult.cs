namespace LowSizeMp4;

public sealed record CompressionJobResult(
    bool Success,
    string? OutputPath,
    string Message);
