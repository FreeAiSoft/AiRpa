using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Agent.Core.Contracts;
using Agent.Core.Models;

namespace RPA.Chat.Services.Workflow
{
    /// <summary>
    /// Создание активности в <b>существующем</b> проекте библиотеки активностей (карточка <c>AG-08</c>):
    /// файл класса, дизайнер с code-behind, регистрация в <c>.csproj</c> и — по запросу — сборка.
    /// <para>
    /// Служба живёт в <see cref="RPA.Chat"/> и не зависит ни от WF-типов, ни от WPF: она пишет текст,
    /// правит XML проекта и дергает <see cref="IBuildFeedbackService"/>. Поэтому весь сценарий проверяется
    /// без UI, а загрузку сборки в домен и обновление палитры делает хост студии
    /// (<c>IActivityPaletteNotifier</c>).
    /// </para>
    /// <para>
    /// <b>Атомарность — часть контракта:</b> перед первой записью запоминается поблочное состояние
    /// <c>.csproj</c>, и любая ошибка (исключение или ошибки компиляции при <c>build=true</c>) возвращает
    /// проект к исходным байтам и удаляет созданные файлы. «Полуготовой» активности в проекте не остаётся.
    /// </para>
    /// </summary>
    public sealed class ActivityCreationService
    {
        /// <summary>Базовый тип по умолчанию: синхронная <c>CodeActivity</c>.</summary>
        public const string CodeActivityBase = "CodeActivity";

        /// <summary>Асинхронная активность (<c>BeginExecute</c>/<c>EndExecute</c>).</summary>
        public const string AsyncCodeActivityBase = "AsyncCodeActivity";

        /// <summary>Нативная активность (доступ к планировщику WF).</summary>
        public const string NativeActivityBase = "NativeActivity";

        /// <summary>Целевая платформа библиотеки активностей (правило <c>CP-10</c>: всё, что попадает к роботу, — 4.7.2).</summary>
        public const string RequiredTargetFramework = "v4.7.2";

        private const string RequiredTargetFrameworkHint = "net472";

        private static readonly Regex IdentifierRegex = new Regex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

        private readonly IBuildFeedbackService _buildFeedback;

        /// <param name="buildFeedback">
        /// Служба сборки студии; <c>null</c> допустим, но тогда <see cref="ActivityCreateRequest.Build"/>
        /// отклоняется до создания файлов, а не «молча не собирает».
        /// </param>
        public ActivityCreationService(IBuildFeedbackService buildFeedback = null)
        {
            _buildFeedback = buildFeedback;
        }

        /// <summary>Создаёт активность. Ничего не пишет на диск, если запрос некорректен.</summary>
        public async Task<ActivityCreateResult> CreateAsync(ActivityCreateRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                return ActivityCreateResult.Fail("пустой запрос: нужны projectPath и className");
            }

            string projectPath = (request.ProjectPath ?? string.Empty).Trim();
            if (projectPath.Length == 0)
            {
                return ActivityCreateResult.Fail(
                    "не задан projectPath: передайте путь к файлу проекта библиотеки активностей (.csproj)");
            }

            if (!File.Exists(projectPath))
            {
                return ActivityCreateResult.Fail("файл проекта не найден: " + projectPath);
            }

            if (!projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                return ActivityCreateResult.Fail(
                    "projectPath должен указывать на файл проекта Visual Studio (.csproj), получено: " + Path.GetFileName(projectPath));
            }

            string className = (request.ClassName ?? string.Empty).Trim();
            if (!IsIdentifier(className))
            {
                return ActivityCreateResult.Fail(
                    "недопустимое имя класса: \"" + (request.ClassName ?? string.Empty) + "\" — нужен идентификатор C# ([A-Za-z_][A-Za-z0-9_]*)");
            }

            string baseType = NormalizeBaseType(request.BaseType);
            if (baseType == null)
            {
                return ActivityCreateResult.Fail(
                    "неизвестный базовый тип \"" + request.BaseType + "\": допустимы " + CodeActivityBase + ", " + AsyncCodeActivityBase + ", " + NativeActivityBase);
            }

