using System;
using System.Collections.Generic;
using RPA.Chat.Services.Spec;

namespace RPA.Core.Workflow
{
    /// <summary>
    /// Прогонщик тест-кейсов ТЗ внутри студии (волна 8, <c>TZ-08</c>): единственный исполнитель —
    /// <see cref="WorkflowRunEngine"/>, тот же движок, что у студии и будущего раннера <c>OR-02</c>.
    /// <para>
    /// Студия подключает службу в <c>ChatEnvironment.WorkflowRun</c>; когда появится <c>RpaRunner.exe</c>,
    /// он встанет за тем же контрактом <see cref="IWorkflowRunService"/>, и карточка не переписывается.
    /// </para>
    /// </summary>
    public sealed class StudioWorkflowRunService : IWorkflowRunService
    {
        public static readonly StudioWorkflowRunService Default = new StudioWorkflowRunService();

        public bool Available => true;

        public string UnavailableReason => string.Empty;

        public SpecRunOutcome Run(SpecRunRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.WorkflowPath))
            {
                return SpecRunOutcome.Failure("не указан путь процесса");
            }

            try
            {
                var inputs = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (var pair in request.Inputs ?? new Dictionary<string, string>())
                {
                    inputs[pair.Key] = pair.Value;
                }

                var timeout = request.TimeoutSeconds > 0
                    ? TimeSpan.FromSeconds(request.TimeoutSeconds)
                    : WorkflowRunEngine.DefaultTimeout;

                var result = WorkflowRunEngine.RunFile(request.WorkflowPath, inputs, timeout);

                var outputs = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (var pair in result.Outputs)
                {
                    outputs[pair.Key] = pair.Value;
                }

                return new SpecRunOutcome
                {
                    Ok = result.IsSuccess,
                    State = result.State.ToString(),
                    ExitCode = result.ExitCode,
                    Outputs = outputs,
                    ErrorText = result.ErrorText,
                    TimedOut = result.TimedOut
                };
            }
            catch (Exception ex)
            {
                // Ошибка загрузки XAML — это «прогон не выполнен» с причиной и стеком, а не падение инструмента.
                return new SpecRunOutcome
                {
                    Ok = false,
                    State = "Faulted",
                    ExitCode = 1,
                    ErrorText = Helpers.ErrorText.Of(ex)
                };
            }
        }
    }
}
