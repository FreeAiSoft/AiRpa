using System;
using System.Activities.Presentation;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

using RPA.Logging;

namespace RPA.Helpers
{
    /// <summary>
    /// Цвета дизайнера WF <b>до первого кадра</b>: подмена словаря
    /// <c>System.Activities.Presentation;component/System/Activities/Presentation/ColorResources.xaml</c>
    /// в кэше <c>CachedResourceDictionaryExtension</c> (F-05 плана docs\Vs2026_Theme_Plan.md).
    /// <para>
    /// Почему это нужно. Шаблоны WF (<c>WorkflowElementDesignerDefaultTemplate.xaml</c>,
    /// <c>DesignerView</c>, контекстные меню, shell bar) подмешивают <c>ColorResources.xaml</c> в
    /// <b>свой</b> <c>ControlTemplate.Resources</c> через <c>{wf:CachedResourceDictionaryExtension Source=…}</c>.
    /// Это расширение раздаёт <b>один и тот же</b> экземпляр словаря всем шаблонам из статического кэша
    /// (<c>CachedResourceDictionaryExtension.Cache</c>, ключ — URI). Поэтому локальные значения и словари
    /// приложения до плиты активности не доходят, а единственный способ не «докрашивать» элементы после
    /// построения — положить в кэш свой словарь <b>до</b> того, как WF впервые разберёт шаблон.
    /// </para>
    /// <para>
    /// Что даёт. Первый же кадр активности (открытие документа, перетаскивание из панели «Активности»,
    /// «провал» в активность) строится на кистях палитры: кадра с цветами WF (<c>#FFFFFFFF</c>,
    /// <c>#FFE9ECEE</c>, <c>#FFD0D7E2</c>) больше нет, значит нет и мерцания. Пост-покраска активностей
    /// (<see cref="WfActivityTheme"/>) в этом режиме не выполняется вовсе.
    /// </para>
    /// <para>
    /// Кисти — <b>связанные</b> (<see cref="SolidColorBrush.ColorProperty"/> привязан к
    /// <see cref="ThemeColor"/>-источнику токена), а не замороженные константы. Причина: после загрузки
    /// документа WF замораживает кисти шаблона и делает словарь read-only, поэтому менять цвет постфактум
    /// нечем. Связанную кисть заморозить нельзя (<c>CanFreeze == false</c>), WPF делает её клон на элемент и
    /// <b>копирует выражение привязки</b> — смена <see cref="ThemeColor.Value"/> перекрашивает всё созданное
    /// без обхода дерева (замерено: 59 ключей × 4 типа активностей, ноль проходов темизации).
    /// </para>
    /// <para>
    /// Границы. Ключи <c>WorkflowDesignerColors.PropertyInspector*</c> в <c>ColorResources.xaml</c>
    /// отсутствуют (они живут в <c>FontAndColorResources</c> → <c>Application.Resources</c>), панель
    /// «Свойства» остаётся на <see cref="WfDesignerTheme"/>; Toolbox <c>ColorResources</c> не использует.
    /// <c>DesignerViewBackground</c>/<c>DesignerViewForeground</c> в словаре WF тоже нет — их по-прежнему
    /// кладёт <see cref="WfDesignerTheme"/> в ресурсы приложения и документа.
    /// </para>
    /// <para>
    /// Отказоустойчивость. Любой шаг подмены (нет поля кэша, не загрузился словарь WF) возвращает
    /// <c>false</c> и оставляет <see cref="IsInstalled"/> = <c>false</c> — студия работает в прежнем режиме
    /// <see cref="WfActivityTheme"/>. Режимы взаимоисключающие: в режиме кэша пост-покраска выключена,
    /// потому что локальные значения перекрыли бы связанные кисти. Ручной тумблер поддержки — switch
    /// <c>Studio.WfLegacyTheming</c> в <c>AiRpaStudio.exe.config</c>.
    /// </para>
    /// </summary>
    internal static class WfColorResourcesOverride
    {
        /// <summary>Приватное статическое поле кэша (единственная закрытая точка, к которой нужен доступ).</summary>
        internal const string CacheFieldName = "Cache";

