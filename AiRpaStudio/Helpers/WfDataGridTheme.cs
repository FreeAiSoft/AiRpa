using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace RPA.Helpers
{
    /// <summary>
    /// Тематизация таблиц дизайнера WF — «Переменные», «Аргументы» и «Пространства имён»
    /// (F-04 плана <c>docs\Vs2026_Theme_Plan.md</c>).
    /// <para>
    /// Почему одного словаря палитры недостаточно: WF объявляет таблицы стилем
    /// <c>defaultDataGridStyle</c> (Generic.xaml), а он приходит со <c>{StaticResource}</c> на дефолтные
    /// цвета WF — <c>GridViewRowUnderlineColor</c>, <c>GridViewRowSelectedColor</c>,
    /// <c>GridViewRowHoverColor</c>. <see cref="ResourceDictionary"/> не пересчитывает
    /// <c>StaticResource</c>, поэтому шапка, сетка и выделение остаются светлыми при любой палитре.
    /// Второй источник — шаблоны системной темы (Aero2): плашки шапки, угла «выделить всё», пустой области
    /// и строки рисуются её триггерами, а триггер шаблона сильнее сеттера и триггера стиля.
    /// </para>
    /// <para>
    /// Отсюда три прохода, как и у остальных WF-контролов (<see cref="WfDesignerTheme"/>,
    /// <see cref="WfVisualTreeThemer"/>): 1) стили таблицы и её частей оборачиваются стилями палитры
    /// (<see cref="WfThemeStyles.Wrap"/>, шаблон WF сохраняется через <c>BasedOn</c>); 2) у уже
    /// материализованных контейнеров (шапка, строка, ячейка) цвет ставится локальным значением через
    /// <see cref="WfThemeStyles.WatchState"/>; 3) светлые кисти шаблона гасятся общим проходом
    /// <see cref="WfThemeStyles.NeutralizeLightBrushes"/>.
    /// </para>
    /// <para>
    /// Строки виртуализованы и создаются после показа панели, поэтому проход повторяется по
    /// <see cref="ItemContainerGenerator.StatusChanged"/> и по изменению отпечатка поддерева
    /// (<see cref="FrameworkElement.LayoutUpdated"/>): иначе поздно созданная строка вернула бы системную
    /// плашку. В шаблоны и их словари ресурсов темизатор ничего не пишет (контекстное меню и правка ячейки
    /// живут на самой таблице), а у запечатанных шаблонов дизайнера WF словарь только для чтения —
    /// дефект 1.0.0.0, docs\Designer_Variable_ContextMenu_Principles.md.
    /// </para>
    /// <para>
    /// Цвета берутся только из <see cref="StudioPalette"/>: один фон <c>Surface</c>, шапка — <c>SurfaceAlt</c>,
    /// разделители — <c>Separator</c>, состояния — <c>Hover</c>/<c>Selected</c>, фокус ячейки — hairline
    /// <c>SeparatorStrong</c>. Акцент не используется (правило §1 плана).
    /// </para>
    /// </summary>
    internal static class WfDataGridTheme
    {
        /// <summary>Насколько глубоко поднимаемся к панели-контейнеру таблицы (панель WF + хост дизайнера).</summary>
        private const int HostDepth = 6;

        /// <summary>Пределы отпечатка поддерева: ограничивают стоимость прохода по раскладке.</summary>
        private const int FingerprintDepth = 8;
        private const int FingerprintLimit = 512;

        /// <summary>Подписки на контейнеры таблицы ставятся один раз на таблицу.</summary>
        private static readonly ConditionalWeakTable<DataGrid, GridHook> GridHooks =
            new ConditionalWeakTable<DataGrid, GridHook>();

        /// <summary>
        /// Темизирует все таблицы поддерева (в <c>DesignerView</c> их три: переменные, аргументы,
        /// пространства имён). Поиск идёт по типу, а не по именам элементов: имена объявляет шаблон WF, и они
        /// отличаются между версиями (<c>variableDataGrid</c> против <c>variables1</c>).
        /// </summary>
        public static void Apply(DependencyObject root, StudioThemeColors palette)
        {
            if (root == null || palette == null)
            {
                return;
            }

            if (root is DataGrid rootGrid)
            {
                Theme(rootGrid, palette);
            }

            foreach (DataGrid grid in FindTables(root))
            {
                Theme(grid, palette);
            }
        }

        /// <summary>Темизирует одну таблицу (безопасно вызывать многократно, в том числе при смене темы).</summary>
        internal static void Theme(DataGrid grid, StudioThemeColors palette)
        {
            if (grid == null || palette == null)
            {
                return;
            }

            SolidColorBrush surface = WfThemeStyles.Brush(palette.Surface);
            SolidColorBrush separator = WfThemeStyles.Brush(palette.Separator);
            SolidColorBrush foreground = WfThemeStyles.Brush(palette.Foreground);

            grid.Background = surface;
            grid.RowBackground = surface;
            grid.AlternatingRowBackground = surface;
            grid.Foreground = foreground;
            grid.FontFamily = WfThemeStyles.Font;

            // Верхняя граница — hairline зоны: таблица отделена от полосы над ней, как в остальных панелях.
            grid.BorderBrush = separator;
            grid.BorderThickness = new Thickness(0, 1, 0, 0);

            // Линии сетки: дефолт WF — GridViewRowUnderlineColor через StaticResource (не пересчитывается).
            grid.HorizontalGridLinesBrush = separator;
            grid.VerticalGridLinesBrush = separator;

            grid.ColumnHeaderStyle = CreateColumnHeaderStyle(grid.ColumnHeaderStyle, palette);
            grid.CellStyle = CreateCellStyle(grid.CellStyle, palette);
            grid.RowStyle = CreateRowStyle(grid.RowStyle, palette);

            // Панель-контейнер (VariableDesigner/ArgumentDesigner/ImportDesigner): белый хост шаблона WF
            // просвечивал вокруг таблицы, поэтому фон ставится и панели, и её предкам до дизайнера.
            PaintHost(grid, palette);

            Refresh(grid, palette);
            HookGrid(grid, palette);
        }

        /// <summary>Сколько раз подписывались на таблицу (стражи идемпотентности в тестах).</summary>
        internal static int HookCount(DataGrid grid)
        {
            return grid != null && GridHooks.TryGetValue(grid, out _) ? 1 : 0;
        }

        /// <summary>Все таблицы поддерева (логические и визуальные дети: до применения шаблона таблиц в визуальном дереве нет).</summary>
        internal static IEnumerable<DataGrid> FindTables(DependencyObject root)
        {
            if (root == null)
            {
                yield break;
            }

            HashSet<DependencyObject> seen = new HashSet<DependencyObject>();
            Stack<DependencyObject> pending = new Stack<DependencyObject>();

            foreach (DependencyObject child in XamlHelper.Children(root))
            {
                pending.Push(child);
            }

            while (pending.Count > 0)
            {
                DependencyObject element = pending.Pop();
                if (!seen.Add(element))
                {
                    continue;
                }

                if (element is DataGrid grid)
                {
                    yield return grid;

                    // Внутрь таблицы не заходим: её дети — строки, а не вложенные таблицы.
                    continue;
                }

                foreach (DependencyObject child in XamlHelper.Children(element))
                {
                    pending.Push(child);
                }
            }
        }

        /// <summary>
        /// Стиль шапки колонок: фон <c>SurfaceAlt</c>, текст палитры, разделитель шапки — hairline,
        /// hover — <c>Hover</c>. Базовый стиль WF (в нём живут <c>SeparatorVisibility</c> и доступность)
        /// сохраняется через <c>BasedOn</c>.
        /// </summary>
        internal static Style CreateColumnHeaderStyle(Style baseStyle, StudioThemeColors palette)
        {
            return CreateThemedStyle(typeof(DataGridColumnHeader), baseStyle, palette, style =>
            {
                style.Setters.Add(new Setter(Control.BackgroundProperty, WfThemeStyles.Brush(palette.SurfaceAlt)));
                style.Setters.Add(new Setter(Control.ForegroundProperty, WfThemeStyles.Brush(palette.Foreground)));
                style.Setters.Add(new Setter(Control.BorderBrushProperty, WfThemeStyles.Brush(palette.Separator)));
                style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
                style.Setters.Add(new Setter(Control.FontFamilyProperty, WfThemeStyles.Font));

                // Заголовок — не акцент: жирность системного стиля шапки убирается, иначе текст «кричит».
                style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Normal));

                // Разделитель между шапками WF берёт из GridViewRowUnderlineColor (StaticResource).
                style.Setters.Add(new Setter(DataGridColumnHeader.SeparatorBrushProperty, WfThemeStyles.Brush(palette.Separator)));
                style.Setters.Add(new Setter(DataGridColumnHeader.SeparatorVisibilityProperty, Visibility.Visible));

                Trigger hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
                hover.Setters.Add(new Setter(Control.BackgroundProperty, WfThemeStyles.Brush(palette.Hover)));
                style.Triggers.Add(hover);
            });
        }

        /// <summary>
        /// Стиль строки: нейтральный фон — поверхность палитры, hover — <c>Hover</c>, выделение —
        /// <c>Selected</c> (системное синее выделение Aero2 остаётся только в шаблоне).
        /// </summary>
        internal static Style CreateRowStyle(Style baseStyle, StudioThemeColors palette)
        {
            return CreateThemedStyle(typeof(DataGridRow), baseStyle, palette, style =>
            {
                style.Setters.Add(new Setter(Control.BackgroundProperty, WfThemeStyles.Brush(palette.Surface)));
                style.Setters.Add(new Setter(Control.ForegroundProperty, WfThemeStyles.Brush(palette.Foreground)));
                style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
                style.Setters.Add(new Setter(Control.FontFamilyProperty, WfThemeStyles.Font));

                Trigger hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
                hover.Setters.Add(new Setter(Control.BackgroundProperty, WfThemeStyles.Brush(palette.Hover)));
                style.Triggers.Add(hover);

                // Выделение добавлено последним: при одновременном hover оно выигрывает.
                Trigger selected = new Trigger { Property = DataGridRow.IsSelectedProperty, Value = true };
                selected.Setters.Add(new Setter(Control.BackgroundProperty, WfThemeStyles.Brush(palette.Selected)));
                style.Triggers.Add(selected);
            });
        }

        /// <summary>
        /// Стиль ячейки: фон прозрачный (его рисует строка, иначе выделение ячейки перекрывает выделение
        /// строки), текст палитры, фокус — hairline <c>SeparatorStrong</c>.
        /// </summary>
        internal static Style CreateCellStyle(Style baseStyle, StudioThemeColors palette)
        {
            return CreateThemedStyle(typeof(DataGridCell), baseStyle, palette, style =>
            {
                style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
                style.Setters.Add(new Setter(Control.ForegroundProperty, WfThemeStyles.Brush(palette.Foreground)));
                style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
                style.Setters.Add(new Setter(Control.FontFamilyProperty, WfThemeStyles.Font));

                Trigger selected = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
                selected.Setters.Add(new Setter(Control.ForegroundProperty, WfThemeStyles.Brush(palette.Foreground)));
                style.Triggers.Add(selected);

                Trigger focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
                focus.Setters.Add(new Setter(Control.BorderBrushProperty, WfThemeStyles.Brush(palette.SeparatorStrong)));
                style.Triggers.Add(focus);
            });
        }

        /// <summary>
        /// Общая обёртка стиля WF стилем палитры: шаблон контрола сохраняется (<c>BasedOn</c>), повторный
        /// вызов с той же палитрой возвращает готовый стиль, а после смены темы обёртка пересоздаётся от
        /// исходного стиля WF (иначе остались бы кисти прежней палитры).
        /// </summary>
        private static Style CreateThemedStyle(Type targetType, Style baseStyle, StudioThemeColors palette, Action<Style> configure)
        {
            if (palette == null)
            {
                return baseStyle;
            }

            if (baseStyle != null && WfThemeStyles.IsThemed(baseStyle))
            {
                if (ReferenceEquals(WfThemeStyles.PaletteOf(baseStyle), palette))
                {
                    return baseStyle;
                }

                baseStyle = UnwrapThemed(baseStyle);
            }

            if (baseStyle != null && baseStyle.TargetType != null && !baseStyle.TargetType.IsAssignableFrom(targetType))
            {
                // Стиль с чужим TargetType обернуть нельзя: WPF бросит на несовпадении, а подмена стиля
                // потеряла бы шаблон WF.
                return baseStyle;
            }

            Style style = WfThemeStyles.Wrap(targetType, baseStyle);
            configure(style);
            WfThemeStyles.MarkThemed(style, palette);
            return style;
        }

        private static Style UnwrapThemed(Style style)
        {
            Style current = style;

            while (current != null && WfThemeStyles.IsThemed(current))
            {
                current = current.BasedOn;
            }

            return current;
        }

        /// <summary>
        /// Фон панели-контейнера таблицы и её предков до поверхности дизайнера. Ставится только там, где
        /// фон пуст, прозрачный, светлый или остался от прежней темы: чужая осмысленная заливка не трогается.
        /// </summary>
        private static void PaintHost(DataGrid grid, StudioThemeColors palette)
        {
            SolidColorBrush surface = WfThemeStyles.Brush(palette.Surface);
            DependencyObject element = ParentOf(grid);

            for (int depth = 0; element != null && depth < HostDepth; depth++)
            {
                if (element is FrameworkElement named && IsDesignerPart(named.Name))
                {
                    // Именованные части дизайнера темизируются собственными проходами.
                    break;
                }

                SetOpaqueBackground(element, palette, surface);
                element = ParentOf(element);
            }
        }

        private static bool IsDesignerPart(string name)
        {
            return string.Equals(name, WfDesignerTheme.DesignerViewElementName, StringComparison.Ordinal)
                || string.Equals(name, WfDesignerTheme.ShellBarElementName, StringComparison.Ordinal)
                || string.Equals(name, WfDesignerTheme.BreadCrumbElementName, StringComparison.Ordinal);
        }

        private static void SetOpaqueBackground(DependencyObject element, StudioThemeColors palette, SolidColorBrush surface)
        {
            DependencyProperty property;
            Brush current;

            if (element is Control control)
            {
                property = Control.BackgroundProperty;
                current = control.Background;
            }
            else if (element is Panel panel)
            {
                property = Panel.BackgroundProperty;
                current = panel.Background;
            }
            else if (element is Border border)
            {
                property = Border.BackgroundProperty;
                current = border.Background;
            }
            else
            {
                return;
            }

            if (current == null
                || (current is SolidColorBrush solid && solid.Color.A == 0)
                || WfThemeStyles.TryMapStalePaletteColour(current, palette, out _)
                || IsLightAgainstSurface(current, palette))
            {
                element.SetValue(property, surface);
            }
        }

        /// <summary>Светлее ли кисть поверхности палитры на порог «светлой плашки» (белый хост шаблона WF).</summary>
        private static bool IsLightAgainstSurface(Brush brush, StudioThemeColors palette)
        {
            if (!(brush is SolidColorBrush solid) || solid.Color.A == 0)
            {
                return false;
            }

            if (IsPaletteColour(palette, solid.Color))
            {
                return false;
            }

            return StudioPalette.Luminance(solid.Color)
                > StudioPalette.Luminance(palette.Surface) + WfThemeStyles.LightPlateThreshold;
        }
        /// <summary>Цвет объявлен в палитре: собственная работа темизатора не перекрашивается.</summary>
        private static bool IsPaletteColour(StudioThemeColors palette, Color color)
        {
            foreach (Color token in palette.Tokens.Values)
            {
                if (token == color)
                {
                    return true;
                }
            }

            return false;
        }



        private static DependencyObject ParentOf(DependencyObject element)
        {
            if (element == null)
            {
                return null;
            }

            DependencyObject parent = LogicalTreeHelper.GetParent(element);
            if (parent == null && XamlHelper.IsVisual(element))
            {
                parent = VisualTreeHelper.GetParent(element);
            }

            return parent;
        }

        /// <summary>
        /// Проход по материализованным частям таблицы: шапки, строки, ячейки и глифы сортировки.
        /// Повторяется на каждую генерацию контейнеров и на изменение поддерева.
        /// </summary>
        private static void Refresh(DataGrid grid, StudioThemeColors palette)
        {
            if (grid == null || palette == null)
            {
                return;
            }

            _ = grid.ApplyTemplate();

            foreach (DataGridColumnHeader header in XamlHelper.FindVisualChildren<DataGridColumnHeader>(grid))
            {
                ThemeHeader(header, palette);
            }

            foreach (DataGridRow row in XamlHelper.FindVisualChildren<DataGridRow>(grid))
            {
                ThemeRow(row, palette);
            }

            foreach (DataGridCell cell in XamlHelper.FindVisualChildren<DataGridCell>(grid))
            {
                ThemeCell(cell, palette);
            }

            // Плашки шаблона Aero2 (шапка, угол «выделить всё», пустая область) и системные кисти
            // внутри материализованных строк гасятся общим правилом WF-контролов.
            WfThemeStyles.NeutralizeLightBrushes(grid, palette);

            // Комбобоксы таблицы идут последними: «Переменные» и «Аргументы» держат столбцы Scope и
            // Modifiers (DataGridComboBoxColumn), а шаблон Aero2 рисует под их текстом опаковую белую
            // плашку. ThemeComboBoxes гасит эту плашку сам и ставит стиль элементов списка; список
            // раскрытия живёт в popup — вне дерева таблицы, поэтому общий проход его не видит.
            WfVisualTreeThemer.ThemeComboBoxes(grid, palette);
        }

        /// <summary>
        /// Шапка колонки: фон и рамка ставятся локально (локальное значение сильнее триггеров шаблона Aero2,
        /// которые красят шапку системными кистями), состояние hover обновляется подпиской.
        /// </summary>
        private static void ThemeHeader(DataGridColumnHeader header, StudioThemeColors palette)
        {
            WfThemeStyles.WatchState(header, palette, (element, colors) =>
            {
                if (!(element is DataGridColumnHeader target))
                {
                    return;
                }

                target.Background = target.IsMouseOver
                    ? WfThemeStyles.Brush(colors.Hover)
                    : WfThemeStyles.Brush(colors.SurfaceAlt);
                target.Foreground = WfThemeStyles.Brush(colors.Foreground);
                target.BorderBrush = WfThemeStyles.Brush(colors.Separator);
                target.BorderThickness = new Thickness(0, 0, 1, 1);
                target.FontFamily = WfThemeStyles.Font;
                target.FontWeight = FontWeights.Normal;

                PaintSortGlyph(target, colors);
            }, UIElement.IsMouseOverProperty);
        }

        /// <summary>Строка: фон зависит от выделения и наведения, обновляется подпиской на состояния.</summary>
        private static void ThemeRow(DataGridRow row, StudioThemeColors palette)
        {
            WfThemeStyles.WatchState(row, palette, (element, colors) =>
            {
                if (!(element is DataGridRow target))
                {
                    return;
                }

                target.Background = target.IsSelected
                    ? WfThemeStyles.Brush(colors.Selected)
                    : target.IsMouseOver
                        ? WfThemeStyles.Brush(colors.Hover)
                        : WfThemeStyles.Brush(colors.Surface);
                target.Foreground = WfThemeStyles.Brush(colors.Foreground);
                target.BorderBrush = Brushes.Transparent;
                target.FontFamily = WfThemeStyles.Font;
            }, DataGridRow.IsSelectedProperty, UIElement.IsMouseOverProperty);
        }

        /// <summary>Ячейка: фон отдаётся строке, фокус рисуется hairline, текст — палитра.</summary>
        private static void ThemeCell(DataGridCell cell, StudioThemeColors palette)
        {
            WfThemeStyles.WatchState(cell, palette, (element, colors) =>
            {
                if (!(element is DataGridCell target))
                {
                    return;
                }

                target.Background = Brushes.Transparent;
                target.Foreground = WfThemeStyles.Brush(colors.Foreground);
                target.BorderBrush = target.IsKeyboardFocusWithin
                    ? WfThemeStyles.Brush(colors.SeparatorStrong)
                    : Brushes.Transparent;
                target.FontFamily = WfThemeStyles.Font;
            }, UIElement.IsKeyboardFocusWithinProperty);
        }

        /// <summary>
        /// Глиф сортировки и стрелки шапки: в шаблонах системной темы они закрашены тёмным цветом
        /// <c>SystemColors</c> и на тёмной поверхности не видны.
        /// </summary>
        private static void PaintSortGlyph(DataGridColumnHeader header, StudioThemeColors palette)
        {
            SolidColorBrush muted = WfThemeStyles.Brush(palette.ForegroundMuted);

            foreach (Shape shape in XamlHelper.FindVisualChildren<Shape>(header))
            {
                if (shape.Fill != null)
                {
                    shape.Fill = muted;
                }

                if (shape.Stroke != null)
                {
                    shape.Stroke = muted;
                }
            }
        }

        /// <summary>
        /// Подписки на поздние контейнеры: строки таблица создаёт виртуализацией уже после показа панели,
        /// поэтому проход повторяется по генерации контейнеров и по изменению отпечатка поддерева.
        /// </summary>
        private static void HookGrid(DataGrid grid, StudioThemeColors palette)
        {
            GridHook hook = GridHooks.GetValue(grid, key => new GridHook());
            hook.Palette = palette;
            hook.Fingerprint = Fingerprint(grid);

            if (hook.Subscribed)
            {
                return;
            }

            hook.Subscribed = true;

            grid.ItemContainerGenerator.StatusChanged += (sender, args) =>
            {
                if (!(sender is ItemContainerGenerator generator) || generator.Status != GeneratorStatus.ContainersGenerated)
                {
                    return;
                }

                if (!GridHooks.TryGetValue(grid, out GridHook current))
                {
                    return;
                }

                // Шаблон ячейки достраивается после генерации контейнеров: проход идёт следующим циклом.
                _ = grid.Dispatcher.BeginInvoke(
                    new Action(() => Refresh(grid, current.Palette)),
                    DispatcherPriority.Background);
            };

            grid.LayoutUpdated += (sender, args) =>
            {
                if (!GridHooks.TryGetValue(grid, out GridHook current) || current.Pending)
                {
                    return;
                }

                int fingerprint = Fingerprint(grid);
                if (fingerprint == current.Fingerprint)
                {
                    return;
                }

                current.Fingerprint = fingerprint;
                current.Pending = true;

                // Отложенно: проход меняет шрифт контейнеров, то есть сам вызывает раскладку.
                _ = grid.Dispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        current.Pending = false;

                        if (GridHooks.TryGetValue(grid, out GridHook latest))
                        {
                            latest.Fingerprint = Fingerprint(grid);
                            Refresh(grid, latest.Palette);
                        }
                    }),
                    DispatcherPriority.Background);
            };

            // Вход в правку ячейки: редактор уже создан, и палитра ставится сразу. Через LayoutUpdated
            // комбобокс (столбцы Scope и Modifiers) окрашивался бы кадром позже — видна вспышка белой
            // плашки шаблона Aero2. Для DataGridTemplateColumn в EditingElement лежит ContentPresenter,
            // поэтому редактор доокрашивается ещё раз — после применения шаблона правки.
            grid.PreparingCellForEdit += (sender, args) =>
            {
                if (!GridHooks.TryGetValue(grid, out GridHook current)
                    || !(args.EditingElement is DependencyObject editingElement))
                {
                    return;
                }

                WfVisualTreeThemer.ApplyEditorPalette(editingElement, current.Palette);

                if (editingElement is FrameworkElement host && host.Dispatcher != null)
                {
                    _ = host.Dispatcher.BeginInvoke(
                        DispatcherPriority.Loaded,
                        new Action(() =>
                        {
                            if (GridHooks.TryGetValue(grid, out GridHook latest))
                            {
                                WfVisualTreeThemer.ApplyEditorPalette(editingElement, latest.Palette);
                            }
                        }));
                }
            };
        }

        /// <summary>Отпечаток поддерева таблицы: число визуальных детей (глубина и размер ограничены).</summary>
        private static int Fingerprint(DataGrid grid)
        {
            int count = 0;
            CountVisuals(grid, ref count, 0);
            return ((grid.Columns?.Count ?? 0) * 397) ^ count;
        }

        private static void CountVisuals(DependencyObject root, ref int count, int depth)
        {
            if (root == null || depth > FingerprintDepth || count > FingerprintLimit || !XamlHelper.IsVisual(root))
            {
                return;
            }

            int children = VisualTreeHelper.GetChildrenCount(root);
            count += children;

            for (int index = 0; index < children; index++)
            {
                CountVisuals(VisualTreeHelper.GetChild(root, index), ref count, depth + 1);
            }
        }

        /// <summary>Состояние подписки на таблицу (генератор контейнеров и раскладка).</summary>
        private sealed class GridHook
        {
            public StudioThemeColors Palette { get; set; }

            public bool Subscribed { get; set; }

            /// <summary>Отложенный проход уже запланирован — защита от повторной постановки.</summary>
            public bool Pending { get; set; }

            /// <summary>Отпечаток поддерева на момент последнего прохода по раскладке.</summary>
            public int Fingerprint { get; set; }
        }
    }
}
