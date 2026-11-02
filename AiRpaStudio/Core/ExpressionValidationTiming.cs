using System;

namespace RPA.Core.Services
{
    /// <summary>
    /// Политика времени проверки C#-выражения в <c>RoslynExpressionEditor</c>.
    /// <para>
    /// Баг «ошибка выражения не показывается сразу»: единственным триггером проверки был фоновый цикл
    /// <c>BackgroundWorker</c> с паузой <c>ValidationWaitTime</c> = 10 000 мс и потеря фокуса — подписки
    /// на изменение текста не было, поэтому бейдж ошибки появлялся через десяток секунд после набора.
    /// </para>
    /// <para>
    /// Правило после исправления: проверка идёт по изменению текста, но через паузу-«дребезг»
    /// (<see cref="DebounceMilliseconds"/>), чтобы Roslyn не компилировал выражение на каждое нажатие.
    /// <see cref="PollIntervalMilliseconds"/> — резервный фоновый цикл, он остаётся для случаев, когда
    /// текст изменился не через редактор (подстановка переменной, отмена правки), и не должен быть
    /// длиннее нескольких секунд.
    /// </para>
    /// </summary>
    internal static class ExpressionValidationTiming
    {
        /// <summary>Нижняя граница паузы-дребезга: ниже неё Roslyn компилируется на каждое нажатие.</summary>
        public const int MinDebounceMilliseconds = 150;

        /// <summary>Верхняя граница паузы-дребезга: выше неё ошибка появляется «с задержкой».</summary>
        public const int MaxDebounceMilliseconds = 1000;

        /// <summary>Пауза после последнего изменения текста перед проверкой.</summary>
        public const int DebounceMilliseconds = 350;

        /// <summary>
        /// Период резервного фонового цикла проверки (было 10 000 мс — это и была задержка появления бейджа).
        /// </summary>
        public const int PollIntervalMilliseconds = 3000;

        /// <summary>
        /// Нужно ли проверять выражение. Неизменённый текст проверять не нужно: иначе Roslyn
        /// компилируется на каждый тик таймера. <c>null</c> и пустая строка — одно состояние поля.
        /// </summary>
        public static bool ShouldValidate(string previousText, string currentText)
        {
            return !string.Equals(previousText ?? string.Empty, currentText ?? string.Empty, StringComparison.Ordinal);
        }
    }
}
