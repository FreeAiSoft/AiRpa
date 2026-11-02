using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace RPA.Helpers
{
    /// <summary>
    /// Единственный источник шрифтов студии (этап 12 плана docs\Vs2026_Theme_Plan.md).
    /// <para>
    /// Правило проекта: шрифт — <b>Roboto везде</b>. В XAML нельзя писать
    /// <c>FontFamily="Consolas"</c> / <c>FontFamily="Segoe UI"</c> — только
    /// <c>{DynamicResource Studio.FontFamily}</c> (или <c>Studio.CodeFontFamily</c> для редакторов кода),
    /// в C# — только <see cref="UiFont"/> / <see cref="CodeFont"/>. Стражи:
    /// <c>AiRpaStudio.Tests\StudioFontTests.cs</c>.
    /// </para>
    /// <para>
    /// Начертания Roboto лежат в сборке студии как ресурсы
    /// (<c>Resources\Roboto\*.ttf</c>), поэтому шрифт работает и на машине без установленного Roboto.
    /// Порядок выбора: встроенный шрифт → установленный «Roboto» → системный fallback (WPF подставит сам).
    /// Исключений хелпер не бросает: без шрифта окно просто отрисуется системным.
    /// </para>
    /// </summary>
    public static class StudioFonts
    {
        /// <summary>Имя семейства для всего интерфейса студии.</summary>
        public const string FamilyName = "Roboto";

        /// <summary>
        /// Имя семейства для редакторов кода и XAML.
        /// Отдельная константа — единственная точка правки, если для кода понадобится моноширинный Roboto Mono.
        /// </summary>
        public const string CodeFamilyName = FamilyName;

        /// <summary>Ключ <see cref="FontFamily"/> интерфейса (его же читает тема AvalonDock и сторонние сборки).</summary>
        public const string FontFamilyKey = "Studio.FontFamily";

        /// <summary>Ключ <see cref="FontFamily"/> редакторов кода и XAML.</summary>
        public const string CodeFontFamilyKey = "Studio.CodeFontFamily";

        /// <summary>Ключ <see cref="string"/> с именем семейства — для сборок, которым нужен текст, а не <see cref="FontFamily"/>.</summary>
        public const string FontFamilyNameKey = "Studio.FontFamilyName";

        /// <summary>Ключи, которые обязан записывать <see cref="Apply(ResourceDictionary)"/>.</summary>
        public static readonly IReadOnlyList<string> ResourceKeys = new[]
        {
            FontFamilyKey,
            CodeFontFamilyKey,
            FontFamilyNameKey
        };

        private const string RobotoFolderUri = "pack://application:,,,/RPA;component/Resources/Roboto/";

        private static readonly Lazy<FontFamily> UiFamily = new Lazy<FontFamily>(() => Load(FamilyName));
        private static readonly Lazy<FontFamily> CodeFamily = new Lazy<FontFamily>(() => Load(CodeFamilyName));

        /// <summary>Шрифт интерфейса (Roboto).</summary>
        public static FontFamily UiFont => UiFamily.Value;

        /// <summary>Шрифт редакторов кода и XAML.</summary>
        public static FontFamily CodeFont => CodeFamily.Value;

        /// <summary>Пишет шрифты в ресурсы приложения (после каждой смены темы не требуется, но безопасно).</summary>
        public static void ApplyToApplication()
        {
            Apply(Application.Current?.Resources);
        }

        /// <summary>Пишет шрифты в указанный словарь. Существующие значения заменяются (DynamicResource пересчитается).</summary>
        public static void Apply(ResourceDictionary target)
        {
            if (target == null)
            {
                return;
            }

            target[FontFamilyKey] = UiFont;
            target[CodeFontFamilyKey] = CodeFont;
            target[FontFamilyNameKey] = FamilyName;
        }

        /// <summary>Шрифт из ресурсов словаря (для сборок, у которых нет ссылки на хелперы студии).</summary>
        public static FontFamily Resolve(ResourceDictionary resources, string key, FontFamily fallback)
        {
            return resources != null && key != null && resources.Contains(key) && resources[key] is FontFamily family ? family : fallback;
        }

        /// <summary>Встроенный Roboto из ресурсов сборки, иначе установленный в системе, иначе системный fallback WPF.</summary>
        private static FontFamily Load(string familyName)
        {
            FontFamily embedded = TryCreateEmbedded(familyName);
            return embedded ?? new FontFamily(familyName);
        }

        private static FontFamily TryCreateEmbedded(string familyName)
        {
            try
            {
                Uri baseUri = new Uri(RobotoFolderUri, UriKind.Absolute);
                FontFamily family = new FontFamily(baseUri, "./#" + familyName);

                // Без файлов начертаний GetTypefaces() молча отдаёт глобальный fallback,
                // поэтому сначала проверяем, что каталог шрифтов вообще читается.
                return HasTypefaces(family) ? family : null;
            }
            catch (Exception)
            {
                // Нет pack-схемы/ресурсов (например, усечённая сборка) — работаем установленным шрифтом.
                return null;
            }
        }

        private static bool HasTypefaces(FontFamily family)
        {
            foreach (Typeface typeface in family.GetTypefaces())
            {
                if (typeface != null)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
