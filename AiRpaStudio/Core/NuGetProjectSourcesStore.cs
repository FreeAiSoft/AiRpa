using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RPA.NuGet.Models;

namespace RPA.Core
{
    /// <summary>
    /// Персист per-project списка источников пакетов NuGet. Значение хранится в JSON-файле
    /// <c>NuGetPaths.ProjectSourcesFile</c> как словарь <c>workspaceFolder → [источники]</c>.
    /// <para>
    /// Логика намеренно без WPF и без <c>Properties.Settings</c>: чтение/запись строки проверяются
    /// юнит-тестами, а панель NuGet подключается к хранилищу хуками
    /// <c>LoadProjectSources</c>/<c>SaveProjectSources</c>.
    /// </para>
    /// </summary>
    public static class NuGetProjectSourcesStore
    {
        /// <summary>Возвращает сохранённый список источников для воркспейса или пустой список.</summary>
        public static IReadOnlyList<NuGetSourceDefinition> Read(string json, string workspaceFolder)
        {
            string key = NormalizeKey(workspaceFolder);
            if (key == null || string.IsNullOrWhiteSpace(json))
                return new List<NuGetSourceDefinition>();

            try
            {
                if (!(JToken.Parse(json) is JObject obj))
                    return new List<NuGetSourceDefinition>();

                JProperty match = obj.Properties().FirstOrDefault(
                    property => string.Equals(NormalizeKey(property.Name), key, StringComparison.OrdinalIgnoreCase));

                if (match == null || !(match.Value is JArray array))
                    return new List<NuGetSourceDefinition>();

                return array.OfType<JObject>()
                    .Select(ParseEntry)
                    .Where(entry => entry != null)
                    .ToList();
            }
            catch (Exception)
            {
                // Битый JSON (обрезанный файл, ручная правка) — списка просто нет.
                return new List<NuGetSourceDefinition>();
            }
        }

        /// <summary>
        /// Записывает список источников для воркспейса и возвращает новый JSON. Пустой
        /// <paramref name="sources"/> удаляет запись воркспейса.
        /// </summary>
        public static string Write(string json, string workspaceFolder, IEnumerable<NuGetSourceDefinition> sources)
        {
            JObject obj = ParseOrEmpty(json);
            string key = NormalizeKey(workspaceFolder);

            if (key == null)
                return obj.ToString(Formatting.None);

            // Удаляем прежнюю запись этого воркспейса (ключи Windows сравниваются без учёта регистра).
            foreach (JProperty property in obj.Properties().ToList())
            {
                if (string.Equals(NormalizeKey(property.Name), key, StringComparison.OrdinalIgnoreCase))
                    property.Remove();
            }

            var entries = (sources ?? Enumerable.Empty<NuGetSourceDefinition>())
                .Where(source => source != null && !string.IsNullOrWhiteSpace(source.Url))
                .ToList();

            if (entries.Count == 0)
                return obj.ToString(Formatting.None);

            var array = new JArray();
            foreach (NuGetSourceDefinition source in entries)
            {
                array.Add(new JObject
                {
                    ["name"] = string.IsNullOrWhiteSpace(source.Name) ? source.Url : source.Name,
                    ["url"] = source.Url,
                    ["enabled"] = source.IsEnabled
                });
            }

            obj[key] = array;
            return obj.ToString(Formatting.None);
        }

        private static NuGetSourceDefinition ParseEntry(JObject entry)
        {
            string url = entry["url"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(url))
                return null;

            return new NuGetSourceDefinition
            {
                Name = entry["name"]?.Value<string>() ?? url,
                Url = url,
                IsEnabled = !(entry["enabled"] is JValue enabled) || enabled.Value<bool>()
            };
        }

        private static JObject ParseOrEmpty(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new JObject();

            try
            {
                return JToken.Parse(json) as JObject ?? new JObject();
            }
            catch (Exception)
            {
                return new JObject();
            }
        }

        private static string NormalizeKey(string workspaceFolder)
        {
            if (string.IsNullOrWhiteSpace(workspaceFolder))
                return null;

            string result = workspaceFolder.Trim();
            if (result.Length > 3)
                result = result.TrimEnd('\\', '/');

            return result.Length == 0 ? null : result;
        }
    }
}
