using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using RPA.Helpers;

namespace RPA.Core.Generation
{
    /// <summary>
    /// Одно сообщение компилятора в машиночитаемом виде (этап 5 плана
    /// <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c>): код, строка, столбец, текст и подсказка.
    /// <para>
    /// Формат нужен агенту для самокоррекции: «строка 12, столбец 5, CS0103» — это правка,
    /// а «компиляция не удалась» — это тупик.
    /// </para>
    /// </summary>
    internal sealed class RoslynDiagnostic
    {
        internal RoslynDiagnostic(string id, string severity, int line, int column, string message, string hint = null)
        {
            Id = id;
            Severity = severity;
            Line = line;
            Column = column;
            Message = message;
            Hint = hint;
        }

        /// <summary>Код диагностики Roslyn (<c>CS0103</c>) или код песочницы (<c>code.banned</c>).</summary>
        internal string Id { get; }

        /// <summary>Важность: <c>error</c> или <c>warning</c>.</summary>
        internal string Severity { get; }

        /// <summary>Строка кода начиная с 1 (<c>0</c> — позиция неизвестна).</summary>
        internal int Line { get; }

        /// <summary>Столбец кода начиная с 1 (<c>0</c> — позиция неизвестна).</summary>
        internal int Column { get; }

        internal string Message { get; }

        /// <summary>Подсказка «что делать», привязанная к коду диагностики.</summary>
        internal string Hint { get; }

        internal bool IsError =>
            string.Equals(Severity, "error", StringComparison.OrdinalIgnoreCase);

        /// <summary>Текст для <c>errors[]</c> ответа инструмента: код + позиция + объяснение + подсказка.</summary>
        internal string Describe()
        {
            var text = new StringBuilder();
            text.Append(Id);

            if (Line > 0)
            {
                text.Append(" (строка ").Append(Line.ToString(CultureInfo.InvariantCulture));
                text.Append(", столбец ").Append(Column.ToString(CultureInfo.InvariantCulture)).Append(")");
            }

            text.Append(": ").Append(Message);

            if (!string.IsNullOrWhiteSpace(Hint))
            {
                text.Append(" — подсказка: ").Append(Hint);
            }

            return text.ToString();
        }
    }

    /// <summary>
    /// Результат компиляции кода агента: сборка, диагностика и разрешённые имена (пространства имён
    /// из <c>using</c> и сборки, в которых эти пространства имён реально живут).
    /// </summary>
    internal sealed class RoslynCompileResult
    {
        private RoslynCompileResult()
        {
        }

        /// <summary>Код скомпилирован: сборка загружена и готова к разбору объектной модели.</summary>
        internal bool Ok { get; private set; }

        /// <summary>Загруженная сборка кода агента (<c>null</c> при ошибке).</summary>
        internal Assembly Assembly { get; private set; }

        /// <summary>Имя скомпилированной сборки (уникально для каждой компиляции).</summary>
        internal string AssemblyName { get; private set; }

        /// <summary>Все сообщения компилятора: ошибки и предупреждения.</summary>
        internal List<RoslynDiagnostic> Diagnostics { get; } = new List<RoslynDiagnostic>();

        /// <summary>Ошибки в формате ответа инструмента.</summary>
        internal List<string> Errors { get; } = new List<string>();

        /// <summary>Предупреждения: не блокируют генерацию, но возвращаются агенту.</summary>
        internal List<string> Warnings { get; } = new List<string>();

        /// <summary>Подсказки: как исправить ошибку.</summary>
        internal List<string> Hints { get; } = new List<string>();

        /// <summary>Пространства имён из <c>using</c> кода — они попадут в <c>TextExpression.NamespacesForImplementation</c>.</summary>
        internal List<string> Namespaces { get; } = new List<string>();

        /// <summary>
        /// Сборки, в которых живут эти пространства имён: они попадут в
        /// <c>TextExpression.ReferencesForImplementation</c>. Иначе конструктор покажет нерезолвленные выражения.
        /// </summary>
        internal List<string> References { get; } = new List<string>();

