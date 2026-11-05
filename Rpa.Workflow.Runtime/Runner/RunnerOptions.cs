using System;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;

namespace RPA.Runner
{
    /// <summary>
    /// Разобранные аргументы командной строки <c>RpaRunner.exe</c> (`OR-02`).
    /// <para>
    /// Разбор отделён от исполнения намеренно: все ошибки командной строки обязаны давать
    /// код <see cref="RunnerExitCode.BadArguments"/> и справку, а не падение внутри прогона.
    /// Поэтому <see cref="TryParse"/> никогда не бросает исключений и проверяет
    /// взаимоисключающие источники (проект / XAML / сборка) до запуска.
    /// </para>
    /// <para>
    /// JSON аргументов разбирается здесь же (сериализатор — <see cref="JavaScriptSerializer"/>
    /// из .NET Framework): невалидный JSON обязан быть ошибкой аргументов, а не ошибкой робота.
    /// Скаляры приводятся к обычным типам CLR: целые — <see cref="int"/>/<see cref="long"/>,
    /// дробные — <see cref="double"/>, логические — <see cref="bool"/>, строки — как есть.
    /// </para>
    /// </summary>
    public sealed class RunnerOptions
    {
        /// <summary>Имя файла сценария по умолчанию внутри проекта робота.</summary>
        public const string DefaultWorkflowFileName = "Main.xaml";

        private RunnerOptions()
        {
            Arguments = new Dictionary<string, object>(StringComparer.Ordinal);
        }

        /// <summary>Путь к <c>*.csproj</c> робота — основной способ указать, что прогонять.</summary>
        public string ProjectPath { get; private set; }

        /// <summary>Путь к XAML-файлу: прогон без сборки проекта.</summary>
        public string WorkflowPath { get; private set; }

        /// <summary>Путь к собранной сборке робота: прогон по сборке и типу.</summary>
        public string AssemblyPath { get; private set; }

        /// <summary>Имя типа активности в сборке; по умолчанию берётся из <c>x:Class</c> XAML.</summary>
        public string TypeName { get; private set; }

        /// <summary>Каталог, из которого грузятся зависимости робота (перекрывает автопоиск).</summary>
        public string BinDirectory { get; private set; }

        /// <summary>Исходный JSON аргументов (как пришёл в командной строке).</summary>
        public string ArgumentsJson { get; private set; }

        /// <summary>Разобранные входные аргументы робота.</summary>
        public IDictionary<string, object> Arguments { get; private set; }

        /// <summary>Идентификатор прогона для лога и отчёта.</summary>
        public string RunId { get; private set; }

        /// <summary>Путь к <c>result.json</c>.</summary>
        public string ResultPath { get; private set; }

        /// <summary>Путь к файлу лога (JSON-lines).</summary>
        public string LogPath { get; private set; }

        /// <summary>Предел прогона; <c>null</c> — предел движка (5 минут).</summary>
        public TimeSpan? Timeout { get; private set; }

        /// <summary>Только загрузить и проверить workflow, не прогоняя его.</summary>
        public bool ValidateOnly { get; private set; }

        /// <summary>Показать справку и выйти успешно.</summary>
        public bool ShowHelp { get; private set; }

