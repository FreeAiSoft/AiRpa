using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace RPA.Orchestrator.Contracts.Packages
{
    /// <summary>
    /// Открытый и проверенный пакет робота (`OR-03`).
    /// <para>
    /// Объект означает «манифест разобран, правила соблюдены, содержимое совпало с хэшами».
    /// Только после этого раннер имеет право распаковывать пакет и запускать робота: иначе
    /// подменённый или недокачанный файл в кэше давал бы падение внутри загрузки workflow
    /// с непонятной причиной.
    /// </para>
    /// </summary>
    public sealed class RobotPackage
    {
        internal RobotPackage(string packagePath, string packageSha256, RobotPackageManifest manifest)
        {
            PackagePath = packagePath;
            PackageSha256 = packageSha256;
            Manifest = manifest;
        }

        /// <summary>Полный путь к <c>robot.zip</c>.</summary>
        public string PackagePath { get; }

        /// <summary>sha256 файла пакета (не путать с <see cref="RobotPackageManifest.ContentHash"/>).</summary>
        public string PackageSha256 { get; }

        /// <summary>Манифест пакета.</summary>
        public RobotPackageManifest Manifest { get; }

        /// <summary>Идентификатор робота.</summary>
        public string Id
        {
            get { return Manifest.Id; }
        }

        /// <summary>Версия пакета.</summary>
        public string Version
        {
            get { return Manifest.Version; }
        }

        /// <summary>Точка входа (полное имя типа активности).</summary>
        public string EntryType
        {
            get { return Manifest.EntryType; }
        }

        /// <summary>Однострочное описание для лога.</summary>
        public string Describe()
        {
            return Manifest.Describe() + ", файлов " + Manifest.Files.Count
                + ", хэш " + (Manifest.ContentHash ?? string.Empty);
        }
    }

    /// <summary>
    /// Чтение пакета <c>robot.zip</c> (`OR-03`): манифест, правила, сверка содержимого, распаковка.
    /// <para>
    /// Здесь же закрыта «zip-slip»: пути берутся только из манифеста и только после
    /// <see cref="RobotPackageFormat.IsSafeEntryPath"/>, поэтому архив с <c>..\..\Windows\…</c>
    /// не может ничего записать вне каталога распаковки. Лишние файлы в архиве, которых нет
    /// в манифесте, — тоже отказ: иначе через пакет можно принести сборку, которую никто не проверял.
    /// </para>
    /// </summary>
    public static class RobotPackageReader
    {
        /// <summary>
        /// Открывает пакет и проверяет его целиком. <c>false</c> — пакета нет, он не читается,
        /// манифест не проходит правила или содержимое не совпало с хэшами; <paramref name="error"/>
        /// всегда содержит причину и путь.
        /// </summary>
        public static bool TryOpen(string packagePath, out RobotPackage package, out string error)
        {
            package = null;
            error = null;

            if (string.IsNullOrWhiteSpace(packagePath))
            {
                error = "путь к пакету не указан";
                return false;
            }

            string full;

            try
            {
                full = Path.GetFullPath(packagePath);
            }
            catch (Exception ex)
            {
                error = "путь к пакету «" + packagePath + "» не разобран: " + ex.Message;
                return false;
            }

            if (!File.Exists(full))
            {
                error = "пакет робота не найден: " + full;
                return false;
            }

            try
            {
                using (FileStream stream = File.OpenRead(full))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    if (!TryReadManifest(archive, out RobotPackageManifest manifest, out error))
                    {
                        return false;
                    }

                    RobotPackageValidationResult validation = RobotPackageValidator.Validate(manifest);

                    if (!validation.IsValid)
                    {
                        error = "манифест пакета «" + full + "» не прошёл проверку:" + validation.Describe();
                        return false;
                    }

                    if (!TryVerifyEntries(archive, manifest, out error))
                    {
                        return false;
                    }

                    package = new RobotPackage(full, RobotPackageHash.OfFile(full), manifest);
                    return true;
                }
            }
            catch (InvalidDataException ex)
            {
                error = "файл «" + full + "» не читается как zip-архив: " + ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = "не удалось прочитать пакет «" + full + "»: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Распаковывает пакет в каталог: файлы из манифеста под своими путями плюс
        /// <c>manifest.json</c>. Каталог создаётся; ошибка не оставляет «полураспакованного» вида —
        /// распаковка идёт во временный каталог рядом и переименовывается (см. <see cref="RobotPackageCache"/>).
        /// </summary>
        public static bool TryExtract(RobotPackage package, string targetDirectory, out string error)
        {
            error = null;

            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            if (string.IsNullOrWhiteSpace(targetDirectory))
            {
                throw new ArgumentException("Каталог распаковки не указан.", nameof(targetDirectory));
            }

            string target = Path.GetFullPath(targetDirectory);

            try
            {
                if (!Directory.Exists(target))
                {
                    Directory.CreateDirectory(target);
                }

                using (FileStream stream = File.OpenRead(package.PackagePath))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    foreach (RobotPackageFile file in package.Manifest.Files)
                    {
                        string entryPath = RobotPackageFormat.NormalizeEntryPath(file.Path);
                        ZipArchiveEntry entry = FindEntry(archive, entryPath);

                        if (entry == null)
                        {
                            error = "в пакете нет файла «" + entryPath + "» из манифеста";
                            return false;
                        }

                        string destination = Path.Combine(target, entryPath.Replace('/', Path.DirectorySeparatorChar));
                        string directory = Path.GetDirectoryName(destination);

                        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }

                        entry.ExtractToFile(destination, true);
                    }

                    File.WriteAllText(
                        Path.Combine(target, RobotPackageFormat.ManifestFileName),
                        RobotPackageManifestJson.Write(package.Manifest),
                        Encoding.UTF8);
                }

                return true;
            }
            catch (Exception ex)
            {
                error = "не удалось распаковать пакет «" + package.PackagePath + "» в «" + target + "»: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Ищет запись по пути внутри пакета, не полагаясь на разделитель, которым её записал архиватор:
        /// пакет могли перепаковать сторонним инструментом, и «\» в именах записей — обычное дело.
        /// </summary>
        private static ZipArchiveEntry FindEntry(ZipArchive archive, string entryPath)
        {
            string wanted = RobotPackageFormat.NormalizeEntryPath(entryPath);

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (string.Equals(RobotPackageFormat.NormalizeEntryPath(entry.FullName), wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            return null;
        }

        private static bool TryReadManifest(ZipArchive archive, out RobotPackageManifest manifest, out string error)
        {
            manifest = null;

            ZipArchiveEntry entry = FindEntry(archive, RobotPackageFormat.ManifestFileName);

            if (entry == null)
            {
                error = "в пакете нет «" + RobotPackageFormat.ManifestFileName + "»: это не пакет робота";
                return false;
            }

            string json;

            using (Stream stream = entry.Open())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                json = reader.ReadToEnd();
            }

            if (!RobotPackageManifestJson.TryRead(json, out manifest, out string parseError))
            {
                error = "манифест пакета не разобран: " + parseError;
                return false;
            }

            error = null;
            return true;
        }

        private static bool TryVerifyEntries(
            ZipArchive archive,
            RobotPackageManifest manifest,
            out string error)
        {
            error = null;

            var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (RobotPackageFile file in manifest.Files)
            {
                string entryPath = RobotPackageFormat.NormalizeEntryPath(file.Path);
                declared.Add(entryPath);

                ZipArchiveEntry entry = FindEntry(archive, entryPath);

                if (entry == null)
                {
                    error = "в пакете нет файла «" + entryPath + "», объявленного в манифесте";
                    return false;
                }

                string actual;

                using (Stream stream = entry.Open())
                {
                    actual = RobotPackageHash.OfStream(stream);
                }

                if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    error = "файл «" + entryPath + "» не совпал с манифестом: ожидался sha256 "
                        + file.Sha256 + ", в пакете " + actual;
                    return false;
                }

                if (entry.Length != file.Size)
                {
                    error = "файл «" + entryPath + "» другого размера: в манифесте "
                        + file.Size + " байт, в пакете " + entry.Length;
                    return false;
                }
            }

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string name = RobotPackageFormat.NormalizeEntryPath(entry.FullName);

                if (name.Length == 0 || name.EndsWith("/", StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(name, RobotPackageFormat.ManifestFileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!declared.Contains(name))
                {
                    error = "в пакете есть файл «" + name + "», которого нет в манифесте";
                    return false;
                }
            }

            return true;
        }
    }
}