        /// <summary>Аварийный switch конфигурации: принудительно оставить прежнюю пост-покраску.</summary>
        internal const string LegacyThemingSwitch = "Studio.WfLegacyTheming";

        /// <summary>Компонентный URI словаря цветов WF (без ведущего слэша).</summary>
        internal const string ColorResourcesComponent =
            "System.Activities.Presentation;component/System/Activities/Presentation/ColorResources.xaml";

        /// <summary>Схема абсолютного pack-URI (литерал совпадает с тем, что стоит в шаблонах WF).</summary>
        internal const string PackUriPrefix = "pack://application:,,,/";

        /// <summary>Тот же URI в абсолютной форме — второй ключ кэша, которым пользуется часть шаблонов WF.</summary>
        internal const string ColorResourcesPackUriText = PackUriPrefix + ColorResourcesComponent;

        /// <summary>Ключ кэша, которым пользуются шаблоны WF: относительный URI с ведущим слэшем.</summary>
        internal static readonly Uri ColorResourcesRelativeUri =
            new Uri("/" + ColorResourcesComponent, UriKind.Relative);

        /// <summary>Источники цвета по токену палитры: один источник на токен, а не на ключ.</summary>
        private static readonly Dictionary<string, ThemeColor> Sources =
            new Dictionary<string, ThemeColor>(StringComparer.Ordinal);

        /// <summary>Абсолютный ключ кэша (создаётся один раз, может остаться <c>null</c> — см. ниже).</summary>
        private static Uri _packUri;

        /// <summary>
        /// Пары «ключ ресурса WF → токен <see cref="StudioPalette"/>» для поверхности дизайнера.
        /// <para>
        /// Только ключи, которые объявляет <c>ColorResources.xaml</c> (проверяются при установке: ключ,
        /// которого в словаре WF нет, пропускается с записью в лог). Ключи инспектора свойств здесь
        /// намеренно отсутствуют — он берёт цвета из <c>FontAndColorResources</c>.
        /// </para>
        /// </summary>
        internal static IReadOnlyDictionary<string, string> OverrideTokens { get; } =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // Плита, рамка и подпись элемента активности.
                ["WorkflowViewElementBackground"] = StudioPalette.SurfaceToken,
                ["WorkflowViewElementBorderBrush"] = StudioPalette.SeparatorStrongToken,
                ["WorkflowViewElementSelectedBackground"] = StudioPalette.SelectedToken,
                ["WorkflowViewElementSelectedBorder"] = StudioPalette.SeparatorStrongToken,
                ["WorkflowViewElementCaptionBrush"] = StudioPalette.ForegroundToken,
                ["WorkflowViewElementSelectedCaptionBrush"] = StudioPalette.ForegroundToken,

                // Поверхность и служебные слои дизайнера. DesignerViewBackground — ключ неявного стиля
                // WorkflowViewElement: именно он даёт белую плиту активности, если его не переопределить.
                ["DesignerViewBackground"] = StudioPalette.SurfaceToken,
                ["DesignerViewStatusBarBackground"] = StudioPalette.SurfaceAltToken,
                ["ParallelSpacerColor"] = StudioPalette.SeparatorToken,
                ["FlowchartConnectorColor"] = StudioPalette.SeparatorStrongToken,

                // Shell bar (Imports / Variables / Arguments / Zoom / PanMode). В словаре WF это
                // градиентные кисти; палитра студии однотонная, стопы не нужны.
                ["ShellBarBackgroundBrush"] = StudioPalette.SurfaceToken,
                ["ShellBarControlBackground"] = StudioPalette.SurfaceToken,
                ["ShellBarForegroundActiveColor"] = StudioPalette.ForegroundToken,
                ["ShellBarForegroundInactiveColor"] = StudioPalette.ForegroundMutedToken,
                ["ShellStatusBarSelectedBrush"] = StudioPalette.SelectedToken,
                ["ShellStatusBarPressedBrush"] = StudioPalette.SelectedToken,
                ["ShellStatusBarHoverBrush"] = StudioPalette.HoverToken,

