using System;
using System.Globalization;
using System.Windows.Data;
using RPA.Core;

namespace RPA.Converters
{
    /// <summary>
    /// Converter for buttons that should be enabled when workflow is stopped
    /// Used for: Add, Save, Undo, Redo buttons
    /// </summary>
    public class IsStoppedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is CustomWfDesigner designer && designer.CustomWfDesignerState == CustomWfDesignerState.Stop;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converter for Run button - enabled when stopped, at breakpoint, or paused
    /// </summary>
    public class CanRunConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is CustomWfDesigner designer && (designer.CustomWfDesignerState == CustomWfDesignerState.Stop ||
                       designer.CustomWfDesignerState == CustomWfDesignerState.Breakpoint ||
                       designer.CustomWfDesignerState == CustomWfDesignerState.Pause);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converter for Pause button - enabled only when running
    /// </summary>
    public class CanPauseConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is CustomWfDesigner designer && designer.CustomWfDesignerState == CustomWfDesignerState.Run;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converter for Stop button - enabled when NOT stopped
    /// </summary>
    public class CanStopConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is CustomWfDesigner designer && designer.CustomWfDesignerState != CustomWfDesignerState.Stop;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
