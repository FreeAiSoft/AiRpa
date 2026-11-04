using System;
using System.IO;
using Newtonsoft.Json;
using Agent.Core.Models;

namespace RPA.Chat.Services
{
    /// <summary>Chat extension settings persisted locally as JSON (studio-adapted, no theme/MCP).</summary>
    public class SettingsData
    {
        public string BaseUrl { get; set; } = "https://api.openai.com/v1";
        public string ApiKey { get; set; } = "";
        public string Model { get; set; } = "gpt-4o-mini";

        /// <summary>Sampling temperature sent to the model (0..1).</summary>
        public double Temperature { get; set; } = 0.2;

        /// <summary>Directory containing skills (each skill is a subdirectory with a SKILL.md).</summary>
        public string SkillsPath { get; set; } = "";

        /// <summary>
        /// Тема продукта (студия и чат): <c>"Dark"</c> или <c>"Light"</c>.
        /// <para>
        /// Хранится строкой имени перечисления <see cref="StudioTheme"/>, а не <c>bool</c>: в файле должно
        /// лежать то же значение, что и в коде, иначе появляется второй источник истины по теме.
        /// </para>
        /// <para>
        /// <c>null</c> означает «поля в файле не было» — так выглядит первый запуск и файл старого
        /// формата, поэтому дефолт подставляет <see cref="SettingsStore"/> при загрузке, а не инициализатор:
        /// иначе отличить «тема не сохранена» от «сохранена тёмная» невозможно и миграция не сработает.
        /// </para>
        /// </summary>
        [JsonProperty("Theme", NullValueHandling = NullValueHandling.Ignore)]
        public string Theme { get; set; }

        /// <summary>
        /// Легаси-поле старого формата (<c>true</c> = тёмная). Только для миграции: <see cref="SettingsStore"/>
        /// переносит его в <see cref="Theme"/> и обнуляет, поэтому в файл оно больше не попадает.
        /// </summary>
        [JsonProperty("UseDarkTheme", NullValueHandling = NullValueHandling.Ignore)]
        public bool? LegacyUseDarkTheme { get; set; }

        public int ContextWindowSize { get; set; } = 409600;

        /// <summary>HTTP request timeout in seconds (per provider request).</summary>
        public int TimeoutSeconds { get; set; } = 300;

        /// <summary>Total budget for retrying a failing provider request, in minutes.</summary>
        public int RetryTimeoutMinutes { get; set; } = 30;

        /// <summary>Initial delay between retries, in seconds (doubles each attempt).</summary>
        public int RetryBaseDelaySeconds { get; set; } = 1;

        /// <summary>Maximum delay between retries, in seconds.</summary>
        public int RetryMaxDelaySeconds { get; set; } = 30;

        /// <summary>Price per 1M uncached input (cache-miss) tokens, in USD.</summary>
        public double InputPricePerMillion { get; set; } = 0.27;

        /// <summary>Price per 1M cache-hit input tokens, in USD.</summary>
        public double CacheHitPricePerMillion { get; set; } = 0.07;

        /// <summary>Price per 1M output tokens, in USD.</summary>
        public double OutputPricePerMillion { get; set; } = 1.10;

        // ---- Proxy ----
        public bool ProxyEnabled { get; set; } = false;
        public string ProxyHost { get; set; } = "";
        public int ProxyPort { get; set; } = 8080;
        public string ProxyUsername { get; set; } = "";
        public string ProxyPassword { get; set; } = "";

        /// <summary>Automatically summarize the conversation when approaching the context limit.</summary>
        public bool AutoCompact { get; set; } = true;

        /// <summary>Fraction of the context window that triggers compaction (0..1).</summary>
        public double CompactThreshold { get; set; } = 0.85;

        /// <summary>Fraction of the context window reserved for the model's reply (0..1).</summary>
        public double ReserveFraction { get; set; } = 0.12;

        /// <summary>How many most-recent messages are never summarized/truncated.</summary>
        public int KeepRecentMessages { get; set; } = 6;

        /// <summary>Maximum characters of a tool result fed back into the conversation.</summary>
        public int ToolResultMaxChars { get; set; } = 12000;

        /// <summary>Use the "tool" role for tool results; false falls back to "user".</summary>
        public bool UseToolRole { get; set; } = true;

        /// <summary>How the agent calls tools: "Native" or "JsonChat".</summary>
        public string ToolCallingMode { get; set; } = "JsonChat";

        /// <summary>How the agent uses the project memory bank: "Disabled", "Tool" or "Convention".</summary>
        public string MemoryBankMode { get; set; } = "Disabled";

        /// <summary>Maximum number of agent-loop iterations per run.</summary>
        public int MaxIterations { get; set; } = 8;

        // ---- Agent permissions: category-level gates for the agent tools. ----
        public bool AllowReadFiles { get; set; } = true;
        public bool AllowEditFiles { get; set; } = true;
        public bool AllowExecuteCommand { get; set; } = false;
        public bool AllowWebFetch { get; set; } = false;
        public bool AllowSubagents { get; set; } = false;

        /// <summary>Maximum agent-loop iterations for a single sub-agent.</summary>
        public int SubagentMaxIterations { get; set; } = 4;

        /// <summary>Maximum nesting depth of sub-agents.</summary>
        public int SubagentMaxDepth { get; set; } = 2;

        /// <summary>How to address the user (e.g. a first name).</summary>
        public string UserName { get; set; } = "";

        /// <summary>Whether the agent should address the user by name in its replies.</summary>
        public bool AddressByName { get; set; } = false;

