using System;
using System.Activities;
using System.Activities.Tracking;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using ActivitiesTracker.Entities;
using ActivitiesTracker.Extensions;

namespace ActivitiesTracker.Core
{
    internal static class LocalsHelper
    {
        private static Guid _instanceId;
        private static Guid InstanceId
        {
            get => _instanceId;
            set
            {
                if (value != _instanceId && _instanceId != Guid.Empty)
                {
                    throw new InvalidOperationException("invalid _instanceId");
                }
                _instanceId = value;
            }
        }

        private static readonly ReadOnlyCollection<string> standartPropNames
            = new ReadOnlyCollection<string>(new List<string> { "IsSafe", "IsNeedTrace", "IsEnabled", "IsNeedLogging" });

        public static Dictionary<string, object> GetArgsFromRecord(ActivityStateRecord activityStateRecord)
        {
            return activityStateRecord.Arguments
                .Where(p => !standartPropNames.Contains(p.Key))
                .ToDictionary(s => s.Key, s => s.Value);
        }

        public static List<LocalVar> GetLocalsAndArgs(ActivityStateRecord record)
        {
            InstanceId = record.InstanceId;
            List<LocalVar> locals = new List<LocalVar>();
            Activity activity;
            do
            {
                activity = record.Activity.GetProperty<Activity>("Activity");
                Dictionary<string, object> localVarsOrArgs = GetLocals(record);
                Dictionary<string, object>.Enumerator en = localVarsOrArgs.GetEnumerator();
                while (en.MoveNext())
                {
                    Type localVarType = en.Current.Value?.GetType();
                    locals.Add(new LocalVar(en.Current.Key, en.Current.Value, localVarType?.GetFormattedName(), activity.DisplayName));

                    ActivityInstance parentInstance = record.Activity
                        .GetProperty<ActivityInstance>("Instance")
                        .GetProperty<ActivityInstance>("Parent");
                    if (parentInstance != null)
                    {
                        ActivityInfo activityInfo = CreateInstance<ActivityInfo>(parentInstance);
                        record = new ActivityStateRecord(InstanceId, 0, activityInfo, parentInstance.State.ToString());
                    }
                }
            }
            while (activity.GetType().BaseType.Name != "Activity");
            return locals;
        }

        public static T CreateInstance<T>(params object[] args)
        {
            Type type = typeof(T);
            object instance = type.Assembly.CreateInstance(
                type.FullName, false,
                BindingFlags.Instance | BindingFlags.NonPublic,
                null, args, null, null);
            return (T)instance;

        }

        private static Dictionary<string, object> GetLocals(ActivityStateRecord record)
        {
            Activity activity = record.Activity.GetProperty<Activity>("Activity");
            string baseTypeName = activity.GetType().BaseType.Name;
            Dictionary<string, object> locals = new Dictionary<string, object>();
            if (baseTypeName == "Activity")
            {
                ActivityStateRecord activityStateRecord = new ActivityStateRecord(record.InstanceId, 0, record.Activity, "Closed");
                Dictionary<string, object> args = GetArgsFromRecord(activityStateRecord);
                Dictionary<string, object>.Enumerator en = args.GetEnumerator();
                while (en.MoveNext())
                {
                    locals.Add(en.Current.Key, en.Current.Value);
                }
            }
            else
            {
                Dictionary<string, object> vars = new Dictionary<string, object>(record.Variables);
                Dictionary<string, object>.Enumerator en = vars.GetEnumerator();
                while (en.MoveNext())
                {
                    locals.Add(en.Current.Key, en.Current.Value);
                }
            }
            return locals;
        }
    }
}
