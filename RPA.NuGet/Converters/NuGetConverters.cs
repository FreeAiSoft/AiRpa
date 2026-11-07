using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RPA.NuGet.Converters
{
    /// <summary>
    /// Строка → видимость: пустая строка скрывает элемент. Нужен плейсхолдерам
    /// («Search (Ctrl+L)») и необязательным строкам метаданных (README, теги, лицензия).
    /// </summary>
    public class EmptyStringToVisibilityConverter : IValueConverter
    {
        /// <summary><c>true</c> — показывать при пустой строке (плейсхолдер), иначе при непустой.</summary>
        public bool VisibleWhenEmpty { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isEmpty = string.IsNullOrWhiteSpace(value as string);
            return isEmpty == VisibleWhenEmpty ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>Инверсия булева значения (для «показать, когда не занято»).</summary>
    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !(value is bool flag && flag);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !(value is bool flag && flag);
        }
    }

    /// <summary>
    /// Инверсия «bool → видимость»: элемент виден, когда значение <c>false</c>
    /// (монограмм вместо иконки, заглушка «деталей нет», пустой список).
    /// </summary>
    public class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool flag && flag ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
