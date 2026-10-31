using System;
using ActivitiesTracker.Enums;
using RpaCommon.Enums;

namespace RPA.Extensions
{
    public static class StringExtensions
    {

        public static ActivityState ToActityState(this string activityInstanceState)
        {
            return activityInstanceState switch
            {
                "Executing" => ActivityState.Executing,
                "Closed" => ActivityState.Closed,
                "Canceled" => ActivityState.Canceled,
                "Faulted" => ActivityState.Faulted,
                _ => throw new ArgumentException($"Unknow state {activityInstanceState}"),
            };
        }

        public static LogLevel ToLogLevel(this string logLevel)
        {
            return logLevel.ToLower() switch
            {
                "trace" => LogLevel.Trace,
                "debug" => LogLevel.Debug,
                "info" => LogLevel.Info,
                "warn" => LogLevel.Warn,
                "error" => LogLevel.Error,
                "fatal" => LogLevel.Fatal,
                _ => throw new ArgumentException($"Unknow log level {logLevel}"),
            };
        }
    }
}