        internal static RoslynCompileResult Success(string assemblyName, Assembly assembly)
        {
            return new RoslynCompileResult
            {
                Ok = true,
                AssemblyName = assemblyName,
                Assembly = assembly
            };
        }

        internal static RoslynCompileResult Failure(
            IEnumerable<RoslynDiagnostic> diagnostics,
            IEnumerable<string> hints = null)
        {
            var result = new RoslynCompileResult { Ok = false };

            result.WithDetails(diagnostics, null, null, hints);

            if (result.Errors.Count == 0)
            {
                result.Errors.Add("code.compile: компиляция не удалась без диагностики");
            }

            return result;
        }

        /// <summary>Переносит в результат диагностику, разрешённые имена и подсказки.</summary>
        internal RoslynCompileResult WithDetails(
            IEnumerable<RoslynDiagnostic> diagnostics,
            IEnumerable<string> namespaces,
            IEnumerable<string> references,
            IEnumerable<string> hints)
        {
            if (diagnostics != null)
            {
                Diagnostics.AddRange(diagnostics);
            }

            foreach (RoslynDiagnostic diagnostic in Diagnostics)
            {
                if (diagnostic.IsError)
                {
                    Errors.Add(diagnostic.Describe());
                }
                else
                {
                    Warnings.Add(diagnostic.Describe());
                }
            }

            AddNames(Namespaces, namespaces);
            AddNames(References, references);
            AddHints(hints);

            return this;
        }

        internal RoslynCompileResult AddHints(IEnumerable<string> hints)
        {
            if (hints == null)
            {
                return this;
            }

            foreach (string hint in hints.Where(hint => !string.IsNullOrWhiteSpace(hint)))
            {
                if (!Hints.Contains(hint))
                {
                    Hints.Add(hint);
                }
            }

            return this;
        }

        /// <summary>Текст отчёта для сообщения об ошибке теста или для лога.</summary>
        internal string Describe()
        {
            var text = new StringBuilder();
            text.AppendLine(Ok ? "компиляция: ок" : "компиляция: ОШИБКИ");
            text.AppendLine($"сборка: {AssemblyName ?? "(нет)"}");
            text.AppendLine($"ошибок: {Errors.Count}, предупреждений: {Warnings.Count}");

            AppendBlock(text, "ошибки", Errors);
            AppendBlock(text, "предупреждения", Warnings);
            AppendBlock(text, "подсказки", Hints);

            return text.ToString();
        }

