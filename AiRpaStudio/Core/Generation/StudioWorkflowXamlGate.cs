using System;
using RPA.Chat.Services.Workflow;
using RPA.Helpers;

namespace RPA.Core.Generation
{
    /// <summary>
    /// Гейт валидации XAML для чата (карточка <c>AG-07</c>): инструмент <c>editor</c> после записи
    /// <c>.xaml</c> спрашивает у студии, примет ли файл конструктор. Своей проверки здесь нет намеренно —
    /// вызов идёт в тот же <see cref="WorkflowValidationGate"/>, который проверяет генерируемые процессы
    /// (форма корня, загрузка WF, валидация WF), иначе «валидно» значило бы разное в двух местах.
    /// </summary>
    internal sealed class StudioWorkflowXamlGate : IWorkflowXamlGate
    {
        private static readonly Lazy<StudioWorkflowXamlGate> Shared =
            new Lazy<StudioWorkflowXamlGate>(() => new StudioWorkflowXamlGate());

        private readonly WorkflowValidationGate _gate;

        /// <param name="gate">Гейт валидации; по умолчанию — стандартный гейт студии.</param>
        internal StudioWorkflowXamlGate(WorkflowValidationGate gate = null)
        {
            _gate = gate ?? new WorkflowValidationGate();
        }

        /// <summary>Служба для хоста чата: <c>ChatEnvironment.WorkflowXamlGate</c>.</summary>
        internal static StudioWorkflowXamlGate Default => Shared.Value;

        public WorkflowXamlGateResult ValidateText(string xaml)
        {
            try
            {
                WorkflowValidationReport report = _gate.ValidateText(xaml);
                return WorkflowXamlGateResult.From(report.Errors, report.Warnings);
            }
            catch (Exception ex)
            {
                // Упавшая проверка — это «не выполнена», а не «ошибок нет»: иначе сломанный файл
                // выглядел бы проверенным.
                return WorkflowXamlGateResult.NotPerformed(ErrorText.Describe("валидация XAML не выполнена", ex));
            }
        }
    }
}
