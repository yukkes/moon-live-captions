using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace MoonLiveCaptions.Converters
{
    /// <summary>
    /// Converts a boolean to Visibility (true → Visible, false → Collapsed).
    /// </summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool invert = parameter as string == "Invert";
            bool b = value is bool bv && bv;
            if (invert) b = !b;
            return b ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts an enum value to bool for use with radio-button style toggles.
    /// Usage: IsChecked="{Binding MyEnum, Converter={StaticResource EnumToBoolConverter}, ConverterParameter=EnumValue}"
    /// </summary>
    public class EnumToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null) return false;
            return value.ToString() == parameter.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b && parameter != null)
                return Enum.Parse(targetType, parameter.ToString());
            return Binding.DoNothing;
        }
    }

    /// <summary>
    /// Converts a double (0.0 - 1.0) to a SolidColorBrush with the specified opacity.
    /// The color is passed as the converter parameter (e.g., "#1A1A1A").
    /// </summary>
    public class OpacityToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double opacity = value is double d ? d : 0.85;
            string colorStr = parameter as string ?? "#1A1A1A";

            try
            {
                var color = (Color)ColorConverter.ConvertFromString(colorStr);
                color.A = (byte)(opacity * 255);
                return new SolidColorBrush(color);
            }
            catch
            {
                return new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 26, 26, 26));
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Multiplies a double value by a factor (ConverterParameter).
    /// </summary>
    public class MultiplyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d && parameter is string s && double.TryParse(s, out double factor))
                return d * factor;
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts a (progress 0-100, containerWidth) pair to pixel width for a progress bar border.
    /// </summary>
    public class ProgressWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 2 && values[1] is double width)
            {
                double progress = System.Convert.ToDouble(values[0]);
                return Math.Max(0, width * progress / 100.0);
            }
            return 0.0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Returns Visible when the string value is non-null and non-empty, Collapsed otherwise.
    /// </summary>
    public class NullOrEmptyToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
