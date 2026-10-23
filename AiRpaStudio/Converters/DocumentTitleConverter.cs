using System;
using System.Globalization;
using System.Windows.Data;

namespace RPA.Converters
{
    /// <summary>
    /// Converter that combines filename and IsEdited property to create document title with asterisk when edited
    /// </summary>
    public class DocumentTitleConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2)
            {
                return string.Empty;
            }

            string fileName = values[0] as string;
            if (string.IsNullOrEmpty(fileName))
            {
                return string.Empty;
            }

            // Second value is IsEdited property
            bool isEdited = false;
            if (values[1] is bool editedValue)
            {
                isEdited = editedValue;
            }

            // Add asterisk if edited
            return isEdited ? $"{fileName} *" : fileName;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException("ConvertBack is not supported for DocumentTitleConverter");
        }
    }
}