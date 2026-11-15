using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace RPA.Core
{
    /// <summary>
    /// Сериализуемая запись несохранённого буфера закрытого документа (редактор кода/XAML или
    /// дизайнер WF). В отличие от <see cref="UnsavedDocumentBuffer"/>, имеет сеттеры для JSON.
    /// </summary>
    public sealed class ProjectUnsavedBufferEntry
    {
        /// <summary>Полный (абсолютный) путь к файлу.</summary>
        [JsonProperty("path")]
        public string FilePath { get; set; }

        /// <summary>Содержимое буфера на момент закрытия проекта.</summary>
        [JsonProperty("text")]
        public string Text { get; set; }

        /// <summary>Тип вкладки: <c>"code"</c> или <c>"workflow"</c>.</summary>
        [JsonProperty("kind")]
        public string Kind { get; set; }

        /// <summary>Преобразует живой буфер в сериализуемую запись.</summary>
        public static ProjectUnsavedBufferEntry FromBuffer(UnsavedDocumentBuffer buffer)
        {
            if (buffer == null)
            {
                return null;
            }

            return new ProjectUnsavedBufferEntry
            {
                FilePath = buffer.FilePath,
                Text = buffer.Text,
                Kind = buffer.IsWorkflow ? "workflow" : "code"
            };
        }

        /// <summary>Преобразует запись обратно в живой буфер.</summary>
        public UnsavedDocumentBuffer ToBuffer()
        {
            UnsavedDocumentBufferKind kind = string.Equals(Kind, "workflow", StringComparison.OrdinalIgnoreCase)
                ? UnsavedDocumentBufferKind.Workflow
                : UnsavedDocumentBufferKind.Code;

            return new UnsavedDocumentBuffer(FilePath, Text, kind);
        }
    }

    /// <summary>
    /// Персист несохранённых буферов по проектам: словарь «путь проекта → список буферов».
    /// <para>
    /// Нужен для сценария «Сохранить в буфер» при закрытии проекта: содержимое изменённых, но не
    /// сохранённых на диск вкладок переживает закрытие и перезапуск студии. Ключ проекта и сравнение
    /// — регистронезависимые по полному пути.
    /// </para>
    /// </summary>
    public static class ProjectUnsavedBuffersStore
    {
        /// <summary>Разбирает JSON-словарь; пустой/битый JSON даёт пустой словарь без исключения.</summary>
        public static Dictionary<string, List<ProjectUnsavedBufferEntry>> Parse(string json)
        {
            Dictionary<string, List<ProjectUnsavedBufferEntry>> result =
                new Dictionary<string, List<ProjectUnsavedBufferEntry>>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(json))
            {
                return result;
            }

            try
            {
                Dictionary<string, List<ProjectUnsavedBufferEntry>> deserialized =
                    JsonConvert.DeserializeObject<Dictionary<string, List<ProjectUnsavedBufferEntry>>>(json);

                if (deserialized == null)
                {
                    return result;
                }

                foreach (KeyValuePair<string, List<ProjectUnsavedBufferEntry>> pair in deserialized)
                {
                    if (string.IsNullOrWhiteSpace(pair.Key))
                    {
                        continue;
                    }

                    result[pair.Key] = (pair.Value ?? new List<ProjectUnsavedBufferEntry>())
                        .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.FilePath))
                        .ToList();
                }
            }
            catch (Exception)
            {
                // Битый JSON — просто пустой словарь.
            }

            return result;
        }

        /// <summary>Сериализует словарь в компактный JSON, отбрасывая пустые ключи и пустые списки.</summary>
        public static string Serialize(IReadOnlyDictionary<string, List<ProjectUnsavedBufferEntry>> map)
        {
            Dictionary<string, List<ProjectUnsavedBufferEntry>> clean =
                new Dictionary<string, List<ProjectUnsavedBufferEntry>>(StringComparer.OrdinalIgnoreCase);

            if (map != null)
            {
                foreach (KeyValuePair<string, List<ProjectUnsavedBufferEntry>> pair in map)
                {
                    if (string.IsNullOrWhiteSpace(pair.Key))
                    {
                        continue;
                    }

                    List<ProjectUnsavedBufferEntry> entries = (pair.Value ?? new List<ProjectUnsavedBufferEntry>())
                        .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.FilePath))
                        .ToList();

                    if (entries.Count > 0)
                    {
                        clean[pair.Key] = entries;
                    }
                }
            }

            return JsonConvert.SerializeObject(clean, Formatting.None);
        }

        /// <summary>Буферы проекта (пустой список, если записи нет).</summary>
        public static IReadOnlyList<ProjectUnsavedBufferEntry> Get(string json, string projectPath)
        {
            string key = NormalizeKey(projectPath);
            if (key == null)
            {
                return new List<ProjectUnsavedBufferEntry>();
            }

            Dictionary<string, List<ProjectUnsavedBufferEntry>> map = Parse(json);
            return map.TryGetValue(key, out List<ProjectUnsavedBufferEntry> entries)
                ? entries
                : new List<ProjectUnsavedBufferEntry>();
        }

        /// <summary>Кладёт (или перезаписывает) буферы проекта. Пустой список удаляет запись.</summary>
        public static string Set(string json, string projectPath, IEnumerable<ProjectUnsavedBufferEntry> entries)
        {
            string key = NormalizeKey(projectPath);
            if (key == null)
            {
                return json ?? string.Empty;
            }

            Dictionary<string, List<ProjectUnsavedBufferEntry>> map = Parse(json);
            List<ProjectUnsavedBufferEntry> list = (entries ?? Enumerable.Empty<ProjectUnsavedBufferEntry>())
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.FilePath))
                .ToList();

            if (list.Count == 0)
            {
                map.Remove(key);
            }
            else
            {
                map[key] = list;
            }

            return Serialize(map);
        }

        /// <summary>Удаляет буферы проекта.</summary>
        public static string Remove(string json, string projectPath)
        {
            string key = NormalizeKey(projectPath);
            if (key == null)
            {
                return json ?? string.Empty;
            }

            Dictionary<string, List<ProjectUnsavedBufferEntry>> map = Parse(json);
            map.Remove(key);
            return Serialize(map);
        }

        private static string NormalizeKey(string projectPath)
        {
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                return null;
            }

            string trimmed = projectPath.Trim().Trim('"').Trim();
            if (trimmed.Length == 0)
            {
                return null;
            }

            try
            {
                string full = Path.GetFullPath(trimmed);
                return full.Length > 3
                    ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    : full;
            }
            catch (Exception)
            {
                return trimmed;
            }
        }
    }
}
