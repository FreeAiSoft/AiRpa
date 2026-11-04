using System.Collections.Generic;
using System.Activities.Presentation.Validation;

namespace AiRpaStudio.Tests.Design;

/// <summary>
/// Сервис ошибок валидации для стенда: вместо <c>DebugValidationErrorService</c> (модальное окно)
/// ошибки записываются и попадают в отчёт.
/// <para>
/// Это не «заглушка от окна», а единственный способ увидеть ошибки, которые дизайнер нашёл сам:
/// <c>ValidationService.ValidateWorkflow()</c> отдаёт результат именно через
/// <see cref="IValidationErrorService.ShowValidationErrors"/>.
/// </para>
/// </summary>
internal sealed class RecordingValidationErrorService : IValidationErrorService
{
    private readonly object _sync = new object();
    private readonly List<ValidationErrorInfo> _errors = new List<ValidationErrorInfo>();

    private int _requestCount;

    /// <summary>Сколько раз WF передавал ошибки в сервис (признак, что валидация действительно шла).</summary>
    internal int RequestCount
    {
        get
        {
            lock (_sync)
            {
                return _requestCount;
            }
        }
    }

    /// <summary>Все ошибки, переданные дизайнером, в порядке поступления.</summary>
    internal IReadOnlyList<ValidationErrorInfo> Errors
    {
        get
        {
            lock (_sync)
            {
                return _errors.ToArray();
            }
        }
    }

    public void ShowValidationErrors(IList<ValidationErrorInfo> errors)
    {
        lock (_sync)
        {
            _requestCount++;

            if (errors != null)
            {
                _errors.AddRange(errors);
            }
        }
    }

    /// <summary>Ошибки в виде строк отчёта: «свойство: текст [id]».</summary>
    internal IReadOnlyList<string> Describe()
    {
        var result = new List<string>();
        foreach (ValidationErrorInfo error in Errors)
        {
            if (error == null)
            {
                continue;
            }

            string property = string.IsNullOrWhiteSpace(error.PropertyName) ? string.Empty : error.PropertyName + ": ";
            string id = string.IsNullOrWhiteSpace(error.Id) ? string.Empty : $" [{error.Id}]";
            string warning = error.IsWarning ? " (warning)" : string.Empty;

            result.Add($"{property}{error.Message}{id}{warning}");
        }

        return result;
    }
}
