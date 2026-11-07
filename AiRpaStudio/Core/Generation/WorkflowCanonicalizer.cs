using System;
using System.Activities;
using System.Activities.Expressions;
using System.Activities.Presentation.Expressions;
using System.Activities.Presentation.ViewState;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using RPA.Helpers;

namespace RPA.Core.Generation
{
    /// <summary>
    /// Приводит объектную модель рабочего процесса к формату студии (этап 2 плана
    /// конвейер генерации XAML, п. 1.1).
    /// <para>
    /// Канонизация работает <b>по объектной модели</b>, а не по тексту XAML: всё, что ниже, —
    /// присвоенные свойства (<c>ActivityBuilder.Name</c>, вложенные свойства <c>TextExpression.*</c>,
    /// <c>sap2010</c>-вложенные свойства). Текст XAML после этого пишет сериализатор WF
    /// (<see cref="WorkflowXamlSerializer"/>), поэтому структура документа не может «разъехаться»
    /// с объектной моделью, как это бывает у строковых шаблонов.
    /// </para>
    /// <para>
    /// Что именно добавляется и почему:
    /// </para>
    /// <list type="bullet">
    /// <item><c>x:Class</c> (<see cref="ActivityBuilder.Name"/>) — без него студия не связывает файл
    /// с типом проекта и открывает «пустую» диаграмму;</item>
    /// <item><c>sap2010:ExpressionActivityEditor.ExpressionActivityEditor="C#"</c> — без него выражения
    /// не редактируются Roslyn-редактором студии и показываются текстом;</item>
    /// <item><c>TextExpression.NamespacesForImplementation</c> / <c>TextExpression.ReferencesForImplementation</c> —
    /// без них C#-выражения не компилируются, а ссылка на сборку проекта ещё и снимает модальное окно
    /// «Конструктор рабочих процессов»;</item>
    /// <item><c>sap2010:WorkflowViewState.IdRef</c> на корне и на каждой активности — состояние
    /// конструктора (по нему дизайнер связывает элементы диаграммы с моделью).</item>
    /// </list>
    /// </summary>
    internal static class WorkflowCanonicalizer
    {
        /// <summary>Суффикс идентификатора корня: в эталоне у <c>RPA_1.Main</c> IdRef равен <c>RPA_1.Main_1</c>.</summary>
        private const string RootIdRefSuffix = "_1";

        /// <summary>
        /// Собирает <see cref="ActivityBuilder"/> вокруг готовой реализации и канонизирует его.
        /// </summary>
        /// <param name="implementation">Корневая активность процесса (обычно <c>Sequence</c>).</param>
        /// <param name="options">Параметры канонизации.</param>
        /// <param name="hints">Необязательный список диагностических замечаний для ответа инструмента.</param>
        internal static ActivityBuilder Canonicalize(
            Activity implementation,
            WorkflowCanonicalizationOptions options,
            IList<string> hints = null)
        {
            if (implementation == null)
            {
                throw new ArgumentNullException(nameof(implementation));
            }

            return Canonicalize(new ActivityBuilder { Implementation = implementation }, options, hints);
        }

        /// <summary>
        /// Приводит готовый <see cref="ActivityBuilder"/> к формату студии. Метод идемпотентен:
        /// повторная канонизация даёт тот же результат (идентификаторы IdRef не «размножаются»).
        /// </summary>
        /// <param name="builder">Объектная модель процесса.</param>
        /// <param name="options">Параметры канонизации.</param>
        /// <param name="hints">Необязательный список диагностических замечаний для ответа инструмента.</param>
        /// <returns>Тот же экземпляр <paramref name="builder"/> (для цепочек вызовов).</returns>
        internal static ActivityBuilder Canonicalize(
            ActivityBuilder builder,
            WorkflowCanonicalizationOptions options,
            IList<string> hints = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            builder.Name = options.QualifiedClassName;

            ExpressionActivityEditor.SetExpressionActivityEditor(builder, options.ExpressionActivityEditor);

            ApplyNamespaces(builder, options);
            ApplyReferences(builder, options);
            ApplyIdRefs(builder, options, hints);

            return builder;
        }

        /// <summary>
        /// Пространства имён для C#-выражений: набор шаблона студии + дополнительные, без дубликатов.
        /// Коллекция — <see cref="Collection{T}"/>, потому что сериализатор пишет её как
        /// <c>sco:Collection</c> (так в эталоне), а не как <c>scg:List</c> с атрибутом <c>Capacity</c>.
        /// </summary>
        private static void ApplyNamespaces(ActivityBuilder builder, WorkflowCanonicalizationOptions options)
        {
            var namespaces = new Collection<string>();

            foreach (string name in WorkflowCanonicalizationOptions.DefaultNamespaces.Concat(options.Namespaces))
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                string trimmed = name.Trim();
                if (namespaces.Any(existing => string.Equals(existing, trimmed, StringComparison.Ordinal)))
                {
                    continue;
                }

                namespaces.Add(trimmed);
            }

