using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Data;
using RPA.AiAgent.Models;

namespace RPA.AiAgent.Converters
{
    /// <summary>
    /// Converts message content string to a list of MessageContentPart objects
    /// </summary>
    public class MessageContentToPartsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            Stopwatch sw = Stopwatch.StartNew();

            if (value is string content)
            {
                List<MessageContentPart> result = MessageContentParser.Parse(content);
                sw.Stop();
                return result;
            }

            sw.Stop();
            return new List<MessageContentPart>();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