        /// <summary>Идентификатор прогона: заданный явно или сгенерированный (<c>yyyyMMdd-HHmmss-xxxxxxxx</c>).</summary>
        public string ResolveRunId()
        {
            if (!string.IsNullOrWhiteSpace(RunId))
            {
                return RunId.Trim();
            }

            return DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
                + "-"
                + Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        /// <summary>Однострочное описание источника — для лога и отчёта.</summary>
        public string DescribeSource()
        {
            if (!string.IsNullOrWhiteSpace(ProjectPath))
            {
                return "--project " + ProjectPath;
            }

            if (!string.IsNullOrWhiteSpace(AssemblyPath))
            {
                return "--assembly " + AssemblyPath;
            }

            if (!string.IsNullOrWhiteSpace(WorkflowPath))
            {
                return "--workflow " + WorkflowPath;
            }

            return "источник не указан";
        }

        /// <summary>
        /// Разбирает командную строку. Возвращает <c>false</c> и текст ошибки, если аргументы
        /// непригодны: вызывающая сторона обязана напечатать справку и вернуть
        /// <see cref="RunnerExitCode.BadArguments"/>.
        /// </summary>
        public static bool TryParse(IList<string> args, out RunnerOptions options, out string error)
        {
            options = new RunnerOptions();
            error = null;

            if (args == null || args.Count == 0)
            {
                error = "не указан источник прогона: --project, --workflow или --assembly";
                return false;
            }

            for (int index = 0; index < args.Count; index++)
            {
                string argument = args[index];

                if (string.IsNullOrWhiteSpace(argument))
                {
                    error = "пустой аргумент командной строки";
                    return false;
                }

                string name = argument.Trim();
                string value;

                switch (name.ToLowerInvariant())
                {
                    case "--help":
                    case "-h":
                    case "-?":
                    case "/?":
                        options.ShowHelp = true;
                        break;

                    case "--validate":
                        options.ValidateOnly = true;
                        break;

                    case "--project":
                        if (!TakeValue(args, ref index, name, out value, out error))
                        {
                            return false;
                        }

                        options.ProjectPath = value;
                        break;

                    case "--workflow":
                        if (!TakeValue(args, ref index, name, out value, out error))
                        {
                            return false;
                        }

                        options.WorkflowPath = value;
                        break;

                    case "--assembly":
                        if (!TakeValue(args, ref index, name, out value, out error))
                        {
                            return false;
                        }

                        options.AssemblyPath = value;
                        break;

                    case "--type":
                        if (!TakeValue(args, ref index, name, out value, out error))
                        {
                            return false;
                        }

                        options.TypeName = value;
                        break;

                    case "--bin":
                        if (!TakeValue(args, ref index, name, out value, out error))
                        {
                            return false;
                        }

                        options.BinDirectory = value;
                        break;

                    case "--args":
                        if (!TakeValue(args, ref index, name, out value, out error))
                        {
                            return false;
                        }

                        if (!options.TryParseArguments(value, out string jsonError))
                        {
                            error = jsonError;
                            return false;
                        }

                        options.ArgumentsJson = value;
                        break;

                    case "--arg":
                        if (!TakeValue(args, ref index, name, out value, out error))
                        {
                            return false;
                        }

                        if (!options.AddNamedArgument(value, out string pairError))
                        {
                            error = pairError;
                            return false;
                        }

                        break;

                    case "--run-id":
                        if (!TakeValue(args, ref index, name, out value, out error))
                        {
                            return false;
                        }

                        options.RunId = value;
                        break;

                    case "--out":
                        if (!TakeValue(args, ref index, name, out value, out error))
                        {
                            return false;
                        }

                        options.ResultPath = value;
                        break;

                    case "--log":
                        if (!TakeValue(args, ref index, name, out value, out error))
                        {
                            return false;
                        }

                        options.LogPath = value;
                        break;

                    case "--timeout":
                        if (!TakeValue(args, ref index, name, out value, out error))
                        {
                            return false;
                        }

                        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds) || seconds <= 0)
                        {
                            error = $"параметр «{name}» ждёт целое число секунд больше нуля, получено «{value}»";
                            return false;
                        }

                        options.Timeout = TimeSpan.FromSeconds(seconds);
                        break;

                    default:
                        error = name.StartsWith("-", StringComparison.Ordinal)
                            ? $"неизвестный параметр «{name}»"
                            : $"лишний параметр «{name}»: значения передаются только через именованные параметры";
                        return false;
                }
            }

            if (options.ShowHelp)
            {
                return true;
            }

            return Validate(options, out error);
        }

        private static bool Validate(RunnerOptions options, out string error)
        {
            error = null;

            int sources = 0;
            sources += string.IsNullOrWhiteSpace(options.ProjectPath) ? 0 : 1;
            sources += string.IsNullOrWhiteSpace(options.WorkflowPath) ? 0 : 1;
            sources += string.IsNullOrWhiteSpace(options.AssemblyPath) ? 0 : 1;

            if (sources == 0)
            {
                error = "не указан источник прогона: --project, --workflow или --assembly";
                return false;
            }

            if (sources > 1)
            {
                error = "источник прогона указывается один раз: --project, --workflow или --assembly";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(options.AssemblyPath) && string.IsNullOrWhiteSpace(options.TypeName))
            {
                error = "для --assembly обязателен --type: имя активности в сборке";
                return false;
            }

            return true;
        }

