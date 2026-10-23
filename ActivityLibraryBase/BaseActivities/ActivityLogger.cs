using System;
using System.Activities;
using System.Activities.Tracking;
using System.Collections.Generic;
using ActivityLibraryBase.Enums;

namespace ActivityLibraryBase.BaseActivities
{
    public class ActivityLogger
    {
        private readonly CodeActivityContext _context;
        public static Action<string, int> OutputAction;

        public ActivityLogger(CodeActivityContext codeActivityContext)
        {
            _context = codeActivityContext;
        }

        public void Trace(string message, bool isNeedLogging)
        {
            if (isNeedLogging)
            {
                Trace(message);
            }
        }

        public void Trace(string message)
        {
            Log(message, EventLevel.Trace);
        }

        public void Log(string message, EventLevel level, Exception ex = null)
        {
            LogWithProperties(message, null, level, ex);
        }

        public void Debug(string message)
        {
            Log(message, EventLevel.Debug);
        }

        public void Info(string message)
        {
            Log(message, EventLevel.Info);
        }

        public void Warning(string message)
        {
            Log(message, EventLevel.Warn);
        }

        public void Error(string message)
        {
            Log(message, EventLevel.Error);
        }

        public void Fatal(string message)
        {
            Log(message, EventLevel.Fatal);
        }

        public void LogWithProperties(string message, Dictionary<string, object> fields, EventLevel level, Exception exception = null, string recordTitle = null)
        {
            CustomTrackingRecord trackingRecord = new CustomTrackingRecord(recordTitle ?? level.ToString());
            trackingRecord.Data.Add("message", message);
            trackingRecord.Data.Add("fields", fields);
            trackingRecord.Data.Add("exception", exception);
            trackingRecord.Data.Add("level", level.ToString());
            _context.Track(trackingRecord);

            OutputAction?.Invoke(message, (int)level);
        }
    }
}
