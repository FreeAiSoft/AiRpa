using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Agent.Core.Contracts;
using Agent.Core.Services;

namespace RPA.Chat.Services
{
    /// <summary>
    /// Реализация канонического <see cref="ISettingsStore"/> поверх существующего файла
    /// <c>%AppData%\AiRpaStudio\Chat\chat_settings.json</c>.
    /// <para>
    /// Раньше настройки хранил форк <c>RPA.Chat.Services.SettingsData</c> с полями
    /// <c>Theme</c>/<c>UseDarkTheme</c> и RPA-флагами WF. Этот стора переводит файл на
    /// канонический <see cref="SettingsData"/>: <c>Theme</c>/<c>UseDarkTheme</c> мигрируют в
    /// <see cref="SettingsData.BaseTheme"/>, а отсутствующие поля дозаполняются
    /// <see cref="SettingsData.Normalize"/>. RPA-флаги WF живут отдельно в <see cref="RpaAgentOptions"/>.
    /// </para>
    /// </summary>
    public sealed class ChatSettingsStore : ISettingsStore
    {
        public SettingsData Load()
        {
            string rawJson = null;
            try
            {
                if (File.Exists(ChatPaths.SettingsFile))
                    rawJson = File.ReadAllText(ChatPaths.SettingsFile);
            }
            catch
            {
                // ignore read errors — fall back to defaults
            }

            SettingsData settings;
            if (!string.IsNullOrWhiteSpace(rawJson))
            {
                try
                {
                    settings = JsonConvert.DeserializeObject<SettingsData>(rawJson) ?? new SettingsData();
                }
                catch
                {
                    settings = new SettingsData();
                }
            }
            else
            {
                settings = new SettingsData();
            }

            MigrateLegacyTheme(settings, rawJson);
            SettingsData.Normalize(settings);
            return settings;
        }

        public void Save(SettingsData settings)
        {
            if (settings == null) return;
            try
            {
                ChatPaths.EnsureDirectories();
                File.WriteAllText(ChatPaths.SettingsFile, JsonConvert.SerializeObject(settings, Formatting.Indented));
            }
            catch
            {
                // ignore write errors
            }
        }

        /// <summary>
        /// Переносит легаси-поля темы из старого файла в канонический <see cref="SettingsData.BaseTheme"/>.
        /// Канонический тип больше не содержит <c>Theme</c>/<c>UseDarkTheme</c>, поэтому читаем их из
        /// сырого JSON до/после десериализации. Неизвестное/отсутствующее значение оставляет дефолт
        /// (<see cref="SettingsData.Normalize"/> приведёт его к <c>"Dark"</c>).
        /// </summary>
        private static void MigrateLegacyTheme(SettingsData settings, string rawJson)
        {
            if (settings == null || string.IsNullOrWhiteSpace(rawJson))
                return;

            try
            {
                var obj = JObject.Parse(rawJson);
                var legacyTheme = (string)obj["Theme"];
                var legacyUseDark = obj["UseDarkTheme"]?.Type == JTokenType.Boolean
                    ? (bool?)obj["UseDarkTheme"]
                    : null;

                string baseTheme = null;
                if (!string.IsNullOrWhiteSpace(legacyTheme))
                    baseTheme = string.Equals(legacyTheme, "Light", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark";
                else if (legacyUseDark == false)
                    baseTheme = "Light";

                if (!string.IsNullOrWhiteSpace(baseTheme))
                    settings.BaseTheme = baseTheme;
            }
            catch
            {
                // ignore migration errors — Normalize will restore a valid theme
            }
        }
    }
}
