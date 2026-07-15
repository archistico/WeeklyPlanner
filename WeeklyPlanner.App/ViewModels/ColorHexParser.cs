using System.Globalization;
using Avalonia.Media;

namespace WeeklyPlanner.App.ViewModels;

internal static class ColorHexParser
{
    public static IBrush ToBrush(string? value)
    {
        if (value is null || value.Length != 7 || value[0] != '#')
        {
            return Brushes.Transparent;
        }

        if (!byte.TryParse(value.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red) ||
            !byte.TryParse(value.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green) ||
            !byte.TryParse(value.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue))
        {
            return Brushes.Transparent;
        }

        return new SolidColorBrush(Color.FromRgb(red, green, blue));
    }
}
