using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace RPA.Orchestrator.Contracts.Packages
{
    /// <summary>
    /// Манифест пакета робота (`OR-03`) — всё, что нужно оркестратору, раннеру и запускатору,
    /// чтобы <b>запустить пакет, не открывая проект и не имея студии</b>: имя робота, версия,
    /// точка входа, целевая платформа, входные аргументы, зависимости и пофайловые хэши.
    /// <para>
    /// Манифест — часть контракта, а не «служебный файл упаковщика»: по полям <see cref="Id"/>,
    /// <see cref="Version"/> и <see cref="ContentHash"/> оркестратор отличает версию робота,
    /// проверяет неизменяемость и ищет пакет в кэше. Поэтому имена полей в JSON зафиксированы
    /// через <see cref="DataMemberAttribute"/> в нижнем регистре, а не выводятся из имён свойств.
    /// </para>
    /// </summary>
    [DataContract]
    public sealed class RobotPackageManifest
    {
        /// <summary>Версия формата манифеста (сейчас <see cref="RobotPackageFormat.CurrentVersion"/>).</summary>
        [DataMember(Name = "formatVersion", Order = 1)]
        public int FormatVersion { get; set; }

        /// <summary>Идентификатор робота (совпадает с именем сборки): ключ версий и кэша.</summary>
        [DataMember(Name = "id", Order = 2)]
        public string Id { get; set; }

        /// <summary>Человеческое имя робота (по умолчанию — <see cref="Id"/>).</summary>
        [DataMember(Name = "name", Order = 3)]
        public string Name { get; set; }

        /// <summary>Версия пакета; неизменяема: та же версия с другим содержимым не публикуется.</summary>
        [DataMember(Name = "version", Order = 4)]
        public string Version { get; set; }

        /// <summary>Целевая платформа робота в нормализованном виде (<c>net472</c>).</summary>
        [DataMember(Name = "targetFramework", Order = 5)]
        public string TargetFramework { get; set; }

        /// <summary>Полное имя типа активности — точки входа.</summary>
        [DataMember(Name = "entryType", Order = 6)]
        public string EntryType { get; set; }

        /// <summary>Путь сборки робота внутри пакета (<c>lib/Name.dll</c>).</summary>
        [DataMember(Name = "entryAssembly", Order = 7)]
        public string EntryAssembly { get; set; }

        /// <summary>Путь XAML-сценария внутри пакета (<c>robot/Main.xaml</c>); может отсутствовать.</summary>
        [DataMember(Name = "entryWorkflow", Order = 8)]
        public string EntryWorkflow { get; set; }

        /// <summary>Когда пакет собран (ISO-8601 со смещением).</summary>
        [DataMember(Name = "createdAt", Order = 9)]
        public string CreatedAt { get; set; }

        /// <summary>Чем собран пакет: инструмент, его версия, версия MSBuild.</summary>
        [DataMember(Name = "builder", Order = 10)]
        public RobotPackageBuilderInfo Builder { get; set; }

        /// <summary>Из чего собран пакет: проект, конфигурация, исходная сборка.</summary>
        [DataMember(Name = "source", Order = 11)]
        public RobotPackageSourceInfo Source { get; set; }

        /// <summary>Входные и выходные аргументы робота (как их объявил автор в XAML).</summary>
        [DataMember(Name = "arguments", Order = 12)]
        public List<RobotPackageArgument> Arguments { get; set; }

        /// <summary>Зависимости пакета: имя сборки и её версия (для диагностики конфликтов).</summary>
        [DataMember(Name = "dependencies", Order = 13)]
        public List<RobotPackageDependency> Dependencies { get; set; }

        /// <summary>Все файлы пакета с размерами и хэшами — по ним проверяется целостность.</summary>
        [DataMember(Name = "files", Order = 14)]
        public List<RobotPackageFile> Files { get; set; }

        /// <summary>Хэш содержимого: считается по файлам, не зависит от времени сборки и порядка.</summary>
        [DataMember(Name = "contentHash", Order = 15)]
        public string ContentHash { get; set; }

        /// <summary>Замечания упаковки (например, «TFM отличается от 4.7.2»), не мешающие запуску.</summary>
        [DataMember(Name = "warnings", Order = 16)]
        public List<string> Warnings { get; set; }

        /// <summary>Файл манифеста по его пути в пакете; <c>null</c>, если такого файла нет.</summary>
        public RobotPackageFile FindFile(string entryPath)
        {
            if (Files == null || string.IsNullOrWhiteSpace(entryPath))
            {
                return null;
            }

            string normalized = RobotPackageFormat.NormalizeEntryPath(entryPath);

            return Files.FirstOrDefault(f => string.Equals(
                RobotPackageFormat.NormalizeEntryPath(f.Path),
                normalized,
                StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Манифест заполнен настолько, чтобы его можно было читать (без проверки правил).</summary>
        public bool HasIdentity()
        {
            return !string.IsNullOrWhiteSpace(Id) && !string.IsNullOrWhiteSpace(Version);
        }

        /// <summary>Однострочное описание версии для лога: <c>Name 1.0.0 (net472)</c>.</summary>
        public string Describe()
        {
            string name = string.IsNullOrWhiteSpace(Name) ? Id : Name;

            return name + " " + Version + " (" + TargetFramework + ")";
        }
    }

    /// <summary>Чем собран пакет: без этих полей «пакет от 6 ноября» невозможно связать со сборкой.</summary>
    [DataContract]
    public sealed class RobotPackageBuilderInfo
    {
        /// <summary>Имя инструмента упаковки (<c>RpaPack</c>).</summary>
        [DataMember(Name = "tool", Order = 1)]
        public string Tool { get; set; }

        /// <summary>Версия инструмента упаковки.</summary>
        [DataMember(Name = "version", Order = 2)]
        public string Version { get; set; }

        /// <summary>Версия MSBuild, которой собрана сборка робота (пусто при упаковке готового бинарника).</summary>
        [DataMember(Name = "msbuildVersion", Order = 3)]
        public string MsBuildVersion { get; set; }
    }

    /// <summary>Из чего собран пакет.</summary>
    [DataContract]
    public sealed class RobotPackageSourceInfo
    {
        /// <summary>Имя файла проекта робота (без каталога: путь автора машины в пакет не попадает).</summary>
        [DataMember(Name = "project", Order = 1)]
        public string Project { get; set; }

        /// <summary>Конфигурация сборки (<c>Debug</c>/<c>Release</c>).</summary>
        [DataMember(Name = "configuration", Order = 2)]
        public string Configuration { get; set; }

        /// <summary>Каталог сборки, откуда взята сборка робота (для диагностики, не для запуска).</summary>
        [DataMember(Name = "binDirectory", Order = 3)]
        public string BinDirectory { get; set; }
    }

    /// <summary>Один аргумент робота, как его объявил автор в XAML.</summary>
    [DataContract]
    public sealed class RobotPackageArgument
    {
        /// <summary>Имя аргумента.</summary>
        [DataMember(Name = "name", Order = 1)]
        public string Name { get; set; }

        /// <summary>Тип CLR (<c>System.String</c>); сложные типы сохраняются как объявлены.</summary>
        [DataMember(Name = "type", Order = 2)]
        public string Type { get; set; }

        /// <summary>Направление: <c>In</c>, <c>Out</c> или <c>InOut</c>.</summary>
        [DataMember(Name = "direction", Order = 3)]
        public string Direction { get; set; }

        /// <summary>Значение по умолчанию, если объявлено в XAML.</summary>
        [DataMember(Name = "defaultValue", Order = 4)]
        public string DefaultValue { get; set; }

        /// <summary>Аргумент обязателен: входной и без значения по умолчанию.</summary>
        [DataMember(Name = "required", Order = 5)]
        public bool Required { get; set; }
    }

    /// <summary>Зависимость пакета: сборка из каталога робота.</summary>
    [DataContract]
    public sealed class RobotPackageDependency
    {
        /// <summary>Путь файла внутри пакета (<c>lib/System.Activities.dll</c>).</summary>
        [DataMember(Name = "file", Order = 1)]
        public string File { get; set; }

        /// <summary>Простое имя сборки.</summary>
        [DataMember(Name = "name", Order = 2)]
        public string Name { get; set; }

        /// <summary>Версия сборки (сведения из метаданных); может быть пустой.</summary>
        [DataMember(Name = "version", Order = 3)]
        public string Version { get; set; }
    }

    /// <summary>Файл пакета: путь внутри пакета, размер и sha256.</summary>
    [DataContract]
    public sealed class RobotPackageFile
    {
        /// <summary>Путь внутри пакета (<c>lib/Name.dll</c>, <c>robot/Main.xaml</c>).</summary>
        [DataMember(Name = "path", Order = 1)]
        public string Path { get; set; }

        /// <summary>Размер в байтах.</summary>
        [DataMember(Name = "size", Order = 2)]
        public long Size { get; set; }

        /// <summary>sha256 в нижнем регистре, 64 символа.</summary>
        [DataMember(Name = "sha256", Order = 3)]
        public string Sha256 { get; set; }
    }
}
