using System;
using System.Text;

namespace RPA.Runner
{
    /// <summary>
    /// Справка <c>--help</c> для <c>RpaRunner.exe</c> (`OR-02`).
    /// <para>
    /// Собирается из <see cref="RunnerExitCodes"/> намеренно: таблица кодов возврата в справке
    /// и в коде — один и тот же список, поэтому «в справке забыли код» не проходит тест
    /// (<c>RunnerUsageTests.Help_ListsEveryExitCode</c>).
    /// </para>
    /// </summary>
    public static class RunnerUsage
    {
        /// <summary>Полный текст справки: назначение, параметры, коды возврата и примеры.</summary>
        public static string Text
        {
            get
            {
                var text = new StringBuilder();

                text.AppendLine("RpaRunner — headless-прогон WF-робота без конструктора и без UI (OR-02).");
                text.AppendLine();
                text.AppendLine("Использование:");
                text.AppendLine("  RpaRunner --project <файл.csproj> [параметры]");
                text.AppendLine("  RpaRunner --workflow <файл.xaml> [параметры]");
                text.AppendLine("  RpaRunner --assembly <робот.dll> --type <Тип> [--bin <каталог>] [параметры]");
                text.AppendLine("  RpaRunner --package <robot.zip> [параметры]");
                text.AppendLine();
                text.AppendLine("Параметры:");
                text.AppendLine("  --project <csproj>  проект робота: раннер сам найдёт собранную сборку в bin\\<Config>");
                text.AppendLine("                      и тип активности из x:Class файла Main.xaml. Раннер НЕ собирает проект:");
                text.AppendLine("                      сначала сборка в студии, затем прогон.");
                text.AppendLine("  --workflow <xaml>   сценарий напрямую, без сборки проекта");
                text.AppendLine("  --assembly <dll>    собранный робот; обязателен --type");
                text.AppendLine("  --type <Тип>        полное имя типа активности (по умолчанию из x:Class)");
                text.AppendLine("  --bin <каталог>     каталог зависимостей робота (перекрывает автопоиск bin)");
                text.AppendLine("  --package <zip>     пакет робота (robot.zip + манифест): проект и студия не нужны;");
                text.AppendLine("                      содержимое сверяется с манифестом, пакет распаковывается в кэш");
                text.AppendLine("  --package-cache <каталог>  корень кэша пакетов (по умолчанию %LOCALAPPDATA%\\RPA\\packages,");
                text.AppendLine("                      переменная окружения RPA_PACKAGE_CACHE)");
                text.AppendLine("  --args <json>       входные аргументы: {\"InputText\":\"max\"}");
                text.AppendLine("  --arg <имя=знач.>   один аргумент без JSON: --arg InputText=max (можно повторять;");
                text.AppendLine("                      удобно в PowerShell/cmd, где кавычки внутри --args съедает оболочка)");
                text.AppendLine("  --run-id <id>       идентификатор прогона в логе и отчёте (по умолчанию генерируется)");
                text.AppendLine("  --out <файл>        куда писать result.json");
                text.AppendLine("  --log <файл>        куда писать лог (JSON-lines, одна запись на строку)");
                text.AppendLine("  --timeout <секунды> предел прогона (по умолчанию 300)");
                text.AppendLine("  --validate          только загрузить и проверить workflow, не прогоняя его");
                text.AppendLine("  --help              эта справка");
                text.AppendLine();
                text.AppendLine("Коды возврата:");
                text.Append(RunnerExitCodes.Table());
                text.AppendLine();
                text.AppendLine("Примеры:");
                text.AppendLine("  RpaRunner --project Robot\\Robot.csproj --args {\"InputText\":\"max\"} --out result.json");
                text.AppendLine("  RpaRunner --workflow Robot\\Main.xaml --validate");
                text.AppendLine("  RpaRunner --project Robot\\Robot.csproj --run-id nightly-42 --log logs\\nightly.jsonl");

                return text.ToString();
            }
        }
    }
}