        private static void AddNames(ICollection<string> target, IEnumerable<string> names)
        {
            if (names == null)
            {
                return;
            }

            foreach (string name in names.Where(name => !string.IsNullOrWhiteSpace(name)))
            {
                string trimmed = name.Trim();
                if (!target.Contains(trimmed))
                {
                    target.Add(trimmed);
                }
            }
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

    /// <summary>
    /// Параметры песочницы Roslyn (этап 5 плана <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c>).
    /// </summary>
    internal sealed class RoslynSandboxOptions
    {
        /// <summary>Предел размера кода агента: 128 КиБ — с запасом больше любого рабочего процесса.</summary>
        internal const int DefaultMaxCodeLength = 128 * 1024;

        /// <summary>
        /// Запрещённые пространства имён: ввод-вывод, рефлексия, сеть, диагностика (п. 5 плана) и
        /// соседние опасные API, через которые песочница обходится.
        /// </summary>
        internal static readonly string[] DefaultBannedNamespaces =
        {
            "System.IO",
            "System.Reflection",
            "System.Net",
            "System.Diagnostics",
            "System.Runtime.InteropServices",
            "System.Security",
            "System.Management",
            "Microsoft.Win32"
        };

        /// <summary>
        /// Whitelist сборок, доступных коду агента: WF, mscorlib, System.*, сборки активностей студии.
        /// Сборка, которой нет в списке, не попадёт и в ссылки XAML.
        /// </summary>
        private static readonly string[] DefaultAllowedAssemblies =
        {
            "mscorlib",
            "System",
            "System.Core",
            "System.Data",
            "System.Runtime.Serialization",
            "System.Activities",
            "System.ServiceModel.Activities",
            "System.Xaml",
            "System.Xml",
            "System.Xml.Linq",
            "Microsoft.CSharp"
        };

        internal RoslynSandboxOptions()
        {
            foreach (string name in DefaultAllowedAssemblies)
            {
                AllowedAssemblies.Add(name);
            }

            // Сборки активностей студии: код агента может ссылаться на её активности, но только на те,
            // что уже загружены в процесс (то есть реально существуют в этой установке).
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name = assembly.GetName().Name;

                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                bool studioAssembly = name.StartsWith("Activities", StringComparison.OrdinalIgnoreCase)
                                      || name.StartsWith("Rpa.", StringComparison.OrdinalIgnoreCase)
                                      || name.StartsWith("RPA.", StringComparison.OrdinalIgnoreCase);

                if (studioAssembly && !AllowedAssemblies.Contains(name))
                {
                    AllowedAssemblies.Add(name);
                }
            }
        }

        /// <summary>Предел размера кода в символах.</summary>
        internal int MaxCodeLength { get; set; } = DefaultMaxCodeLength;

        /// <summary>Предел времени компиляции: превышен — ошибка инструмента, а не зависший вызов MCP.</summary>
        internal TimeSpan CompileTimeout { get; set; } = TimeSpan.FromSeconds(20);

        /// <summary>Запрещённые пространства имён (сравнение по префиксу: <c>System.IO</c> и <c>System.IO.*</c>).</summary>
        internal List<string> BannedNamespaces { get; } = new List<string>(DefaultBannedNamespaces);

        /// <summary>Разрешённые сборки (простые имена).</summary>
        internal List<string> AllowedAssemblies { get; } = new List<string>();

        internal static RoslynSandboxOptions CreateDefault()
        {
            return new RoslynSandboxOptions();
        }
    }

    /// <summary>
    /// Песочница для кода агента: компиляция Roslyn с whitelist сборок, запретом опасных пространств имён
    /// и лимитами (этап 5 плана <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c>).
    /// <para>
    /// Почему песочница, а не «просто <c>CSharpCompilation</c>»: код приходит от ИИ-агента, то есть из
    /// недоверенного источника. <c>System.IO</c>/<c>System.Reflection</c>/<c>System.Net</c>/
    /// <c>System.Diagnostics</c> запрещены п. 5 плана — иначе под видом генерации процесса можно прочитать
    /// файлы пользователя или запустить произвольный код в процессе студии. Дополнительно запрещены
    /// <c>unsafe</c>, <c>fixed</c>, <c>stackalloc</c> и <c>[DllImport]</c>: это обход и типов, и памяти.
    /// </para>
    /// <para>
    /// Компиляция ограничена по размеру кода и по времени: занятая компиляция возвращает ошибку
    /// песочницы, а не блокирует MCP-вызов.
    /// </para>
    /// </summary>
    internal sealed class RoslynSandbox
    {
        /// <summary>Подсказка песочницы: что именно запрещено и почему.</summary>
        internal const string SandboxHint =
            "код агента компилируется в песочнице: запрещены System.IO, System.Reflection, System.Net, " +
            "System.Diagnostics, System.Runtime.InteropServices, System.Security, System.Management, Microsoft.Win32, " +
            "а также unsafe/fixed/stackalloc и [DllImport]";

        /// <summary>Контракт кода процесса — одна и та же подсказка во всех ошибках трансляции.</summary>
        internal const string CodeFirstContractHint =
            "код процесса объявляет либо public static Activity Build() (также допускаются имена Create, CreateWorkflow, GetWorkflow), " +
            "либо public-класс без параметров конструктора, наследующий System.Activities.Activity; " +
            "внутри — встроенные активности WF (Sequence, If, ForEach, While, TryCatch, Assign, WriteLine) " +
            "и выражения CSharpValue<T>/CSharpReference<T>";

        private static readonly Lazy<RoslynSandbox> DefaultSandbox = new Lazy<RoslynSandbox>(() => new RoslynSandbox());

        private static int _counter;

        /// <summary>Единственная песочница с параметрами по умолчанию (создаётся при первом обращении).</summary>
        internal static RoslynSandbox Default => DefaultSandbox.Value;

