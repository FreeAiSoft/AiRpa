using System;
using System.Collections.Generic;
using System.Activities.Presentation.Model;
using RPA.CSharpExpressionEditor;

namespace RPA.Core.Services
{
    /// <summary>
    /// Снимок окружения выражения: что видно из точки выражения (аргументы процесса и переменные
    /// в области видимости) плюс подключённые пространства имён.
    /// <para>
    /// Снимок нужен, чтобы компиляция Roslyn перестала зависеть от <c>ModelItem</c>: <c>ModelItem</c>
    /// живёт только на UI-потоке дизайнера, а компиляция — самая дорогая часть прохода. Раньше
    /// окружение собиралось внутри <c>RoslynExpressionValidator</c> из модели, поэтому проверку нельзя
    /// было унести в фон, и она блокировала диспетчер (дважды на литерал).
    /// </para>
    /// <para>Снимок неизменяем: после сборки на UI-потоке он безопасно читается в фоновой задаче.</para>
    /// </summary>
    internal sealed class ExpressionEnvironment
    {
        private readonly Dictionary<string, Type> _names;

        internal ExpressionEnvironment(IEnumerable<KeyValuePair<string, Type>> names, string usingNamespaces)
        {
            _names = new Dictionary<string, Type>(StringComparer.Ordinal);
            if (names != null)
            {
                foreach (KeyValuePair<string, Type> pair in names)
                {
                    if (!string.IsNullOrWhiteSpace(pair.Key) && pair.Value != null && !_names.ContainsKey(pair.Key))
                    {
                        _names.Add(pair.Key, pair.Value);
                    }
                }
            }

            UsingNamespaces = usingNamespaces ?? string.Empty;
        }

        internal static ExpressionEnvironment Empty { get; } =
            new ExpressionEnvironment(null, string.Empty);

        /// <summary>Имена, видимые из точки выражения (аргументы и переменные), с их типами.</summary>
        internal IReadOnlyDictionary<string, Type> Names => _names;

        /// <summary>Блок <c>using ...;</c> для компиляции (пустая строка — только типы по умолчанию).</summary>
        internal string UsingNamespaces { get; }

        /// <summary>Есть ли имя в окружении (проверка OutArgument и левой части <c>Assign</c>).</summary>
        internal bool Contains(string name)
        {
            return !string.IsNullOrEmpty(name) && _names.ContainsKey(name.Trim());
        }

        /// <summary>Тип имени из окружения.</summary>
        internal bool TryGetType(string name, out Type type)
        {
            type = null;
            return !string.IsNullOrEmpty(name) && _names.TryGetValue(name.Trim(), out type);
        }
    }

    /// <summary>
    /// Сборка <see cref="ExpressionEnvironment"/> из модели дизайнера. Вызывается <b>только</b> на
    /// UI-потоке: сразу после этого модель в фоновой задаче не читается.
    /// </summary>
    internal static class ModelExpressionEnvironment
    {
        /// <summary>
        /// Окружение активности. Ошибки чтения модели не пробрасываются: пустое окружение даёт
        /// замечание «имя не существует в текущем контексте», а не падение прохода.
        /// </summary>
        internal static ExpressionEnvironment FromModelItem(ModelItem ownerActivity, string usingNamespaces)
        {
            if (ownerActivity == null)
            {
                return new ExpressionEnvironment(null, usingNamespaces);
            }

            try
            {
                Dictionary<string, Type> names = CSharpExpressionHelper.GetEnvironment(ownerActivity);
                return new ExpressionEnvironment(names, usingNamespaces);
            }
            catch (Exception)
            {
                return new ExpressionEnvironment(null, usingNamespaces);
            }
        }
    }
}
