using System;
using System.Activities;
using System.Activities.Presentation.Model;
using System.Globalization;
using System.Windows.Data;

namespace Activities.Converters
{
    public class BoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            ModelItem modelItem = value as ModelItem;
            if (value != null)
            {
                InArgument<bool> inArgument = modelItem.GetCurrentValue() as InArgument<bool>;
                return inArgument.Expression.ToString() != "False";
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool itemContent = (bool)value;
            InArgument<bool> inArgument = new InArgument<bool>(itemContent);
            return inArgument;
        }
    }
}
