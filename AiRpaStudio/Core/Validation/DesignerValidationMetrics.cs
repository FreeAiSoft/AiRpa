using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RPA.Core.Services
{
    /// <summary>
    /// Замеры одного прохода валидации дизайнера: сколько времени ушло на каждую фазу и что получилось.
    /// <para>
    /// Нужны для шага 0 плана <c>dev-docs\UI02_Background_Validation_Plan.md</c>: без цифр «до/после» нельзя
    /// ни доказать, что фон разгрузил UI-поток, ни узнать, не стало ли узким местом то, что осталось на
    /// UI (<c>ValidationService.ValidateWorkflow()</c>).
    /// </para>
    /// <para>
    /// Разделение фаз повторяет прежний и текущий проход:
    /// </para>
    /// <list type="bullet">
    /// <item><see cref="ModelWalkMs"/>, <see cref="SnapshotMs"/>, <see cref="ValidateWorkflowMs"/> и
    /// <see cref="ApplyMs"/> — UI-поток и сейчас, и раньше;</item>
    /// <item><see cref="ExpressionsMs"/> — <b>раньше тоже UI-поток</b> (компиляция Roslyn внутри
    /// <c>GetArgumentErrors</c>, на литерал дважды), сейчас — фоновая задача. Поэтому
    /// <see cref="OldUiMs"/> = <see cref="UiMs"/> + <see cref="ExpressionsMs"/> — оценка «как было»
    /// <b>снизу</b>: двойную компиляцию литералов она не учитывает.</item>
    /// </list>
    /// </summary>
    internal sealed class DesignerValidationMetrics
    {
        /// <summary>Поколение прохода (см. <see cref="DesignerValidationPass"/>).</summary>
        internal int Generation { get; set; }

        /// <summary>Сколько активностей попало в область валидации (включая тела и ветки).</summary>
        internal int ActivityCount { get; set; }

        /// <summary>Сколько проверок выражений снято с модели.</summary>
        internal int CheckCount { get; set; }

        /// <summary>Сколько замечаний нашла фоновая фаза.</summary>
        internal int ErrorCount { get; set; }

        /// <summary>
        /// Адреса активностей, по которым есть замечания студии. Именно они — доказательство, что
        /// проверка дошла до тела <c>ForEach</c>, ветки <c>Pick</c> и <c>Then</c>, а не остановилась на
        /// первом контейнере.
        /// </summary>
        internal IReadOnlyList<string> ErrorAddresses => _errorAddresses;

        /// <summary>Замечание с адресом — в отчёт (порядок сохраняется).</summary>
        internal void AddErrorAddress(string address)
        {
            if (!string.IsNullOrEmpty(address) && !_errorAddresses.Contains(address))
            {
                _errorAddresses.Add(address);
            }
        }

        /// <summary>Обход модели с адресами (UI-поток).</summary>
        internal double ModelWalkMs { get; set; }

        /// <summary>Снимок окружения и проверок (UI-поток).</summary>
        internal double SnapshotMs { get; set; }

        /// <summary><c>ValidationService.ValidateWorkflow()</c> (UI-поток, остаётся там намеренно).</summary>
        internal double ValidateWorkflowMs { get; set; }

        /// <summary>Компиляция и сравнение типов (фон; раньше — UI-поток).</summary>
        internal double ExpressionsMs { get; set; }

        /// <summary>Применение замечаний к иконкам по адресам (UI-поток).</summary>
        internal double ApplyMs { get; set; }

        /// <summary>Время UI-потока на проход сейчас.</summary>
        private readonly List<string> _errorAddresses = new List<string>();

        internal double UiMs => ModelWalkMs + SnapshotMs + ValidateWorkflowMs + ApplyMs;

        /// <summary>Время UI-потока на проход до переноса компиляции в фон (оценка снизу).</summary>
        internal double OldUiMs => UiMs + ExpressionsMs;

        /// <summary>
        /// Строка отчёта: цифры по фазам. Одна строка — чтобы отчёт было видно и в логе отладки,
        /// и в выводе теста, и в приёмочном прогоне стенда.
        /// </summary>
        internal string Describe()
        {
            var text = new StringBuilder();
            text.AppendFormat(
                CultureInfo.InvariantCulture,
                "проход #{0}: активностей {1}, проверок {2}, замечаний {3}; " +
                "UI: обход модели {4:0.0} мс, снимок {5:0.0} мс, ValidateWorkflow {6:0.0} мс, иконки {7:0.0} мс " +
                "= {8:0.0} мс (до переноса в фон было бы {9:0.0} мс); фон: выражения {10:0.0} мс",
                Generation,
                ActivityCount,
                CheckCount,
                ErrorCount,
                ModelWalkMs,
                SnapshotMs,
                ValidateWorkflowMs,
                ApplyMs,
                UiMs,
                OldUiMs,
                ExpressionsMs);

            return text.ToString();
        }
    }
}
