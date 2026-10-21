using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace SmartSchool.Admin.Helpers;

public sealed class BoolToBrushConverter : IValueConverter
{
    public string TrueBrush { get; set; } = "#4CAF50";
    public string FalseBrush { get; set; } = "#B0BEC5";

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => new SolidColorBrush((Color)ColorConverter.ConvertFromString(
            value is true ? TrueBrush : FalseBrush));
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not true;
}

/// <summary>PickIn -> green, DropOut -> orange, anything else -> grey.</summary>
public sealed class EventTypeToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => new SolidColorBrush((Color)ColorConverter.ConvertFromString(value?.ToString() switch
        {
            "PickIn" => "#4CAF50",
            "DropOut" => "#FF9800",
            _ => "#90A4AE"
        }));
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class InverseBoolToVisConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class StringNotEmptyToVisConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value?.ToString()) ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

// <-- CHANGED (added): empty string -> Visible (photo-na-ho fallback icon dikhane ke liye)
public sealed class StringEmptyToVisConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value?.ToString()) ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>"Picked" / "Dropped" / "" live status -> green/orange/transparent chip.</summary>
public sealed class LiveStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => new SolidColorBrush((Color)ColorConverter.ConvertFromString(value?.ToString() switch
        {
            "Picked" => "#334CAF50",
            "Dropped" => "#33FF9800",
            _ => "#00000000"
        }));
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
