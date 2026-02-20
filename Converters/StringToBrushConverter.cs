using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace DutyListPlugin.Converters;

/// <summary>
/// 将 "#RRGGBB" 字符串转换为 SolidColorBrush，供设置页颜色预览使用。
/// 解析失败时返回透明色，不抛异常。
/// </summary>
public class StringToBrushConverter : IValueConverter
{
    public static readonly StringToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && Color.TryParse(hex, out var color))
            return new SolidColorBrush(color);
        return Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
