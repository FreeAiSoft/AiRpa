using ActivitiesTracker.Entities;
using RPA.Logging;

namespace RPA.Extensions
{
    public static class TrackingEventArgsExtensions
    {
        public static LogItem GetLogItem(this TrackingEventArgs trackingEventArgs)
        {
            return new LogItem
            {
                Message = trackingEventArgs.Message,
                LogLevel = trackingEventArgs.Level?.ToLogLevel() ?? RpaCommon.Enums.LogLevel.Trace
            };
        }
    }
}
