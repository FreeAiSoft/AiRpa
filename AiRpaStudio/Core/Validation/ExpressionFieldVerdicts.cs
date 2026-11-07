using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace RPA.Core.Services
{
    /// <summary>
    /// Поле C#-выражения, которому можно показать вердикт документной проверки.
    /// <para>
    /// Интерфейс намеренно не знает о WPF: <see cref="ExpressionFieldVerdicts"/> — логика доставки
    /// результата, а как именно рисуется бейдж, решает поле (<c>RoslynExpressionEditor</c>).
    /// </para>
    /// </summary>
    internal interface IExpressionFieldBadge
    {
        /// <summary>Ключ поля — элемент модели выражения (<c>CSharpValue&lt;T&gt;</c>/<c>CSharpReference&lt;T&gt;</c>).</summary>
        object ExpressionKey { get; }

        /// <summary>
        /// Поле в правке: им управляет собственная проверка редактора (правила «по месту»), поэтому
        /// вердикт документной проверки в него не пишется — иначе бейдж мигал бы между двумя наборами правил.
        /// </summary>
        bool IsBeingEdited { get; }

        /// <summary>Показать вердикт: текст ошибки или <c>null</c> — ошибки нет.</summary>
        void ApplyVerdict(string message);
    }

    /// <summary>
    /// Вердикты проверки выражений по отдельным полям — то, что связывает фоновую проверку документа
    /// (<c>DesignerValidator</c>) с полями, которые видит пользователь.
    /// <para>
    /// Баг «ошибка валидации поля видна только после клика»: результат фоновой проверки доходил только до
    /// иконки активности (<c>Border x:Name="validationVisuals"</c>), а поле выражения узнавало об ошибке
    /// лишь от собственных триггеров — изменения текста и фокуса. Клик по полю и был этим фокусом.
    /// </para>
    /// <para>
    /// Почему доставка, а не повторная проверка в поле: фоновая проверка уже скомпилировала каждое
    /// выражение (и это стоит десятки миллисекунд на поле). Проверять поля ещё раз на UI-потоке значило бы
    /// скомпилировать весь документ второй раз и подвесить конструктор на большом роботе.
    /// </para>
    /// <para>
    /// Ключ — элемент модели выражения, а не адрес и не имя аргумента: при правке WF заменяет элемент
    /// выражения, поэтому устаревший вердикт просто перестаёт находиться по ключу.
    /// </para>
    /// </summary>
    internal static class ExpressionFieldVerdicts
    {
        private static readonly object Gate = new object();

        /// <summary>
        /// Вердикты по ключу выражения. Таблица слабых ключей: закрытый документ забирает свои записи с
        /// собой, отдельного «сброса документа» не требуется.
        /// </summary>
        private static readonly ConditionalWeakTable<object, Verdict> Verdicts =
            new ConditionalWeakTable<object, Verdict>();

        /// <summary>Зарегистрированные поля: слабые ссылки, чтобы реестр не держал визуальные деревья.</summary>
        private static readonly List<WeakReference> Fields = new List<WeakReference>();

        /// <summary>Сколько раз документная проверка публиковала вердикты (диагностика и тесты).</summary>
        internal static int PublishCount { get; private set; }

        /// <summary>Сколько полей зарегистрировано (диагностика и тесты).</summary>
        internal static int FieldCount
        {
            get
            {
                lock (Gate)
                {
                    Prune();
                    return Fields.Count;
                }
            }
        }

        /// <summary>Сообщает вердикт по выражению: текст ошибки или <c>null</c> — «ошибок нет».</summary>
        internal static void Set(object expressionKey, string message)
        {
            if (expressionKey == null)
            {
                return;
            }

            lock (Gate)
            {
                Verdicts.Remove(expressionKey);
                Verdicts.Add(expressionKey, new Verdict(message));
            }
        }

        /// <summary>
        /// Есть ли вердикт по этому выражению. <c>true</c> с пустым сообщением значит «проверено, ошибок
        /// нет» — это отличается от «проверки ещё не было», иначе исправленное поле оставляло бы бейдж.
        /// </summary>
        internal static bool TryGet(object expressionKey, out string message)
        {
            message = null;

            if (expressionKey == null)
            {
                return false;
            }

            lock (Gate)
            {
                if (!Verdicts.TryGetValue(expressionKey, out Verdict verdict))
                {
                    return false;
                }

                message = verdict.Message;
                return true;
            }
        }

        /// <summary>Забывает вердикт по выражению (правка текста делает его устаревшим).</summary>
        internal static void Forget(object expressionKey)
        {
            if (expressionKey == null)
            {
                return;
            }

            lock (Gate)
            {
                Verdicts.Remove(expressionKey);
            }
        }

        /// <summary>Регистрирует поле: дальше оно получает вердикты без собственной компиляции.</summary>
        internal static void Register(IExpressionFieldBadge field)
        {
            if (field == null)
            {
                return;
            }

            lock (Gate)
            {
                Prune();

                foreach (WeakReference reference in Fields)
                {
                    if (ReferenceEquals(reference.Target, field))
                    {
                        return;
                    }
                }

                Fields.Add(new WeakReference(field));
            }
        }

        /// <summary>Снимает поле с регистрации (поле закрыто/перестроено).</summary>
        internal static void Unregister(IExpressionFieldBadge field)
        {
            if (field == null)
            {
                return;
            }

            lock (Gate)
            {
                for (int index = Fields.Count - 1; index >= 0; index--)
                {
                    if (ReferenceEquals(Fields[index].Target, field) || !Fields[index].IsAlive)
                    {
                        Fields.RemoveAt(index);
                    }
                }
            }
        }

        /// <summary>
        /// Раздаёт текущие вердикты зарегистрированным полям. Вызывается на UI-потоке после того, как
        /// фоновая проверка документа закончила проход (там же, где ставятся иконки активностей).
        /// </summary>
        internal static void Publish()
        {
            List<IExpressionFieldBadge> fields;

            lock (Gate)
            {
                Prune();

                fields = new List<IExpressionFieldBadge>(Fields.Count);
                foreach (WeakReference reference in Fields)
                {
                    if (reference.Target is IExpressionFieldBadge field)
                    {
                        fields.Add(field);
                    }
                }

                PublishCount++;
            }

            foreach (IExpressionFieldBadge field in fields)
            {
                try
                {
                    if (field.IsBeingEdited)
                    {
                        continue;
                    }

                    if (TryGet(field.ExpressionKey, out string message))
                    {
                        field.ApplyVerdict(message);
                    }
                }
                catch (Exception ex)
                {
                    // Одно поле не должно обрывать раздачу вердиктов остальным.
                    System.Diagnostics.Debug.WriteLine("Вердикт поля не применён: " + ex);
                }
            }
        }

        /// <summary>Очистка для тестов: вердикты и регистрация полей.</summary>
        internal static void Reset()
        {
            lock (Gate)
            {
                Fields.Clear();
                PublishCount = 0;
            }
        }

        /// <summary>Убирает из реестра поля, которые уже собраны сборщиком мусора.</summary>
        private static void Prune()
        {
            for (int index = Fields.Count - 1; index >= 0; index--)
            {
                if (!Fields[index].IsAlive)
                {
                    Fields.RemoveAt(index);
                }
            }
        }

        /// <summary>Вердикт по выражению: <c>null</c> сообщение — проверено, ошибок нет.</summary>
        private sealed class Verdict
        {
            internal Verdict(string message)
            {
                Message = message;
            }

            internal string Message { get; }
        }
    }
}
