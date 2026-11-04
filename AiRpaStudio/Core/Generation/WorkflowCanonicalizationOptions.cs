using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RPA.Core.Generation
{
    /// <summary>
    /// Параметры канонизации рабочего процесса (этап 2 плана <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c>).
    /// <para>
    /// Канонический XAML — тот же формат, который сохраняет сама студия (эталон <c>RPA-1\Main.xaml</c>).
    /// Из этих параметров берутся элементы, которые WF-сериализатор сам не придумывает: имя класса
    /// (<c>x:Class</c>), сборка проекта в <c>TextExpression.ReferencesForImplementation</c>, пространства
    /// имён и сборки для C#-выражений, язык редактора выражений.
    /// </para>
    /// </summary>
    internal sealed class WorkflowCanonicalizationOptions
    {
        /// <summary>Язык выражений студии. Другого редактора выражений в студии нет.</summary>
        internal const string CSharpExpressionActivityEditor = "C#";

        /// <summary>
        /// Пространства имён по умолчанию — как в шаблоне рабочего процесса Visual Studio
        /// (<c>RPA-1\Main.xaml</c>): без них новый процесс нельзя редактировать выражениями.
        /// </summary>
        private static readonly string[] DefaultNamespacesValue =
        {
            "System",
            "System.Collections.Generic",
            "System.Data",
            "System.Linq",
            "System.Text"
        };

        /// <summary>
        /// Ссылки по умолчанию — набор шаблона рабочего процесса Visual Studio. Сборка проекта
        /// добавляется отдельно (<see cref="ProjectAssemblyName"/>): без неё типы проекта в конструкторе
        /// не резолвятся и открывается модальное окно «Конструктор рабочих процессов».
        /// </summary>
        private static readonly string[] DefaultReferencesValue =
        {
            "mscorlib",
            "System",
            "System.Activities",
            "System.Core",
            "System.Data",
            "System.Runtime.Serialization",
            "System.ServiceModel",
            "System.ServiceModel.Activities",
            "System.Xaml",
            "System.Xml",
            "System.Xml.Linq",
            "Microsoft.CSharp"
        };

        /// <summary>
        /// Параметры канонизации для процесса, который лежит в проекте <paramref name="projectAssemblyName"/>.
        /// </summary>
        /// <param name="namespaceName">
        /// Пространство имён класса процесса (для <c>RPA-1</c> это <c>RPA_1</c>). Может быть пустым —
        /// тогда <c>x:Class</c> равен имени класса.
        /// </param>
        /// <param name="className">Имя класса процесса (обычно имя файла без расширения, например <c>Main</c>).</param>
        /// <param name="projectAssemblyName">
        /// Имя сборки проекта (например <c>RPA-1</c>). Обязательно: это единственная ссылка, которую
        /// сериализатор не может вывести из объектной модели.
        /// </param>
        /// <exception cref="ArgumentException">Имя класса или сборки проекта не задано либо недопустимо.</exception>
        internal WorkflowCanonicalizationOptions(string namespaceName, string className, string projectAssemblyName)
        {
            if (string.IsNullOrWhiteSpace(className))
            {
                throw new ArgumentException("Имя класса процесса не задано", nameof(className));
            }

            if (!IsValidQualifiedIdentifier(className))
            {
                throw new ArgumentException(
                    $"Имя класса «{className}» не является идентификатором C#: x:Class с таким значением конструктор не откроет",
                    nameof(className));
            }

            if (!string.IsNullOrWhiteSpace(namespaceName) && !IsValidQualifiedIdentifier(namespaceName))
            {
                throw new ArgumentException(
                    $"Пространство имён «{namespaceName}» не является идентификатором C#",
                    nameof(namespaceName));
            }

            if (string.IsNullOrWhiteSpace(projectAssemblyName))
            {
                throw new ArgumentException(
                    "Имя сборки проекта не задано: без него сборка проекта не попадает в " +
                    "TextExpression.ReferencesForImplementation и типы проекта в конструкторе не резолвятся",
                    nameof(projectAssemblyName));
            }

            if (!IsValidAssemblyName(projectAssemblyName))
            {
                throw new ArgumentException(
                    $"«{projectAssemblyName}» не является именем сборки",
                    nameof(projectAssemblyName));
            }

            NamespaceName = namespaceName?.Trim() ?? string.Empty;
            ClassName = className.Trim();
            ProjectAssemblyName = projectAssemblyName.Trim();
        }

        /// <summary>Пространство имён класса процесса (может быть пустым).</summary>
        internal string NamespaceName { get; }

        /// <summary>Имя класса процесса.</summary>
        internal string ClassName { get; }

        /// <summary>Имя сборки проекта — обязательная ссылка в <c>TextExpression.ReferencesForImplementation</c>.</summary>
        internal string ProjectAssemblyName { get; }

        /// <summary>Значение <c>x:Class</c>: «пространство_имён.Класс» (или только класс).</summary>
        internal string QualifiedClassName =>
            string.IsNullOrEmpty(NamespaceName) ? ClassName : NamespaceName + "." + ClassName;

        /// <summary>Язык редактора выражений (<c>sap2010:ExpressionActivityEditor</c>).</summary>
        internal string ExpressionActivityEditor { get; set; } = CSharpExpressionActivityEditor;

        /// <summary>
        /// Дополнительные пространства имён для выражений: то, что нужно конкретному процессу
        /// (например <c>System.Text.RegularExpressions</c>). Дубликаты отбрасываются.
        /// </summary>
        internal IList<string> Namespaces { get; } = new List<string>();

        /// <summary>
        /// Дополнительные ссылки на сборки: сборки активностей студии, библиотек процесса и т. п.
        /// Дубликаты и ссылка на сборку проекта отбрасываются (она добавляется последней, как в эталоне).
        /// </summary>
        internal IList<string> References { get; } = new List<string>();

        /// <summary>Пространства имён шаблона рабочего процесса студии.</summary>
        internal static IReadOnlyList<string> DefaultNamespaces => DefaultNamespacesValue;

        /// <summary>Ссылки на сборки шаблона рабочего процесса студии (без сборки проекта).</summary>
        internal static IReadOnlyList<string> DefaultReferences => DefaultReferencesValue;

        /// <summary>
        /// Параметры для файла процесса <paramref name="filePath"/> в проекте
        /// <paramref name="projectName"/>: пространство имён выводится из имени проекта,
        /// класс — из имени файла (как это делает Visual Studio: <c>RPA-1\Main.xaml</c> → <c>RPA_1.Main</c>).
        /// </summary>
        internal static WorkflowCanonicalizationOptions ForProject(string projectName, string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("Путь к файлу процесса не задан", nameof(filePath));
            }

            return new WorkflowCanonicalizationOptions(
                NamespaceFromProjectName(projectName),
                ClassNameFromFilePath(filePath),
                projectName);
        }

        /// <summary>
        /// Пространство имён по имени проекта: недопустимые в идентификаторе символы заменяются на «_»
        /// (<c>RPA-1</c> → <c>RPA_1</c>). Именно так выглядит <c>x:Class</c> в эталоне.
        /// </summary>
        internal static string NamespaceFromProjectName(string projectName)
        {
            return SanitizeIdentifier(projectName);
        }

        /// <summary>Имя класса по пути файла: имя без расширения, приведённое к идентификатору.</summary>
        internal static string ClassNameFromFilePath(string filePath)
        {
            string name = Path.GetFileNameWithoutExtension(filePath);
            string sanitized = SanitizeIdentifier(name);

            return string.IsNullOrEmpty(sanitized) ? "Main" : sanitized;
        }

        /// <summary>Приводит произвольную строку к идентификатору C# (точки сохраняются как разделители).</summary>
        internal static string SanitizeIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length + 1);
            bool startOfPart = true;

            foreach (char symbol in value.Trim())
            {
                char current = char.IsLetterOrDigit(symbol) || symbol == '_' || symbol == '.'
                    ? symbol
                    : '_';

                if (current == '.')
                {
                    // Точка в начале/конце и две точки подряд — не идентификатор.
                    if (startOfPart || builder.Length == 0)
                    {
                        startOfPart = true;
                        continue;
                    }

                    builder.Append('.');
                    startOfPart = true;
                    continue;
                }

                if (startOfPart && char.IsDigit(current))
                {
                    builder.Append('_');
                }

                builder.Append(current);
                startOfPart = false;
            }

            return builder.ToString().TrimEnd('.');
        }

        /// <summary>Проверка «строка — идентификатор C# (возможно, с точками)».</summary>
        internal static bool IsValidQualifiedIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            foreach (string part in value.Split('.'))
            {
                if (part.Length == 0 || !IsValidIdentifier(part))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsValidIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value) || char.IsDigit(value[0]))
            {
                return false;
            }

            foreach (char symbol in value)
            {
                if (!char.IsLetterOrDigit(symbol) && symbol != '_')
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsValidAssemblyName(string value)
        {
            // Полное имя содержит пробелы после запятых («mscorlib, Version=4.0.0.0, ...»), а простое имя
            // с пробелом — уже не имя сборки («RPA 1»): AssemblyName такое пропускает, и ошибка всплыла бы
            // позже, внутри сериализатора WF, без указания виноватой ссылки.
            if (value.IndexOf(',') < 0 && value.IndexOf(' ') >= 0)
            {
                return false;
            }

            try
            {
                new System.Reflection.AssemblyName(value);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
