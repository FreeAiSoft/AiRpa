using System;
using System.Collections.Generic;
using System.Globalization;

namespace RpaPack
{
    /// <summary>Коды возврата <c>RpaPack.exe</c> (`OR-03`).</summary>
    internal enum PackExitCode
    {
        /// <summary>Пакет собран (или переиспользован) и лежит по указанному пути.</summary>
        Success = 0,

        /// <summary>Сборка проекта робота не прошла.</summary>
        BuildFailed = 1,

        /// <summary>Неверные аргументы командной строки: печатается справка.</summary>
        BadArguments = 2,

        /// <summary>Из проекта нельзя получить робота: нет проекта, XAML или точки входа.</summary>
        ProjectInvalid = 3,

        /// <summary>Собранный пакет не прошёл проверку формата.</summary>
        PackageInvalid = 4,

        /// <summary>Версия уже упакована с другим содержимым — пакет неизменяем.</summary>
        VersionConflict = 5,

        /// <summary>Ошибка ввода-вывода: не записался пакет, недоступен каталог.</summary>
        IoFailed = 6,

        /// <summary>Робот не собран, а сборку не просили.</summary>
        NotBuilt = 7
    }

    /// <summary>Разобранные параметры <c>RpaPack.exe</c>.</summary>
    internal sealed class PackOptions
    {
        /// <summary>Путь к <c>*.csproj</c> робота.</summary>
        public string ProjectPath { get; set; }

        /// <summary>Версия пакета; пусто — из проекта.</summary>
        public string Version { get; set; }

        /// <summary>Имя робота для человека; пусто — идентификатор.</summary>
        public string Name { get; set; }

        /// <summary>Конфигурация сборки (по умолчанию <c>Release</c>).</summary>
        public string Configuration { get; set; } = "Release";

        /// <summary>Куда положить пакет; пусто — в локальный кэш.</summary>
        public string OutputPath { get; set; }

        /// <summary>Корень кэша пакетов; пусто — <c>%LOCALAPPDATA%\RPA\packages</c>.</summary>
        public string CacheRoot { get; set; }

        /// <summary>Собирать проект перед упаковкой.</summary>
        public bool Build { get; set; } = true;

        /// <summary>Печатать итог в JSON (для оркестратора и скриптов).</summary>
        public bool Json { get; set; }

        /// <summary>Показать справку и выйти успешно.</summary>
        public bool ShowHelp { get; set; }