        /// <summary>Speech recognition provider: "Auto", "OpenAiWhisper" or "Vosk".</summary>
        public string SpeechToTextProvider { get; set; } = "Auto";

        /// <summary>Whisper model used for speech-to-text.</summary>
        public string SpeechToTextModel { get; set; } = "whisper-1";

        /// <summary>Language code for speech recognition (e.g. "ru").</summary>
        public string SpeechToTextLanguage { get; set; } = "ru";

        /// <summary>Directory containing the Vosk model (offline fallback).</summary>
        public string VoskModelPath { get; set; } = "";

        /// <summary>Automatically send recognized speech.</summary>
        public bool SpeechAutoSend { get; set; } = false;
    }

    /// <summary>Loads/saves chat settings at %AppData%\AiRpaStudio\Chat\chat_settings.json.</summary>
    public static class SettingsStore
    {
        public static SettingsData Load()
        {
            try
            {
                if (File.Exists(ChatPaths.SettingsFile))
                {
                    var json = File.ReadAllText(ChatPaths.SettingsFile);
                    var settings = JsonConvert.DeserializeObject<SettingsData>(json) ?? new SettingsData();
                    EnsureDefaults(settings);
                    return settings;
                }
            }
            catch
            {
                // ignore read errors — fall back to defaults
            }

            var defaults = new SettingsData();
            EnsureDefaults(defaults);
            return defaults;
        }

        public static void Save(SettingsData settings)
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

        private static void EnsureDefaults(SettingsData settings)
        {
            if (settings == null) return;
            MigrateTheme(settings);
            if (settings.ContextWindowSize <= 0)
                settings.ContextWindowSize = 409600;
            if (double.IsNaN(settings.Temperature) || settings.Temperature < 0.0 || settings.Temperature > 1.0)
                settings.Temperature = 0.2;
            if (settings.TimeoutSeconds <= 0)
                settings.TimeoutSeconds = 300;
            if (settings.RetryTimeoutMinutes <= 0)
                settings.RetryTimeoutMinutes = 30;
            if (settings.RetryBaseDelaySeconds <= 0)
                settings.RetryBaseDelaySeconds = 1;
            if (settings.RetryMaxDelaySeconds <= 0)
                settings.RetryMaxDelaySeconds = 30;
            if (settings.InputPricePerMillion < 0)
                settings.InputPricePerMillion = 0.27;
            if (settings.CacheHitPricePerMillion < 0)
                settings.CacheHitPricePerMillion = 0.07;
            if (settings.OutputPricePerMillion < 0)
                settings.OutputPricePerMillion = 1.10;
            if (settings.CompactThreshold <= 0 || settings.CompactThreshold > 1)
                settings.CompactThreshold = 0.85;
            if (settings.ReserveFraction < 0 || settings.ReserveFraction > 0.5)
                settings.ReserveFraction = 0.12;
            if (settings.KeepRecentMessages <= 0)
                settings.KeepRecentMessages = 6;
            if (settings.ToolResultMaxChars <= 0)
                settings.ToolResultMaxChars = 12000;
            if (settings.MaxIterations <= 0)
                settings.MaxIterations = 8;
            if (settings.SubagentMaxIterations <= 0)
                settings.SubagentMaxIterations = 4;
            if (settings.SubagentMaxDepth < 1)
                settings.SubagentMaxDepth = 2;
            if (settings.SubagentMaxDepth > 5)
                settings.SubagentMaxDepth = 5;
            if (string.IsNullOrWhiteSpace(settings.ToolCallingMode) ||
                (!string.Equals(settings.ToolCallingMode, "Native", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(settings.ToolCallingMode, "JsonChat", StringComparison.OrdinalIgnoreCase)))
                settings.ToolCallingMode = "JsonChat";
            if (string.IsNullOrWhiteSpace(settings.MemoryBankMode) ||
                (!string.Equals(settings.MemoryBankMode, "Disabled", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(settings.MemoryBankMode, "Tool", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(settings.MemoryBankMode, "Convention", StringComparison.OrdinalIgnoreCase)))
                settings.MemoryBankMode = "Disabled";
            if (string.IsNullOrWhiteSpace(settings.SpeechToTextProvider) ||
                (!string.Equals(settings.SpeechToTextProvider, "Auto", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(settings.SpeechToTextProvider, "OpenAiWhisper", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(settings.SpeechToTextProvider, "Vosk", StringComparison.OrdinalIgnoreCase)))
                settings.SpeechToTextProvider = "Auto";
            if (string.IsNullOrWhiteSpace(settings.SpeechToTextModel))
                settings.SpeechToTextModel = "whisper-1";
            if (string.IsNullOrWhiteSpace(settings.SpeechToTextLanguage))
                settings.SpeechToTextLanguage = "ru";
            if (settings.ProxyPort <= 0 || settings.ProxyPort > 65535)
                settings.ProxyPort = 8080;
        }

        /// <summary>
        /// Приводит тему к перечислению <see cref="StudioTheme"/> и переносит старый формат
        /// (<c>{"UseDarkTheme": false}</c>) в строку <see cref="SettingsData.Theme"/>.
        /// Неизвестное значение — тёмная тема: она же дефолт студии.
        /// </summary>
        private static void MigrateTheme(SettingsData settings)
        {
            if (!StudioThemes.TryParse(settings.Theme, out StudioTheme theme))
            {
                // bool? — null означает «поля в файле не было», и тогда остаётся дефолт (тёмная).
                theme = settings.LegacyUseDarkTheme == false ? StudioTheme.Light : StudioTheme.Dark;
            }

            settings.Theme = theme.ToName();
            settings.LegacyUseDarkTheme = null;
        }
    }
}