        internal RoslynSandbox(RoslynSandboxOptions options = null)
        {
            Options = options ?? RoslynSandboxOptions.CreateDefault();
        }

        internal RoslynSandboxOptions Options { get; }

        /// <summary>
        /// Компилирует код агента в сборку. Ошибки возвращаются в <see cref="RoslynCompileResult"/>
        /// с кодом, строкой и столбцом, чтобы агент мог исправить код сам.
        /// </summary>
        internal RoslynCompileResult Compile(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return RoslynCompileResult.Failure(
                    new[] { new RoslynDiagnostic("code.empty", "error", 0, 0, "код процесса не задан") },
                    new[] { CodeFirstContractHint });
            }

            if (Options.MaxCodeLength > 0 && code.Length > Options.MaxCodeLength)
            {
                return RoslynCompileResult.Failure(
                    new[]
                    {
                        new RoslynDiagnostic(
                            "code.too_long",
                            "error",
                            0,
                            0,
                            $"код процесса длиннее лимита песочницы: {code.Length} символов при пределе {Options.MaxCodeLength}")
                    },
                    new[] { "сократите код процесса: лимит защищает студию от зависания" });
            }

            SyntaxTree tree;
            try
            {
                tree = CSharpSyntaxTree.ParseText(
                    code,
                    new CSharpParseOptions(LanguageVersion.CSharp7_3),
                    "Workflow.cs");
            }
            catch (Exception ex)
            {
                return RoslynCompileResult.Failure(
                    new[]
                    {
                        new RoslynDiagnostic("code.parse", "error", 0, 0, ErrorText.Describe("код не разобран", ex))
                    },
                    new[] { CodeFirstContractHint });
            }

            SyntaxNode root = tree.GetRoot();

            List<string> violations = RoslynCodePolicy.Inspect(root, Options.BannedNamespaces);
            if (violations.Count > 0)
            {
                return RoslynCompileResult.Failure(
                    violations.Select(violation => new RoslynDiagnostic("code.banned", "error", 0, 0, violation)),
                    new[] { SandboxHint });
            }

            List<string> namespaces = RoslynCodePolicy.CollectNamespaces(root);
            List<string> references = RoslynAssemblyCatalog.ResolveNamespaces(namespaces, Options.AllowedAssemblies);

            CSharpCompilation compilation;
            string assemblyName;

            try
            {
                assemblyName = NextAssemblyName();
                compilation = CreateCompilation(tree, assemblyName);
            }
            catch (Exception ex)
            {
                return RoslynCompileResult.Failure(
                    new[]
                    {
                        new RoslynDiagnostic(
                            "code.references",
                            "error",
                            0,
                            0,
                            ErrorText.Describe("ссылки на сборки не собраны", ex))
                    },
                    new[] { SandboxHint });
            }

            Task<RoslynCompileResult> compileTask = Task.Run(() =>
                Emit(compilation, assemblyName, namespaces, references));

            try
            {
                // Ожидание ограничено: занятая компиляция возвращает ошибку инструменту, а не вешает MCP-вызов.
                if (!compileTask.Wait(Options.CompileTimeout))
                {
                    return RoslynCompileResult.Failure(
                        new[]
                        {
                            new RoslynDiagnostic(
                                "code.timeout",
                                "error",
                                0,
                                0,
                                $"компиляция не уложилась в лимит песочницы: {Options.CompileTimeout.TotalSeconds:0.###} с")
                        },
                        new[] { "сократите код процесса: лимит времени защищает студию от зависания" });
                }

                return compileTask.Result;
            }
            catch (AggregateException ex)
            {
                return RoslynCompileResult.Failure(
                    new[]
                    {
                        new RoslynDiagnostic(
                            "code.compile",
                            "error",
                            0,
                            0,
                            ErrorText.Describe("компиляция не выполнена", ex.GetBaseException()))
                    },
                    new[] { CodeFirstContractHint });
            }
        }

