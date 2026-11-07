using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RPA.NuGet.Models;

namespace RPA.NuGet.Services
{
    /// <summary>
    /// Установка, обновление и удаление пакетов проекта одной операцией: скачивание <c>.nupkg</c>
    /// (только для <c>packages.config</c>-проектов), правка файла проекта и, по требованию,
    /// <c>msbuild -t:restore</c>.
    /// <para>
    /// Правка XML сама по себе не делает проект рабочим — сборка падает, пока не выполнен restore
    /// (список пакетов в <c>obj\project.assets.json</c> отсутствует). Поэтому restore вынесен в
    /// отдельный метод: панель вызывает его, только если пользователь включил опцию в «Options».
    /// </para>
    /// </summary>
    public class NuGetInstallService
    {
        private const int RestoreTimeoutMilliseconds = 5 * 60 * 1000;

        private readonly NuGetPackageDownloader _downloader;

        public NuGetInstallService(NuGetPackageDownloader downloader = null)
        {
            _downloader = downloader ?? new NuGetPackageDownloader();
        }

        /// <summary>Ставит (или меняет) версию пакета в проекте.</summary>
        public async Task InstallAsync(
            NuGetProjectPackages project,
            string id,
            string version,
            CancellationToken cancellationToken = default)
        {
            if (project == null)
                throw new ArgumentNullException(nameof(project));
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Package id is required.", nameof(id));
            if (string.IsNullOrWhiteSpace(version))
                throw new ArgumentException("Package version is required.", nameof(version));

            if (!project.IsSdkStyle)
            {
                byte[] nupkg = await _downloader.DownloadNupkgAsync(id, version).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                _downloader.Extract(nupkg, id, version, GetPackagesFolder(project));
            }

            NuGetProjectConfig.Add(project, id, version);
            NuGetProjectConfig.Save(project);
        }

        /// <summary>Убирает пакет из проекта (папку <c>packages\{id}.{version}</c> не трогаем — как NuGet).</summary>
        public void Uninstall(NuGetProjectPackages project, string id)
        {
            if (project == null)
                throw new ArgumentNullException(nameof(project));

            NuGetProjectConfig.Remove(project, id);
            NuGetProjectConfig.Save(project);
        }

        /// <summary>Каталог <c>packages</c> рядом с проектом — раскладка legacy <c>packages.config</c>.</summary>
        public static string GetPackagesFolder(NuGetProjectPackages project)
        {
            string projectDir = Path.GetDirectoryName(project?.ProjectPath) ?? string.Empty;
            return Path.Combine(projectDir, "packages");
        }

        /// <summary>
        /// Выполняет <c>msbuild -t:restore</c> для проекта. <c>null</c> — успех, иначе текст ошибки
        /// (в т.ч. «MSBuild не найден»): панель показывает его в строке состояния.
        /// </summary>
        public Task<string> RestoreAsync(string projectPath)
        {
            return Task.Run(() =>
            {
                string msbuild = FindMsBuild();
                if (string.IsNullOrWhiteSpace(msbuild))
                {
                    return "MSBuild not found: restore skipped. Build the project from Visual Studio.";
                }

                try
                {
                    var info = new ProcessStartInfo(msbuild, "-t:restore -nologo -v:m \"" + projectPath + "\"")
                    {
                        WorkingDirectory = Path.GetDirectoryName(projectPath) ?? string.Empty,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using (Process process = Process.Start(info))
                    {
                        if (process == null)
                            return "Restore failed: process could not be started.";

                        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                        if (!process.WaitForExit(RestoreTimeoutMilliseconds))
                        {
                            try
                            {
                                process.Kill();
                            }
                            catch (Exception)
                            {
                                // Процесс уже завершился.
                            }

                            return "Restore timed out.";
                        }

                        return process.ExitCode == 0
                            ? null
                            : "Restore failed (exit " + process.ExitCode + "): " + FirstMeaningfulLine(output);
                    }
                }
                catch (Exception ex)
                {
                    return "Restore failed: " + ex.Message;
                }
            });
        }

        /// <summary>
        /// Ищет MSBuild: переменная <c>MSBUILD_EXE_PATH</c> (её выставляет хост, если уже запускал сборку),
        /// затем vswhere, затем <c>msbuild.exe</c> из PATH.
        /// </summary>
        public static string FindMsBuild()
        {
            string fromEnvironment = Environment.GetEnvironmentVariable("MSBUILD_EXE_PATH");
            if (!string.IsNullOrWhiteSpace(fromEnvironment) && File.Exists(fromEnvironment))
                return fromEnvironment;

            string vswhere = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft Visual Studio",
                "Installer",
                "vswhere.exe");

            if (File.Exists(vswhere))
            {
                string found = RunAndRead(vswhere,
                    "-latest -prerelease -requires Microsoft.Component.MSBuild -find MSBuild\\**\\Bin\\MSBuild.exe");
                if (!string.IsNullOrWhiteSpace(found) && File.Exists(found))
                    return found;
            }

            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory))
                    continue;

                string candidate = Path.Combine(directory.Trim(), "msbuild.exe");
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        private static string RunAndRead(string fileName, string arguments)
        {
            try
            {
                var info = new ProcessStartInfo(fileName, arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (Process process = Process.Start(info))
                {
                    if (process == null)
                        return null;

                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(10000);
                    return output?.Trim();
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string FirstMeaningfulLine(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
                return string.Empty;

            foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = line.Trim();
                if (trimmed.Length > 0 && trimmed.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0)
                    return trimmed;
            }

            string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return lines.Length > 0 ? lines[lines.Length - 1].Trim() : string.Empty;
        }
    }
}
