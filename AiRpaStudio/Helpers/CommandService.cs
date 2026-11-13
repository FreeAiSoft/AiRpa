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
using RPA.Logging;

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
                    if (!TryResolveSourceLocation(parameters, out SourceLocation insertLocation))
                    {
                        return;
                    }

                    _workflowDesigner.Context.Services.GetService<IDesignerDebugView>().UpdateBreakpoint(insertLocation, GetBreakpointTypes(parameters) | BreakpointTypes.Enabled);
                    AddToBreakpointList(insertLocation);
                    AutoSaveBreakpoints();
                    break;

                case CommandValues.DeleteBreakpoint:
                    if (!TryResolveSourceLocation(parameters, out SourceLocation deleteLocation))
                    {
                        return;
                    }

                    _workflowDesigner.Context.Services.GetService<IDesignerDebugView>().UpdateBreakpoint(deleteLocation, BreakpointTypes.None);
                    RemoveFromBreakpointList(deleteLocation);
                    AutoSaveBreakpoints();
                    break;

                case CommandValues.EnableBreakpoint:
                    if (!TryResolveSourceLocation(parameters, out SourceLocation enableLocation))
                    {
                        return;
                    }

                    _workflowDesigner.Context.Services.GetService<IDesignerDebugView>().UpdateBreakpoint(enableLocation, BreakpointTypes.Enabled | BreakpointTypes.Bounded);
                    AddToBreakpointList(enableLocation);
                    AutoSaveBreakpoints();
                    break;

                case CommandValues.DisableBreakpoint:
                    if (!TryResolveSourceLocation(parameters, out SourceLocation disableLocation))
                    {
                        return;
                    }

                    _workflowDesigner.Context.Services.GetService<IDesignerDebugView>().UpdateBreakpoint(disableLocation, BreakpointTypes.Bounded);
                    RemoveFromBreakpointList(disableLocation);
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
        /// Резолвит <see cref="SourceLocation"/> команды: сначала из параметров, переданных
        /// <c>DesignerView</c>, затем из собственного <see cref="WorkflowDesignerEx.DesignerSourceLocationMapping"/>
        /// по текущему выделению. Второй путь нужен, когда <see cref="IDesignerDebugView.SelectedLocation"/>
        /// вернул <c>null</c> — при loose-открытии штатный маппинг дизайнера пуст, а ручной валиден.
        /// </summary>
        private bool TryResolveSourceLocation(Dictionary<string, object> parameters, out SourceLocation sourceLocation)
        {
            return TryGetSourceLocation(parameters, out sourceLocation)
                || TryGetSelectedSourceLocation(out sourceLocation);
        }

        /// <summary>
        /// Безопасно извлекает <see cref="SourceLocation"/> из параметров команды DesignerView.
        /// Значение может быть <c>null</c>, когда для выбранного элемента не построен маппинг исходников
        /// (например, для корня ActivityBuilder или до первой генерации SourceLocationProvider).
        /// </summary>
        private static bool TryGetSourceLocation(Dictionary<string, object> parameters, out SourceLocation sourceLocation)
        {
            sourceLocation = null;

            if (parameters != null
                && parameters.TryGetValue("SourceLocation", out object value)
                && value is SourceLocation location)
            {
                sourceLocation = location;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Извлекает <see cref="SourceLocation"/> текущего выделения из собственного
        /// <see cref="WorkflowDesignerEx.DesignerSourceLocationMapping"/>.
        /// </summary>
        private bool TryGetSelectedSourceLocation(out SourceLocation sourceLocation)
        {
            sourceLocation = null;

            try
            {
                ModelItem mi = _workflowDesigner.Context.Items.GetValue<Selection>()?.PrimarySelection;
                if (!(mi?.GetCurrentValue() is Activity activity))
                {
                    return false;
                }

                return _workflowDesigner.DesignerSourceLocationMapping.TryGetValue(activity, out sourceLocation);
            }
            catch (Exception ex)
            {
                SourceLocationMappingDiagnostics.WriteException(ex, "CommandService.TryGetSelectedSourceLocation failed");
                return false;
            }
        }

        private void AddToBreakpointList(SourceLocation sourceLocation)
        {
            if (sourceLocation != null && !_workflowDesigner.BreakpointList.Contains(sourceLocation))
            {
                _workflowDesigner.BreakpointList.Add(sourceLocation);
            }
        }

        private void RemoveFromBreakpointList(SourceLocation sourceLocation)
        {
            if (sourceLocation != null)
            {
                _ = _workflowDesigner.BreakpointList.Remove(sourceLocation);
            }
        }

        /// <summary>
        /// Извлекает тип точки останова из параметров команды. Если параметр отсутствует,
        /// используется <see cref="BreakpointTypes.Bounded"/> — значение по умолчанию, которое
        /// DesignerView передаёт при вставке точки останова.
        /// </summary>
        private static BreakpointTypes GetBreakpointTypes(Dictionary<string, object> parameters)
        {
            if (parameters != null
                && parameters.TryGetValue("BreakpointTypes", out object value)
                && value is BreakpointTypes breakpointTypes)
            {
                return breakpointTypes;
            }

            return BreakpointTypes.Bounded;
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
