using System;
using System.Activities;
using System.Activities.Debugger;
using System.Activities.Presentation.View;
using System.Activities.XamlIntegration;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Threading;
using Activities.Flow;
using ActivitiesTracker.Core;
using ActivitiesTracker.Entities;
using FW.EasyMvvm.Core;
using RPA.Enums;
using RPA.Helpers;

namespace RPA.Core
{
    internal class ActivityRunner : BaseModel
    {
        private readonly Action<string, int> _log = CustomWfDesigner.GetInstance().Log;

        private WorkflowDesignerEx _workflowDesignerEx;
        private CustomTrackingParticipant _trackingParticipant;
        private WorkflowApplication _wfApp;
        private AutoResetEvent _resumeRuntimeFromHost;

        private readonly DispatcherTimer _subWorkflowTimer; // таймер для запуска дочернего WF в потоке UI
        private string _bookmarkName;
        private Activity _subWorkflowActivity;
        private DynamicActivity _activityExecute;
        private ActivityRunner _subWorkflowActivityRunner;
        private Dictionary<string, object> _subWorkflowArguments;
        private readonly Dictionary<string, object> _arguments;
        private WorkflowState _workflowState;
        private bool _isNeedPause = false;

        //public bool IsWorkflowComplete { get; private set; }
        public WorkflowState WorkflowState
        {
            get => _workflowState;
            set
            {
                _workflowState = value;
                OnPropertyChanged();
            }
        }

        public ActivityRunner(Dictionary<string, object> arguments = null)
        {
            _subWorkflowTimer = new DispatcherTimer()
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _subWorkflowTimer.Tick += RunSubWorkflow;
            _subWorkflowTimer.Start();
            _arguments = arguments;
        }

        internal void Run(string pathToXaml = null)
        {
            _isNeedPause = false;
            if (WorkflowState == WorkflowState.Complete)
            {
                return;
            }

            try
            {
                if (_resumeRuntimeFromHost is null)
                {
                    // остановка потока в точках останова
                    _resumeRuntimeFromHost = new AutoResetEvent(false);

                    // CP-08: зависимости робота ищутся в его выходном каталоге — до десериализации XAML,
                    // иначе собственные пакеты робота (bin\<Config>[\<TFM>]) не находятся и WF падает.
                    RobotAssemblyResolver.InstallForProject(CustomWfDesigner.GetInstance().Project?.ProjectDirPath);

                    // загрузка активности
                    _workflowDesignerEx = LoadActivityToDesignerHelper.LoadActivityToDesigner(pathToXaml);
                    SetDesignerIsReadonly(true);
                    //get workflow source from designer
                    CustomWfDesigner.GetInstance().ActiveWorkflowDesignerEx = _workflowDesignerEx;
                    DocGroupHelper.GetInstance().SetSelectedTab(_workflowDesignerEx);
                    // обновление символов для отладки
                    //Log.Write("breakpoints and map before DebuggerMappingsHelper.RegenerateSourceDebuggerMappings(_workflowDesignerEx);");
                    //LogBreakpoints(_workflowDesignerEx);
                    DebuggerMappingsHelper.RegenerateSourceDebuggerMappings(_workflowDesignerEx);
                    //Log.Write("breakpoints and map after DebuggerMappingsHelper.RegenerateSourceDebuggerMappings(_workflowDesignerEx);");
                    //LogBreakpoints(_workflowDesignerEx);
                    _workflowDesignerEx.LastBreakpoint = null;

                    // XAML грузится общим headless-движком: путь прогона один для студии и раннера (OR-02);
                    // остальное — студийная обвязка: дизайнер, подсветка, точки останова.
                    _activityExecute = WorkflowRunEngine.LoadActivityFromText(_workflowDesignerEx.Text) as DynamicActivity;
                    WorkflowState = WorkflowState.AwaitDesignerReady;
                }
                else
                {
                    _ = _resumeRuntimeFromHost.Set();
                    _subWorkflowActivityRunner?.Run();
                }
            }
            catch (Exception ex)
            {
                _log.Invoke($"{ex}", (int)RpaCommon.Enums.LogLevel.Error);
                WorkflowState = WorkflowState.StartingError;
                SetDesignerIsReadonly(false);
            }
        }

