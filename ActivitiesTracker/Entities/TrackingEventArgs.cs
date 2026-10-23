using System;
using System.Activities;
using System.Activities.Tracking;
using System.Collections.Generic;
using ActivitiesTracker.Enums;

namespace ActivitiesTracker.Entities
{
    public class TrackingEventArgs : EventArgs
    {
        /// <summary>
        /// Message text
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// For logger fields
        /// </summary>
        public Dictionary<string, object> Fields { get; set; }

        /// <summary>
        /// Logger level, if need
        /// </summary>
        public string Level { get; set; }

        /// <summary>
        /// Exception, if need
        /// </summary>
        public Exception Exception { get; set; }

        /// <summary>
        /// Activity parameters for ActivityStateRecord
        /// </summary>
        public Dictionary<string, object> ActivityParameters { get; set; }

        public TrackingRecord Record { get; set; }

        public Activity Activity { get; set; }

        public ActivityInfo ActivityInfo { get; set; }

        public ActivityState? ActivityState { get; set; }

        public TrackingEventArgs() { }

        public TrackingEventArgs(string message, Dictionary<string, object> fields, string level, Exception exception)
        {
            Message = message;
            Fields = fields;
            Level = level;
            Exception = exception;
        }

        public TrackingEventArgs(string message)
        {
            Message = message;
            Level = "trace";
        }
    }
}