            if (request.Build && _buildFeedback == null)
            {
                return ActivityCreateResult.Fail(
                    "сборка запрошена (build=true), но служба сборки не подключена хостом студии: создание отменено, файлы не тронуты");
            }

            XDocument project;
            try
            {
                project = XDocument.Parse(File.ReadAllText(projectPath));
            }
            catch (Exception ex)
            {
                return ActivityCreateResult.Fail("не удалось прочитать файл проекта: " + ex.Message);
            }

            XNamespace ns = project.Root?.Name.Namespace ?? XNamespace.None;

            if (!string.Equals(project.Root?.Name.LocalName, "Project", StringComparison.Ordinal))
            {
                return ActivityCreateResult.Fail("файл \"" + Path.GetFileName(projectPath) + "\" не является проектом MSBuild");
            }

            string targetFrameworkError = CheckTargetFramework(project, ns);
            if (targetFrameworkError != null)
            {
                return ActivityCreateResult.Fail(targetFrameworkError);
            }

            string projectDirectory = Path.GetDirectoryName(Path.GetFullPath(projectPath));
            string projectNamespace = ResolveNamespace(project, ns, request.Namespace, projectPath);
            if (projectNamespace == null)
            {
                return ActivityCreateResult.Fail(
                    "недопустимое пространство имён \"" + request.Namespace + "\" — нужны идентификаторы C#, разделённые точками");
            }

            string classFile = Path.Combine(projectDirectory, className + ".cs");
            string designerFile = request.WithDesigner ? Path.Combine(projectDirectory, className + "Designer.xaml") : null;
            string designerCodeFile = request.WithDesigner ? designerFile + ".cs" : null;

            List<ActivityCreateArgument> arguments;
            string argumentsError = NormalizeArguments(request, out arguments);
            if (argumentsError != null)
            {
                return ActivityCreateResult.Fail(argumentsError);
            }

            string duplicateError = CheckDuplicates(project, ns, projectDirectory, className, request.WithDesigner);
            if (duplicateError != null)
            {
                return ActivityCreateResult.Fail(duplicateError);
            }

            byte[] projectSnapshot = File.ReadAllBytes(projectPath);
            var createdFiles = new List<string>();

            try
            {
                WriteClassFile(classFile, projectNamespace, className, baseType, request, arguments);
                createdFiles.Add(classFile);

                if (request.WithDesigner)
                {
                    WriteDesignerFile(designerFile, projectNamespace, className, request, arguments);
                    createdFiles.Add(designerFile);

                    WriteDesignerCodeFile(designerCodeFile, projectNamespace, className);
                    createdFiles.Add(designerCodeFile);
                }

                RegisterInProject(projectPath, className, request.WithDesigner);
            }
            catch (Exception ex)
            {
                Rollback(projectPath, projectSnapshot, createdFiles);
                return ActivityCreateResult.Fail("не удалось создать активность: " + ex.Message);
            }

            string configuration = string.IsNullOrWhiteSpace(request.Configuration) ? "Debug" : request.Configuration.Trim();
            var requiresDecision = new List<string>();
            string assemblyPath = null;
            var buildErrors = new List<string>();

            if (!request.Build)
            {
                requiresDecision.Add("сборка не запрашивалась (build=false): тип не виден ни каталогу, ни панели «Активности» до сборки");
            }
            else
            {
                try
                {
                    await _buildFeedback.BuildAsync(configuration, cancellationToken).ConfigureAwait(false);
                    IReadOnlyList<BuildError> errors = await _buildFeedback.GetCompilationErrorsAsync(cancellationToken).ConfigureAwait(false);
                    buildErrors.AddRange(DescribeErrors(errors));
                }
                catch (OperationCanceledException)
                {
                    Rollback(projectPath, projectSnapshot, createdFiles);
                    throw;
                }
                catch (Exception ex)
                {
                    Rollback(projectPath, projectSnapshot, createdFiles);
                    return ActivityCreateResult.Fail("сборка не выполнена: " + ex.Message + "; изменения отменены");
                }

                if (buildErrors.Count > 0)
                {
                    Rollback(projectPath, projectSnapshot, createdFiles);
                    return ActivityCreateResult.Fail(
                        "сборка завершилась с ошибками (" + buildErrors.Count + "): изменения отменены; " + string.Join("; ", buildErrors), buildErrors);
                }

                assemblyPath = ResolveAssemblyPath(projectPath, configuration);
                if (assemblyPath == null)
                {
                    requiresDecision.Add(
                        "сборка прошла, но собранная сборка не найдена по ожидаемому пути (OutputPath/AssemblyName): палитра не обновлена, проверьте проект");
                }
            }