        private void SetDesignerIsReadonly(bool isReadOnly)
        {
            DesignerView designerView = _workflowDesignerEx.Context.Services.GetService<DesignerView>();
            if (designerView != null)
            {
                _ = designerView.Dispatcher.Invoke(() => designerView.IsReadOnly = isReadOnly);
            }
        }

        //private void LogBreakpoints(WorkflowDesignerEx workflowDesignerEx)
        //{
        //    foreach (SourceLocation bp in workflowDesignerEx.BreakpointList)
        //    {
        //        _log.Invoke($" start {bp.StartLine} {bp.StartColumn} end {bp.EndLine} {bp.EndColumn}", 1);
        //    }
        //}


        //private void LogBreakpoints(WorkflowDesignerEx workflowDesignerEx)
        //{
        //    // log breakpoints
        //    Log.Write("breakpoint:");
        //    workflowDesignerEx.BreakpointList.ForEach(b => Log.Write($"breakpoint {b.GetLogString()}"));

        //    // log DesignerSourceLocationMapping
        //    foreach (KeyValuePair<object, SourceLocation> item in workflowDesignerEx.DesignerSourceLocationMapping)
        //    {
        //        Activity activity = (Activity)item.Key;
        //        SourceLocation sourceLocation = item.Value;

        //        Log.Write($"activity {activity.Id} {activity.DisplayName} {sourceLocation.GetLogString()}");
        //    }
        //}


        /// <summary>
        /// Workflow Stop
        /// </summary>
        public void WorkflowStop()
        {
            //manual stop
            _subWorkflowActivityRunner?.WorkflowStop();
            _subWorkflowTimer?.Stop();
            _wfApp?.Abort("Stopped by User");
            _log.Invoke("Stopped by User", (int)RpaCommon.Enums.LogLevel.Trace);
            _workflowDesignerEx.DebugManagerView.CurrentLocation = null;
            WorkflowState = WorkflowState.Aborted;
            SetDesignerIsReadonly(false);
        }

        private void RunSubWorkflow(object sender, EventArgs e)
        {
            switch (WorkflowState)
            {
                case WorkflowState.NeedRunSubWorkflow:
                    WorkflowState = WorkflowState.RunSubWorkflow;
                    RunScript rs = (RunScript)_subWorkflowActivity;
                    string relativePathToXaml = _subWorkflowArguments["PathToXamlActivity"].ToString();
                    // удаляем аргумент PathToXamlActivity, т.к. он к делу не относится
                    _ = _subWorkflowArguments.Remove("PathToXamlActivity");
                    _bookmarkName = relativePathToXaml;
                    string pathToXaml = Path.Combine(CustomWfDesigner.GetInstance().Project.ProjectDirPath, relativePathToXaml);
                    _subWorkflowActivityRunner = new ActivityRunner(_subWorkflowArguments);
                    _subWorkflowActivityRunner.Run(pathToXaml);
                    break;
                case WorkflowState.AwaitDesignerReady:
                    // идентификаторы меняются после загрузки активности в дизайнер, поэтому так
                    Dictionary<object, SourceLocation>.Enumerator en = _workflowDesignerEx.DesignerSourceLocationMapping.GetEnumerator();
                    // поиск наличия активности для запуска (частный случай)
                    while (en.MoveNext())
                    {
                        if (((Activity)en.Current.Key).DisplayName == "Sequence")
                        {
                            if (((Activity)en.Current.Key).Id == "1.1")
                            {
                                WorkflowState = WorkflowState.Run;
                                break;
                            }
                        }
                    }
                    break;
                case WorkflowState.Run:
                    if (_wfApp == null)
                    {
                        try
                        {
                            // приложение создаёт общий движок: тот же трекер и те же аргументы, что и у
                            // headless-прогона, — здесь добавляется только студийное отображение
                            _wfApp = WorkflowRunEngine.CreateApplication(_activityExecute, _arguments, out _trackingParticipant);

                            //As the tracking events are received
                            _trackingParticipant.TrackingEventArgsRecieved += TrackingParticipant_TrackingEventArgsRecieved;
                            _wfApp.Completed = WfExecutionCompleted;
                            _wfApp.OnUnhandledException = WFOnUnhandledException;

                            //execute 
                            _wfApp.Run();
                        }
                        catch (Exception ex)
                        {
                            _log.Invoke($"{ex}", (int)RpaCommon.Enums.LogLevel.Error);
                            WorkflowState = WorkflowState.StartingError;
                        }
                    }
                    break;
                case WorkflowState.RunSubWorkflow:
                    if (_subWorkflowActivityRunner.WorkflowState == WorkflowState.Complete)
                    {
                        CustomWfDesigner.GetInstance().ActiveWorkflowDesignerEx = _workflowDesignerEx;
                        DocGroupHelper.GetInstance().SetSelectedTab(_workflowDesignerEx);
                        // остановка потока в точках останова
                        _resumeRuntimeFromHost = new AutoResetEvent(false);
                        WorkflowState = WorkflowState.Run;
                        _ = _wfApp.ResumeBookmark(_bookmarkName, null);
                    }
                    break;
                case WorkflowState.Sleep:
                    break;
                case WorkflowState.Debug:
                    break;
                case WorkflowState.Pause:
                    break;
                case WorkflowState.Aborted:
                    break;
                case WorkflowState.StartingError:
                    break;
                case WorkflowState.Complete:
                    break;
                default:
                    throw new ArgumentException($"unknow state {WorkflowState}");
            }
        }

#pragma warning disable IDE0051 // Remove unused private members
        private string GetArgumentValue(InArgument<string> pathToXamlActivity)
#pragma warning restore IDE0051 // Remove unused private members
        {
            throw new NotImplementedException();
        }

