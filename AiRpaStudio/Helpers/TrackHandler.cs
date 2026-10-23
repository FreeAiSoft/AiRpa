using System;
using System.Activities;
using System.Activities.Debugger;
using System.Activities.Tracking;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using ActivitiesTracker.Entities;
using RPA.Core;
using RPA.Enums;
using RPA.Extensions;
using RPA.Logging;

namespace RPA.Helpers
{
    internal class TrackHandler
    {
        public static void HandleTrackingRecord(
            TrackingEventArgs trackingEventArgs,
            WorkflowDesignerEx workflowDesignerEx,
            AutoResetEvent resumeRuntimeFromHost,
            ref WorkflowState workflowState,
            ref Activity subWorkflowActivity,
            bool isNeedPause,
            out Dictionary<string, object> arguments)
        {
            arguments = null;

            if (isNeedPause)
            {
                ((Window)CustomWfDesigner.GetInstance().ViewModel.View).Dispatcher.Invoke(
                        () =>
                        {
                            CustomWfDesigner.GetInstance().LogViewerControl.AddLog(LogControlLibrary.Models.LogLevel.Trace, "pause");
                        }, DispatcherPriority.Send);
                _ = resumeRuntimeFromHost.WaitOne();
            }

            if (trackingEventArgs.Activity != null)
            {
                LogItem logItem = trackingEventArgs.GetLogItem();
                ((Window)CustomWfDesigner.GetInstance().ViewModel.View).Dispatcher.Invoke(
                        () =>
                        {
                            CustomWfDesigner.GetInstance().LogViewerControl.AddLog((LogControlLibrary.Models.LogLevel)logItem.LogLevel, logItem.Message);
                        }, DispatcherPriority.Send);


                SourceLocation sourceLocation = GetSourceLocation(workflowDesignerEx.DesignerSourceLocationMapping, trackingEventArgs.Activity);
                if (sourceLocation != null)
                {
                    ShowDebug(sourceLocation, workflowDesignerEx, resumeRuntimeFromHost);
                }
                else
                {
                    CustomWfDesigner.GetInstance().Log($"Not found SourceLocation for {trackingEventArgs.Activity}");
                }
            }

            // Запуск вложенной активности
            if (trackingEventArgs.ActivityInfo?.TypeName == "Activities.Flow.RunScript")
            {
                if (((ActivityStateRecord)trackingEventArgs.Record).State == "Executing")
                {
                    workflowState = WorkflowState.NeedRunSubWorkflow;
                    subWorkflowActivity = trackingEventArgs.Activity;
                    arguments = trackingEventArgs.ActivityParameters;
                }
            }
        }

        /// <summary>
        /// Здесь устанавливается жёлтая рамка на исполняемой активности
        /// и происходит блокирование потока при попадании в точку останова
        /// </summary>
        /// <param name="srcLoc"></param>
        private static void ShowDebug(SourceLocation srcLoc, WorkflowDesignerEx workflowDesignerEx, AutoResetEvent resumeRuntimeFromHost)
        {
            workflowDesignerEx.DebugManagerView.CurrentLocation = srcLoc;

            // Check if this is where any BP is set
            bool isBreakpointHit = false;
            foreach (SourceLocation src in workflowDesignerEx.BreakpointList)
            {
                if (src.StartLine == srcLoc.StartLine &&
                    src.EndLine == srcLoc.EndLine)
                {
                    if (workflowDesignerEx.LastBreakpoint != src) // хуйня для предотвращения двойной остановки
                    {
                        ((Window)CustomWfDesigner.GetInstance().ViewModel.View).Dispatcher.Invoke(
                        () =>
                        {
                            CustomWfDesigner.GetInstance().LogViewerControl.AddLog(LogControlLibrary.Models.LogLevel.Trace, "breakpoint hit");
                        }, DispatcherPriority.Send);
                        isBreakpointHit = true;
                        workflowDesignerEx.LastBreakpoint = src;
                    }
                    break;
                }
            }

            if (isBreakpointHit)
            {
                ((Window)CustomWfDesigner.GetInstance().ViewModel.View).Dispatcher.Invoke(
                        () =>
                        {
                            CustomWfDesigner.GetInstance().CustomWfDesignerState = CustomWfDesignerState.Breakpoint;
                        });
                _ = resumeRuntimeFromHost.WaitOne();
            }
        }

        public static SourceLocation GetSourceLocation(Dictionary<object, SourceLocation> designerSourceLocationMapping, Activity activity)
        {
            try
            {
                return designerSourceLocationMapping.First(a => ((Activity)a.Key).Id == activity.Id).Value;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
