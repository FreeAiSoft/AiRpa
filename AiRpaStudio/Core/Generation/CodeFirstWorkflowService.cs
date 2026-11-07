using System;
using System.Activities;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using RPA.Helpers;

namespace RPA.Core.Generation
{
    /// <summary>Запрос инструмента Code-First: код процесса, файл и параметры класса.</summary>
    internal sealed class CodeFirstWorkflowRequest
    {
        /// <summary>Код агента на C# (см. <see cref="RoslynSandbox.CodeFirstContractHint"/>).</summary>
        internal string Code { get; set; }

        /// <summary>Путь к файлу процесса. Пустой — только собрать XAML, ничего не записывая.</summary>
        internal string FilePath { get; set; }

        /// <summary>Имя проекта (сборки) для <c>TextExpression.ReferencesForImplementation</c>.</summary>
        internal string ProjectName { get; set; }

        internal string NamespaceName { get; set; }

        internal string ClassName { get; set; }
    }

    /// <summary>
    /// Ответ инструмента Code-First: единый формат
    /// <c>{ok, workflowId, filePath, entryPoint, diagnostics[], errors[], warnings[], hints[]}</c>.
    /// <para>
    /// <see cref="Diagnostics"/> — машиночитаемая диагностика компиляции (код, строка, столбец, подсказка):
    /// по ней агент правит код сам, без «угадай, что не так».
    /// </para>
    /// </summary>
    internal sealed class CodeFirstResult
    {
        internal bool Ok { get; set; }

        internal string WorkflowId { get; set; }

        internal string FilePath { get; set; }

        internal string Xaml { get; set; }

        /// <summary>Вход процесса, найденный в коде (например <c>Workflow.Build</c>).</summary>
        internal string EntryPoint { get; set; }

        /// <summary>Сколько активностей в собранном дереве.</summary>
        internal int ActivityCount { get; set; }

        /// <summary>Вкладка конструктора открыта.</summary>
        internal bool Opened { get; set; }

        /// <summary>Вкладка конструктора активна.</summary>
        internal bool Activated { get; set; }

        internal List<string> Errors { get; } = new List<string>();

        internal List<string> Warnings { get; } = new List<string>();

        internal List<string> Hints { get; } = new List<string>();

        /// <summary>Диагностика компиляции: код, строка, столбец, текст, подсказка.</summary>
        internal List<RoslynDiagnostic> Diagnostics { get; } = new List<RoslynDiagnostic>();

        /// <summary>Пространства имён, попавшие в XAML из кода.</summary>
        internal List<string> Namespaces { get; } = new List<string>();

        /// <summary>Сборки, попавшие в XAML из кода.</summary>
        internal List<string> References { get; } = new List<string>();

        /// <summary>Текст отчёта для сообщения об ошибке теста или для лога.</summary>
        internal string Describe()
        {
            var text = new StringBuilder();
            text.AppendLine(Ok ? "code-first: ок" : "code-first: ОШИБКИ");
            text.AppendLine($"workflowId: {WorkflowId ?? "(нет)"}");
            text.AppendLine($"файл: {FilePath ?? "(не сохранён)"}");
            text.AppendLine($"вход: {EntryPoint ?? "(нет)"}, активностей: {ActivityCount}");
            text.AppendLine($"ошибок: {Errors.Count}, предупреждений: {Warnings.Count}");

            AppendBlock(text, "ошибки", Errors);
            AppendBlock(text, "предупреждения", Warnings);
            AppendBlock(text, "подсказки", Hints);

            return text.ToString();
        }

        private static void AppendBlock(StringBuilder text, string title, IList<string> items)
        {
            if (items == null || items.Count == 0)
            {
                return;
            }

            text.AppendLine(title + ":");
            foreach (string item in items)
            {
                text.AppendLine("  - " + item);
            }
        }
    }

    /// <summary>Инструменты Code-First через сервис: так их можно проверять без компиляции и конструктора.</summary>
    internal interface ICodeFirstWorkflowService
    {
        /// <summary>Собирает (и при заданном пути сохраняет) процесс из кода агента.</summary>
        CodeFirstResult Create(CodeFirstWorkflowRequest request);
    }

