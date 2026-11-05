using System;
using System.Collections.Generic;
using System.Linq;

namespace RPA.Chat.Services.Workflow
{
    /// <summary>
    /// Один аргумент создаваемой активности (карточка <c>AG-08</c>): имя, тип, направление и признаки
    /// из атрибутов WF. Направление задаётся словом <c>In</c>/<c>Out</c>/<c>InOut</c> — так же, как его
    /// показывает <c>activity_describe</c>, чтобы агенту не приходилось переводить одно в другое.
    /// </summary>
    public sealed class ActivityCreateArgument
    {
        /// <summary>Имя аргумента (идентификатор C#).</summary>
        public string Name { get; set; }

        /// <summary>Имя типа без пространства имён: <c>String</c>, <c>Int32</c>. По умолчанию <c>String</c>.</summary>
        public string TypeName { get; set; }

        /// <summary>Направление: <c>In</c> (по умолчанию), <c>Out</c>, <c>InOut</c>.</summary>
        public string Direction { get; set; }

        /// <summary>Аргумент обязателен (атрибут <c>[RequiredArgument]</c>).</summary>
        public bool Required { get; set; }

        /// <summary>Описание для панели «Свойства».</summary>
        public string Description { get; set; }

        /// <summary>Категория свойства в панели «Свойства» (например, «Общие»).</summary>
        public string Category { get; set; }

        public override string ToString()
        {
            return (Direction ?? "In") + " " + (TypeName ?? "String") + " " + (Name ?? "?");
        }
    }

    /// <summary>
    /// Запрос «создай активность в существующем проекте библиотеки активностей» (карточка <c>AG-08</c>).
    /// <para>
    /// Создаём <b>только в существующем</b> проекте (решение владельца): проект с нуля из шаблона
    /// Visual Studio остаётся за карточкой <c>UI-08</c>. Поэтому единственный обязательный путь —
    /// <see cref="ProjectPath"/>, файл <c>.csproj</c>; папка проекта и остальные настройки выводятся из него.
    /// </para>
    /// </summary>
    public sealed class ActivityCreateRequest
    {
        /// <summary>Путь к файлу проекта библиотеки активностей (<c>.csproj</c>).</summary>
        public string ProjectPath { get; set; }

        /// <summary>Имя класса активности (идентификатор C#).</summary>
        public string ClassName { get; set; }

        /// <summary>Пространство имён; пусто — <c>RootNamespace</c> проекта.</summary>
        public string Namespace { get; set; }

        /// <summary>Описание активности (атрибут <c>[Description]</c>).</summary>
        public string Description { get; set; }

        /// <summary>Категория свойств по умолчанию (атрибут <c>[Category]</c>).</summary>
        public string Category { get; set; }

        /// <summary>Базовый тип: <c>CodeActivity</c> (по умолчанию), <c>AsyncCodeActivity</c>, <c>NativeActivity</c>.</summary>
        public string BaseType { get; set; }

        /// <summary>Создавать дизайнер (<c>&lt;Имя&gt;Designer.xaml</c> + code-behind).</summary>
        public bool WithDesigner { get; set; } = true;

        /// <summary>Собрать проект после создания (через <c>IBuildFeedbackService</c>).</summary>
        public bool Build { get; set; }

        /// <summary>Конфигурация сборки; по умолчанию <c>Debug</c>.</summary>
        public string Configuration { get; set; }

        /// <summary>Аргументы активности.</summary>
        public List<ActivityCreateArgument> Arguments { get; } = new List<ActivityCreateArgument>();

        public override string ToString()
        {
            return (ProjectPath ?? "?") + ": " + (ClassName ?? "?");
        }
    }

    /// <summary>
    /// Результат создания активности. Три вещи названы явно, чтобы агент не догадывался:
    /// что создано/изменено, есть ли собранная сборка (<see cref="AssemblyPath"/> — только после
    /// успешной сборки) и что осталось на решение человека (<see cref="RequiresDecision"/>).
    /// </summary>
    public sealed class ActivityCreateResult
    {
        private static readonly string[] Empty = new string[0];

