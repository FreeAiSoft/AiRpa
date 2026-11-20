using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RPA.Chat.Services
{
    /// <summary>
    /// RPA-специфичные флаги способов написания WF (план «3 галочки»). Эти флаги не входят в
    /// канонический <see cref="Agent.Core.Services.SettingsData"/> тулкита, поэтому вынесены в
    /// отдельный хост-контракт и персистятся отдельным файлом <c>rpa_agent_options.json</c>.
    /// </summary>
    public static class RpaAgentOptions
    {
        private static bool _loaded;

        /// <summary>Разрешает агент-инструмент «палитра» (MCP-способ написания WF).</summary>
        public static bool AllowWfPalette { get; set; } = true;

        /// <summary>Разрешает агент-инструмент «JSON AST» (MCP-способ написания WF).</summary>
        public static bool AllowWfJsonAst { get; set; } = true;

        /// <summary>Разрешает агент-инструмент «code-first» (MCP-способ написания WF).</summary>
        public static bool AllowWfCodeFirst { get; set; } = true;

        public static string OptionsFile => Path.Combine(ChatPaths.DataDirectory, "rpa_agent_options.json");

        /// <summary>
        /// Загружает флаги из файла один раз за сессию (идемпотентно). При первом запуске после
        /// миграции читает легаси-поля из <c>chat_settings.json</c>, чтобы не сбросить сохранённые
        /// пользователем значения.
        /// </summary>
        public static void Load()
        {
            if (_loaded) return;
            _loaded = true;

            try
            {
                if (File.Exists(OptionsFile))
                {
                    var data = JsonConvert.DeserializeObject<RpaAgentOptionsData>(File.ReadAllText(OptionsFile));
                    if (data != null)
                    {
                        AllowWfPalette = data.AllowWfPalette;
                        AllowWfJsonAst = data.AllowWfJsonAst;
                        AllowWfCodeFirst = data.AllowWfCodeFirst;
                    }
                }
                else
                {
                    MigrateFromLegacySettings();
                }
            }
            catch
            {
                // ignore read errors — keep defaults
            }
        }

        public static void Save()
        {
            try
            {
                ChatPaths.EnsureDirectories();
                File.WriteAllText(OptionsFile, JsonConvert.SerializeObject(new RpaAgentOptionsData
                {
                    AllowWfPalette = AllowWfPalette,
                    AllowWfJsonAst = AllowWfJsonAst,
                    AllowWfCodeFirst = AllowWfCodeFirst
                }, Formatting.Indented));
            }
            catch
            {
                // ignore write errors
            }
        }

        /// <summary>Сбрасывает состояние (тесты): дефолты и разрешение повторной загрузки.</summary>
        public static void Reset()
        {
            AllowWfPalette = true;
            AllowWfJsonAst = true;
            AllowWfCodeFirst = true;
            _loaded = false;
        }

        private static void MigrateFromLegacySettings()
        {
            try
            {
                if (!File.Exists(ChatPaths.SettingsFile))
                    return;

                var obj = JObject.Parse(File.ReadAllText(ChatPaths.SettingsFile));
                if (obj["AllowWfPalette"]?.Type == JTokenType.Boolean)
                    AllowWfPalette = (bool)obj["AllowWfPalette"];
                if (obj["AllowWfJsonAst"]?.Type == JTokenType.Boolean)
                    AllowWfJsonAst = (bool)obj["AllowWfJsonAst"];
                if (obj["AllowWfCodeFirst"]?.Type == JTokenType.Boolean)
                    AllowWfCodeFirst = (bool)obj["AllowWfCodeFirst"];
            }
            catch
            {
                // ignore migration errors — keep defaults
            }
        }

        private sealed class RpaAgentOptionsData
        {
            public bool AllowWfPalette { get; set; } = true;
            public bool AllowWfJsonAst { get; set; } = true;
            public bool AllowWfCodeFirst { get; set; } = true;
        }
    }
}
