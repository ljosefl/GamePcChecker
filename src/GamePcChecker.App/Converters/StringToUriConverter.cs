using System.Globalization;
using System.Windows.Data;

namespace GamePcChecker.App.Converters;

/// <summary>Преобразует строку URL в Uri для Hyperlink.NavigateUri; пустая строка → null.</summary>
public sealed class StringToUriConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s || string.IsNullOrWhiteSpace(s))
            return null;
        if (!Uri.TryCreate(s.Trim(), UriKind.Absolute, out var uri))
            return null;
        return uri;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
