using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace DualView.GUI.Converters;

public class EnumToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return false;

        var checkValue = parameter.ToString();
        var targetValue = value.ToString();

        return checkValue != null && checkValue.Equals(targetValue, StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true || parameter == null)
            return BindingOperations.DoNothing;

        return Enum.Parse(targetType, parameter.ToString()!, true);
    }
}
