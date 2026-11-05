using System;
using System.Collections.Generic;
using System.Linq;

namespace RPA.Chat.Services.Workflow
{
    /// <summary>
    /// Вердикт валидации записанного XAML (карточка <c>AG-07</c>). Три состояния названы явно:
    /// ошибок нет, ошибки есть, валидация <b>не выполнялась</b>. Последнее нельзя показывать как
    /// «ошибок нет» — именно так агент пропускает сломанный файл.
    /// </summary>
    public sealed class WorkflowXamlGateResult
    {
        private static readonly string[] Empty = new string[0];

        private WorkflowXamlGateResult(bool validated, IReadOnlyList<string> errors, IReadOnlyList<string> warnings, string reason)
        {
            Validated = validated;
            Errors = errors ?? Empty;
            Warnings = warnings ?? Empty;
            Reason = reason;
        }

        /// <summary>Проверка действительно выполнялась (гейт подключён и не упал).</summary>
        public bool Validated { get; }

        /// <summary>Проверка выполнялась и ошибок нет.</summary>
        public bool Ok => Validated && Errors.Count == 0;

        /// <summary>Ошибки проверки (пусто, если их нет или проверка не выполнялась).</summary>
        public IReadOnlyList<string> Errors { get; }

        /// <summary>Предупреждения проверки: не блокируют запись.</summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>Почему проверка не выполнялась; <c>null</c> — выполнялась.</summary>
        public string Reason { get; }

        /// <summary>Результат выполненной проверки.</summary>
        public static WorkflowXamlGateResult From(IEnumerable<string> errors, IEnumerable<string> warnings = null)
        {
            return new WorkflowXamlGateResult(
                true,
                errors?.Where(item => !string.IsNullOrWhiteSpace(item)).ToList() ?? Empty.ToList(),
                warnings?.Where(item => !string.IsNullOrWhiteSpace(item)).ToList() ?? Empty.ToList(),
                null);
        }

        /// <summary>Проверка не выполнена: гейт не подключён или упал.</summary>
        public static WorkflowXamlGateResult NotPerformed(string reason)
        {
            return new WorkflowXamlGateResult(
                false,
                Empty,
                Empty,
                string.IsNullOrWhiteSpace(reason) ? "гейт валидации XAML недоступен" : reason);
        }
    }

    /// <summary>
    /// Гейт валидации XAML для инструмента <c>editor</c> (карточка <c>AG-07</c>). Контракт живёт в чате,
    /// реализация — в студии: проверка «примет ли файл конструктор» уже написана в
    /// <c>WorkflowValidationGate</c>, второй такой проверки быть не должно.
    /// </summary>
    public interface IWorkflowXamlGate
    {
        /// <summary>Проверяет текст XAML, которого может ещё не быть на диске.</summary>
        WorkflowXamlGateResult ValidateText(string xaml);
    }

    /// <summary>
    /// Null-object: хост студии не подключил гейт. Инструмент отвечает «валидация не выполнена» и не
    /// выдаёт это за «ошибок нет».
    /// </summary>
    public sealed class NullWorkflowXamlGate : IWorkflowXamlGate
    {
        public static readonly NullWorkflowXamlGate Instance = new NullWorkflowXamlGate();

        private const string UnavailableMessage =
            "гейт валидации XAML не подключён хостом студии: проверка «примет ли файл конструктор» выполняется в студии; " +
            "перечитайте файл через read_files и вызовите workflow_validate";

        public WorkflowXamlGateResult ValidateText(string xaml)
        {
            return WorkflowXamlGateResult.NotPerformed(UnavailableMessage);
        }
    }
}