        private static bool TakeValue(
            IList<string> args,
            ref int index,
            string name,
            out string value,
            out string error)
        {
            value = null;
            error = null;

            if (index + 1 >= args.Count)
            {
                error = $"у параметра «{name}» нет значения";
                return false;
            }

            value = args[index + 1];
            index++;

            if (string.IsNullOrWhiteSpace(value))
            {
                error = $"у параметра «{name}» пустое значение";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Разбирает JSON аргументов в <see cref="Arguments"/>. Ошибка формата — ошибка аргументов
        /// командной строки, а не робота: чинить её должен тот, кто запускает.
        /// </summary>
        private bool TryParseArguments(string json, out string error)
        {
            error = null;

            string text = (json ?? string.Empty).Trim();

            if (text.Length == 0)
            {
                error = "параметр «--args» пуст: ожидается JSON-объект вида {\"InputText\":\"max\"}";
                return false;
            }

            if (!text.StartsWith("{", StringComparison.Ordinal))
            {
                error = "параметр «--args» ждёт JSON-объект вида {\"InputText\":\"max\"}";
                return false;
            }

            try
            {
                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                object parsed = serializer.DeserializeObject(text);

                if (!(parsed is IDictionary<string, object> map))
                {
                    error = "параметр «--args» ждёт JSON-объект: получено «" + text + "»";
                    return false;
                }

                foreach (KeyValuePair<string, object> pair in map)
                {
                    Arguments[pair.Key] = NormalizeScalar(pair.Value);
                }

                return true;
            }
            catch (Exception ex)
            {
                error = "параметр «--args» не разобран как JSON: " + ex.Message + Environment.NewLine + text;
                return false;
            }
        }

        /// <summary>
        /// Добавляет один входной аргумент в форме <c>имя=значение</c> (параметр <c>--arg</c>).
        /// <para>
        /// Нужен там, где JSON неудобен: в PowerShell и cmd кавычки внутри <c>--args</c> съедаются
        /// оболочкой, и <c>{"InputText":"max"}</c> приходит как <c>{InputText:max}</c>. Форма
        /// <c>--arg InputText=max</c> не содержит кавычек вовсе. Скаляры разбираются как в JSON:
        /// <c>true</c>/<c>false</c>, целые, дробные; всё остальное — строка.
        /// </para>
        /// </summary>
        private bool AddNamedArgument(string pair, out string error)
        {
            error = null;

            int separator = (pair ?? string.Empty).IndexOf('=');

            if (separator <= 0)
            {
                error = "параметр «--arg» ждёт пару имя=значение, получено «" + pair + "»";
                return false;
            }

            string argumentName = pair.Substring(0, separator).Trim();
            string rawValue = pair.Substring(separator + 1);

            if (argumentName.Length == 0)
            {
                error = "параметр «--arg»: пустое имя аргумента в «" + pair + "»";
                return false;
            }

            Arguments[argumentName] = NormalizeLiteral(rawValue);

            return true;
        }

        /// <summary>Скаляр из строки: число, логическое значение или строка как есть.</summary>
        private static object NormalizeLiteral(string value)
        {
            if (value == null)
            {
                return null;
            }

            if (bool.TryParse(value.Trim(), out bool flag))
            {
                return flag;
            }

            if (long.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long integral))
            {
                return integral >= int.MinValue && integral <= int.MaxValue ? (object)(int)integral : integral;
            }

            if (double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double real))
            {
                return real;
            }

            return value;
        }

        /// <summary>Приводит скаляры JSON к обычным типам CLR (см. комментарий класса).</summary>
        private static object NormalizeScalar(object value)
        {
            if (!(value is decimal number))
            {
                return value;
            }

            if (number == decimal.Truncate(number))
            {
                if (number >= int.MinValue && number <= int.MaxValue)
                {
                    return (int)number;
                }

                if (number >= long.MinValue && number <= long.MaxValue)
                {
                    return (long)number;
                }
            }

            return (double)number;
        }
        /// <summary>
        /// Параметры, собранные в обход командной строки.
        /// <para>
        /// Правило «для <c>--assembly</c> обязателен <c>--type</c>» живёт в двух местах: в
        /// <see cref="TryParse"/> (это ошибка запускающего, код 2) и в <see cref="RobotLocator"/>
        /// (публичный API: оркестратор <c>OR-05</c> может звать его напрямую, минуя CLI). Чтобы
        /// защита локатора была проверяема тестом, а не только через командную строку, сборка
        /// параметров открыта сборке тестов — как и остальные внутренние правила раннера.
        /// </para>
        /// </summary>
        internal static RunnerOptions FromValues(string assemblyPath = null, string typeName = null)
        {
            return new RunnerOptions
            {
                AssemblyPath = assemblyPath,
                TypeName = typeName
            };
        }

    }
}
