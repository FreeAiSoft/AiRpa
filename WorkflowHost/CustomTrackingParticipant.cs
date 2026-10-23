using System;
using System.Activities.Tracking;
using System.Collections.Generic;
using System.Activities;

namespace WorkflowHost
{
    /// <summary>
    /// Workflow Tracking Participant - Custom Implementation
    /// </summary>
    internal class CustomTrackingParticipant : TrackingParticipant
    {
        public string TrackData = string.Empty;
        public event EventHandler<TrackingEventArgs> TrackingRecordReceived;

        public Dictionary<string, Activity> ActivityIdToWorkflowElementMap { get; set; }

        protected override void Track(TrackingRecord record, TimeSpan timeout)
        {
            OnTrackingRecordReceived(record, timeout);
        }

        //On Tracing Record Received call the TrackingRecordReceived with the record received information from the TrackingParticipant. 
        //We also do not worry about Expressions' tracking data
        protected void OnTrackingRecordReceived(TrackingRecord record, TimeSpan timeout)
        {
            ActivityInfo activityInfo = null;
            if (record is ActivityStateRecord recordEntry)
            {
                TrackData += string.Format("[{0}] [{1}] [{2}]" + Environment.NewLine,
                    recordEntry.EventTime.ToLocalTime().ToString(),
                    recordEntry.Activity.Name,
                    recordEntry.State);
                activityInfo = recordEntry.Activity;
            }
            else if (record is WorkflowInstanceUnhandledExceptionRecord activityRecord)
            {
                TrackData += string.Format("[{0}] [{1}] [{2}]" + Environment.NewLine,
                    activityRecord.EventTime.ToLocalTime().ToString(),
                    activityRecord.UnhandledException.Message,
                    activityRecord.State);
            }
            else if (record is WorkflowInstanceRecord workflowInstanceRecord)
            {
                TrackData += string.Format("[{0}] [{1}] [{2}]" + Environment.NewLine,
                    workflowInstanceRecord.EventTime.ToLocalTime().ToString(),
                    workflowInstanceRecord.ActivityDefinitionId,
                    workflowInstanceRecord.State);
            }

            if (TrackingRecordReceived != null)
            {
                ActivityStateRecord activityStateRecord = record as ActivityStateRecord;
                TrackingRecordReceived(this, new TrackingEventArgs(record, timeout, activityInfo));
            }
        }

        internal static CustomTrackingParticipant Get()
        {
            const string all = "*";
            return new CustomTrackingParticipant
            {
                TrackingProfile = new TrackingProfile()
                {
                    Name = "CustomTrackingProfile",
                    Queries =
                    {
                        new CustomTrackingQuery
                        {
                                Name = all,
                                ActivityName = all
                        },
                        new WorkflowInstanceQuery
                        {
                                // Limit workflow instance tracking records for started and completed workflow states
                                //States = { WorkflowInstanceStates.Started, WorkflowInstanceStates.Completed },
                                States = {all}
                        },
                        new ActivityStateQuery
                        {
                                // Subscribe for track records from all activities for all states
                                ActivityName = all,
                                States = { all },
                                Arguments = {all},
                                
                                // Extract workflow variables and arguments as a part of the activity tracking record
                                // VariableName = "*" allows for extraction of all variables in the scope
                                // of the activity
                                Variables =
                                {
                                    { all }
                                }
                        }
                    }
                }
            };
        }
    }

    //Custom Tracking EventArgs
    public class TrackingEventArgs : EventArgs
    {
        public TrackingRecord Record { get; set; }

        public TimeSpan Timeout { get; set; }

        public Activity Activity { get; set; }

        public ActivityInfo ActivityInfo { get; set; }

        public TrackingEventArgs(TrackingRecord trackingRecord, TimeSpan timeout, ActivityInfo activityInfo)
        {
            Record = trackingRecord;
            Timeout = timeout;
            ActivityInfo = activityInfo;
        }
    }
}