    /// <summary>Результат трансляции «сборка кода → активность WF».</summary>
    internal sealed class CodeFirstActivity
    {
        private CodeFirstActivity()
        {
        }

        internal bool Ok { get; private set; }

        internal Activity Activity { get; private set; }

        /// <summary>Имя входа: <c>Тип.Метод</c> или имя класса активности.</summary>
        internal string EntryPoint { get; private set; }

        internal List<string> Errors { get; } = new List<string>();

        internal List<string> Hints { get; } = new List<string>();

        internal static CodeFirstActivity Success(Activity activity, string entryPoint)
        {
            return new CodeFirstActivity
            {
                Ok = true,
                Activity = activity,
                EntryPoint = entryPoint
            };
        }

        internal static CodeFirstActivity Failure(IEnumerable<string> errors, IEnumerable<string> hints = null)
        {
            var result = new CodeFirstActivity { Ok = false };

            if (errors != null)
            {
                result.Errors.AddRange(errors.Where(error => !string.IsNullOrWhiteSpace(error)));
            }

            if (hints != null)
            {
                result.Hints.AddRange(hints.Where(hint => !string.IsNullOrWhiteSpace(hint)));
            }

            return result;
        }

        internal string Describe()
        {
            var text = new StringBuilder();
            text.AppendLine(Ok ? "трансляция: ок" : "трансляция: ОШИБКИ");
            text.AppendLine($"вход: {EntryPoint ?? "(нет)"}");

            foreach (string error in Errors)
            {
                text.AppendLine("  - " + error);
            }

            return text.ToString();
        }
    }

    /// <summary>
    /// Трансляция «сборка кода → <c>Activity</c>» (этап 5 плана
    /// конвейер генерации XAML).
    /// <para>
    /// Вход процесса ищется по контракту, а не по договорённости «как-нибудь»:
    /// </para>
    /// <list type="number">
    /// <item>public static метод без параметров, возвращающий <c>Activity</c> или <c>ActivityBuilder</c>
    /// (предпочтительные имена — <c>Build</c>, <c>Create</c>, <c>CreateWorkflow</c>, <c>GetWorkflow</c>);</item>
    /// <item>public-класс без параметров конструктора, наследующий <c>System.Activities.Activity</c>
    /// (предпочтительные имена — <c>Workflow</c>, <c>Main</c>).</item>
    /// </list>
    /// <para>
    /// Дальше дерево проверяется на сериализуемость: активность, объявленная в самом коде, в XAML попасть
    /// не может — её типа нет в проекте студии, и конструктор покажет ошибку загрузки. Поэтому у корневого
    /// класса берётся <c>Implementation</c>, а такие активности в дереве считаются ошибкой.
    /// </para>
    /// </summary>
    internal static class CodeFirstWorkflowTranslator
    {
        private static readonly string[] PreferredMethodNames =
        {
            "Build",
            "Create",
            "CreateWorkflow",
            "GetWorkflow",
            "GetActivity"
        };

        private static readonly string[] PreferredTypeNames = { "Workflow", "Main" };

        /// <summary>Ищет вход процесса и возвращает его дерево активностей.</summary>
        internal static CodeFirstActivity Translate(Assembly assembly)
        {
            if (assembly == null)
            {
                return CodeFirstActivity.Failure(
                    new[] { "code.entry: сборка кода не загружена" },
                    new[] { RoslynSandbox.CodeFirstContractHint });
            }

            Type[] types = ReadTypes(assembly, out string readError);
            if (types == null)
            {
                return CodeFirstActivity.Failure(
                    new[] { readError },
                    new[] { RoslynSandbox.CodeFirstContractHint });
            }

            MethodInfo factory = FindFactoryMethod(types);
            if (factory != null)
            {
                return InvokeFactory(factory, assembly);
            }

            Type activityType = FindActivityType(types);
            if (activityType != null)
            {
                return CreateInstance(activityType, assembly);
            }

            return CodeFirstActivity.Failure(
                new[] { "code.entry: в коде не найден вход процесса — ни статического метода, возвращающего Activity, ни класса-активности" },
                new[] { RoslynSandbox.CodeFirstContractHint });
        }