        private CSharpCompilation CreateCompilation(SyntaxTree tree, string assemblyName)
        {
            var options = new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug,
                allowUnsafe: false,
                assemblyIdentityComparer: DesktopAssemblyIdentityComparer.Default);

            return CSharpCompilation.Create(assemblyName, new[] { tree }, CreateMetadataReferences(), options);
        }

        private IReadOnlyList<MetadataReference> CreateMetadataReferences()
        {
            var references = new List<MetadataReference>();

            foreach (string name in Options.AllowedAssemblies)
            {
                // Путь достаточно найти: MetadataReference не требует загрузки сборки.
                string location = RoslynAssemblyCatalog.FindLocation(name);
                // Файла нет — сборки нет в этой установке, и она просто не попадает в ссылки.

                if (string.IsNullOrEmpty(location) || !File.Exists(location))
                {
                    continue;
                }

                try
                {
                    references.Add(MetadataReference.CreateFromFile(location));
                }
                catch (Exception ex)
                {
                    // Одна недоступная сборка не должна ронять песочницу: остальные дают рабочий компилятор.
                    System.Diagnostics.Debug.WriteLine(
                        $"Сборка «{name}» не добавлена в ссылки песочницы: " + ErrorText.Of(ex));
                }
            }

            return references;
        }

        private RoslynCompileResult Emit(
            CSharpCompilation compilation,
            string assemblyName,
            IReadOnlyList<string> namespaces,
            IReadOnlyList<string> references)
        {
            try
            {
                using (var stream = new MemoryStream())
                {
                    EmitResult emit = compilation.Emit(stream);
                    List<RoslynDiagnostic> diagnostics = ConvertDiagnostics(emit.Diagnostics);
                    IEnumerable<string> hints = DiagnosticHints(diagnostics);

                    if (!emit.Success)
                    {
                        return RoslynCompileResult.Failure(diagnostics, hints);
                    }

                    Assembly assembly = Assembly.Load(stream.ToArray());

                    return RoslynCompileResult.Success(assemblyName, assembly)
                        .WithDetails(diagnostics, namespaces, references, hints);
                }
            }
            catch (Exception ex)
            {
                return RoslynCompileResult.Failure(
                    new[]
                    {
                        new RoslynDiagnostic(
                            "code.emit",
                            "error",
                            0,
                            0,
                            ErrorText.Describe("сборка кода не загружена", ex))
                    },
                    new[] { CodeFirstContractHint });
            }
        }

        private static List<RoslynDiagnostic> ConvertDiagnostics(IEnumerable<Diagnostic> diagnostics)
        {
            var converted = new List<RoslynDiagnostic>();

            foreach (Diagnostic diagnostic in diagnostics)
            {
                int line = 0;
                int column = 0;

                if (diagnostic.Location != Location.None)
                {
                    FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
                    line = span.StartLinePosition.Line + 1;
                    column = span.StartLinePosition.Character + 1;
                }

                converted.Add(new RoslynDiagnostic(
                    diagnostic.Id,
                    diagnostic.Severity == DiagnosticSeverity.Error ? "error" : "warning",
                    line,
                    column,
                    diagnostic.GetMessage(CultureInfo.InvariantCulture),
                    RoslynDiagnosticHints.HintFor(diagnostic.Id)));
            }

            return converted;
        }

        private static IEnumerable<string> DiagnosticHints(IEnumerable<RoslynDiagnostic> diagnostics)
        {
            return (diagnostics ?? Enumerable.Empty<RoslynDiagnostic>())
                .Select(diagnostic => diagnostic.Hint)
                .Where(hint => !string.IsNullOrWhiteSpace(hint))
                .Distinct();
        }

