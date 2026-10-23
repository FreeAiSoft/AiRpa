using System;
using System.Activities;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using ActivityLibraryBase.Attributes;
using ActivityLibraryBase.Helpers;
using ActivityLibraryBase.Resources;

namespace ActivityLibraryBase.BaseActivities
{
    public abstract class BaseCodeActivity : CodeActivity
    {
        [Browsable(false)]
        public ActivityLogger ActivityLogger { get; set; }

        private readonly List<string> standartPropNames = new List<string> { "IsSafe", "IsNeedTrace", "IsEnabled", "IsNeedLogging" };
        private CodeActivityContext _context;

        [LocalizedCategory("activities_base_standart", typeof(ActivityLibraryBaseResorces))]
        [LocalizedDescription("activities_base_not_gen_error_desc", typeof(ActivityLibraryBaseResorces))]
        [LocalizedDisplayName("activities_base_not_gen_error", typeof(ActivityLibraryBaseResorces))]
        public bool IsSafe { get; set; }

        [LocalizedCategory("activities_base_standart", typeof(ActivityLibraryBaseResorces))]
        [LocalizedDescription("activities_base_args_trace_desc", typeof(ActivityLibraryBaseResorces))]
        [LocalizedDisplayName("activities_base_args_trace", typeof(ActivityLibraryBaseResorces))]
        public bool IsNeedTrace { get; set; }

        [LocalizedCategory("activities_base_standart", typeof(ActivityLibraryBaseResorces))]
        [LocalizedDescription("activities_base_logging_desc", typeof(ActivityLibraryBaseResorces))]
        [LocalizedDisplayName("activities_base_logging", typeof(ActivityLibraryBaseResorces))]
        public bool IsNeedLogging { get; set; }

        [LocalizedCategory("activities_base_standart", typeof(ActivityLibraryBaseResorces))]
        [LocalizedDescription("activities_base_is_enable_desc", typeof(ActivityLibraryBaseResorces))]
        [LocalizedDisplayName("activities_base_is_enable", typeof(ActivityLibraryBaseResorces))]
        public bool IsEnabled { get; set; } = true;

        [Browsable(false)]
        public string DispName => GetType().GetCustomAttribute<LocalizedDisplayNameAttribute>().DisplayName;

        [LocalizedCategory("activities_base_standart", typeof(ActivityLibraryBaseResorces))]
        [LocalizedDescription("activities_base_display_name_desc", typeof(ActivityLibraryBaseResorces))]
        [LocalizedDisplayName("activities_base_display_name", typeof(ActivityLibraryBaseResorces))]
        public new string DisplayName
        {
            get => base.DisplayName == GetType().Name ? DispName : base.DisplayName;
            set => base.DisplayName = value;
        }

        public BaseCodeActivity()
        {
            if (base.DisplayName == GetType().Name)
            {
                base.DisplayName = DispName;
            }
        }

        public abstract void Run(CodeActivityContext context);

        protected override void Execute(CodeActivityContext context)
        {
            if (!IsEnabled)
            {
                return;
            }

            ActivityLogger = new ActivityLogger(context);

            _context = context;

            ActivityLogger.Trace(LocalizeHelper.Get("activities_base_log_start_activity", DisplayName, GetType().Name), IsNeedLogging);

            IEnumerable<PropertyInfo> propertiesIn = GetType().GetRuntimeProperties()
                .Where(p => p.PropertyType.BaseType == typeof(InArgument));

            TraceArguments(propertiesIn, IsNeedTrace, true);

            try
            {
                Run(context);
            }
            catch (Exception exception)
            {
                Exception ex = null;
                if (exception is AggregateException)
                {
                    if (exception.InnerException?.InnerException != null)
                    {
                        ex = exception.InnerException.InnerException;
                    }
                    else if (exception.InnerException != null)
                    {
                        ex = exception.InnerException;
                    }
                }
                ex = ex ?? exception;
                ActivityLogger.Trace(LocalizeHelper.Get("activities_base_trace_exception", ex.Message, Environment.NewLine, ex.StackTrace), IsNeedLogging);
                if (!IsSafe)
                {
                    throw exception;
                }
            }

            IEnumerable<PropertyInfo> propertiesOut = GetType().GetRuntimeProperties()
                .Where(p => p.PropertyType.BaseType == typeof(OutArgument));

            TraceArguments(propertiesOut, IsNeedTrace, false);

            ActivityLogger.Trace(LocalizeHelper.Get("activities_base_activity_completed", DisplayName), IsNeedLogging);
        }

        private void TraceArguments(IEnumerable<PropertyInfo> properties, bool isNeedTracing, bool isInput)
        {
            if (!isNeedTracing && properties != null && properties.Count() > 0)
            {
                return;
            }

            try
            {
                Dictionary<string, object> fields = new Dictionary<string, object>();
                string message = isInput ? LocalizeHelper.Get("activities_base_input_args") : LocalizeHelper.Get("activities_base_output_args");
                foreach (PropertyInfo pi in properties)
                {
                    string propName = pi.Name;
                    if (standartPropNames.Contains(propName))
                    {
                        continue;
                    }

                    string description = null;
                    try
                    {
                        description = pi.CustomAttributes
                            .FirstOrDefault(a => a.AttributeType.Name == nameof(DescriptionAttribute))?.ConstructorArguments[0].ToString();
                    }
                    catch { }
                    Argument argInstance = (Argument)GetType().GetProperty(pi.Name).GetValue(this, null);
                    //по умолчанию тип object
                    Type inArgType = typeof(object);
                    if ((pi.PropertyType.GenericTypeArguments?.Length ?? 0) > 0)
                    {
                        inArgType = pi.PropertyType.GenericTypeArguments[0];
                    }

                    object argValue = _context.GetValue(argInstance);
                    fields.Add($"{propName}({description})", argValue);
                }

                if (fields.Count > 0)
                {
                    ActivityLogger.LogWithProperties(message, fields, Enums.EventLevel.Trace, null, "args");
                }
            }
            catch (Exception ex)
            {
                ActivityLogger.LogWithProperties(LocalizeHelper.Get("activities_base_argument_tracing_error"), null, Enums.EventLevel.Error, ex);
            }
        }
    }
}
