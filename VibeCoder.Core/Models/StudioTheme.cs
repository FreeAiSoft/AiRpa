using System;

namespace VibeCoder.Core.Models
{
    /// <summary>
    /// Тема студии и её контролов (окно, докинг, чат, редакторы кода, поверхность дизайнера WF).
    /// <para>
    /// Единственный источник истины по теме. Раньше тема ходила по коду как <c>bool dark</c> и как
    /// «угадывание» по яркости <c>MaterialDesign.Brush.Background</c>; из-за этого разные подсистемы
    /// могли разойтись на один переключатель. Теперь везде используется это перечисление, а активное
    /// значение хранит <c>RPA.Helpers.StudioPalette.Current</c>.
    /// </para>
    /// <para>
    /// Ровно две темы, третьей «смешанной» нет. <see cref="Dark"/> = 0 намеренно: <c>default(StudioTheme)</c>
    /// совпадает с дефолтом студии (<c>App.xaml</c> <c>BaseTheme="Dark"</c>, дефолт настроек чата),
    /// поэтому «неинициализированное» состояние невозможно.
    /// </para>
    /// <para>
    /// Тип лежит в <c>VibeCoder.Core</c>, потому что тема нужна двум сторонам сразу: студии
    /// (<c>AiRpaStudio</c>) и панели чата (<c>RPA.Chat</c>, отдельная сборка). Дублировать перечисление
    /// в двух сборках нельзя — это вернуло бы рассинхрон, ради устранения которого тип и вводится.
    /// </para>
    /// </summary>
    public enum StudioTheme
    {
        /// <summary>Тёмная тема. Тема по умолчанию: с неё стартует студия и чат.</summary>
        Dark = 0,

        /// <summary>Светлая тема.</summary>
        Light = 1
    }

    /// <summary>Операции над <see cref="StudioTheme"/>: противоположная тема и имена для персиста.</summary>
    public static class StudioThemes
    {
        /// <summary>Имя тёмной темы в файлах настроек.</summary>
        public const string DarkName = nameof(StudioTheme.Dark);

        /// <summary>Имя светлой темы в файлах настроек.</summary>
        public const string LightName = nameof(StudioTheme.Light);

        /// <summary>Противоположная тема: нужна проходам темизации, узнающим работу предыдущей темы.</summary>
        public static StudioTheme Other(this StudioTheme theme)
        {
            return theme == StudioTheme.Dark ? StudioTheme.Light : StudioTheme.Dark;
        }

        /// <summary>Имя темы для файла настроек: <c>"Dark"</c> или <c>"Light"</c>.</summary>
        public static string ToName(this StudioTheme theme)
        {
            return theme == StudioTheme.Light ? LightName : DarkName;
        }

        /// <summary>
        /// Разбирает имя темы без учёта регистра. Неизвестное/пустое имя — <c>false</c>
        /// (вызывающий сам решает, какая тема является запасной).
        /// </summary>
        public static bool TryParse(string name, out StudioTheme theme)
        {
            theme = StudioTheme.Dark;

            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            string trimmed = name.Trim();

            if (string.Equals(trimmed, DarkName, StringComparison.OrdinalIgnoreCase))
            {
                theme = StudioTheme.Dark;
                return true;
            }

            if (string.Equals(trimmed, LightName, StringComparison.OrdinalIgnoreCase))
            {
                theme = StudioTheme.Light;
                return true;
            }

            return false;
        }

        /// <summary>Разбирает имя темы; неизвестное имя даёт <paramref name="fallback"/>.</summary>
        public static StudioTheme Parse(string name, StudioTheme fallback = StudioTheme.Dark)
        {
            return TryParse(name, out StudioTheme theme) ? theme : fallback;
        }
    }
}