        /// <summary>Разбирает командную строку; ошибки — это код 2 и справка, а не исключение.</summary>
        public static bool TryParse(IList<string> args, out PackOptions options, out string error)
        {
            options = new PackOptions();
            error = null;

            if (args == null || args.Count == 0)
            {
                error = "не указан проект робота: --project <путь к csproj>";
                return false;
            }

            for (int index = 0; index < args.Count; index++)
            {
                string name = (args[index] ?? string.Empty).Trim();

                if (name.Length == 0)
                {
                    error = "пустой аргумент командной строки";
                    return false;
                }

                switch (name.ToLowerInvariant())
                {
                    case "--help":
                    case "-h":
                    case "-?":
                    case "/?":
                        options.ShowHelp = true;
                        break;

                    case "--no-build":
                        options.Build = false;
                        break;

                    case "--json":
                        options.Json = true;
                        break;

                    case "--project":
                        if (!TakeValue(args, ref index, name, out string project, out error))
                        {
                            return false;
                        }

                        options.ProjectPath = project;
                        break;

                    case "--version":
                        if (!TakeValue(args, ref index, name, out string version, out error))
                        {
                            return false;
                        }

                        options.Version = version;
                        break;

                    case "--name":
                        if (!TakeValue(args, ref index, name, out string robotName, out error))
                        {
                            return false;
                        }

                        options.Name = robotName;
                        break;

                    case "--configuration":
                    case "--config":
                        if (!TakeValue(args, ref index, name, out string configuration, out error))
                        {
                            return false;
                        }

                        string normalized = configuration.Trim();

                        if (!string.Equals(normalized, "Debug", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(normalized, "Release", StringComparison.OrdinalIgnoreCase))
                        {
                            error = $"параметр «{name}» ждёт Debug или Release, получено «{configuration}»";
                            return false;
                        }

                        options.Configuration = normalized;
                        break;

                    case "--out":
                        if (!TakeValue(args, ref index, name, out string output, out error))
                        {
                            return false;
                        }

                        options.OutputPath = output;
                        break;

                    case "--cache":
                        if (!TakeValue(args, ref index, name, out string cache, out error))
                        {
                            return false;
                        }

                        options.CacheRoot = cache;
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

            if (string.IsNullOrWhiteSpace(options.ProjectPath))
            {
                error = "не указан проект робота: --project <путь к csproj>";
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
                error = string.Format(CultureInfo.InvariantCulture, "у параметра «{0}» нет значения", name);
                return false;
            }

            value = args[index + 1];
            index++;

            if (string.IsNullOrWhiteSpace(value))
            {
                error = string.Format(CultureInfo.InvariantCulture, "у параметра «{0}» пустое значение", name);
                return false;
            }

            return true;
        }
    }

    /// <summary>Справка <c>RpaPack.exe</c>: параметры и таблица кодов возврата — из одного места.</summary>
    internal static class PackUsage
    {
        /// <summary>Текст справки.</summary>
        public static string Text { get; } = BuildText();

        /// <summary>Описание кода возврата одной строкой.</summary>
        public static string Describe(PackExitCode code)
        {
            switch (code)
            {
                case PackExitCode.Success:
                    return "пакет собран или переиспользован";
                case PackExitCode.BuildFailed:
                    return "сборка проекта робота не прошла";
                case PackExitCode.BadArguments:
                    return "неверные аргументы командной строки";
                case PackExitCode.ProjectInvalid:
                    return "из проекта нельзя получить робота (нет проекта, XAML или точки входа)";
                case PackExitCode.PackageInvalid:
                    return "собранный пакет не прошёл проверку формата";
                case PackExitCode.VersionConflict:
                    return "версия уже упакована с другим содержимым (пакет неизменяем)";
                case PackExitCode.IoFailed:
                    return "ошибка ввода-вывода при записи пакета";
                case PackExitCode.NotBuilt:
                    return "робот не собран, а сборка отключена";
                default:
                    return "неизвестный код";
            }
        }

        private static string BuildText()
        {
            var text = new System.Text.StringBuilder();

            text.AppendLine("RpaPack — упаковка робота в robot.zip (манифест, хэш, локальный кэш)");
            text.AppendLine();
            text.AppendLine("Использование:");
            text.AppendLine("  RpaPack --project <путь к csproj> [параметры]");
            text.AppendLine();
            text.AppendLine("Источник:");
            text.AppendLine("  --project <файл>         проект робота (*.csproj) — единственный обязательный параметр");
            text.AppendLine();
            text.AppendLine("Параметры:");
            text.AppendLine("  --version <версия>       версия пакета (по умолчанию из проекта, иначе 1.0.0)");
            text.AppendLine("  --name <имя>             имя робота для человека (по умолчанию — идентификатор)");
            text.AppendLine("  --configuration <имя>    Debug или Release (по умолчанию Release)");
            text.AppendLine("  --out <файл>             куда положить robot.zip (по умолчанию — в локальный кэш)");
            text.AppendLine("  --cache <каталог>        корень кэша пакетов (по умолчанию %LOCALAPPDATA%\\RPA\\packages,");
            text.AppendLine("                           переменная окружения RPA_PACKAGE_CACHE)");
            text.AppendLine("  --no-build               не собирать проект, упаковать уже собранный бинарник");
            text.AppendLine("  --json                   напечатать итог в JSON (для оркестратора и скриптов)");
            text.AppendLine("  --help                   эта справка");
            text.AppendLine();
            text.AppendLine("Коды возврата:");

            foreach (PackExitCode code in new[]
            {
                PackExitCode.Success,
                PackExitCode.BuildFailed,
                PackExitCode.BadArguments,
                PackExitCode.ProjectInvalid,
                PackExitCode.PackageInvalid,
                PackExitCode.VersionConflict,
                PackExitCode.NotBuilt,
                PackExitCode.IoFailed
            })
            {
                text.Append("  ")
                    .Append(((int)code).ToString(CultureInfo.InvariantCulture))
                    .Append(" — ")
                    .AppendLine(Describe(code));
            }

            return text.ToString();
        }
    }
}
