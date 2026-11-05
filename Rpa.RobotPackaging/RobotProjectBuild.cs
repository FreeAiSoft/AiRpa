using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using Agent.Core.Services;

namespace RPA.Packaging
{
    /// <summary>Итог сборки проекта робота перед упаковкой (`OR-03`).</summary>
    public sealed class RobotProjectBuildResult
    {
        /// <summary>Сборка вообще запускалась (при упаковке готового бинарника — нет).</summary>
        public bool Attempted { get; set; }

        /// <summary>Сборка завершилась успехом.</summary>
        public bool Success { get; set; }

        /// <summary>Код возврата MSBuild.</summary>
        public int ExitCode { get; set; } = -1;

        /// <summary>Чем собирали (путь к MSBuild.exe).</summary>
        public string MsBuildPath { get; set; }

        /// <summary>Версия MSBuild — попадает в манифест, чтобы пакет был связан со сборкой.</summary>
        public string MsBuildVersion { get; set; }

        /// <summary>Весь вывод сборки (stdout + stderr).</summary>
        public string Output { get; set; }

        /// <summary>Строки с ошибками компилятора/MSBuild.</summary>
        public List<string> Errors { get; } = new List<string>();

        /// <summary>Однострочный итог для лога.</summary>
        public string Describe()
        {
            if (!Attempted)
            {
                return "сборка не запускалась (упаковка готового бинарника)";
            }

            return Success
                ? "сборка успешна (MSBuild " + MsBuildVersion + ")"
                : "сборка не удалась (код " + ExitCode + ", ошибок " + Errors.Count + ")";
        }
    }

    /// <summary>
    /// Сборка проекта робота. Интерфейс — тестовый шов: упаковку пакета можно проверять
    /// без MSBuild, подставив готовый результат, а живая сборка остаётся отдельным тестом.
    /// </summary>
    public interface IRobotProjectBuilder
    {
        /// <summary>Собирает проект; исключения наружу не выходят — только результат.</summary>
        RobotProjectBuildResult Build(RobotProject project, string configuration, Action<string> log);
    }

    /// <summary>
    /// Сборка проекта робота через MSBuild (`OR-03`).
    /// <para>
    /// MSBuild ищется <b>тем же</b> локатором, что и студия (`CP-04`,
    /// <see cref="MsBuildLocator"/>): иначе «студия собрала одним тулчейном, упаковщик другим»
    /// давало бы пакеты, которые не воспроизводятся. Запуск — отдельным процессом: упаковщик
    /// не тащит в себя сборки MSBuild и не ломается от их версий.
    /// </para>
    /// </summary>
    public sealed class MsBuildRobotProjectBuilder : IRobotProjectBuilder
    {
        /// <summary>Тулчейн для сборки (по умолчанию — новейший установленный).</summary>
        public MsBuildToolchain Toolchain { get; set; } = MsBuildToolchain.Auto;

        /// <summary>Собирать ли с предварительным restore (нужно legacy-проектам с packages.config).</summary>
        public bool Restore { get; set; } = true;

        /// <inheritdoc/>
        public RobotProjectBuildResult Build(RobotProject project, string configuration, Action<string> log)
        {
            if (project == null)
            {
                throw new ArgumentNullException(nameof(project));
            }

            var result = new RobotProjectBuildResult { Attempted = true };
            MsBuildLocation location = MsBuildLocator.Resolve(Toolchain);

            log?.Invoke(location.Describe());

            if (!location.Found)
            {
                result.ExitCode = -1;
                result.Errors.Add("MSBuild не найден: " + (location.Diagnostic ?? "vswhere не нашёл ни одной установки Visual Studio"));
                return result;
            }

            result.MsBuildPath = location.Path;
            result.MsBuildVersion = location.Version;

            string config = string.IsNullOrWhiteSpace(configuration) ? "Release" : configuration.Trim();

            if (Restore)
            {
                string restoreArguments = Quote(project.ProjectPath)
                    + " /nologo /m /v:minimal /t:Restore /p:Configuration=" + config
                    + " /p:RestorePackagesConfig=true";

                if (!Execute(location, restoreArguments, result, out string restoreOutput))
                {
                    return result;
                }

                result.Output = restoreOutput;

                if (result.ExitCode != 0)
                {
                    log?.Invoke("restore завершился кодом " + result.ExitCode);
                    AppendRestoreHint(result);
                    CollectErrors(result, restoreOutput);
                    return result;
                }

                log?.Invoke("restore: ok");
            }

            string buildArguments = Quote(project.ProjectPath)
                + " /nologo /m /v:minimal /p:Configuration=" + config;

            if (!Execute(location, buildArguments, result, out string output))
            {
                return result;
            }

            result.Output = (result.Output ?? string.Empty) + Environment.NewLine + output;
            result.Success = result.ExitCode == 0;

            if (result.Success)
            {
                log?.Invoke("сборка: ok");
            }
            else
            {
                CollectErrors(result, output);
                log?.Invoke("сборка завершилась кодом " + result.ExitCode);
            }

            return result;
        }

        private static bool Execute(
            MsBuildLocation location,
            string arguments,
            RobotProjectBuildResult result,
            out string output)
        {
            output = null;

            var startInfo = new ProcessStartInfo
            {
                FileName = location.Path,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // Без этого SDK-style проекты падают с MSB4236 на машинах, где SDK не в PATH.
            foreach (KeyValuePair<string, string> pair in MsBuildLocator.GetEnvironmentOverrides(location))
            {
                startInfo.EnvironmentVariables[pair.Key] = pair.Value;
            }

            try
            {
                using (Process process = new Process { StartInfo = startInfo })
                {
                    if (!process.Start())
                    {
                        result.Errors.Add("не удалось запустить MSBuild: " + location.Path);
                        result.ExitCode = -1;
                        return false;
                    }

                    // stderr читаем параллельно: иначе на большом выводе труба переполняется и процесс виснет.
                    Task<string> stderr = Task.Run(() => process.StandardError.ReadToEnd());
                    string stdout = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    result.ExitCode = process.ExitCode;
                    output = stdout + stderr.Result;

                    return true;
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add("не удалось запустить MSBuild: " + ex.Message);
                result.ExitCode = -1;
                return false;
            }
        }

        private static void CollectErrors(RobotProjectBuildResult result, string output)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                return;
            }

            foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Contains(": error "))
                {
                    result.Errors.Add(line.Trim());
                }
            }
        }

        private static void AppendRestoreHint(RobotProjectBuildResult result)
        {
            result.Errors.Add(
                "restore не прошёл: на этой машине нет пакетов проекта. "
                + "Соберите проект в студии (или выполните restore с доступом к источнику пакетов) и повторите упаковку.");
        }

        private static string Quote(string value)
        {
            return "\"" + value + "\"";
        }
    }
}
