using System.Globalization;
using Avalonia.Data.Converters;

namespace Lychee;

/// <summary>
/// True when the bound string is non-null and non-empty; used to hide the
/// optional "Detail" line of a module row.
/// </summary>
public sealed class HasTextConverter : IValueConverter
{
    public static readonly HasTextConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !string.IsNullOrEmpty(value as string);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
