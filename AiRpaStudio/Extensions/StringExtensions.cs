using System;
using ActivitiesTracker.Enums;
using RpaCommon.Enums;

namespace RPA.Extensions
{
    public static class StringExtensions
    {

        public static ActivityState ToActityState(this string activityInstanceState)
        {
            switch (activityInstanceState)
            {
                case "Executing":
                    return ActivityState.Executing;
                case "Closed":
                    return ActivityState.Closed;
                case "Canceled":
                    return ActivityState.Canceled;
                case "Faulted":
                    return ActivityState.Faulted;
                default:
                    throw new ArgumentException($"Unknow state {activityInstanceState}");
            }
        }

        public static LogLevel ToLogLevel(this string logLevel)
        {
            switch (logLevel.ToLower())
            {
                case "trace":
                    return LogLevel.Trace;
                case "debug":
                    return LogLevel.Debug;
                case "info":
                    return LogLevel.Info;
                case "warn":
                    return LogLevel.Warn;
                case "error":
                    return LogLevel.Error;
                case "fatal":
                    return LogLevel.Fatal;
                default:
                    throw new ArgumentException($"Unknow log level {logLevel}");
            }
        }
    }
}