        private UnhandledExceptionAction WFOnUnhandledException(WorkflowApplicationUnhandledExceptionEventArgs args)
        {
            Exception ex = null;
            if (args.UnhandledException is AggregateException)
            {
                if (args.UnhandledException.InnerException?.InnerException != null)
                {
                    ex = args.UnhandledException.InnerException.InnerException;
                }
                else if (args.UnhandledException.InnerException != null)
                {
                    ex = args.UnhandledException.InnerException;
                }
            }
            ex ??= args.UnhandledException;

            StringBuilder stringBuilder = new StringBuilder();
            _ = stringBuilder.AppendLine($"Activity: {args.ExceptionSource.DisplayName}");
            _ = stringBuilder.AppendLine(ex.Message);
            _ = stringBuilder.AppendLine(ex.StackTrace);
            _log.Invoke(stringBuilder.ToString(), (int)RpaCommon.Enums.LogLevel.Error);
            _isNeedPause = false;
            _workflowDesignerEx.DebugManagerView.CurrentLocation = null;
            SetDesignerIsReadonly(false);
            WorkflowState = WorkflowState.Aborted;
            return UnhandledExceptionAction.Abort;
        }

        private void WfExecutionCompleted(WorkflowApplicationCompletedEventArgs args)
        {
            try
            {
                //retrieve & display execution output
                foreach (KeyValuePair<string, object> item in args.Outputs)
                {
                    _log(string.Format("[{0}] {1}" + Environment.NewLine, item.Key, item.Value), (int)RpaCommon.Enums.LogLevel.Info);
                }
            }
            catch (Exception ex)
            {
                _log(ex.ToString(), (int)RpaCommon.Enums.LogLevel.Error);
            }

            SetDesignerIsReadonly(false);
            _resumeRuntimeFromHost = null;
            _workflowDesignerEx.LastBreakpoint = null;
            _isNeedPause = false;

            WorkflowState = WorkflowState.Complete;

            // This is to remove the final debug adornment
            _workflowDesignerEx.DebugManagerView.CurrentLocation = null;
        }

        private void TrackingParticipant_TrackingEventArgsRecieved(object sender, TrackingEventArgs trackingEventArgs)
        {
            TrackHandler.HandleTrackingRecord(trackingEventArgs, _workflowDesignerEx, _resumeRuntimeFromHost,
                ref _workflowState, ref _subWorkflowActivity, _isNeedPause, out Dictionary<string, object> keyValuePairs);
            if (keyValuePairs != null)
            {
                _subWorkflowArguments = keyValuePairs;
            }
        }

        internal void WorkflowPause()
        {
            _subWorkflowActivityRunner?.WorkflowPause();
            _isNeedPause = true;
        }
    }
}