        private ActivityCreateResult(
            bool succeeded,
            string message,
            string fullTypeName,
            string classFile,
            string designerFile,
            string assemblyPath,
            IReadOnlyList<string> createdFiles,
            IReadOnlyList<string> modifiedFiles,
            IReadOnlyList<string> requiresDecision,
            IReadOnlyList<string> buildErrors,
            bool buildPerformed,
            string configuration)
        {
            Succeeded = succeeded;
            Message = message;
            FullTypeName = fullTypeName;
            ClassFile = classFile;
            DesignerFile = designerFile;
            AssemblyPath = assemblyPath;
            CreatedFiles = createdFiles ?? Empty;
            ModifiedFiles = modifiedFiles ?? Empty;
            RequiresDecision = requiresDecision ?? Empty;
            BuildErrors = buildErrors ?? Empty;
            BuildPerformed = buildPerformed;
            Configuration = configuration;
        }

        /// <summary>Активность создана и (если просили) собрана.</summary>
        public bool Succeeded { get; }

        /// <summary>Человеческое объяснение: причина отказа либо краткая сводка.</summary>
        public string Message { get; }

        /// <summary>Полное имя созданного типа (<c>TestLibrary.SayHello</c>); <c>null</c> при отказе.</summary>
        public string FullTypeName { get; }

        /// <summary>Путь к файлу класса активности.</summary>
        public string ClassFile { get; }

        /// <summary>Путь к файлу дизайнера; <c>null</c>, если дизайнер не создавался.</summary>
        public string DesignerFile { get; }

        /// <summary>
        /// Путь к собранной сборке. Заполняется только после успешной сборки и только если файл
        /// действительно найден: «сборка прошла» и «сборка лежит здесь» — разные утверждения.
        /// </summary>
        public string AssemblyPath { get; }

        /// <summary>Созданные файлы (абсолютные пути).</summary>
        public IReadOnlyList<string> CreatedFiles { get; }

        /// <summary>Изменённые файлы (абсолютные пути); сейчас это <c>.csproj</c>.</summary>
        public IReadOnlyList<string> ModifiedFiles { get; }

        /// <summary>Что требует решения человека (например, сборка не запрашивалась или не найдена).</summary>
        public IReadOnlyList<string> RequiresDecision { get; }

        /// <summary>Ошибки сборки в формате «код: файл: строка: сообщение».</summary>
        public IReadOnlyList<string> BuildErrors { get; }

        /// <summary>Сборка выполнялась (не путать с «сборка прошла без ошибок»).</summary>
        public bool BuildPerformed { get; }

        /// <summary>Конфигурация сборки (<c>Debug</c>/<c>Release</c>).</summary>
        public string Configuration { get; }

        /// <summary>Активность создана.</summary>
        public static ActivityCreateResult Ok(
            string fullTypeName,
            string classFile,
            string designerFile,
            string assemblyPath,
            IEnumerable<string> createdFiles,
            IEnumerable<string> modifiedFiles,
            IEnumerable<string> requiresDecision,
            bool buildPerformed,
            string configuration)
        {
            return new ActivityCreateResult(
                true,
                "активность " + fullTypeName + " создана",
                fullTypeName,
                classFile,
                designerFile,
                assemblyPath,
                createdFiles?.ToList(),
                modifiedFiles?.ToList(),
                requiresDecision?.ToList(),
                null,
                buildPerformed,
                configuration);
        }

        /// <summary>Отказ: файлы не создавались либо откат уже выполнен.</summary>
        public static ActivityCreateResult Fail(string message, IEnumerable<string> buildErrors = null)
        {
            return new ActivityCreateResult(
                false,
                string.IsNullOrWhiteSpace(message) ? "активность не создана" : message,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                buildErrors?.ToList(),
                false,
                null);
        }
    }
}
