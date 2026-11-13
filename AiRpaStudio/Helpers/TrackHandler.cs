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
            bool isNeedPause,
            out Dictionary<string, object> arguments)
        {
            arguments = null;

            Dispatcher dispatcher = ((Window)CustomWfDesigner.GetInstance().ViewModel.View).Dispatcher;

            if (isNeedPause)
            {
                dispatcher.Invoke(
                    () =>
                    {
                        CustomWfDesigner.GetInstance().LogViewerControl.AddLog(LogControlLibrary.Models.LogLevel.Trace, "pause");
                    }, DispatcherPriority.Send);
                _ = resumeRuntimeFromHost.WaitOne();
            }

            if (trackingEventArgs.Activity != null)
            {
                LogItem logItem = trackingEventArgs.GetLogItem();

                dispatcher.Invoke(
                    () =>
                    {
                        CustomWfDesigner.GetInstance().LogViewerControl.AddLog((LogControlLibrary.Models.LogLevel)logItem.LogLevel, logItem.Message);
                    }, DispatcherPriority.Send);

                SourceLocation sourceLocation = GetSourceLocation(workflowDesignerEx.DesignerSourceLocationMapping, trackingEventArgs.Activity);

                if (sourceLocation != null)
                {
                    ShowDebug(sourceLocation, workflowDesignerEx, resumeRuntimeFromHost, dispatcher);
                }
                else
                {
                    dispatcher.Invoke(
                        () =>
                        {
                            CustomWfDesigner.GetInstance().Log($"Not found SourceLocation for {trackingEventArgs.Activity}");
                        });
                }
            }

            // Запуск вложенной активности: параметры отдаются вызывающей стороне (ActivityRunner),
            // которая запускает дочерний прогон событийно, а не поллингом по DispatcherTimer.
            if (trackingEventArgs.ActivityInfo?.TypeName == "Activities.Flow.RunScript")
            {
                if (((ActivityStateRecord)trackingEventArgs.Record).State == "Executing")
                {
                    arguments = trackingEventArgs.ActivityParameters;
                }
            }
        }

        /// <summary>
        /// Здесь устанавливается жёлтая рамка на исполняемой активности и происходит блокирование
        /// потока при попадании в точку останова. Подсветка и состояние ставятся на UI-потоке,
        /// а <c>WaitOne()</c> остаётся на фоновом потоке прогона — иначе точка останова заморозила бы
        /// студию.
        /// </summary>
        private static void ShowDebug(SourceLocation srcLoc, WorkflowDesignerEx workflowDesignerEx, AutoResetEvent resumeRuntimeFromHost, Dispatcher dispatcher)
        {
            bool isBreakpointHit = false;

            dispatcher.Invoke(() =>
            {
                workflowDesignerEx.DebugManagerView.CurrentLocation = srcLoc;

                // Check if this is where any BP is set
                foreach (SourceLocation src in workflowDesignerEx.BreakpointList)
                {
                    if (src.StartLine == srcLoc.StartLine &&
                        src.EndLine == srcLoc.EndLine)
                    {
                        if (workflowDesignerEx.LastBreakpoint != src) // хуйня для предотвращения двойной остановки
                        {
                            CustomWfDesigner.GetInstance().LogViewerControl.AddLog(LogControlLibrary.Models.LogLevel.Trace, "breakpoint hit");
                            isBreakpointHit = true;
                            workflowDesignerEx.LastBreakpoint = src;
                        }
                        break;
                    }
                }

                if (isBreakpointHit)
                {
                    CustomWfDesigner.GetInstance().CustomWfDesignerState = CustomWfDesignerState.Breakpoint;
                }
            });

            if (isBreakpointHit)
            {
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
