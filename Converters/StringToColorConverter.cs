using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace DutyListPlugin.Converters;

/// <summary>
/// 在 DutyItem.Color（"#RRGGBB" 字符串）与 Avalonia.Media.Color 之间双向转换，
/// 供设置页 ColorPicker 绑定使用。
/// </summary>
public class StringToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && Color.TryParse(s, out var c))
            return c;
        return Color.FromRgb(0, 191, 255); // 默认天蓝
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Color c)
            return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        return "#00BFFF";
    }
}
