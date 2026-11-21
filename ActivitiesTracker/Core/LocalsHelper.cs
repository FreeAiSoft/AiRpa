using System;
using System.Activities.Tracking;
using System.Collections.Generic;
using System.Linq;
using ActivitiesTracker.Entities;
using ActivitiesTracker.Extensions;

namespace ActivitiesTracker.Core
{
    internal static class LocalsHelper
    {
        private static readonly string[] StandardArgumentNames =
        {
            "IsSafe", "IsNeedTrace", "IsEnabled", "IsNeedLogging"
        };

        public static Dictionary<string, object> GetArgsFromRecord(ActivityStateRecord activityStateRecord)
        {
            return GetArgsFromRecord(activityStateRecord, false);
        }

        public static Dictionary<string, object> GetArgsFromRecord(ActivityStateRecord activityStateRecord, bool excludeStandardArguments)
        {
            if (activityStateRecord == null || activityStateRecord.Arguments == null)
            {
                return new Dictionary<string, object>();
            }

            IEnumerable<KeyValuePair<string, object>> arguments = activityStateRecord.Arguments;
            if (excludeStandardArguments)
            {
                arguments = arguments.Where(p => !StandardArgumentNames.Contains(p.Key));
            }

            return arguments.ToDictionary(s => s.Key, s => s.Value);
        }

        public static List<LocalVar> GetLocalsAndArgs(ActivityStateRecord record)
        {
            List<LocalVar> locals = new List<LocalVar>();
            if (record == null)
            {
                return locals;
            }

            string scope = record.Activity?.Name;

            if (record.Variables != null)
            {
                foreach (KeyValuePair<string, object> variable in record.Variables)
                {
                    locals.Add(CreateLocalVar(variable.Key, variable.Value, scope));
                }
            }

            foreach (KeyValuePair<string, object> argument in GetArgsFromRecord(record))
            {
                locals.Add(CreateLocalVar(argument.Key, argument.Value, scope));
            }

            return locals;
        }

        private static LocalVar CreateLocalVar(string name, object value, string scope)
        {
            Type type = value?.GetType();
            return new LocalVar(name, value, type?.GetFormattedName(), scope);
        }
    }
}