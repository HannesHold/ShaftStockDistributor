using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ShaftStockDistributor.Desktop.Converters;

/// <summary>Invariant-culture bridge so numeric fields stay compiled bindings.</summary>
public sealed class InvariantNumberConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            null => "",
            double number => number.ToString("0.###", CultureInfo.InvariantCulture),
            float number => number.ToString("0.###", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string text = value?.ToString()?.Trim() ?? "";
        Type type = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (type == typeof(int))
        {
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int whole) ? whole : 0;
        }

        if (type == typeof(double))
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ? number : 0d;
        }

        return value;
    }
}

public sealed class HexBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string hex = value?.ToString() ?? "";
        try
        {
            return new SolidColorBrush(Color.Parse(string.IsNullOrWhiteSpace(hex) ? "#5C6770" : hex));
        }
        catch (FormatException)
        {
            return new SolidColorBrush(Color.Parse("#5C6770"));
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
