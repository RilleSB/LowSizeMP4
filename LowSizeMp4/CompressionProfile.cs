namespace LowSizeMp4;

public sealed record CompressionProfile(
    string Name,
    string Badge,
    string Description,
    string Arguments)
{
    public override string ToString() => Name;
}
