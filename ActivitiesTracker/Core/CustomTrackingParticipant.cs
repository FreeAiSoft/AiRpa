using System;
using System.Activities;
using System.Activities.Tracking;
using System.Collections.Generic;
using System.Linq;
using ActivitiesTracker.Entities;
using ActivitiesTracker.Enums;
using ActivitiesTracker.Extensions;

namespace ActivitiesTracker.Core
{
    /// <summary>
    /// Tracker present info for:
    /// 1. Studio logger window with LogItem
    /// 2. Visual tracking in WF designer and break points
    /// 3. Locals window
    /// </summary>
    public class CustomTrackingParticipant : TrackingParticipant
    {
        private const string all = "*";
        public event EventHandler<TrackingEventArgs> TrackingEventArgsRecieved;

        public CustomTrackingParticipant()
        {
            TrackingProfile = new TrackingProfile()
            {
                Name = "CustomTrackingProfile",
                ImplementationVisibility = ImplementationVisibility.All,
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
            };
        }

        protected override void Track(TrackingRecord trackingRecord, TimeSpan timeout)
        {
            try
            {
                if (TrackingEventArgsRecieved == null)
                {
                    return;
                }

                if (trackingRecord is ActivityStateRecord activityStateRecord)
                {
                    if (activityStateRecord.Activity?.Name == "Logger")
                    {
                        return;
                    }

                    Activity trackingActivity = activityStateRecord.Activity.GetProperty<Activity>("Activity");
                    string trackingMessage = CreateActivityStateMessage(activityStateRecord);
                    Dictionary<string, object> arguments = LocalsHelper.GetArgsFromRecord(activityStateRecord);

                    TrackingEventArgsRecieved.Invoke(this, new TrackingEventArgs()
                    {
                        Message = trackingMessage,
                        ActivityParameters = arguments,
                        Activity = trackingActivity,
                        ActivityState = activityStateRecord.State.ToActityState(),
                        ActivityInfo = activityStateRecord.Activity,
                        Record = activityStateRecord
                    });

                    // предсказание запуска активности
                    Activity nextActivity = null;
                    string trackingActivityType = trackingActivity.GetType().Name;
                    Activity parent = trackingActivity.GetProperty<Activity>("Parent");
                    string parentActivityType = parent?.GetType().Name;
                    // Был запуск Sequence, ищем первого потомка
                    if (activityStateRecord.State == "Executing" && trackingActivityType == "Sequence")
                    {
                        dynamic children = trackingActivity.GetProperty<dynamic>("Children");
                        if (children.Count > 0)
                        {
                            nextActivity = children[0];
                        }
                    }
                    // Завершение активности внутри Sequence, ищем следующую
                    else if (activityStateRecord.State == "Closed" && parentActivityType == "Sequence")
                    {
                        dynamic neighbours = parent.GetProperty<dynamic>("Children");
                        if (neighbours.Count > 1)
                        {
                            int index = neighbours.IndexOf(trackingActivity);
                            if (index < neighbours.Count - 1)
                            {
                                nextActivity = neighbours[index + 1];
                            }
                        }
                    }

                    if (nextActivity != null)
                    {
                        TrackingEventArgsRecieved.Invoke(this, new TrackingEventArgs()
                        {
                            Message = $"Запуск {nextActivity.DisplayName}",
                            Activity = nextActivity,
                            ActivityState = ActivityState.Prestart
                        });
                    }
                }
                else if (trackingRecord is WorkflowInstanceRecord workflowInstanceRecord)
                {
                    string trackingMessage = CreateWorkflowInstanceMessage(workflowInstanceRecord);
                    TrackingEventArgsRecieved.Invoke(this, new TrackingEventArgs()
                    {
                        Message = trackingMessage
                    });
                }
                else if (trackingRecord is CustomTrackingRecord customTrackingRecord)
                {
                    if (customTrackingRecord.Activity?.Name == "Logger")
                    {
                        TrackingEventArgs trackingEventArgs = GetLoggerEventArgs(customTrackingRecord);
                        TrackingEventArgsRecieved.Invoke(this, trackingEventArgs);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        private TrackingEventArgs GetLoggerEventArgs(CustomTrackingRecord customTrackingRecord)
        {
            Dictionary<string, object> data = customTrackingRecord.Data.ToDictionary(s => s.Key, s => s.Value);

            string message = (string)data["message"];
            Dictionary<string, object> fields = (Dictionary<string, object>)data["fields"];
            string level = (string)data["level"];
            Exception exception = (Exception)data["exception"];

            return new TrackingEventArgs(message, fields, level, exception);
        }

        private string CreateWorkflowInstanceMessage(WorkflowInstanceRecord workflowInstanceRecord)
        {
            string state;
            switch (workflowInstanceRecord.State)
            {
                case "Started":
                    state = "запущен";
                    break;
                case "Completed":
                    state = "завершён";
                    break;
                default:
                    state = workflowInstanceRecord.State;
                    break;
            }
            return $"Процесс {workflowInstanceRecord.ActivityDefinitionId} {state}";
        }

        //private void TraceArgumentsForDebug(ActivityStateRecord activityStateRecord)
        //{
        //    Dictionary<string, object> arguments = LocalsHelper.GetArgsFromRecord(activityStateRecord);
        //    IEnumerator<KeyValuePair<string, object>> en = arguments.GetEnumerator();
        //    while (en.MoveNext())
        //    {

        //    }
        //}

        private string CreateActivityStateMessage(ActivityStateRecord activityStateRecord)
        {
            string state;
            switch (activityStateRecord.State)
            {
                case "Executing":
                    state = "Запущена";
                    break;
                case "Closed":
                    state = "Завершена";
                    break;
                default:
                    state = activityStateRecord.State;
                    break;
            }
            return $"{state} активность {activityStateRecord.Activity?.Name}";
        }
    }
}
