using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Activities.Converters
{
    /// <summary>
    /// Цвет подписи активности по состоянию «включена/выключена».
    /// <para>
    /// Цвета берутся из палитры студии по строковым ключам <c>Studio.Foreground</c> /
    /// <c>Studio.ForegroundMuted</c> (их пишет <c>StudioBrushes.ApplyToApplication</c>): библиотека
    /// активностей не ссылается на <c>RPA.Helpers</c>. Зашитых кистей Black/LightGray
    /// быть не должно — в тёмной теме чёрные буквы на тёмной плите не читаются.
    /// </para>
    /// </summary>
    public class BoolToColorConverter : IValueConverter
    {
        /// <summary>Ключ палитры для включённой активности.</summary>
        public const string EnabledKey = "Studio.Foreground";

        /// <summary>Ключ палитры для выключенной активности.</summary>
        public const string DisabledKey = "Studio.ForegroundMuted";

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool enabled = value is bool flag && flag;
            return Brush(enabled);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        private static Brush Brush(bool enabled)
        {
            ResourceDictionary resources = Application.Current == null ? null : Application.Current.Resources;
            object themed = resources == null ? null : resources[enabled ? EnabledKey : DisabledKey];

            if (themed is Brush brush)
            {
                return brush;
            }

            return enabled ? SystemColors.ControlTextBrush : SystemColors.GrayTextBrush;
        }
    }
}