            TextExpression.SetNamespacesForImplementation(builder, namespaces);
        }

        /// <summary>
        /// Ссылки на сборки для C#-выражений: набор шаблона студии + дополнительные, затем сборка проекта
        /// (последней, как в эталоне). Без сборки проекта типы проекта не резолвятся.
        /// </summary>
        private static void ApplyReferences(ActivityBuilder builder, WorkflowCanonicalizationOptions options)
        {
            var names = new List<string>();

            foreach (string name in WorkflowCanonicalizationOptions.DefaultReferences.Concat(options.References))
            {
                AddReferenceName(names, name);
            }

            AddReferenceName(names, options.ProjectAssemblyName);

            var references = new Collection<AssemblyReference>();
            foreach (string name in names)
            {
                references.Add(new AssemblyReference { AssemblyName = new System.Reflection.AssemblyName(name) });
            }

            TextExpression.SetReferencesForImplementation(builder, references);
        }

        private static void AddReferenceName(ICollection<string> names, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            string trimmed = name.Trim();
            string simpleName = SimpleAssemblyName(trimmed);

            if (names.Any(existing => string.Equals(SimpleAssemblyName(existing), simpleName, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            try
            {
                // Проверка до сериализации: некорректное имя всплыло бы исключением внутри
                // WF-сериализатора без указания, какая именно ссылка виновата.
                new System.Reflection.AssemblyName(trimmed);
            }
            catch (Exception ex)
            {
                throw new ArgumentException(
                    $"Ссылка «{trimmed}» не является именем сборки", nameof(name), ex);
            }

            names.Add(trimmed);
        }

        private static string SimpleAssemblyName(string assemblyName)
        {
            if (string.IsNullOrEmpty(assemblyName))
            {
                return string.Empty;
            }

            int comma = assemblyName.IndexOf(',');
            return (comma < 0 ? assemblyName : assemblyName.Substring(0, comma)).Trim();
        }

        /// <summary>
        /// Назначает <c>sap2010:WorkflowViewState.IdRef</c> корню и каждой активности: у корня —
        /// «Класс_1» (как в эталоне), у активностей — «Тип_N» с независимым счётчиком на тип.
        /// <para>
        /// Имена детерминированы, поэтому повторная генерация того же процесса даёт те же IdRef,
        /// а не «плавающий» diff. Если дерево не обходится (невалидные обязательные аргументы),
        /// канонизация не падает: IdRef остаётся только у корня, а причина попадает в подсказки.
        /// </para>
        /// </summary>
        private static void ApplyIdRefs(
            ActivityBuilder builder,
            WorkflowCanonicalizationOptions options,
            IList<string> hints)
        {
            WorkflowViewState.SetIdRef(builder, options.QualifiedClassName + RootIdRefSuffix);

            if (builder.Implementation == null)
            {
                hints?.Add(
                    "у процесса нет Implementation: IdRef назначен только корню, диаграмма в конструкторе будет пустой");
                return;
            }

            var counters = new Dictionary<string, int>(StringComparer.Ordinal);
            var visited = new HashSet<Activity>();

            SetIdRef(builder.Implementation, counters, visited);

            try
            {
                foreach (Activity activity in WorkflowInspectionServices.GetActivities(builder.Implementation))
                {
                    SetIdRef(activity, counters, visited);
                }
            }
            catch (Exception ex)
            {
                // GetActivities кэширует метаданные и падает на заведомо невалидном дереве
                // (например Assign без To/Value). Это не повод ронять генерацию: XAML без части
                // IdRef открывается, конструктор проставит их при первом сохранении.
                hints?.Add(
                    "IdRef назначен не всем активностям: дерево не обойдено — " + ErrorText.Of(ex));
            }
        }

        private static void SetIdRef(Activity activity, IDictionary<string, int> counters, ISet<Activity> visited)
        {
            if (activity == null || !visited.Add(activity))
            {
                return;
            }

            string typeName = IdRefPrefix(activity.GetType());
            int index;
            counters[typeName] = counters.TryGetValue(typeName, out index) ? index + 1 : 1;

            WorkflowViewState.SetIdRef(activity, typeName + "_" + counters[typeName]);
        }

        /// <summary>Имя типа без арности: <c>ForEach`1</c> → <c>ForEach</c> (в XAML элемент называется так же).</summary>
        private static string IdRefPrefix(Type activityType)
        {
            string name = activityType.Name;
            int arity = name.IndexOf('`');

            return arity > 0 ? name.Substring(0, arity) : name;
        }
    }
}
