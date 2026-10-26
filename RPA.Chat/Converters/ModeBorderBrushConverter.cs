using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace RPA.Chat.Converters
{
    /// <summary>
    /// Chooses the composer input border brush: the active mode accent while the
    /// field is focused or hovered, otherwise the default outlined border brush.
    /// </summary>
    public sealed class ModeBorderBrushConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var modeBrush = values.Length > 0 ? values[0] as Brush : null;
            var focused = values.Length > 1 && values[1] is bool f && f;
            var hovered = values.Length > 2 && values[2] is bool h && h;
            var defaultBrush = values.Length > 3 ? values[3] as Brush : null;

            if ((focused || hovered) && modeBrush != null)
                return modeBrush;

            return defaultBrush;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
