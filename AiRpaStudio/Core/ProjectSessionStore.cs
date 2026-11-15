using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace RPA.Core
{
    /// <summary>
    /// Персист сессий вкладок по проектам: словарь «путь проекта → JSON-состояние вкладок»
    /// (сериализованный <see cref="WorkflowSessionState"/>).
    /// <para>
    /// Нужен, чтобы порядок вкладок и активная вкладка каждого проекта переживали переключение
    /// проектов и перезапуск студии. Ключ — полный путь к проекту (папка project.json либо файл
    /// <c>.sln</c>/<c>.csproj</c>), сравнение регистронезависимое.
    /// </para>
    /// <para>
    /// Логика намеренно без WPF и без <c>Properties.Settings</c>: работает со строкой JSON, как
    /// <see cref="RecentProjectsStore"/>.
    /// </para>
    /// </summary>
    public static class ProjectSessionStore
    {
        /// <summary>Разбирает JSON-словарь; пустой/битый JSON даёт пустой словарь без исключения.</summary>
        public static Dictionary<string, string> Parse(string json)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(json))
            {
                return result;
            }

            try
            {
                Dictionary<string, string> deserialized =
                    JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                if (deserialized == null)
                {
                    return result;
                }

                foreach (KeyValuePair<string, string> pair in deserialized)
                {
                    if (!string.IsNullOrWhiteSpace(pair.Key))
                    {
                        result[pair.Key] = pair.Value;
                    }
                }
            }
            catch (Exception)
            {
                // Битый JSON — просто пустой словарь.
            }

            return result;
        }

        /// <summary>Сериализует словарь в компактный JSON, отбрасывая пустые ключи и пустые значения.</summary>
        public static string Serialize(IReadOnlyDictionary<string, string> map)
        {
            Dictionary<string, string> clean = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (map != null)
            {
                foreach (KeyValuePair<string, string> pair in map)
                {
                    if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrEmpty(pair.Value))
                    {
                        clean[pair.Key] = pair.Value;
                    }
                }
            }

            return JsonConvert.SerializeObject(clean, Formatting.None);
        }

        /// <summary>
        /// Достаёт сессию проекта. Возвращает <c>true</c> и сам JSON, если запись была (даже пустая);
        /// <c>false</c>, если записи нет — тогда вызывающий может взять fallback.
        /// </summary>
        public static bool TryGet(string json, string projectPath, out string sessionJson)
        {
            sessionJson = null;
            string key = NormalizeKey(projectPath);
            if (key == null)
            {
                return false;
            }

            return Parse(json).TryGetValue(key, out sessionJson);
        }

        /// <summary>
        /// Кладёт (или перезаписывает) сессию проекта. Пустая сессия удаляет запись — хранить
        /// «проект без вкладок» незачем.
        /// </summary>
        public static string Set(string json, string projectPath, string sessionJson)
        {
            string key = NormalizeKey(projectPath);
            if (key == null)
            {
                return json ?? string.Empty;
            }

            Dictionary<string, string> map = Parse(json);
            if (string.IsNullOrEmpty(sessionJson))
            {
                map.Remove(key);
            }
            else
            {
                map[key] = sessionJson;
            }

            return Serialize(map);
        }

        /// <summary>Удаляет сессию проекта.</summary>
        public static string Remove(string json, string projectPath)
        {
            string key = NormalizeKey(projectPath);
            if (key == null)
            {
                return json ?? string.Empty;
            }

            Dictionary<string, string> map = Parse(json);
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
                // Корень диска («C:\») не трогаем — иначе он превратится в «C:».
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
