using System;

namespace RPA.Core.Services
{
    /// <summary>Вид проверки выражения у аргумента активности.</summary>
    internal enum ExpressionCheckKind
    {
        /// <summary>Выражение аргумента: компилируется Roslyn, тип сравнивается с типом аргумента.</summary>
        Expression = 0,

        /// <summary>Литерал: компилируется в кавычках, тип сравнивается с типом аргумента дважды (см. <c>DesignerValidator</c>).</summary>
        Literal = 1,

        /// <summary><c>OutArgument</c>: проверяется только существование имени в области видимости.</summary>
        OutArgument = 2,

        /// <summary>Правая часть <c>Assign</c>: тип сравнивается с типом левой части.</summary>
        AssignValue = 3
    }

    /// <summary>
    /// Одна проверка выражения, снятая с модели дизайнера на UI-потоке.
    /// <para>
    /// Это <b>снимок данных</b>, а не ссылка на модель: после сборки проверки компилируются в фоновой
    /// задаче, где <c>ModelItem</c> трогать нельзя. Все поля — значения и <see cref="Type"/>,
    /// поэтому объект безопасно передавать между потоками.
    /// </para>
    /// </summary>
    internal sealed class ActivityExpressionCheck
    {
        /// <summary>Адрес активности в дереве модели (<see cref="DesignerValidationScope"/>).</summary>
        internal string Address { get; set; } = string.Empty;

        /// <summary>Имя типа активности — только для сообщений.</summary>
        internal string ActivityTypeName { get; set; } = string.Empty;

        /// <summary>Имя аргумента: с него начинается каждое замечание.</summary>
        internal string ArgumentName { get; set; } = string.Empty;

        /// <summary>Вид проверки.</summary>
        internal ExpressionCheckKind Kind { get; set; }

        /// <summary>Исходный текст аргумента (как введён в дизайнере).</summary>
        internal string ExpressionText { get; set; } = string.Empty;

        /// <summary>Текст, который уходит в компилятор (у литерала — в кавычках).</summary>
        internal string CompileText { get; set; } = string.Empty;

        /// <summary>Тип аргумента (generic-параметр <c>InArgument&lt;T&gt;</c>/<c>OutArgument&lt;T&gt;</c>).</summary>
        internal Type ArgumentType { get; set; }

        /// <summary>Тип элемента модели выражения (<c>CSharpValue&lt;T&gt;</c>, <c>Literal&lt;T&gt;</c>).</summary>
        internal Type ExpressionItemType { get; set; }

        /// <summary>Тип литерала (только для <see cref="ExpressionCheckKind.Literal"/>).</summary>
        internal Type LiteralType { get; set; }

        /// <summary>Тип левой части <c>Assign</c> (только для <see cref="ExpressionCheckKind.AssignValue"/>).</summary>
        internal Type TargetType { get; set; }

        /// <summary>Снимок окружения выражения (аргументы и переменные в области видимости).</summary>
        internal ExpressionEnvironment Environment { get; set; } = ExpressionEnvironment.Empty;
    }

    /// <summary>
    /// Замечание валидации, привязанное к адресу активности.
    /// <para>
    /// Адрес — то, что позволяет ставить иконку на <b>нужную</b> активность, включая активности внутри
    /// тела <c>ForEach</c>, ветки <c>Pick</c> и кастомных контейнеров: результат приходит из фоновой
    /// задачи, где модели уже нет, и сопоставляется с моделью по адресу.
    /// </para>
    /// </summary>
    internal sealed class ActivityValidationError
    {
        internal ActivityValidationError(string address, string message)
        {
            Address = address ?? string.Empty;
            Message = message ?? string.Empty;
        }

        /// <summary>Адрес активности.</summary>
        internal string Address { get; }

        /// <summary>Готовая строка замечания: «аргумент: текст».</summary>
        internal string Message { get; }
    }
}
