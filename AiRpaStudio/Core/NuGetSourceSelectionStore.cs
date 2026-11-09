using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RPA.Core
{
    /// <summary>
    /// Персист выбранного источника пакетов NuGet на воркспейс. Значение хранится в одной строковой
    /// настройке <c>NuGetSelectedSources</c> как JSON-словарь <c>workspaceFolder → sourceUrl</c>.
    /// <para>
    /// Логика намеренно без WPF и без <c>Properties.Settings</c>: чтение/запись строки проверяются
    /// юнит-тестами (<c>AiRpaStudio.Tests\NuGetSourceSelectionStoreTests.cs</c>), а панель NuGet
    /// подключается к хранилищу хуками <c>LoadSelectedSourceUrl</c>/<c>SaveSelectedSourceUrl</c>.
    /// </para>
    /// </summary>
    public static class NuGetSourceSelectionStore
    {
        /// <summary>Возвращает сохранённый URL источника для воркспейса или <c>null</c>.</summary>
        public static string Read(string json, string workspaceFolder)
        {
            string key = NormalizeKey(workspaceFolder);
            if (key == null || string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                if (!(JToken.Parse(json) is JObject obj))
                    return null;

                JProperty match = obj.Properties().FirstOrDefault(
                    property => string.Equals(NormalizeKey(property.Name), key, StringComparison.OrdinalIgnoreCase));
                if (match != null && match.Value != null && match.Value.Type == JTokenType.String)
                {
                    string url = match.Value.Value<string>();
                    return string.IsNullOrWhiteSpace(url) ? null : url;
                }

                return null;
            }
            catch (Exception)
            {
                // Битый JSON (обрезанный файл настроек, ручная правка) — выбора просто нет.
                return null;
            }
        }

        /// <summary>
        /// Записывает URL источника для воркспейса и возвращает новый JSON. Пустой <paramref name="sourceUrl"/>
        /// удаляет запись воркспейса.
        /// </summary>
        public static string Write(string json, string workspaceFolder, string sourceUrl)
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

            if (!string.IsNullOrWhiteSpace(sourceUrl))
                obj[key] = sourceUrl;

            return obj.ToString(Formatting.None);
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
