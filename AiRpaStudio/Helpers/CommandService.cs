using System;
using System.Activities;
using System.Activities.Debugger;
using System.Activities.Presentation.Debug;
using System.Activities.Presentation.Hosting;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.View;
using System.Collections.Generic;
using System.IO;
using RPA.Core;

namespace RPA.Helpers
{
    public class CommandService : ICommandService
    {
        private readonly WorkflowDesignerEx _workflowDesigner;
        private readonly CustomWfDesigner _customWfDesigner;

        public CommandService(WorkflowDesignerEx designer)
        {
            _workflowDesigner = designer;
            _customWfDesigner = CustomWfDesigner.GetInstance();
            BreakpointsChanged += CmdSvc_BreakpointsChanged;
        }

        private void CmdSvc_BreakpointsChanged(object sender, EventArgs e)
        {
            ModelItem mi = _workflowDesigner.Context.Items.GetValue<Selection>().PrimarySelection;
            if (!(mi?.GetCurrentValue() is Activity activity))
            {
                return;
            }

            if (!_workflowDesigner.DesignerSourceLocationMapping.ContainsKey(activity))
            {
                return;
            }

            SourceLocation srcLoc = _workflowDesigner.DesignerSourceLocationMapping[activity];
            if (!_workflowDesigner.BreakpointList.Contains(srcLoc))
            {
                _workflowDesigner.Context.Services.GetService<IDesignerDebugView>().UpdateBreakpoint(srcLoc, BreakpointTypes.Bounded | BreakpointTypes.Enabled);
                _workflowDesigner.BreakpointList.Add(srcLoc);
            }
            else
            {
                _workflowDesigner.Context.Services.GetService<IDesignerDebugView>().UpdateBreakpoint(srcLoc, BreakpointTypes.None);
                _ = _workflowDesigner.BreakpointList.Remove(srcLoc);
            }

            // Автоматически сохраняем точки останова после изменения
            AutoSaveBreakpoints();
        }

        public bool CanExecuteCommand(int commandId)
        {
            return true;
        }

        public event EventHandler BreakpointsChanged;
        public event EventHandler ShowPropertiesRequested;

        public void ExecuteCommand(int commandId, Dictionary<string, object> parameters)
        {
            switch (commandId)
            {
                case CommandValues.InsertBreakpoint:
                    _workflowDesigner.Context.Services.GetService<IDesignerDebugView>().UpdateBreakpoint((SourceLocation)parameters["SourceLocation"], (BreakpointTypes)parameters["BreakpointTypes"] | BreakpointTypes.Enabled);
                    BreakpointsChanged?.Invoke(this, new EventArgs());
                    AutoSaveBreakpoints();
                    break;
                case CommandValues.DeleteBreakpoint:
                    _workflowDesigner.Context.Services.GetService<IDesignerDebugView>().UpdateBreakpoint((SourceLocation)parameters["SourceLocation"], BreakpointTypes.None);
                    BreakpointsChanged?.Invoke(this, new EventArgs());
                    AutoSaveBreakpoints();
                    break;
                case CommandValues.EnableBreakpoint:
                    _workflowDesigner.Context.Services.GetService<IDesignerDebugView>().UpdateBreakpoint((SourceLocation)parameters["SourceLocation"], BreakpointTypes.Enabled | BreakpointTypes.Bounded);
                    BreakpointsChanged?.Invoke(this, new EventArgs());
                    AutoSaveBreakpoints();
                    break;
                case CommandValues.DisableBreakpoint:
                    _workflowDesigner.Context.Services.GetService<IDesignerDebugView>().UpdateBreakpoint((SourceLocation)parameters["SourceLocation"], BreakpointTypes.Bounded);
                    BreakpointsChanged?.Invoke(this, new EventArgs());
                    AutoSaveBreakpoints();
                    break;
                case CommandValues.ShowProperties:
                    ShowPropertiesRequested?.Invoke(this, new EventArgs());
                    break;
                default:
                    break;
            }
        }

        /// <summary>
        /// Автоматически сохраняет точки останова после их изменения
        /// </summary>
        private void AutoSaveBreakpoints()
        {
            try
            {
                string workflowFile = _workflowDesigner.CurrentWorkflowFile;
                string breakPointsFile;

                if (string.IsNullOrEmpty(workflowFile) || !Path.IsPathRooted(workflowFile))
                {
                    // Несохраненная активность - создаем временный файл
                    string tempDir = Path.Combine(Path.GetTempPath(), "RPA_Breakpoints");
                    _ = Directory.CreateDirectory(tempDir);

                    // Используем хеш от designer instance для уникальности
                    string designerId = _workflowDesigner.GetHashCode().ToString();
                    breakPointsFile = Path.Combine(tempDir, $"temp_{designerId}.breakpoints");
                }
                else
                {
                    // Сохраненная активность - используем стандартный путь
                    breakPointsFile = $"{workflowFile}.breakpoints";
                }

                _customWfDesigner.SaveBreakpoints(_workflowDesigner, breakPointsFile);
            }
            catch (Exception ex)
            {
                // Логирование ошибки без прерывания работы
                Console.WriteLine($"Error auto-saving breakpoints: {ex}");
            }
        }

        public bool IsCommandSupported(int commandId)
        {
            switch (commandId)
            {
                case CommandValues.ShowProperties:
                case CommandValues.InsertBreakpoint:
                case CommandValues.DeleteBreakpoint:
                case CommandValues.EnableBreakpoint:
                case CommandValues.DisableBreakpoint:
                    return true;
                default:
                    return false;
            }
        }
    }
}