                // Контекстные меню дизайнера.
                ["ContextMenuBackgroundBrush"] = StudioPalette.SurfaceToken,
                ["ContextMenuBorderBrush"] = StudioPalette.SeparatorStrongToken,
                ["ContextMenuIconAreaBrush"] = StudioPalette.SurfaceToken,
                ["ContextMenuTextBrush"] = StudioPalette.ForegroundToken,
                ["ContextMenuTextHoverBrush"] = StudioPalette.ForegroundToken,
                ["ContextMenuTextSelectedBrush"] = StudioPalette.ForegroundToken,
                ["ContextMenuTextDisabledBrush"] = StudioPalette.ForegroundMutedToken,
                ["ContextMenuItemHoverBorderBrush"] = StudioPalette.SeparatorStrongToken,
                ["ContextMenuSeparatorBrush"] = StudioPalette.SeparatorToken
            };

        /// <summary>
        /// Второй ключ кэша. Собирается лениво: Uri со схемой <c>pack</c> разбирается только после
        /// инициализации app-модели WPF — до этого схема не зарегистрирована и конструктор бросает
        /// <see cref="UriFormatException"/> (например, в тестах, где <c>Application</c> не создаётся).
        /// </summary>
        internal static Uri ColorResourcesPackUri => _packUri ?? (_packUri = TryCreatePackUri());

        /// <summary>
        /// Словарь WF уже подменён нашим: цвета дизайнера приходят из палитры на этапе шаблона.
        /// Взаимоисключающий признак — в этом режиме <see cref="WfActivityTheme"/> не красит активности.
        /// </summary>
        internal static bool IsInstalled { get; private set; }

        /// <summary>Прежний режим включён принудительно (switch <see cref="LegacyThemingSwitch"/>).</summary>
        internal static bool IsLegacyForced
        {
            get
            {
                bool forced = false;
                AppContext.TryGetSwitch(LegacyThemingSwitch, out forced);
                return forced;
            }
        }

        /// <summary>
        /// Ставит словарь палитры в кэш WF. Вызывать <b>один раз при старте</b> — до создания первого
        /// <c>WorkflowDesigner</c>/<c>DesignerMetadata().Register()</c>; повторный вызов только обновляет цвет.
        /// </summary>
        /// <returns><c>true</c> — режим кэша включён; <c>false</c> — работает прежняя пост-покраска.</returns>
        internal static bool TryInstall(StudioThemeColors palette)
        {
            if (IsInstalled)
            {
                ApplyPalette(palette);
                return true;
            }

            if (IsLegacyForced)
            {
                Log.Write("Цвета WF: принудительно включён прежний режим (" + LegacyThemingSwitch + ").");
                return false;
            }

            IDictionary cache = CacheDictionary();
            if (cache == null)
            {
                Log.Write("Цвета WF: поле " + CacheFieldName + " не найдено — работает пост-покраска.");
                return false;
            }

            ResourceDictionary defaults = LoadWorkflowDefaults();
            if (defaults == null)
            {
                Log.Write("Цвета WF: словарь ColorResources.xaml не загрузился — работает пост-покраска.");
                return false;
            }

            return TryInstall(palette, defaults, cache);
        }

