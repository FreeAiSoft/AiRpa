using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace RPA.Core
{
    /// <summary>
    /// Запись списка недавних проектов: человекочитаемое имя и полный путь (файл <c>.sln</c>/
    /// <c>.csproj</c> либо папка проекта).
    /// </summary>
    public sealed class RecentProjectEntry
    {
        /// <summary>Имя проекта без расширения/каталога.</summary>
        public string Name { get; set; }

        /// <summary>Полный путь к файлу проекта или папке проекта.</summary>
        public string Path { get; set; }
    }

    /// <summary>
    /// Персист списка недавно открытых проектов в одной строковой настройке
    /// <c>RecentProjects</c> (JSON-массив <see cref="RecentProjectEntry"/>).
    /// <para>
    /// Логика намеренно без WPF и без <c>Properties.Settings</c> — так же, как
    /// <see cref="NuGetSourceSelectionStore"/>: чтение/запись строки проверяются юнит-тестами,
    /// а модель <c>Main</c> подключает её к реальной настройке.
    /// </para>
    /// </summary>
    public static class RecentProjectsStore
    {
        /// <summary>Максимальное количество записей в списке недавних проектов.</summary>
        public const int MaxEntries = 10;

        /// <summary>Разбирает JSON-список; пустой/битый JSON возвращает пустой список без исключения.</summary>
        public static IReadOnlyList<RecentProjectEntry> Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new List<RecentProjectEntry>();

            try
            {
                return JsonConvert.DeserializeObject<List<RecentProjectEntry>>(json)
                       ?? new List<RecentProjectEntry>();
            }
            catch (Exception)
            {
                return new List<RecentProjectEntry>();
            }
        }

        /// <summary>Сериализует список в компактный JSON, отбрасывая пустые записи.</summary>
        public static string Serialize(IEnumerable<RecentProjectEntry> entries)
        {
            List<RecentProjectEntry> list = (entries ?? Enumerable.Empty<RecentProjectEntry>())
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.Path))
                .ToList();

            return JsonConvert.SerializeObject(list, Formatting.None);
        }

        /// <summary>
        /// Добавляет путь в начало списка (дедупликация по регистронезависимому полному пути),
        /// ограничивая длину <see cref="MaxEntries"/>. Возвращает новый JSON.
        /// </summary>
        public static string Add(string json, string path)
        {
            RecentProjectEntry entry = CreateEntry(path);
            if (entry == null)
                return json ?? string.Empty;

            List<RecentProjectEntry> list = Parse(json).ToList();
            list.RemoveAll(existing => SamePath(existing.Path, entry.Path));
            list.Insert(0, entry);

            if (list.Count > MaxEntries)
                list = list.Take(MaxEntries).ToList();

            return Serialize(list);
        }

        /// <summary>Убирает записи, чей путь больше не существует; возвращает новый JSON.</summary>
        public static string RemoveMissing(string json)
        {
            List<RecentProjectEntry> existing = Parse(json).Where(entry => Exists(entry?.Path)).ToList();
            return Serialize(existing);
        }

        /// <summary>Создаёт запись с нормализованным полным путём и именем проекта.</summary>
        public static RecentProjectEntry CreateEntry(string path)
        {
            string full = NormalizeFullPath(path);
            if (string.IsNullOrEmpty(full))
                return null;

            return new RecentProjectEntry { Path = full, Name = GetDisplayName(full) };
        }

        /// <summary>Возвращает имя проекта: для файла проекта — имя без расширения, для папки — имя папки.</summary>
        public static string GetDisplayName(string path)
        {
            string full = NormalizeFullPath(path);
            if (string.IsNullOrEmpty(full))
                return string.Empty;

            string extension = Path.GetExtension(full);
            if (!string.IsNullOrEmpty(extension) && IsProjectFileExtension(extension))
                return Path.GetFileNameWithoutExtension(full);

            try
            {
                if (Directory.Exists(full))
                    return Path.GetFileName(full);
                if (File.Exists(full))
                    return Path.GetFileNameWithoutExtension(full);
            }
            catch (Exception)
            {
                // ignore — fall back to the last path segment below
            }

            return Path.GetFileName(full);
        }

        private static bool IsProjectFileExtension(string extension)
        {
            return string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(extension, ".vbproj", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(extension, ".fsproj", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(extension, ".sln", StringComparison.OrdinalIgnoreCase);
        }

        private static bool Exists(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                return Directory.Exists(path) || File.Exists(path);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool SamePath(string first, string second)
        {
            return string.Equals(NormalizeFullPath(first), NormalizeFullPath(second), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeFullPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            string trimmed = path.Trim().Trim('"').Trim();
            if (trimmed.Length == 0)
                return null;

            string full;
            try
            {
                full = Path.GetFullPath(trimmed);
            }
            catch (Exception)
            {
                full = trimmed;
            }

            // Корень диска («C:\») не трогаем — иначе он превратится в «C:».
            return full.Length > 3
                ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : full;
        }
    }
}
