using System.Globalization;
using System.Windows;
using System.Windows.Data;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using WpfColor = System.Windows.Media.Color;
using NcmdumpCSharpGui.Models;

namespace NcmdumpCSharpGui.Converters;

[ValueConversion(typeof(bool), typeof(Visibility))]
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool flag = value is true;
        // 支援 ConverterParameter="Invert" 反轉顯示結果
        if (parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase))
            flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

// 將 LogLevel 轉換為對應的 WPF 色彩筆刷
public class LogLevelToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush _success = new(WpfColor.FromRgb(27, 94, 32));
    private static readonly SolidColorBrush _error   = new(WpfColor.FromRgb(183, 28, 28));
    private static readonly SolidColorBrush _warning = new(WpfColor.FromRgb(230, 81, 0));
    private static readonly SolidColorBrush _info    = new(WpfColor.FromRgb(33, 33, 33));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is LogLevel level ? level switch
        {
            LogLevel.成功 => _success,
            LogLevel.錯誤 => _error,
            LogLevel.警告 => _warning,
            _            => _info
        } : _info;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>將 int 轉為 Math.Max(1, value)，防止 ProgressBar Maximum=0 時顯示異常。</summary>
[ValueConversion(typeof(int), typeof(int))]
public class MinOneConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int i ? Math.Max(1, i) : 1;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value;
}
