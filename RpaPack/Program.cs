using System;
using System.Text;

using RPA.Orchestrator.Contracts.Packages;
using RPA.Packaging;

namespace RpaPack
{
    /// <summary>
    /// Точка входа <c>RpaPack.exe</c> (`OR-03`).
    /// <para>
    /// Здесь только консоль и код возврата: сборка и упаковка — в <see cref="RobotPackager"/>,
    /// поэтому тот же код вызывают студия (`OR-14`) и сервер (`OR-05`), не копируя логику.
    /// Никакого UI и ожидания ввода: инструмент живёт в скриптах сборки и на агенте.
    /// </para>
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            // Упаковщик печатает по-русски, а оркестратор читает stdout перенаправленным.
            try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }

            if (!PackOptions.TryParse(args, out PackOptions options, out string error))
            {
                Console.Error.WriteLine("Ошибка аргументов: " + error);
                Console.Error.WriteLine();
                Console.Error.WriteLine(PackUsage.Text);

                return (int)PackExitCode.BadArguments;
            }

            if (options.ShowHelp)
            {
                Console.WriteLine(PackUsage.Text);

                return (int)PackExitCode.Success;
            }

            try
            {
                var request = new RobotPackageRequest
                {
                    ProjectPath = options.ProjectPath,
                    Version = options.Version,
                    Name = options.Name,
                    Configuration = options.Configuration,
                    OutputPath = options.OutputPath,
                    CacheRoot = options.CacheRoot,
                    Build = options.Build,
                    // В JSON-режиме журнал не печатаем: stdout занят одной строкой JSON для скрипта.
                    Log = options.Json ? (Action<string>)null : Console.WriteLine
                };

                RobotPackageResult result = new RobotPackager().Pack(request);
                PackExitCode code = MapExitCode(result.Status);

                if (options.Json)
                {
                    Console.WriteLine(ToJson(result, code));
                }
                else
                {
                    Console.WriteLine(result.Summary);
                }

                if (code != PackExitCode.Success)
                {
                    Console.Error.WriteLine("Упаковка не удалась: " + PackUsage.Describe(code));

                    if (!string.IsNullOrWhiteSpace(result.Error))
                    {
                        Console.Error.WriteLine(result.Error);
                    }
                }

                return (int)code;
            }
            catch (Exception ex)
            {
                // Последняя страховка: даже падение упаковщика обязано дать код и текст,
                // а не «процесс завершился с -532462766» в скрипте сборки.
                Console.Error.WriteLine("Упаковщик упал: " + ex);

                return (int)PackExitCode.IoFailed;
            }
        }

        private static PackExitCode MapExitCode(RobotPackStatus status)
        {
            switch (status)
            {
                case RobotPackStatus.Created:
                case RobotPackStatus.Reused:
                    return PackExitCode.Success;
                case RobotPackStatus.BuildFailed:
                    return PackExitCode.BuildFailed;
                case RobotPackStatus.NotBuilt:
                    return PackExitCode.NotBuilt;
                case RobotPackStatus.ProjectInvalid:
                    return PackExitCode.ProjectInvalid;
                case RobotPackStatus.PackageInvalid:
                    return PackExitCode.PackageInvalid;
                case RobotPackStatus.VersionConflict:
                    return PackExitCode.VersionConflict;
                default:
                    return PackExitCode.IoFailed;
            }
        }

        private static string ToJson(RobotPackageResult result, PackExitCode code)
        {
            var text = new StringBuilder();
            text.Append('{');
            text.Append("\"status\":\"").Append(result.Status).Append("\",");
            text.Append("\"exitCode\":").Append((int)code).Append(',');
            text.Append("\"packagePath\":").Append(Quote(result.PackagePath)).Append(',');
            text.Append("\"contentHash\":").Append(Quote(result.Manifest?.ContentHash));

            if (result.Manifest != null)
            {
                text.Append(",\"id\":").Append(Quote(result.Manifest.Id));
                text.Append(",\"version\":").Append(Quote(result.Manifest.Version));
                text.Append(",\"targetFramework\":").Append(Quote(result.Manifest.TargetFramework));
                text.Append(",\"entryType\":").Append(Quote(result.Manifest.EntryType));
                text.Append(",\"files\":").Append(result.Manifest.Files.Count);
            }

            text.Append(",\"error\":").Append(Quote(result.Error)).Append('}');

            return text.ToString();
        }

        private static string Quote(string value)
        {
            if (value == null)
            {
                return "null";
            }

            var text = new StringBuilder("\"");

            foreach (char symbol in value)
            {
                switch (symbol)
                {
                    case '"':
                        text.Append("\\\"");
                        break;
                    case '\\':
                        text.Append("\\\\");
                        break;
                    case '\r':
                        text.Append("\\r");
                        break;
                    case '\n':
                        text.Append("\\n");
                        break;
                    default:
                        text.Append(symbol);
                        break;
                }
            }

            return text.Append('"').ToString();
        }
    }
}
