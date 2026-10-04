using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LowSizeMp4;

public partial class AccentColorItem : ObservableObject
{
    public string Name { get; }
    public string Hex { get; }
    public Brush Brush { get; }

    [ObservableProperty]
    private bool _isSelected;

    public AccentColorItem(string name, string hex, Brush brush, bool isSelected = false)
    {
        Name = name;
        Hex = hex;
        Brush = brush;
        _isSelected = isSelected;
    }
}