        /// <summary>
        /// Установка в явно заданный кэш и на явно заданном снимке словаря WF. Отдельная точка нужна
        /// тестам: глобальный кэш WF на процесс один, и трогать его в тестах нельзя.
        /// </summary>
        internal static bool TryInstall(StudioThemeColors palette, ResourceDictionary workflowDefaults, IDictionary cache)
        {
            if (palette == null || workflowDefaults == null || cache == null)
            {
                return false;
            }
            if (IsInstalled)
            {
                // Повторный вызов — это смена темы, а не переустановка: словарь в кэше WF менять нельзя,
                // он уже отдан шаблонам. Перекрашивают только источники связанных кистей.
                ApplyPalette(palette);
                return true;
            }



            ResourceDictionary dictionary = BuildDictionary(palette, workflowDefaults);

            // Ключи кэша: часть шаблонов WF ссылается относительным путём, часть — абсолютным pack-URI.
            cache[ColorResourcesRelativeUri] = dictionary;

            Uri packUri = ColorResourcesPackUri;
            if (packUri == null)
            {
                Log.Write("Цвета WF: pack-URI недоступен, словарь установлен только по относительному ключу.");
            }
            else
            {
                cache[packUri] = dictionary;
            }

            IsInstalled = true;
            ApplyPalette(palette);

            Log.Write("Цвета WF: словарь палитры установлен в кэш шаблонов (" + Sources.Count + " цветов, "
                + OverrideTokens.Count + " ключей).");
            return true;
        }

        /// <summary>
        /// Собирает словарь для кэша: снимок WF сшивается в <c>MergedDictionaries</c> (непереопределённые
        /// ключи остаются как есть), переопределённые — связанные кисти палитры.
        /// </summary>
        internal static ResourceDictionary BuildDictionary(StudioThemeColors palette, ResourceDictionary workflowDefaults)
        {
            ResourceDictionary dictionary = new ResourceDictionary();

            if (workflowDefaults != null)
            {
                dictionary.MergedDictionaries.Add(workflowDefaults);
            }

            if (palette == null)
            {
                return dictionary;
            }

            List<string> missing = new List<string>();

            foreach (KeyValuePair<string, string> pair in OverrideTokens)
            {
                if (workflowDefaults == null || !workflowDefaults.Contains(pair.Key))
                {
                    // Например, ключи инспектора свойств объявляет FontAndColorResources, а не ColorResources.xaml.
                    missing.Add(pair.Key);
                    continue;
                }

                dictionary[pair.Key] = CreateBoundBrush(SourceFor(pair.Value, palette));
            }

            if (missing.Count > 0)
            {
                Log.Write("Цвета WF: в ColorResources.xaml нет ключей " + string.Join(", ", missing) + ".");
            }

            return dictionary;
        }

        /// <summary>
        /// Переводит связанные кисти на другую палитру: меняются только значения источников, дерево не
        /// обходится. Вызывается из <see cref="WfDesignerTheme.ApplyToApplication(ResourceDictionary, StudioThemeColors)"/>
        /// — при старте и на каждой смене темы.
        /// </summary>
        internal static void ApplyPalette(StudioThemeColors palette)
        {
            if (palette == null || Sources.Count == 0)
            {
                return;
            }

            foreach (KeyValuePair<string, ThemeColor> pair in Sources)
            {
                if (palette.TryGet(pair.Key, out Color color))
                {
                    pair.Value.Value = color;
                }
            }
        }

        /// <summary>
        /// Кисть палитры для декларативных стилей (режим кэша): та же связанная кисть, что и в словаре WF,
        /// поэтому смена темы перекрашивает стилизованные элементы без нового прохода.
        /// </summary>
        internal static SolidColorBrush CreatePaletteBrush(string token, StudioThemeColors palette)
        {
            return IsInstalled ? CreateBoundBrush(SourceFor(token, palette)) : null;
        }

        /// <summary>Снимает установку (только для тестов: кэш WF и признак режима — на процесс).</summary>
        internal static void Reset()
        {
            Sources.Clear();
            IsInstalled = false;
        }

        /// <summary>Поле кэша WF (через него тест проверяет, что версия WF не сменила контракт).</summary>
        internal static FieldInfo FindCacheField()
        {
            return typeof(CachedResourceDictionaryExtension).GetField(
                CacheFieldName,
                BindingFlags.NonPublic | BindingFlags.Static);
        }

