using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace RPA.Orchestrator.Contracts.Packages
{
    /// <summary>
    /// Общие правила «как устроен проект робота» (`OR-03`): имя сборки, целевая платформа,
    /// каталоги <c>bin</c>, точка входа и аргументы из XAML.
    /// <para>
    /// Знания вынесены сюда не ради красоты: их используют <b>двое</b> — упаковщик
    /// (<c>Rpa.RobotPackaging</c>), который кладёт сборку в пакет, и раннер
    /// (<c>RobotLocator</c>), который ищет собранного робота. Если бы они разошлись, получилось бы
    /// «пакет собран из <c>bin\Release\net472</c>, а прогон ищет <c>bin\Debug</c>» — и виноват был бы
    /// не робот, а два списка каталогов в двух местах.
    /// </para>
    /// </summary>
    public static class RobotProjectLayout
    {
        /// <summary>Имя файла сценария по умолчанию внутри проекта робота.</summary>
        public const string DefaultWorkflowFileName = "Main.xaml";

        /// <summary>Пространство имён XAML.</summary>
        public static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

        /// <summary>Конфигурации сборки в порядке предпочтения.</summary>
        public static IReadOnlyList<string> Configurations { get; } = new[] { "Debug", "Release" };

        /// <summary>Платформы сборки в порядке предпочтения (пустая — <c>bin\&lt;Config&gt;</c>).</summary>
        public static IReadOnlyList<string> Platforms { get; } = new[] { string.Empty, "x64", "x86" };

        /// <summary>
        /// Каталоги <c>bin\…</c>, в которых MSBuild и студия кладут сборку робота:
        /// <c>bin\&lt;Config&gt;</c>, <c>bin\&lt;Config&gt;\&lt;TFM&gt;</c>,
        /// <c>bin\&lt;Platform&gt;\&lt;Config&gt;</c>, <c>bin\&lt;Platform&gt;\&lt;Config&gt;\&lt;TFM&gt;</c>.
        /// Порядок фиксирован и проверяется тестами: локальный и упаковочный поиск должны совпадать.
        /// </summary>
        public static List<string> CandidateDirectories(string projectDirectory, IEnumerable<string> targetFrameworks)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var frameworks = new List<string> { string.Empty };

            if (targetFrameworks != null)
            {
                foreach (string framework in targetFrameworks)
                {
                    if (!string.IsNullOrWhiteSpace(framework)
                        && !frameworks.Contains(framework, StringComparer.OrdinalIgnoreCase))
                    {
                        frameworks.Add(framework);
                    }
                }
            }

            foreach (string configuration in Configurations)
            {
                foreach (string platform in Platforms)
                {
                    foreach (string framework in frameworks)
                    {
                        var parts = new List<string> { projectDirectory, "bin" };

                        if (!string.IsNullOrEmpty(platform))
                        {
                            parts.Add(platform);
                        }

                        parts.Add(configuration);

                        if (!string.IsNullOrEmpty(framework))
                        {
                            parts.Add(framework);
                        }

                        string directory = Path.Combine(parts.ToArray());

                        if (seen.Add(directory))
                        {
                            result.Add(directory);
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>Читает <c>csproj</c>; <c>null</c> — файл не читается (ошибка в <paramref name="error"/>).</summary>
        public static XDocument LoadProject(string projectPath, out string error)
        {
            error = null;

            try
            {
                return XDocument.Load(projectPath);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        /// <summary>Значение свойства MSBuild из <c>csproj</c> (последнее непустое объявление выигрывает).</summary>
        public static string ReadProperty(XDocument document, string name)
        {
            if (document?.Root == null)
            {
                return null;
            }

            string value = document
                .Descendants()
                .Where(e => string.Equals(e.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Value)
                .LastOrDefault(v => !string.IsNullOrWhiteSpace(v));

            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>
        /// Имя сборки робота: <c>AssemblyName</c>, иначе <c>RootNamespace</c>, иначе имя файла проекта.
        /// </summary>
        public static string ReadAssemblyName(XDocument document, string projectPath)
        {
            return ReadProperty(document, "AssemblyName")
                ?? ReadProperty(document, "RootNamespace")
                ?? Path.GetFileNameWithoutExtension(projectPath);
        }

        /// <summary>Целевые платформы проекта в нормализованном виде (<c>v4.7.2</c> → <c>net472</c>).</summary>
        public static List<string> ReadTargetFrameworks(XDocument document)
        {
            var result = new List<string>();

            if (document?.Root == null)
            {
                return result;
            }

            IEnumerable<string> raw = document
                .Descendants()
                .Where(e => e.Name.LocalName == "TargetFramework" || e.Name.LocalName == "TargetFrameworks")
                .Select(e => e.Value);

            foreach (string value in raw)
            {
                AddFrameworks(result, value);
            }

            string legacy = document
                .Descendants()
                .Where(e => e.Name.LocalName == "TargetFrameworkVersion")
                .Select(e => e.Value)
                .LastOrDefault(v => !string.IsNullOrWhiteSpace(v));

            AddFrameworks(result, legacy);

            return result;
        }

        /// <summary>Версия пакета по умолчанию: <c>Version</c>, <c>FileVersion</c>, <c>AssemblyVersion</c>, иначе <c>1.0.0</c>.</summary>
        public static string ReadVersion(XDocument document)
        {
            string[] candidates = { "Version", "FileVersion", "AssemblyVersion" };

            foreach (string name in candidates)
            {
                string value = ReadProperty(document, name);

                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return "1.0.0";
        }

        /// <summary><c>v4.7.2</c> → <c>net472</c>; <c>net472</c> остаётся как есть.</summary>
        public static string NormalizeFramework(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            string text = value.Trim();

            if (text.StartsWith("net", StringComparison.OrdinalIgnoreCase)
                && !text.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                return text.ToLowerInvariant();
            }

            if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                && Version.TryParse(text.Substring(1), out Version version))
            {
                var framework = new StringBuilder("net");
                framework.Append(version.Major.ToString(CultureInfo.InvariantCulture));
                framework.Append(version.Minor.ToString(CultureInfo.InvariantCulture));

                // v4.8 — это два числа, а не три: у Version третий компонент равен -1,
                // и без проверки получался каталог «net48-1», которого не бывает.
                if (version.Build >= 0)
                {
                    framework.Append(version.Build.ToString(CultureInfo.InvariantCulture));
                }

                return framework.ToString();
            }

            return null;
        }

        /// <summary>Атрибут <c>x:Class</c> корневого элемента XAML — имя типа активности.</summary>
        public static string ReadEntryType(string xamlPath)
        {
            try
            {
                XDocument xaml = XDocument.Load(xamlPath);

                return xaml.Root?.Attribute(XamlNamespace + "Class")?.Value?.Trim();
            }
            catch (Exception)
            {
                // Битый XAML — не беда разбора проекта: тип просто не определён, а настоящая
                // ошибка загрузки будет показана на шаге прогона с полным стеком.
                return null;
            }
        }

        /// <summary>
        /// Аргументы робота из блока <c>x:Members</c>: имя, тип, направление и значение по умолчанию.
        /// Поддерживаются обе формы записи — атрибутом (<c>Type="InArgument(x:String)"</c>) и
        /// вложенным элементом (<c>&lt;InArgument x:TypeArguments="x:String"&gt;</c>).
        /// </summary>
        public static List<RobotPackageArgument> ReadArguments(string xamlPath)
        {
            var result = new List<RobotPackageArgument>();

            XDocument xaml;

            try
            {
                xaml = XDocument.Load(xamlPath);
            }
            catch (Exception)
            {
                return result;
            }

            foreach (XElement property in xaml.Descendants(XamlNamespace + "Property"))
            {
                string name = property.Attribute("Name")?.Value?.Trim();

                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                string direction;
                string typeName;
                string defaultValue;

                string declared = property.Attribute("Type")?.Value;

                if (!string.IsNullOrWhiteSpace(declared))
                {
                    ParseArgumentDeclaration(declared, out direction, out typeName);
                    defaultValue = ReadElementText(property);
                }
                else
                {
                    // Вложенная форма: <x:Property Name="X"><InArgument x:TypeArguments="x:String"/></x:Property>
                    XElement wrapper = property.Elements().FirstOrDefault();
                    direction = NormalizeDirection(wrapper?.Name.LocalName);
                    typeName = MapClrType(wrapper?.Attribute(XamlNamespace + "TypeArguments")?.Value);
                    defaultValue = ReadElementText(wrapper);
                }

                result.Add(new RobotPackageArgument
                {
                    Name = name,
                    Type = string.IsNullOrWhiteSpace(typeName) ? "System.Object" : typeName,
                    Direction = direction,
                    DefaultValue = defaultValue,
                    Required = direction == "In" && string.IsNullOrEmpty(defaultValue)
                });
            }

            return result;
        }

        /// <summary>Разбирает объявление вида <c>InArgument(x:String)</c>.</summary>
        public static void ParseArgumentDeclaration(string declared, out string direction, out string typeName)
        {
            direction = "In";
            typeName = null;

            string text = (declared ?? string.Empty).Trim();

            if (text.Length == 0)
            {
                return;
            }

            int open = text.IndexOf('(');
            string wrapper = open < 0 ? text : text.Substring(0, open);
            direction = NormalizeDirection(wrapper);

            if (open >= 0)
            {
                int close = text.LastIndexOf(')');
                string inner = close > open ? text.Substring(open + 1, close - open - 1) : text.Substring(open + 1);

                typeName = MapClrType(inner);
            }
        }

        /// <summary><c>InArgument</c> → <c>In</c>, <c>OutArgument</c> → <c>Out</c>, <c>InOutArgument</c> → <c>InOut</c>.</summary>
        public static string NormalizeDirection(string wrapper)
        {
            switch ((wrapper ?? string.Empty).Trim())
            {
                case "OutArgument":
                case "DelegateOutArgument":
                    return "Out";
                case "InOutArgument":
                    return "InOut";
                default:
                    return "In";
            }
        }

        /// <summary>
        /// <c>x:String</c> → <c>System.String</c>. Сложные типы (<c>scg:List(x:String)</c>) сохраняются
        /// как объявлены: превращать их в CLR-имя без компилятора — значит врать в манифесте.
        /// </summary>
        public static string MapClrType(string xamlType)
        {
            if (string.IsNullOrWhiteSpace(xamlType))
            {
                return null;
            }

            string text = xamlType.Trim();

            if (text.IndexOf('(') >= 0)
            {
                return text;
            }

            int colon = text.IndexOf(':');
            string name = colon >= 0 ? text.Substring(colon + 1) : text;

            switch (name)
            {
                case "String":
                    return "System.String";
                case "Int32":
                    return "System.Int32";
                case "Int64":
                    return "System.Int64";
                case "Int16":
                    return "System.Int16";
                case "Boolean":
                    return "System.Boolean";
                case "Double":
                    return "System.Double";
                case "Single":
                    return "System.Single";
                case "Decimal":
                    return "System.Decimal";
                case "Object":
                    return "System.Object";
                case "DateTime":
                    return "System.DateTime";
                case "TimeSpan":
                    return "System.TimeSpan";
                case "Guid":
                    return "System.Guid";
                case "Char":
                    return "System.Char";
                case "Byte":
                    return "System.Byte";
                default:
                    return text;
            }
        }

        private static void AddFrameworks(List<string> result, string value)
        {
            foreach (string part in (value ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string normalized = NormalizeFramework(part.Trim());

                if (!string.IsNullOrWhiteSpace(normalized)
                    && !result.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(normalized);
                }
            }
        }

        private static string ReadElementText(XElement element)
        {
            if (element == null)
            {
                return null;
            }

            string text = element.Value;

            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
    }
}