        private static string NextAssemblyName()
        {
            int number = System.Threading.Interlocked.Increment(ref _counter);
            return "RpaGeneratedWorkflow_"
                   + number.ToString(CultureInfo.InvariantCulture)
                   + "_"
                   + Guid.NewGuid().ToString("N");
        }
    }

    /// <summary>
    /// Подсказки по кодам ошибок Roslyn: агент получает не только «CS0103», но и «что с этим делать».
    /// </summary>
    internal static class RoslynDiagnosticHints
    {
        private const string UnknownNameHint =
            "имя не найдено: проверьте using, объявление переменной и имя активности — " +
            "состав активностей и их аргументов есть в xaml.get_workflow и в палитре (xaml.add_activity)";

        private const string UnknownTypeHint =
            "тип не найден: добавьте using или разрешённую ссылку на сборку (whitelist песочницы), " +
            "либо используйте активность из палитры студии";

        private const string WrongArgumentHint =
            "проверьте имя и тип аргумента активности: значения задаются как CSharpValue<T>/CSharpReference<T>";

        private const string SyntaxHint =
            "исправьте синтаксис C#: незакрытая скобка или пропущенная точка с запятой — самая частая причина";

        private const string UnsafeHint =
            "unsafe-код запрещён песочницей: указатели, fixed и stackalloc недоступны";

        internal static string HintFor(string id)
        {
            switch (id)
            {
                case "CS0103":
                case "CS0104":
                case "CS0117":
                    return UnknownNameHint;

                case "CS0246":
                case "CS0234":
                case "CS0400":
                case "CS0012":
                    return UnknownTypeHint;

                case "CS1061":
                case "CS1501":
                case "CS1503":
                case "CS0029":
                case "CS0266":
                case "CS0023":
                    return WrongArgumentHint;

                case "CS1002":
                case "CS1003":
                case "CS1022":
                case "CS1026":
                case "CS1513":
                case "CS1514":
                case "CS1519":
                case "CS1525":
                    return SyntaxHint;

                case "CS0227":
                case "CS0193":
                case "CS0208":
                case "CS0214":
                    return UnsafeHint;

                default:
                    return null;
            }
        }
    }

    /// <summary>
    /// Проверки кода до компиляции: запрещённые пространства имён (в <c>using</c>, в полных именах
    /// и в алиасах), <c>unsafe</c>/<c>fixed</c>/<c>stackalloc</c> и <c>[DllImport]</c>.
    /// <para>
    /// Проверка идёт по синтаксическому дереву, а не по тексту: строка в комментарии или в литерале
    /// не даёт ложного срабатывания, а <c>System.IO.File</c>, написанное с переносом строки, — даёт.
    /// </para>
    /// </summary>
    internal static class RoslynCodePolicy
    {
        private static readonly string[] BannedAttributeNames = { "DllImport", "DllImportAttribute" };

        internal static List<string> Inspect(SyntaxNode root, IEnumerable<string> bannedNamespaces)
        {
            string[] banned = (bannedNamespaces ?? Enumerable.Empty<string>())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .ToArray();

            var violations = new List<string>();
            var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (root == null)
            {
                return violations;
            }

            foreach (UsingDirectiveSyntax directive in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
            {
                string name = directive.Name?.ToString();
                if (IsBannedNamespace(name, banned))
                {
                    Report(
                        violations,
                        reported,
                        directive.GetLocation(),
                        $"запрещённое пространство имён в using: {name}");
                }
            }

            foreach (QualifiedNameSyntax qualified in root.DescendantNodes().OfType<QualifiedNameSyntax>())
            {
                string name = qualified.ToString();
                if (IsBannedNamespace(name, banned))
                {
                    Report(violations, reported, qualified.GetLocation(), $"запрещённое пространство имён: {name}");
                }
            }

            foreach (MemberAccessExpressionSyntax member in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
            {
                string name = member.Expression.ToString();
                if (IsBannedNamespace(name, banned))
                {
                    Report(violations, reported, member.GetLocation(), $"запрещённое пространство имён: {name}");
                }
            }

            foreach (SyntaxToken token in root.DescendantTokens())
            {
                if (token.IsKind(SyntaxKind.UnsafeKeyword))
                {
                    Report(violations, reported, token.GetLocation(), "запрещено ключевое слово unsafe");
                }

                if (token.IsKind(SyntaxKind.StackAllocKeyword))
                {
                    Report(violations, reported, token.GetLocation(), "запрещено выделение памяти stackalloc");
                }

                if (token.IsKind(SyntaxKind.FixedKeyword) && token.Parent is FixedStatementSyntax)
                {
                    Report(violations, reported, token.GetLocation(), "запрещена фиксация указателя fixed");
                }
            }

            foreach (AttributeSyntax attribute in root.DescendantNodes().OfType<AttributeSyntax>())
            {
                string name = attribute.Name?.ToString();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                string lastSegment = name.Split('.').Last().Trim();
                if (BannedAttributeNames.Any(bannedName =>
                        string.Equals(lastSegment, bannedName, StringComparison.Ordinal)))
                {
                    Report(
                        violations,
                        reported,
                        attribute.GetLocation(),
                        $"запрещён вызов неуправляемого кода: [{lastSegment}]");
                }
            }

            return violations;
        }

        /// <summary>
        /// Пространства имён из <c>using</c> кода: из них строятся <c>TextExpression.NamespacesForImplementation</c>
        /// и набор ссылок на сборки (иначе конструктор покажет нерезолвленные выражения).
        /// </summary>
        internal static List<string> CollectNamespaces(SyntaxNode root)
        {
            var namespaces = new List<string>();

            if (root == null)
            {
                return namespaces;
            }

            foreach (UsingDirectiveSyntax directive in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
            {
                string name = directive.Name?.ToString();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                string trimmed = name.Replace(" ", string.Empty).Replace("\t", string.Empty).Trim();
                if (trimmed.Length > 0 && !namespaces.Contains(trimmed))
                {
                    namespaces.Add(trimmed);
                }
            }

            return namespaces;
        }

        private static bool IsBannedNamespace(string candidate, IReadOnlyCollection<string> banned)
        {
            if (string.IsNullOrWhiteSpace(candidate) || banned.Count == 0)
            {
                return false;
            }

            string normalized = candidate
                .Replace(" ", string.Empty)
                .Replace("\t", string.Empty)
                .Replace("\r", string.Empty)
                .Replace("\n", string.Empty)
                .TrimEnd('.');

            foreach (string prefix in banned)
            {
                if (normalized.Equals(prefix, StringComparison.Ordinal)
                    || normalized.StartsWith(prefix + ".", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void Report(
            ICollection<string> violations,
            ISet<string> reported,
            Location location,
            string message)
        {
            FileLinePositionSpan span = location.GetLineSpan();
            int line = span.StartLinePosition.Line + 1;
            int column = span.StartLinePosition.Character + 1;

            string text = $"строка {line}, столбец {column}: {message}";
            if (reported.Add(text))
            {
                violations.Add(text);
            }
        }
    }

    /// <summary>
    /// Каталог сборок песочницы: находит загруженную сборку по имени и отвечает, в какой сборке живёт
    /// пространство имён. Второе нужно, чтобы ссылки XAML строились «из реально использованных сборок»,
    /// а не из догадок.
    /// </summary>
    internal static class RoslynAssemblyCatalog
    {
        private static readonly object Gate = new object();

        private static readonly Dictionary<string, Assembly> Assemblies =
            new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, HashSet<string>> NamespacesByAssembly =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Путь к сборке по простому имени; <c>null</c> — сборки нет в этой установке.
        /// <para>
        /// Ищется без обязательной загрузки: сначала загруженная копия, потом файл рядом со студией и файл
        /// в каталоге среды выполнения. <c>Assembly.Load</c> для части WF-сборок (например
        /// <c>System.Activities</c>) может не сработать, а путь к файлу есть всегда — и для компиляции
        /// нужен именно путь.
        /// </para>
        /// </summary>
        internal static string FindLocation(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            string trimmed = name.Trim();

            Assembly loaded = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(candidate => string.Equals(
                    candidate.GetName().Name, trimmed, StringComparison.OrdinalIgnoreCase));

            if (loaded != null && !string.IsNullOrEmpty(loaded.Location) && File.Exists(loaded.Location))
            {
                return loaded.Location;
            }

            foreach (string directory in ProbeDirectories())
            {
                foreach (string fileName in new[] { trimmed + ".dll", trimmed })
                {
                    try
                    {
                        string path = Path.Combine(directory, fileName);
                        if (File.Exists(path))
                        {
                            return path;
                        }
                    }
                    catch (Exception)
                    {
                        // Недопустимое имя файла — просто пробуем следующий вариант.
                    }
                }
            }

            try
            {
                Assembly probed = Assembly.Load(new AssemblyName(trimmed));

                if (probed != null && !string.IsNullOrEmpty(probed.Location) && File.Exists(probed.Location))
                {
                    return probed.Location;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Сборка «{trimmed}» не найдена пробой: " + ErrorText.Of(ex));
            }

            return null;
        }

        private static IEnumerable<string> ProbeDirectories()
        {
            yield return AppDomain.CurrentDomain.BaseDirectory;

            string runtimeDirectory = null;

            try
            {
                runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Каталог среды выполнения не определён: " + ErrorText.Of(ex));
            }

            if (!string.IsNullOrEmpty(runtimeDirectory))
            {
                yield return runtimeDirectory;
            }
        }

        /// <summary>Загружает сборку по простому имени; <c>null</c> — сборки нет в этой установке.</summary>
        internal static Assembly Load(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            string trimmed = name.Trim();

            lock (Gate)
            {
                if (Assemblies.TryGetValue(trimmed, out Assembly cached))
                {
                    return cached;
                }
            }

            Assembly assembly = null;

            try
            {
                assembly = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .FirstOrDefault(loaded => string.Equals(
                        loaded.GetName().Name, trimmed, StringComparison.OrdinalIgnoreCase));

                if (assembly == null)
                {
                    string location = FindLocation(trimmed);

                    if (!string.IsNullOrEmpty(location))
                    {
                        assembly = Assembly.LoadFrom(location);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Сборка «{trimmed}» недоступна песочнице: " + ErrorText.Of(ex));
            }

            lock (Gate)
            {
                if (assembly != null)
                {
                    Assemblies[trimmed] = assembly;
                }
            }

            return assembly;
        }

        /// <summary>
        /// Сборки, в которых живут заданные пространства имён. Возвращаются только простые имена
        /// из whitelist: сборка вне списка не может попасть в ссылки XAML.
        /// </summary>
        internal static List<string> ResolveNamespaces(
            IEnumerable<string> namespaces,
            IEnumerable<string> allowedAssemblies)
        {
            var resolved = new List<string>();
            List<string> requested = (namespaces ?? Enumerable.Empty<string>())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (requested.Count == 0)
            {
                return resolved;
            }

            foreach (string assemblyName in allowedAssemblies ?? Enumerable.Empty<string>())
            {
                HashSet<string> assemblyNamespaces = NamespacesOf(assemblyName);
                if (assemblyNamespaces == null || assemblyNamespaces.Count == 0)
                {
                    continue;
                }

                if (requested.Any(name => assemblyNamespaces.Contains(name)) && !resolved.Contains(assemblyName))
                {
                    resolved.Add(assemblyName);
                }
            }

            return resolved;
        }

        private static HashSet<string> NamespacesOf(string assemblyName)
        {
            lock (Gate)
            {
                if (NamespacesByAssembly.TryGetValue(assemblyName, out HashSet<string> cached))
                {
                    return cached;
                }
            }

            HashSet<string> namespaces = null;
            Assembly assembly = Load(assemblyName);

            if (assembly != null)
            {
                try
                {
                    namespaces = new HashSet<string>(
                        assembly.GetExportedTypes()
                            .Select(type => type.Namespace)
                            .Where(name => !string.IsNullOrEmpty(name)),
                        StringComparer.Ordinal);
                }
                catch (ReflectionTypeLoadException ex)
                {
                    namespaces = new HashSet<string>(
                        (ex.Types ?? Array.Empty<Type>())
                            .Where(type => type != null)
                            .Select(type => type.Namespace)
                            .Where(name => !string.IsNullOrEmpty(name)),
                        StringComparer.Ordinal);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Пространства имён сборки «{assemblyName}» не прочитаны: " + ErrorText.Of(ex));
                }
            }

            lock (Gate)
            {
                NamespacesByAssembly[assemblyName] = namespaces;
            }

            return namespaces;
        }
    }
}