        /// <summary>Сколько активностей в дереве (для ответа инструмента и для проверок агента).</summary>
        internal static int CountActivities(Activity root)
        {
            if (root == null)
            {
                return 0;
            }

            int count = 1;

            try
            {
                count += WorkflowInspectionServices.GetActivities(root).Count();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Дерево активностей не обойдено: " + ErrorText.Of(ex));
            }

            return count;
        }

        private static Type[] ReadTypes(Assembly assembly, out string error)
        {
            error = null;

            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                error = null;
                return (ex.Types ?? Array.Empty<Type>()).Where(type => type != null).ToArray();
            }
            catch (Exception ex)
            {
                error = ErrorText.Describe("типы кода не прочитаны", ex);
                return null;
            }
        }

        private static MethodInfo FindFactoryMethod(IEnumerable<Type> types)
        {
            var candidates = new List<MethodInfo>();

            foreach (Type type in types.Where(type => type != null && type.IsPublic))
            {
                MethodInfo[] methods;

                try
                {
                    methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Методы типа «{type.Name}» не прочитаны: " + ErrorText.Of(ex));
                    continue;
                }

                foreach (MethodInfo method in methods)
                {
                    if (method.GetParameters().Length != 0 || method.IsGenericMethodDefinition)
                    {
                        continue;
                    }

                    if (typeof(Activity).IsAssignableFrom(method.ReturnType)
                        || typeof(ActivityBuilder).IsAssignableFrom(method.ReturnType))
                    {
                        candidates.Add(method);
                    }
                }
            }

            if (candidates.Count == 0)
            {
                return null;
            }

            foreach (string name in PreferredMethodNames)
            {
                MethodInfo preferred = candidates
                    .OrderBy(method => method.DeclaringType?.Name, StringComparer.Ordinal)
                    .FirstOrDefault(method => string.Equals(method.Name, name, StringComparison.Ordinal));

                if (preferred != null)
                {
                    return preferred;
                }
            }

            return candidates
                .OrderBy(method => method.DeclaringType?.Name, StringComparer.Ordinal)
                .ThenBy(method => method.Name, StringComparer.Ordinal)
                .First();
        }

        private static Type FindActivityType(IEnumerable<Type> types)
        {
            var candidates = types
                .Where(type => type != null
                               && type.IsPublic
                               && !type.IsAbstract
                               && typeof(Activity).IsAssignableFrom(type)
                               && type.GetConstructor(Type.EmptyTypes) != null)
                .ToList();

            if (candidates.Count == 0)
            {
                return null;
            }

            foreach (string name in PreferredTypeNames)
            {
                Type preferred = candidates
                    .OrderBy(type => type.Name, StringComparer.Ordinal)
                    .FirstOrDefault(type => string.Equals(type.Name, name, StringComparison.Ordinal));

                if (preferred != null)
                {
                    return preferred;
                }
            }

            return candidates.OrderBy(type => type.Name, StringComparer.Ordinal).First();
        }

        private static CodeFirstActivity InvokeFactory(MethodInfo method, Assembly sandboxAssembly)
        {
            string entryPoint = EntryPointName(method.DeclaringType?.Name, method.Name);

            object value;

            try
            {
                value = method.Invoke(null, null);
            }
            catch (TargetInvocationException ex)
            {
                return CodeFirstActivity.Failure(
                    new[] { ErrorText.Describe($"вход процесса «{entryPoint}» упал", ex.InnerException ?? ex) },
                    new[] { RoslynSandbox.CodeFirstContractHint });
            }
            catch (Exception ex)
            {
                return CodeFirstActivity.Failure(
                    new[] { ErrorText.Describe($"вход процесса «{entryPoint}» не вызван", ex) },
                    new[] { RoslynSandbox.CodeFirstContractHint });
            }

            return FromResult(value, entryPoint, sandboxAssembly);
        }

