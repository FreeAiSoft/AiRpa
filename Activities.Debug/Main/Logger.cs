using System;
using System.Activities;
using System.Activities.Presentation.Metadata;
using System.Activities.Presentation.PropertyEditing;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Activities.Debug.Designers;
using ActivityLibraryBase.Attributes;
using ActivityLibraryBase.BaseActivities;
using ActivityLibraryBase.Enums;
using ActivityLibraryBase.Resources;

namespace Activities.Debug.Main
{
    [LocalizedDescription("logger_desc", typeof(Resources.ActivitiesDebug))]
    [LocalizedDisplayName("logger", typeof(Resources.ActivitiesDebug))]
    [Designer(typeof(LoggerDesigner))]
    public sealed class Logger : CodeActivity
    {
        [LocalizedDescription("log_message_desc", typeof(Resources.ActivitiesDebug))]
        [LocalizedDisplayName("log_message", typeof(Resources.ActivitiesDebug))]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        public InArgument<string> LogMessage { get; set; }

        [LocalizedDescription("event_level_desc", typeof(Resources.ActivitiesDebug))]
        [LocalizedDisplayName("event_level", typeof(Resources.ActivitiesDebug))]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        public EventLevel EventLevel { get; set; }

        [LocalizedDescription("fields_desc", typeof(Resources.ActivitiesDebug))]
        [LocalizedDisplayName("fields", typeof(Resources.ActivitiesDebug))]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        public Dictionary<string, InArgument> Fields { get; set; }

        [LocalizedDescription("exception_desc", typeof(Resources.ActivitiesDebug))]
        [LocalizedDisplayName("exception", typeof(Resources.ActivitiesDebug))]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        public InArgument<Exception> Exception { get; set; }

        [LocalizedCategory("activities_base_standart", typeof(ActivityLibraryBaseResorces))]
        [LocalizedDescription("activities_base_is_enable_desc", typeof(ActivityLibraryBaseResorces))]
        [LocalizedDisplayName("activities_base_is_enable", typeof(ActivityLibraryBaseResorces))]
        public bool IsEnabled { get; set; } = true;

        public Logger()
        {
            Fields = new Dictionary<string, InArgument>();
            AttributeTableBuilder attributeTableBuilder = new AttributeTableBuilder();
            attributeTableBuilder.AddCustomAttributes(typeof(Logger),
                "Fields",
                new EditorAttribute(typeof(ArgumentCollectionEditor), typeof(PropertyValueEditor)));
            MetadataStore.AddAttributeTable(attributeTableBuilder.CreateTable());
        }

        protected override void Execute(CodeActivityContext context)
        {
            if (!IsEnabled)
            {
                return;
            }

            string logMessage = LogMessage.Get(context);
            Exception exception = Exception.Get(context);

            ActivityLogger activityLogger = new ActivityLogger(context);

            if (Fields.Count == 0)
            {
                activityLogger.LogWithProperties(logMessage, null, EventLevel, exception, "logger");
            }
            else
            {
                Dictionary<string, object> fields = Fields
                    .Select(f => new KeyValuePair<string, object>(f.Key, f.Value.Get(context)))
                    .ToDictionary(s => s.Key, s => s.Value);
                activityLogger.LogWithProperties(logMessage, fields, EventLevel, exception, "logger");
            }
        }
    }
}
