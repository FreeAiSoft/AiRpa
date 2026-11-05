using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

using RPA.Orchestrator.Contracts.Packages;

namespace RPA.Packaging
{
    /// <summary>
    /// Запись <c>robot.zip</c> (`OR-03`).
    /// <para>
    /// Архив собирается <b>воспроизводимо</b>: файлы идут в фиксированном порядке (сначала
    /// <c>manifest.json</c>, затем остальные по алфавиту), у всех записей одинаковая дата
    /// (1980-01-01, минимум формата zip) и нет каталогов-пустышек. Поэтому «тот же пакет» — это
    /// одинаковый хэш файла, а не «плюс-минус порядок в архиве»: версии и кэш можно сверять
    /// побайтно.
    /// </para>
    /// </summary>
    public static class RobotPackageWriter
    {
        /// <summary>Фиксированная дата записей архива — иначе zip не воспроизводим.</summary>
        public static readonly DateTimeOffset FixedTimestamp = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

        /// <summary>
        /// Пишет пакет из пар «путь в пакете → файл на диске». Манифест уже должен содержать
        /// хэши этих файлов (их считает <see cref="RobotPackager"/>): writer не подменяет
        /// содержимое манифеста, иначе проверка целостности у раннера стала бы бессмысленной.
        /// </summary>
        public static bool TryWrite(
            RobotPackageManifest manifest,
            IEnumerable<KeyValuePair<string, string>> files,
            string destinationPath,
            out string error)
        {
            error = null;

            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            if (files == null)
            {
                throw new ArgumentNullException(nameof(files));
            }

            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                throw new ArgumentException("Путь пакета не указан.", nameof(destinationPath));
            }

            string destination = Path.GetFullPath(destinationPath);
            string temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N").Substring(0, 8);

            try
            {
                string directory = Path.GetDirectoryName(destination);

                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    WriteEntry(archive, RobotPackageFormat.ManifestFileName, Encoding.UTF8.GetBytes(RobotPackageManifestJson.Write(manifest)));

                    foreach (KeyValuePair<string, string> file in files
                        .OrderBy(pair => RobotPackageFormat.NormalizeEntryPath(pair.Key), StringComparer.Ordinal))
                    {
                        string entryPath = RobotPackageFormat.NormalizeEntryPath(file.Key);

                        if (!File.Exists(file.Value))
                        {
                            error = "файл «" + file.Value + "» исчез до записи архива";
                            DeleteTemp(temporary);
                            return false;
                        }

                        ZipArchiveEntry entry = archive.CreateEntry(entryPath);
                        entry.LastWriteTime = FixedTimestamp;

                        using (FileStream source = File.OpenRead(file.Value))
                        using (Stream target = entry.Open())
                        {
                            source.CopyTo(target);
                        }
                    }
                }

                if (File.Exists(destination))
                {
                    File.Delete(destination);
                }

                File.Move(temporary, destination);

                return true;
            }
            catch (Exception ex)
            {
                DeleteTemp(temporary);
                error = "не удалось записать пакет «" + destination + "»: " + ex.Message;
                return false;
            }
        }

        private static void WriteEntry(ZipArchive archive, string entryPath, byte[] content)
        {
            ZipArchiveEntry entry = archive.CreateEntry(entryPath);
            entry.LastWriteTime = FixedTimestamp;

            using (Stream stream = entry.Open())
            {
                stream.Write(content, 0, content.Length);
            }
        }

        private static void DeleteTemp(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
                // Временный файл — мусор, но не повод падать: следующая упаковка создаст новый.
            }
        }
    }
}
