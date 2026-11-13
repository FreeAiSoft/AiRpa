using System;
using System.Activities;
using System.Activities.Debugger;
using System.Activities.Tracking;
using System.Collections.Generic;
using System.IO;
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
            Dictionary<object, SourceLocation> runtimeMapping,
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

                SourceLocation sourceLocation = GetSourceLocation(
                    runtimeMapping,
                    workflowDesignerEx.DesignerSourceLocationMapping,
                    trackingEventArgs.Activity);

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
                SourceLocation normalized = Normalize(srcLoc);
                WorkflowDesignerEx targetDesigner = ResolveTargetDesigner(normalized, workflowDesignerEx);

                if (targetDesigner == null)
                {
                    CustomWfDesigner.GetInstance().Log($"Not found designer for {normalized.FileName}");
                    return;
                }

                targetDesigner.DebugManagerView.CurrentLocation = normalized;

                // Check if this is where any BP is set
                foreach (SourceLocation src in targetDesigner.BreakpointList)
                {
                    if (src.StartLine == normalized.StartLine &&
                        src.EndLine == normalized.EndLine)
                    {
                        if (targetDesigner.LastBreakpoint != src) // защита от двойной остановки
                        {
                            CustomWfDesigner.GetInstance().LogViewerControl.AddLog(LogControlLibrary.Models.LogLevel.Trace, "breakpoint hit");
                            isBreakpointHit = true;
                            targetDesigner.LastBreakpoint = src;
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

        /// <summary>
        /// Сброс checksum: <see cref="SourceLocation.Equals"/> учитывает <c>Checksum</c>, а loose-дизайнер
        /// строит маппинг с <c>null</c>, рантаймный символ — с непустым. Перед сопоставлением с точками
        /// останова и дизайнером локация пересоздаётся без checksum.
        /// </summary>
        private static SourceLocation Normalize(SourceLocation location)
        {
            if (location == null)
            {
                return null;
            }

            return new SourceLocation(
                location.FileName,
                location.StartLine,
                location.StartColumn,
                location.EndLine,
                location.EndColumn);
        }

        /// <summary>
        /// По <see cref="SourceLocation.FileName"/> выбирает целевой дизайнер: текущий → уже открытый
        /// (без учёта регистра) → закрытый (открыть loose-путём и активировать).
        /// </summary>
        private static WorkflowDesignerEx ResolveTargetDesigner(SourceLocation srcLoc, WorkflowDesignerEx currentDesigner)
        {
            string filePath = ResolveActualPath(srcLoc.FileName);
            CustomWfDesigner studio = CustomWfDesigner.GetInstance();

            if (filePath != null && IsSameFile(currentDesigner, filePath))
            {
                return currentDesigner;
            }

            WorkflowDesignerEx opened = studio.GetDesignerByFileIgnoreCase(filePath);
            if (opened != null)
            {
                return opened;
            }

            if (filePath != null)
            {
                WorkflowDesignerEx loaded = LoadActivityToDesignerHelper.LoadActivityToDesigner(
                    filePath,
                    studio.GetLooseRegistryFor(filePath));

                if (loaded != null)
                {
                    studio.ActiveWorkflowDesignerEx = loaded;
                    return loaded;
                }
            }

            return null;
        }

        /// <summary>
        /// Находит фактический путь файла без учёта регистра: <see cref="SourceLocation.FileName"/>
        /// хранится в <c>ToUpperInvariant()</c>, а loose-открытие/поиск точек останова должны получить
        /// реальный путь на диске.
        /// </summary>
        private static string ResolveActualPath(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            if (!File.Exists(fileName))
            {
                return null;
            }

            try
            {
                string directory = Path.GetDirectoryName(fileName);
                string targetName = Path.GetFileName(fileName);

                if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                {
                    return fileName;
                }

                return Directory.EnumerateFiles(directory)
                    .FirstOrDefault(candidate =>
                        string.Equals(Path.GetFileName(candidate), targetName, StringComparison.OrdinalIgnoreCase))
                    ?? fileName;
            }
            catch (Exception)
            {
                return fileName;
            }
        }

        private static bool IsSameFile(WorkflowDesignerEx designer, string filePath)
        {
            if (designer == null || string.IsNullOrWhiteSpace(filePath))
            {
                return false;
            }

            string loaded = CustomWfDesigner.GetLoadedFile(designer);
            return !string.IsNullOrEmpty(loaded)
                && string.Equals(loaded, filePath, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Ищет SourceLocation сначала в рантайм-маппинге (собранная сборка — реальные тела всех скриптов
        /// процесса), затем — фолбэк в маппинге дизайнера (loose-фасады верхнего уровня). Пустой/отсутствующий
        /// рантайм-маппинг сохраняет прежний путь.
        /// </summary>
        public static SourceLocation GetSourceLocation(
            Dictionary<object, SourceLocation> runtimeMapping,
            Dictionary<object, SourceLocation> designerSourceLocationMapping,
            Activity activity)
        {
            return GetSourceLocation(runtimeMapping, activity)
                ?? GetSourceLocation(designerSourceLocationMapping, activity);
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