        private static CodeFirstActivity CreateInstance(Type activityType, Assembly sandboxAssembly)
        {
            string entryPoint = EntryPointName(activityType.Name);

            object value;

            try
            {
                value = Activator.CreateInstance(activityType);
            }
            catch (TargetInvocationException ex)
            {
                return CodeFirstActivity.Failure(
                    new[] { ErrorText.Describe($"класс активности «{entryPoint}» упал при создании", ex.InnerException ?? ex) },
                    new[] { RoslynSandbox.CodeFirstContractHint });
            }
            catch (Exception ex)
            {
                return CodeFirstActivity.Failure(
                    new[] { ErrorText.Describe($"класс активности «{entryPoint}» не создан", ex) },
                    new[] { RoslynSandbox.CodeFirstContractHint });
            }

            return FromResult(value, entryPoint, sandboxAssembly);
        }

        private static CodeFirstActivity FromResult(object value, string entryPoint, Assembly sandboxAssembly)
        {
            if (value == null)
            {
                return CodeFirstActivity.Failure(
                    new[] { $"code.entry: «{entryPoint}» вернул null вместо Activity" },
                    new[] { RoslynSandbox.CodeFirstContractHint });
            }

            if (value is ActivityBuilder builder)
            {
                if (builder.Implementation == null)
                {
                    return CodeFirstActivity.Failure(
                        new[] { $"code.entry: «{entryPoint}» вернул ActivityBuilder без Implementation" },
                        new[] { RoslynSandbox.CodeFirstContractHint });
                }

                return Normalize(builder.Implementation, entryPoint, sandboxAssembly);
            }

            if (value is Activity activity)
            {
                return Normalize(activity, entryPoint, sandboxAssembly);
            }

            return CodeFirstActivity.Failure(
                new[]
                {
                    $"code.entry: «{entryPoint}» вернул {value.GetType().FullName} — ожидалась Activity или ActivityBuilder"
                },
                new[] { RoslynSandbox.CodeFirstContractHint });
        }

        /// <summary>
        /// Приводит полученный объект к дереву встроенных активностей: у класса из кода берётся
        /// <c>Implementation</c> — иначе в XAML попал бы тип из сгенерированной сборки.
        /// </summary>
        private static CodeFirstActivity Normalize(Activity activity, string entryPoint, Assembly sandboxAssembly)
        {
            if (activity.GetType().Assembly == sandboxAssembly)
            {
                Func<Activity> implementation = ReadImplementation(activity);

                if (implementation == null)
                {
                    return CodeFirstActivity.Failure(
                        new[]
                        {
                            $"code.serialization: активности «{activity.GetType().FullName}» из кода нечего сериализовать: " +
                            "задайте Implementation = () => new Sequence { ... }"
                        },
                        new[] { RoslynSandbox.CodeFirstContractHint });
                }

                Activity tree;

                try
                {
                    tree = implementation();
                }
                catch (Exception ex)
                {
                    return CodeFirstActivity.Failure(
                        new[] { ErrorText.Describe($"Implementation «{entryPoint}» упал", ex) },
                        new[] { RoslynSandbox.CodeFirstContractHint });
                }

                if (tree == null)
                {
                    return CodeFirstActivity.Failure(
                        new[] { $"code.serialization: Implementation «{entryPoint}» вернул null" },
                        new[] { RoslynSandbox.CodeFirstContractHint });
                }

                return EnsureBuiltIn(tree, entryPoint, sandboxAssembly);
            }

            return EnsureBuiltIn(activity, entryPoint, sandboxAssembly);
        }

        private static CodeFirstActivity EnsureBuiltIn(Activity root, string entryPoint, Assembly sandboxAssembly)
        {
            foreach (Activity activity in Enumerate(root))
            {
                if (activity.GetType().Assembly != sandboxAssembly)
                {
                    continue;
                }

                return CodeFirstActivity.Failure(
                    new[]
                    {
                        $"code.serialization: активность «{activity.GetType().FullName}» объявлена в коде агента и не " +
                        "сериализуется: в XAML попадёт тип из сгенерированной сборки, и конструктор её не загрузит"
                    },
                    new[]
                    {
                        "используйте встроенные активности WF (Sequence, If, ForEach, While, TryCatch, Assign, WriteLine) " +
                        "и выражения CSharpValue<T>/CSharpReference<T>"
                    });
            }

            return CodeFirstActivity.Success(root, entryPoint);
        }