        /// <summary>
        /// Снимок словаря WF. Загружается по pack-URI, потому что установка может выполняться до появления
        /// окна; при неудаче пробуются остальные формы URI.
        /// </summary>
        internal static ResourceDictionary LoadWorkflowDefaults()
        {
            // Сборка WF должна быть загружена до разрешения pack-URI: WPF ищет её по простому имени
            // (BaseUriHelper → Assembly.Load) и без загруженной сборки бросает FileNotFoundException.
            _ = typeof(CachedResourceDictionaryExtension).Assembly;


            Uri packUri = ColorResourcesPackUri;
            ResourceDictionary dictionary = packUri == null ? null : TryLoadSource(packUri);
            if (dictionary != null)
            {
                return dictionary;
            }

            dictionary = TryLoadSource(ColorResourcesRelativeUri);
            if (dictionary != null)
            {
                return dictionary;
            }

            try
            {
                return Application.LoadComponent(new Uri(ColorResourcesComponent, UriKind.Relative)) as ResourceDictionary;
            }
            catch (Exception ex)
            {
                Log.Write("Цвета WF: снимок ColorResources.xaml не загрузился.", ex);
                return null;
            }
        }

        private static IDictionary CacheDictionary()
        {
            try
            {
                return FindCacheField()?.GetValue(null) as IDictionary;
            }
            catch (Exception ex)
            {
                Log.Write("Цвета WF: кэш шаблонов недоступен.", ex);
                return null;
            }
        }

        /// <summary>
        /// Абсолютный ключ кэша. Обращение к <see cref="Application.ResourceAssembly"/> нужно как побочный
        /// эффект: оно инициализирует app-модель WPF и регистрирует схему <c>pack</c>, без которой
        /// <see cref="Uri"/> такого вида не создаётся.
        /// </summary>
        private static Uri TryCreatePackUri()
        {
            try
            {
                _ = Application.ResourceAssembly;

                return new Uri(ColorResourcesPackUriText, UriKind.Absolute);
            }
            catch (Exception ex)
            {
                Log.Write("Цвета WF: pack-URI словаря не создан.", ex);
                return null;
            }
        }

        private static ResourceDictionary TryLoadSource(Uri source)
        {
            if (source == null)
            {
                return null;
            }

            try
            {
                return new ResourceDictionary { Source = source };
            }
            catch (Exception ex)
            {
                Log.Write("Цвета WF: URI " + source + " не загрузился.", ex);
                return null;
            }
        }

        private static ThemeColor SourceFor(string token, StudioThemeColors palette)
        {
            if (Sources.TryGetValue(token, out ThemeColor source))
            {
                return source;
            }

            source = new ThemeColor();
            if (palette != null && palette.TryGet(token, out Color color))
            {
                source.Value = color;
            }

            Sources[token] = source;
            return source;
        }

        /// <summary>
        /// Кисть для словаря WF: цвет привязан к источнику токена.
        /// <para>
        /// Замораживать нельзя: замороженную кисть нельзя перекрасить, а <c>Freeze</c> вдобавок отдаёт один
        /// экземпляр всем элементам. Связанная кисть не замораживается, клонируется на элемент и клон
        /// сохраняет выражение привязки — отсюда мгновенная смена темы без прохода по дереву.
        /// </para>
        /// </summary>
        private static SolidColorBrush CreateBoundBrush(ThemeColor source)
        {
            SolidColorBrush brush = new SolidColorBrush(source.Value);

            BindingOperations.SetBinding(
                brush,
                SolidColorBrush.ColorProperty,
                new Binding(nameof(ThemeColor.Value)) { Source = source, Mode = BindingMode.OneWay });

            return brush;
        }

        /// <summary>Изменяемый источник цвета: смена значения перекрашивает все клоны кисти.</summary>
        private sealed class ThemeColor : INotifyPropertyChanged
        {
            private Color _value;

            public Color Value
            {
                get => _value;
                set
                {
                    if (_value == value)
                    {
                        return;
                    }

                    _value = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;
        }
    }
}
