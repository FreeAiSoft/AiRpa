using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Documents;

namespace RPA.NuGet.Converters
{
    /// <summary>
    /// Markdown (README пакета) → <see cref="FlowDocument"/> для панели деталей: пользователь видит
    /// оформленный текст, а не «сырой» md.
    /// </summary>
    public class MarkdownToFlowDocumentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return MarkdownRenderer.Render(value as string);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
