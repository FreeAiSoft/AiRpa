using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace RPA.AiAgent.Converters
{
    /// <summary>
    /// Converts a boolean value to a color based on the converter parameter.
    /// Parameter format: "TrueColor|FalseColor" (e.g., "#4CAF50|#999999")
    /// </summary>
    public class BoolToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue && parameter is string colorParam)
            {
                var colors = colorParam.Split('|');
                if (colors.Length == 2)
                {
                    var colorString = boolValue ? colors[0].Trim() : colors[1].Trim();
                    return (SolidColorBrush)new BrushConverter().ConvertFrom(colorString);
                }
            }
            
            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}