            return ActivityCreateResult.Ok(
                projectNamespace + "." + className,
                classFile,
                designerFile,
                assemblyPath,
                createdFiles,
                new[] { projectPath },
                requiresDecision,
                request.Build,
                configuration);
        }

        // ------------------------------------------------------------------
        // Проверки
        // ------------------------------------------------------------------

        private static bool IsIdentifier(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && IdentifierRegex.IsMatch(value);
        }

        private static string NormalizeBaseType(string baseType)
        {
            if (string.IsNullOrWhiteSpace(baseType))
            {
                return CodeActivityBase;
            }

            string value = baseType.Trim();
            if (string.Equals(value, CodeActivityBase, StringComparison.OrdinalIgnoreCase))
            {
                return CodeActivityBase;
            }

            if (string.Equals(value, AsyncCodeActivityBase, StringComparison.OrdinalIgnoreCase))
            {
                return AsyncCodeActivityBase;
            }

            return string.Equals(value, NativeActivityBase, StringComparison.OrdinalIgnoreCase) ? NativeActivityBase : null;
        }

        /// <summary>
        /// Библиотека активностей обязана целиться в <c>net472</c> (правило <c>CP-10</c>): студия — 4.8,
        /// робот — 4.7.2. Проверяются оба написания: legacy <c>v4.7.2</c> и SDK-шное <c>net472</c>.
        /// </summary>
        private static string CheckTargetFramework(XDocument project, XNamespace ns)
        {
            string legacy = PropertyValue(project, ns, "TargetFrameworkVersion");
            if (!string.IsNullOrWhiteSpace(legacy))
            {
                if (string.Equals(legacy.Trim(), RequiredTargetFramework, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                return "проект целится в " + legacy.Trim() + ", а библиотека активностей обязана целиться в "
                       + RequiredTargetFrameworkHint + " (" + RequiredTargetFramework + "): иначе робот на 4.7.2 не загрузит сборку";
            }

            string modern = PropertyValue(project, ns, "TargetFramework");
            if (!string.IsNullOrWhiteSpace(modern))
            {
                string value = modern.Trim();
                if (string.Equals(value, RequiredTargetFrameworkHint, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value, "net4.7.2", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                return "проект целится в " + value + ", а библиотека активностей обязана целиться в " + RequiredTargetFrameworkHint;
            }

            return "в проекте не задана целевая платформа: библиотека активностей обязана целиться в " + RequiredTargetFrameworkHint;
        }

        private static string ResolveNamespace(XDocument project, XNamespace ns, string requested, string projectPath)
        {
            string value = (requested ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                value = PropertyValue(project, ns, "RootNamespace");
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                value = PropertyValue(project, ns, "AssemblyName");
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                value = Path.GetFileNameWithoutExtension(projectPath);
            }

            value = value.Trim();
            foreach (string part in value.Split('.'))
            {
                if (!IsIdentifier(part))
                {
                    return null;
                }
            }

            return value;
        }

        private static string NormalizeArguments(ActivityCreateRequest request, out List<ActivityCreateArgument> arguments)
        {
            arguments = new List<ActivityCreateArgument>();

            if (request.Arguments == null || request.Arguments.Count == 0)
            {
                return null;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (ActivityCreateArgument argument in request.Arguments)
            {
                if (argument == null)
                {
                    continue;
                }

                if (!IsIdentifier((argument.Name ?? string.Empty).Trim()))
                {
                    return "недопустимое имя аргумента: \"" + (argument.Name ?? string.Empty) + "\"";
                }

                string name = argument.Name.Trim();
                if (!seen.Add(name))
                {
                    return "аргумент \"" + name + "\" задан дважды";
                }

                string type = string.IsNullOrWhiteSpace(argument.TypeName) ? "String" : argument.TypeName.Trim();
                if (!IsTypeName(type))
                {
                    return "недопустимый тип аргумента \"" + type + "\" (нужно имя типа без пробелов, например String или List<String>)";
                }

                string direction = NormalizeDirection(argument.Direction);
                if (direction == null)
                {
                    return "недопустимое направление аргумента \"" + argument.Direction + "\": допустимы In, Out, InOut";
                }

                arguments.Add(new ActivityCreateArgument
                {
                    Name = name,
                    TypeName = type,
                    Direction = direction,
                    Required = argument.Required,
                    Description = argument.Description,
                    Category = argument.Category
                });
            }

            if (arguments.Count != request.Arguments.Count(item => item != null))
            {
                return "аргументы содержат пустые элементы";
            }

            return null;
        }

        private static bool IsTypeName(string type)
        {
            return !string.IsNullOrWhiteSpace(type)
                   && type.IndexOf(' ') < 0
                   && type.IndexOf('\t') < 0
                   && type.IndexOf(',') < 0;
        }

        private static string NormalizeDirection(string direction)
        {
            if (string.IsNullOrWhiteSpace(direction))
            {
                return "In";
            }

            string value = direction.Trim();
            if (string.Equals(value, "In", StringComparison.OrdinalIgnoreCase))
            {
                return "In";
            }

            if (string.Equals(value, "Out", StringComparison.OrdinalIgnoreCase))
            {
                return "Out";
            }

            return string.Equals(value, "InOut", StringComparison.OrdinalIgnoreCase) ? "InOut" : null;
        }

        /// <summary>Дубль имени класса: файл на диске или уже объявленный элемент проекта.</summary>
        private static string CheckDuplicates(XDocument project, XNamespace ns, string projectDirectory, string className, bool withDesigner)
        {
            var names = new List<string> { className + ".cs" };
            if (withDesigner)
            {
                names.Add(className + "Designer.xaml");
                names.Add(className + "Designer.xaml.cs");
            }

            foreach (string name in names)
            {
                if (File.Exists(Path.Combine(projectDirectory, name)))
                {
                    return "файл " + name + " уже существует в папке проекта — выберите другое имя класса";
                }
            }

            string[] declared = project.Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "Compile", StringComparison.Ordinal)
                                  || string.Equals(element.Name.LocalName, "Page", StringComparison.Ordinal)
                                  || string.Equals(element.Name.LocalName, "None", StringComparison.Ordinal))
                .Select(element => (string)element.Attribute("Include"))
                .Where(include => !string.IsNullOrWhiteSpace(include))
                .Select(include => Path.GetFileName(include.Replace('\\', Path.DirectorySeparatorChar)))
                .ToArray();

            foreach (string name in names)
            {
                if (declared.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    return "элемент " + name + " уже объявлен в проекте — выберите другое имя класса";
                }
            }

            return null;
        }

        // ------------------------------------------------------------------
        // Файлы
        // ------------------------------------------------------------------

        private static void WriteClassFile(
            string path,
            string projectNamespace,
            string className,
            string baseType,
            ActivityCreateRequest request,
            IReadOnlyList<ActivityCreateArgument> arguments)
        {
            var text = new StringBuilder();
            text.Append("using System;").Append("\r\n");
            text.Append("using System.Collections.Generic;").Append("\r\n");
            text.Append("using System.Activities;").Append("\r\n");
            text.Append("using System.ComponentModel;").Append("\r\n");
            text.Append("\r\n");
            text.Append("namespace ").Append(projectNamespace).Append("\r\n");
            text.Append("{").Append("\r\n");

            if (!string.IsNullOrWhiteSpace(request.Description))
            {
                text.Append("    [Description(\"").Append(Escape(request.Description)).Append("\")]").Append("\r\n");
            }

            text.Append("    public sealed class ").Append(className).Append(" : ").Append(baseType).Append("\r\n");
            text.Append("    {").Append("\r\n");

            foreach (ActivityCreateArgument argument in arguments)
            {
                if (argument.Required)
                {
                    text.Append("        [RequiredArgument]").Append("\r\n");
                }

                string category = string.IsNullOrWhiteSpace(argument.Category) ? request.Category : argument.Category;
                if (!string.IsNullOrWhiteSpace(category))
                {
                    text.Append("        [Category(\"").Append(Escape(category)).Append("\")]").Append("\r\n");
                }

                if (!string.IsNullOrWhiteSpace(argument.Description))
                {
                    text.Append("        [Description(\"").Append(Escape(argument.Description)).Append("\")]").Append("\r\n");
                }

                text.Append("        public ").Append(argument.Direction).Append("Argument<").Append(argument.TypeName)
                    .Append("> ").Append(argument.Name).Append(" { get; set; }").Append("\r\n");
                text.Append("\r\n");
            }

            switch (baseType)
            {
                case AsyncCodeActivityBase:
                    text.Append("        protected override IAsyncResult BeginExecute(AsyncCodeActivityContext context, AsyncCallback callback, object state)").Append("\r\n");
                    text.Append("        {").Append("\r\n");
                    text.Append("            // TODO: реализация активности (асинхронная операция).").Append("\r\n");
                    text.Append("            return callback?.BeginInvoke(null, null);").Append("\r\n");
                    text.Append("        }").Append("\r\n");
                    text.Append("\r\n");
                    text.Append("        protected override void EndExecute(AsyncCodeActivityContext context, IAsyncResult result)").Append("\r\n");
                    text.Append("        {").Append("\r\n");
                    text.Append("            (result as AsyncCallback)?.EndInvoke(result);").Append("\r\n");
                    text.Append("        }").Append("\r\n");
                    break;

                case NativeActivityBase:
                    text.Append("        protected override void Execute(NativeActivityContext context)").Append("\r\n");
                    text.Append("        {").Append("\r\n");
                    text.Append("            // TODO: реализация активности.").Append("\r\n");
                    text.Append("        }").Append("\r\n");
                    break;

                default:
                    text.Append("        protected override void Execute(CodeActivityContext context)").Append("\r\n");
                    text.Append("        {").Append("\r\n");
                    text.Append("            // TODO: реализация активности.").Append("\r\n");
                    text.Append("        }").Append("\r\n");
                    break;
            }

            text.Append("    }").Append("\r\n");
            text.Append("}").Append("\r\n");

            WriteTextWithoutBom(path, text.ToString());
        }

        private static void WriteDesignerFile(
            string path,
            string projectNamespace,
            string className,
            ActivityCreateRequest request,
            IReadOnlyList<ActivityCreateArgument> arguments)
        {
            var text = new StringBuilder();
            text.Append("<sap:ActivityDesigner x:Class=\"").Append(projectNamespace).Append(".").Append(className).Append("Designer\"").Append("\r\n");
            text.Append("                      xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"").Append("\r\n");
            text.Append("                      xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"").Append("\r\n");
            text.Append("                      xmlns:s=\"clr-namespace:System;assembly=mscorlib\"").Append("\r\n");
            text.Append("                      xmlns:sap=\"clr-namespace:System.Activities.Presentation;assembly=System.Activities.Presentation\"").Append("\r\n");
            text.Append("                      xmlns:sapv=\"clr-namespace:System.Activities.Presentation.View;assembly=System.Activities.Presentation\"").Append("\r\n");
            text.Append("                      xmlns:sapc=\"clr-namespace:System.Activities.Presentation.Converters;assembly=System.Activities.Presentation\">").Append("\r\n");
            text.Append("    <sap:ActivityDesigner.Resources>").Append("\r\n");
            text.Append("        <ResourceDictionary>").Append("\r\n");
            text.Append("            <sapc:ArgumentToExpressionConverter x:Key=\"ArgumentToExpressionConverter\" />").Append("\r\n");
            text.Append("        </ResourceDictionary>").Append("\r\n");
            text.Append("    </sap:ActivityDesigner.Resources>").Append("\r\n");
            text.Append("    <StackPanel Margin=\"4\">").Append("\r\n");
            text.Append("        <TextBlock FontWeight=\"Bold\" Text=\"").Append(Escape(className)).Append("\" />").Append("\r\n");

            if (!string.IsNullOrWhiteSpace(request.Description))
            {
                text.Append("        <TextBlock Text=\"").Append(Escape(request.Description)).Append("\" />").Append("\r\n");
            }

            foreach (ActivityCreateArgument argument in arguments)
            {
                text.Append("\r\n");
                text.Append("        <TextBlock Text=\"").Append(Escape(argument.Name)).Append("\" />").Append("\r\n");
                text.Append("        <sapv:ExpressionTextBox Expression=\"{Binding Path=ModelItem.").Append(argument.Name)
                    .Append(", Mode=TwoWay, Converter={StaticResource ArgumentToExpressionConverter}, ConverterParameter=")
                    .Append(argument.Direction).Append("}\"").Append("\r\n");
                text.Append("                                  ExpressionType=\"s:").Append(argument.TypeName).Append("\"").Append("\r\n");
                text.Append("                                  OwnerActivity=\"{Binding Path=ModelItem}\"").Append("\r\n");
                text.Append("                                  MaxLines=\"1\" />").Append("\r\n");
            }

            text.Append("    </StackPanel>").Append("\r\n");
            text.Append("</sap:ActivityDesigner>").Append("\r\n");

            WriteTextWithoutBom(path, text.ToString());
        }

        private static void WriteDesignerCodeFile(string path, string projectNamespace, string className)
        {
            var text = new StringBuilder();
            text.Append("namespace ").Append(projectNamespace).Append("\r\n");
            text.Append("{").Append("\r\n");
            text.Append("    /// <summary>Code-behind дизайнера активности <see cref=\"").Append(className).Append("\"/>.</summary>").Append("\r\n");
            text.Append("    public partial class ").Append(className).Append("Designer").Append("\r\n");
            text.Append("    {").Append("\r\n");
            text.Append("        public ").Append(className).Append("Designer()").Append("\r\n");
            text.Append("        {").Append("\r\n");
            text.Append("            InitializeComponent();").Append("\r\n");
            text.Append("        }").Append("\r\n");
            text.Append("    }").Append("\r\n");
            text.Append("}").Append("\r\n");

            WriteTextWithoutBom(path, text.ToString());
        }

        private static void WriteTextWithoutBom(string path, string content)
        {
            File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        private static string Escape(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", " ")
                .Replace("\n", " ");
        }

        // ------------------------------------------------------------------
        // Регистрация в .csproj
        // ------------------------------------------------------------------

        /// <summary>
        /// Регистрирует файлы в <c>.csproj</b> текстовой вставкой, а не перезаписью документа: правка
        /// должна менять несколько строк, а не форматирование всего проекта (BOM, переводы строк и
        /// отступы остаются как были). Результат обязательно разбирается как XML — иначе откат.
        /// </summary>
        private static void RegisterInProject(string projectPath, string className, bool withDesigner)
        {
            byte[] bytes = File.ReadAllBytes(projectPath);
            bool hadBom = HasUtf8Bom(bytes);

            string original = new UTF8Encoding(false).GetString(hadBom ? bytes.Skip(3).ToArray() : bytes);
            string eol = original.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";

            List<string> lines = original.Replace("\r\n", "\n").Split('\n').ToList();

            InsertItem(lines, "Compile", className + ".cs", null);

            if (withDesigner)
            {
                InsertItem(lines, "Compile", className + "Designer.xaml.cs", className + "Designer.xaml");
                InsertItem(lines, "Page", className + "Designer.xaml", null, new[]
                {
                    new KeyValuePair<string, string>("SubType", "Designer"),
                    new KeyValuePair<string, string>("Generator", "MSBuild:Compile")
                });
            }

            string result = string.Join(eol, lines);

            // Проверка «файл проекта остался проектом»: битый XML вернул бы студию к нерабочему .csproj.
            XDocument.Parse(result);

            File.WriteAllText(projectPath, result, new UTF8Encoding(hadBom));
        }

        private static void InsertItem(
            List<string> lines,
            string elementName,
            string include,
            string dependentUpon,
            IEnumerable<KeyValuePair<string, string>> children = null)
        {
            List<Tuple<int, int>> groups = ItemGroups(lines);

            Tuple<int, int> target = groups.FirstOrDefault(group => GroupHasElement(lines, group, elementName));
            if (target == null)
            {
                target = CreateItemGroup(lines);
            }

            string itemIndent = ItemIndent(lines, target, elementName) ?? GroupIndent(lines[target.Item1]) + "  ";

            var block = new List<string>
            {
                itemIndent + "<" + elementName + " Include=\"" + include + "\"" + (children == null && dependentUpon == null ? " />" : ">")
            };

            if (dependentUpon != null)
            {
                block.Add(itemIndent + "  <DependentUpon>" + dependentUpon + "</DependentUpon>");
            }

            if (children != null)
            {
                foreach (KeyValuePair<string, string> child in children)
                {
                    block.Add(itemIndent + "  <" + child.Key + ">" + child.Value + "</" + child.Key + ">");
                }
            }

            if (!block[0].EndsWith("/>", StringComparison.Ordinal))
            {
                block.Add(itemIndent + "</" + elementName + ">");
            }

            int insertAt = target.Item2;
            foreach (int start in ItemStarts(lines, target, elementName))
            {
                string existingInclude = IncludeOf(lines[start]);
                if (existingInclude != null && string.Compare(existingInclude, include, StringComparison.OrdinalIgnoreCase) > 0)
                {
                    insertAt = start;
                    break;
                }
            }

            lines.InsertRange(insertAt, block);
        }

        private static List<Tuple<int, int>> ItemGroups(List<string> lines)
        {
            var groups = new List<Tuple<int, int>>();
            int start = -1;

            for (int i = 0; i < lines.Count; i++)
            {
                string trimmed = lines[i].Trim();
                if (trimmed.StartsWith("<ItemGroup", StringComparison.OrdinalIgnoreCase))
                {
                    start = i;
                    continue;
                }

                if (start >= 0 && trimmed.StartsWith("</ItemGroup", StringComparison.OrdinalIgnoreCase))
                {
                    groups.Add(Tuple.Create(start, i));
                    start = -1;
                }
            }

            return groups;
        }

        private static bool GroupHasElement(List<string> lines, Tuple<int, int> group, string elementName)
        {
            for (int i = group.Item1 + 1; i < group.Item2; i++)
            {
                if (StartsWithElement(lines[i], elementName))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool StartsWithElement(string line, string elementName)
        {
            string trimmed = line.Trim();
            string prefix = "<" + elementName;
            if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (trimmed.Length == prefix.Length)
            {
                return true;
            }

            char next = trimmed[prefix.Length];
            return next == ' ' || next == '>' || next == '/' || next == '\t';
        }

        private static IEnumerable<int> ItemStarts(List<string> lines, Tuple<int, int> group, string elementName)
        {
            for (int i = group.Item1 + 1; i < group.Item2; i++)
            {
                if (StartsWithElement(lines[i], elementName)
                    && !lines[i].Trim().StartsWith("</", StringComparison.Ordinal))
                {
                    yield return i;
                }
            }
        }

        private static string IncludeOf(string line)
        {
            int start = line.IndexOf("Include=\"", StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return null;
            }

            start += "Include=\"".Length;
            int end = line.IndexOf('"', start);
            return end < 0 ? null : line.Substring(start, end - start);
        }

        private static string ItemIndent(List<string> lines, Tuple<int, int> group, string elementName)
        {
            foreach (int start in ItemStarts(lines, group, elementName))
            {
                return LeadingWhitespace(lines[start]);
            }

            return null;
        }

        private static Tuple<int, int> CreateItemGroup(List<string> lines)
        {
            List<Tuple<int, int>> groups = ItemGroups(lines);
            Tuple<int, int> last = groups.LastOrDefault();
            if (last == null)
            {
                // Групп нет вовсе: вставляем перед первым <Import .../>, иначе в конец проекта.
                int import = lines.FindIndex(line => line.Trim().Equals("</Project>", StringComparison.OrdinalIgnoreCase));
                int at = import >= 0 ? import : Math.Max(0, lines.Count - 1);
                lines.InsertRange(at, new[] { "  <ItemGroup>", "  </ItemGroup>" });
                return Tuple.Create(at, at + 1);
            }

            string indent = GroupIndent(lines[last.Item1]);
            lines.InsertRange(last.Item2 + 1, new[] { indent + "<ItemGroup>", indent + "</ItemGroup>" });
            return Tuple.Create(last.Item2 + 1, last.Item2 + 2);
        }

        private static string GroupIndent(string line)
        {
            string indent = LeadingWhitespace(line);
            return string.IsNullOrEmpty(indent) ? "  " : indent;
        }

        private static string LeadingWhitespace(string line)
        {
            int index = 0;
            while (index < line.Length && (line[index] == ' ' || line[index] == '\t'))
            {
                index++;
            }

            return line.Substring(0, index);
        }

        private static bool HasUtf8Bom(byte[] bytes)
        {
            return bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        }

        // ------------------------------------------------------------------
        // Сборка и откат
        // ------------------------------------------------------------------

        private static IEnumerable<string> DescribeErrors(IReadOnlyList<BuildError> errors)
        {
            if (errors == null)
            {
                yield break;
            }

            foreach (BuildError error in errors)
            {
                if (error == null || !string.Equals(error.Severity, "Error", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var text = new StringBuilder();
                text.Append(error.Code ?? "error").Append(": ");
                if (!string.IsNullOrWhiteSpace(error.File))
                {
                    text.Append(Path.GetFileName(error.File));
                    if (error.Line > 0)
                    {
                        text.Append("(").Append(error.Line).Append(")");
                    }

                    text.Append(": ");
                }

                text.Append(error.Message);
                yield return text.ToString();
            }
        }

        /// <summary>
        /// Ожидаемый путь собранной сборки: <c>OutputPath</c> нужной конфигурации плюс <c>AssemblyName</c>.
        /// <c>null</c> — файла нет; это не «сборка не прошла», но и не «сборка на месте».
        /// </summary>
        private static string ResolveAssemblyPath(string projectPath, string configuration)
        {
            XDocument project;
            try
            {
                project = XDocument.Parse(File.ReadAllText(projectPath));
            }
            catch
            {
                return null;
            }

            XNamespace ns = project.Root?.Name.Namespace ?? XNamespace.None;
            string outputPath = OutputPathFor(project, ns, configuration);
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                outputPath = Path.Combine("bin", configuration);
            }

            outputPath = outputPath.Trim()
                .Replace("$(Configuration)", configuration)
                .Replace("$(ConfigurationName)", configuration);

            if (outputPath.IndexOf("$(", StringComparison.Ordinal) >= 0)
            {
                outputPath = Path.Combine("bin", configuration);
            }

            outputPath = outputPath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

            string assemblyName = PropertyValue(project, ns, "AssemblyName");
            if (string.IsNullOrWhiteSpace(assemblyName))
            {
                assemblyName = Path.GetFileNameWithoutExtension(projectPath);
            }

            string directory = Path.GetDirectoryName(Path.GetFullPath(projectPath));
            string candidate = Path.GetFullPath(Path.Combine(directory, outputPath, assemblyName.Trim() + ".dll"));

            return File.Exists(candidate) ? candidate : null;
        }

        private static string OutputPathFor(XDocument project, XNamespace ns, string configuration)
        {
            string fallback = null;

            foreach (XElement group in project.Descendants(ns + "PropertyGroup"))
            {
                XElement outputPath = group.Element(ns + "OutputPath");
                if (outputPath == null)
                {
                    continue;
                }

                string condition = (string)group.Attribute("Condition");
                if (string.IsNullOrWhiteSpace(condition))
                {
                    fallback = outputPath.Value;
                    continue;
                }

                if (condition.IndexOf(configuration, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return outputPath.Value;
                }
            }

            return fallback;
        }

        private static string PropertyValue(XDocument project, XNamespace ns, string name)
        {
            XElement element = project.Descendants(ns + name).FirstOrDefault();
            return element?.Value;
        }

        /// <summary>Возврат к исходному состоянию: удалить созданные файлы и восстановить <c>.csproj</c>.</summary>
        private static void Rollback(string projectPath, byte[] projectSnapshot, IEnumerable<string> createdFiles)
        {
            foreach (string file in createdFiles ?? Enumerable.Empty<string>())
            {
                try
                {
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                    }
                }
                catch (IOException)
                {
                    // Файл занят: откат остального важнее одной неудачи.
                }
            }

            try
            {
                File.WriteAllBytes(projectPath, projectSnapshot);
            }
            catch (IOException)
            {
                // см. выше
            }
        }
    }
}