        /// <summary>
        /// <c>Activity.Implementation</c> — защищённое свойство (его задаёт сам класс активности),
        /// поэтому читается рефлексией: иначе класс-активность из кода нельзя превратить в дерево,
        /// которое сериализуется.
        /// </summary>
        private static readonly PropertyInfo ImplementationProperty = typeof(Activity).GetProperty(
            "Implementation",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        private static Func<Activity> ReadImplementation(Activity activity)
        {
            if (activity == null || ImplementationProperty == null)
            {
                return null;
            }

            try
            {
                return ImplementationProperty.GetValue(activity) as Func<Activity>;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Implementation активности не прочитан: " + ErrorText.Of(ex));
                return null;
            }
        }

        private static IEnumerable<Activity> Enumerate(Activity root)
        {
            var visited = new HashSet<Activity> { root };
            var activities = new List<Activity> { root };

            try
            {
                foreach (Activity activity in WorkflowInspectionServices.GetActivities(root))
                {
                    if (activity != null && visited.Add(activity))
                    {
                        activities.Add(activity);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Дерево активностей не обойдено: " + ErrorText.Of(ex));
            }

            return activities;
        }

        private static string EntryPointName(string typeName, string methodName = null)
        {
            string type = string.IsNullOrWhiteSpace(typeName) ? "(код)" : typeName;

            return string.IsNullOrWhiteSpace(methodName) ? type : type + "." + methodName;
        }
    }

    /// <summary>
    /// Сервис Code-First (этап 5 конвейера генерации XAML):
    /// <code>
    /// код агента → RoslynSandbox → сборка → CodeFirstWorkflowTranslator (Activity)
    ///   → WorkflowBuildService (канонизация, сериализатор WF, временный файл, гейт)
    ///   → DesignerOpenService (вкладка конструктора)
    /// </code>
    /// <para>
    /// Конвейер тот же, что у палитры (этап 4): расхождение путей сохранения даёт «работает в одном
    /// инструменте, ломается в другом». NamespacesForImplementation и ссылки на сборки строятся
    /// из <c>using</c> кода и реально использованных сборок — иначе конструктор покажет
    /// нерезолвленные выражения.
    /// </para>
    /// </summary>
    internal sealed class CodeFirstWorkflowService : ICodeFirstWorkflowService
    {
        // Подсказка «откуда имя проекта» — общая для всех инструментов генерации (см. GenerationTarget).
        private const string ProjectHint = GenerationTarget.ProjectHint;

        private readonly RoslynSandbox _sandbox;
        private readonly IWorkflowValidationGate _gate;
        private readonly DesignerOpenService _openService;
        private readonly Action<string> _log;

        internal CodeFirstWorkflowService(
            RoslynSandbox sandbox = null,
            IWorkflowDesignerHost host = null,
            IDesignerUiThread uiThread = null,
            IWorkflowValidationGate gate = null,
            Action<string> log = null)
        {
            _sandbox = sandbox ?? RoslynSandbox.Default;
            _gate = gate;
            _log = log;

            if (host != null && uiThread != null)
            {
                _openService = new DesignerOpenService(host, uiThread, log);
            }
        }

        /// <summary>Сервис для запущенной студии: реальная среда конструктора, UI-поток и гейт по умолчанию.</summary>
        internal static CodeFirstWorkflowService ForStudio(Action<string> log = null)
        {
            return new CodeFirstWorkflowService(
                sandbox: null,
                host: new StudioWorkflowDesignerHost(),
                uiThread: DispatcherDesignerUiThread.ForStudio(),
                gate: null,
                log: log);
        }

        /// <summary>
        /// Собирает процесс из кода. Путь не задан — только сборка и гейт (без записи на диск);
        /// путь задан — атомарное сохранение и открытие вкладки конструктора.
        /// </summary>
        public CodeFirstResult Create(CodeFirstWorkflowRequest request)
        {
            if (request == null)
            {
                return Failure("xaml.request: запрос не задан");
            }

            string pathError = GenerationTarget.ValidateFilePath(request.FilePath, out string fullPath);
            if (pathError != null)
            {
                return Failure(pathError);
            }

            string optionsError = GenerationTarget.BuildOptions(
                request.ProjectName,
                request.NamespaceName,
                request.ClassName,
                fullPath,
                out WorkflowCanonicalizationOptions options);
            if (optionsError != null)
            {
                CodeFirstResult failure = Failure(optionsError);
                failure.Hints.Add(ProjectHint);
                return failure;
            }

            var result = new CodeFirstResult { FilePath = fullPath };

            RoslynCompileResult compile = _sandbox.Compile(request.Code);
            result.Diagnostics.AddRange(compile.Diagnostics);
            result.Errors.AddRange(compile.Errors);
            result.Warnings.AddRange(compile.Warnings);
            result.Hints.AddRange(compile.Hints);
            result.Namespaces.AddRange(compile.Namespaces);
            result.References.AddRange(compile.References);

            if (!compile.Ok)
            {
                result.Ok = false;
                return result;
            }

            CodeFirstActivity translated = CodeFirstWorkflowTranslator.Translate(compile.Assembly);
            result.EntryPoint = translated.EntryPoint;
            result.Errors.AddRange(translated.Errors);
            result.Hints.AddRange(translated.Hints);

            if (!translated.Ok)
            {
                result.Ok = false;
                return result;
            }

            // Имена из кода → в XAML: без них конструктор покажет нерезолвленные выражения.
            foreach (string name in compile.Namespaces)
            {
                if (!options.Namespaces.Contains(name))
                {
                    options.Namespaces.Add(name);
                }
            }

            foreach (string reference in compile.References)
            {
                if (!options.References.Contains(reference))
                {
                    options.References.Add(reference);
                }
            }

            result.ActivityCount = CodeFirstWorkflowTranslator.CountActivities(translated.Activity);

            var buildService = new WorkflowBuildService(options, _gate, fullPath == null ? null : _openService);
            WorkflowBuildResult build = fullPath == null
                ? buildService.Build(translated.Activity)
                : buildService.Save(translated.Activity, fullPath);

            result.Ok = build.Ok;
            result.WorkflowId = build.WorkflowId;
            result.Xaml = build.Xaml;

            if (!string.IsNullOrWhiteSpace(build.FilePath))
            {
                result.FilePath = build.FilePath;
            }

            AddDistinct(result.Errors, build.Errors);
            AddDistinct(result.Warnings, build.Warnings);
            result.Hints.AddRange(build.Hints.Where(hint => !result.Hints.Contains(hint)));

            if (!result.Ok || fullPath == null)
            {
                return result;
            }

            OpenInDesigner(result, fullPath);
            return result;
        }

        private void OpenInDesigner(CodeFirstResult result, string fullPath)
        {
            if (_openService == null)
            {
                result.Hints.Add("вкладка не открыта: среда конструктора недоступна");
                return;
            }

            DesignerOpenResult opened = _openService.OpenOrActivate(fullPath);
            result.Opened = opened.Ok;
            result.Activated = opened.Activated;

            if (!opened.Ok)
            {
                result.Ok = false;
                AddDistinct(result.Errors, opened.Errors);
                result.Hints.AddRange(opened.Hints);
                result.Hints.Add("файл записан и валиден, но вкладка конструктора не открылась");
                Log($"вкладка конструктора не открыта для {fullPath}: {string.Join("; ", opened.Errors)}");
                return;
            }

            result.WorkflowId = result.WorkflowId ?? opened.WorkflowId;
            result.Hints.AddRange(opened.Hints.Where(hint => !result.Hints.Contains(hint)));
        }

        private void Log(string message)
        {
            if (_log == null || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            try
            {
                _log(message);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Лог Code-First недоступен: " + ErrorText.Of(ex));
            }
        }

        private static void AddDistinct(ICollection<string> target, IEnumerable<string> items)
        {
            if (items == null)
            {
                return;
            }

            foreach (string item in items.Where(item => !string.IsNullOrWhiteSpace(item)))
            {
                if (!target.Contains(item))
                {
                    target.Add(item);
                }
            }
        }

        private static CodeFirstResult Failure(string error)
        {
            var result = new CodeFirstResult { Ok = false };
            result.Errors.Add(error);

            return result;
        }
    }
}
