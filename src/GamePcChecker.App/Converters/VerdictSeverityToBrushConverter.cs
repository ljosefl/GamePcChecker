using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using GamePcChecker.App.Models;

namespace GamePcChecker.App.Converters;

public sealed class VerdictSeverityToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not VerdictSeverity s)
            return System.Windows.Media.Brushes.Gray;

        return s switch
        {
            VerdictSeverity.Ok => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3F, 0xB9, 0x50)),
            VerdictSeverity.Warn => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD2, 0x99, 0x22)),
            _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF8, 0x51, 0x49)),
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
