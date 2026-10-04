using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace LowSizeMp4;

public static class Converters
{
    private static readonly Brush GreenBrush = new SolidColorBrush(Color.FromRgb(108, 203, 95));
    private static readonly Brush YellowBrush = new SolidColorBrush(Color.FromRgb(252, 225, 0));

    public static readonly IValueConverter NullToVisibleConverter = new SimpleConverter(
        (val, _, _, _) => val == null ? Visibility.Visible : Visibility.Collapsed);

    public static readonly IValueConverter NotNullToVisibleConverter = new SimpleConverter(
        (val, _, _, _) => val != null ? Visibility.Visible : Visibility.Collapsed);

    public static readonly IValueConverter BoolToVisibleConverter = new SimpleConverter(
        (val, _, _, _) => val is true ? Visibility.Visible : Visibility.Collapsed);

    public static readonly IValueConverter InverseBoolConverter = new TwoWayInverseBoolConverter();

    public static readonly IValueConverter InverseBoolToVisibleConverter = new SimpleConverter(
        (val, _, _, _) => val is false ? Visibility.Visible : Visibility.Collapsed);

    public static readonly IValueConverter IntEqualsConverter = new SimpleConverter(
        (val, _, param, _) => val != null && param != null && val.ToString() == param.ToString());

    public static readonly IValueConverter PathButtonTextConverter = new SimpleConverter(
        (val, _, _, _) => val is true ? "Удалить из PATH" : "Добавить в PATH");

    public static readonly IValueConverter EmptyQueueToVisibleConverter = new SimpleConverter(
        (val, _, _, _) => val is int count && count == 0 ? Visibility.Visible : Visibility.Collapsed);

    public static readonly IValueConverter HasQueueToVisibleConverter = new SimpleConverter(
        (val, _, _, _) => val is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed);

    public static readonly IValueConverter PresetSelectedConverter = new SimpleConverter(
        (val, _, param, _) => val != null && param != null && val.ToString() == param.ToString());

    public static readonly IValueConverter BoolToFfmpegBrushConverter = new SimpleConverter(
        (val, _, _, _) => val is true ? GreenBrush : YellowBrush);

    public static readonly IValueConverter BoolToFfmpegTextConverter = new SimpleConverter(
        (val, _, _, _) => val is true ? "FFmpeg готов" : "FFmpeg не найден");

    public static readonly IValueConverter HexToBrushConverter = new SimpleConverter((val, _, _, _) =>
    {
        if (val is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                hex = hex.Trim();
                if (!hex.StartsWith("#")) hex = "#" + hex;
                var color = (Color)ColorConverter.ConvertFromString(hex);
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
            catch
            {
            }
        }
        return Brushes.Transparent;
    });
}

public class TwoWayInverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is false;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is false;
}

public class SimpleConverter(Func<object?, Type, object?, CultureInfo, object?> convert) : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => convert(value, targetType, parameter, culture);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw freshNotSupportedException();

    private static NotSupportedException freshNotSupportedException() => new();
}
