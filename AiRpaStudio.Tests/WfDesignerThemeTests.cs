using System.Globalization;

using System.Activities.Presentation;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;

using System.Windows.Media;

using RPA.Helpers;

namespace AiRpaStudio.Tests;

/// <summary>
/// Этап 7 плана (docs\Vs2026_Theme_Plan.md, §8 п.7, F-04…F-06): поверхность дизайнера WF.
/// <para>
/// Требования этапа: поверхность дизайнера и shell bar на палитре (один фон, hairline вместо рамок),
/// ключи <c>DesignerViewBackground</c> / <c>DesignerViewForeground</c> объявлены в палитре
/// (F-05: на <c>DesignerViewBackground</c> ссылается <c>CSharpExpressionEditor\RoslynExpressionEditor.xaml</c>),
/// адаптеры ошибок и контекстные меню дизайнера — на палитре, красный только для ошибок (F-06).
/// </para>
/// <para>
/// Проверки идут по синтетическому дереву, повторяющему визуальную структуру <c>DesignerView</c>
/// (имена <c>xnDesignerView</c>, <c>shellBar</c>, <c>breadCrumbListBox</c>, <c>miniMap</c> взяты из
/// Reference Source, см. docs\Reference_Source_Storage.md), поэтому HWND и настоящий
/// <c>WorkflowDesigner</c> тестам не нужны.
/// </para>
/// </summary>
[Apartment(ApartmentState.STA)]
public class WfDesignerThemeTests
{
    // --- F-05: ключи палитры, на которые ссылаются шаблоны WF и RoslynExpressionEditor. ---

    [Test]
    public void Resources_DeclareDesignerBackgroundAndForeground()
    {
        Dictionary<string, object> values = WfDesignerTheme.CreateResourceValues(StudioPalette.Dark);

        Assert.Multiple(() =>
        {
            Assert.That(values.ContainsKey(WfDesignerTheme.DesignerViewBackgroundKey), Is.True,
                "ключ DesignerViewBackground обязателен: на него ссылается RoslynExpressionEditor.xaml");
            Assert.That(values.ContainsKey(WfDesignerTheme.DesignerViewForegroundKey), Is.True);
            Assert.That(ColorOf((Brush)values[WfDesignerTheme.DesignerViewBackgroundKey]), Is.EqualTo(StudioPalette.Dark.Surface));
            Assert.That(ColorOf((Brush)values[WfDesignerTheme.DesignerViewForegroundKey]), Is.EqualTo(StudioPalette.Dark.Foreground));
        });
    }

    [Test]
    public void Resources_AreFrozenAndNeutral()
    {
        Dictionary<string, object> values = WfDesignerTheme.CreateResourceValues(StudioPalette.Light);
        HashSet<Color> paletteColors = new(StudioPalette.Light.Tokens.Values);

        Assert.That(values, Is.Not.Empty);

        foreach (KeyValuePair<string, object> pair in values)
        {
            if (!(pair.Value is SolidColorBrush brush))
            {
                // Шрифтовые ключи WF (FontFamilyKey/FontSizeKey/FontWeightKey) — не кисти.
                continue;
            }


            Assert.That(brush.IsFrozen, Is.True, $"{pair.Key}: кисть должна быть заморожена");
            Assert.That(paletteColors.Contains(brush.Color), Is.True,
                $"{pair.Key}: цвет {brush.Color} не из палитры — зашитые hex и акцент MaterialDesign запрещены");
        }
    }

    [Test]
    public void Resources_DeclareWorkflowFont()
    {
        // Этап 12: WF берёт шрифт контролов (панель «Свойства», сетка свойств, редакторы значений)
        // из собственных ключей FontFamilyKey/FontSizeKey/FontWeightKey — иначе там остаётся Segoe UI 12.
        Dictionary<string, object> values = WfDesignerTheme.CreateResourceValues(StudioPalette.Dark);

        Assert.Multiple(() =>
        {
            Assert.That(values.ContainsKey("FontFamilyKey"), Is.True, "WF-шрифт не объявлен");
            Assert.That(values["FontFamilyKey"], Is.InstanceOf<FontFamily>());
            Assert.That(((FontFamily)values["FontFamilyKey"]).Source, Does.Contain("Roboto"));
            Assert.That(values["FontSizeKey"], Is.EqualTo(WfThemeStyles.FontSize));
            Assert.That(values["FontWeightKey"], Is.EqualTo(FontWeights.Normal));
        });
    }


    [Test]
    public void Resources_DeclarePropertyInspectorKeys()
    {
        Dictionary<string, object> values = WfDesignerTheme.CreateResourceValues(StudioPalette.Dark);

        Assert.Multiple(() =>
        {
            Assert.That(values.ContainsKey(WorkflowDesignerColors.PropertyInspectorTextBrushKey), Is.True);
            Assert.That(values.ContainsKey(WorkflowDesignerColors.PropertyInspectorBackgroundBrushKey), Is.True);
            Assert.That(values.ContainsKey(WorkflowDesignerColors.PropertyInspectorSelectedBackgroundBrushKey), Is.True);
            Assert.That(values.ContainsKey(WorkflowDesignerColors.PropertyInspectorToolBarSeparatorBrushKey), Is.True);
            Assert.That(ColorOf((Brush)values[WorkflowDesignerColors.PropertyInspectorSelectedBackgroundBrushKey]),
                Is.EqualTo(StudioPalette.Dark.Selected), "выделение в инспекторе — яркость палитры, а не системный Highlight");
        });
    }

    [Test]
    public void Resources_DeclareDesignerContextMenuKeys()
    {
        Dictionary<string, object> values = WfDesignerTheme.CreateResourceValues(StudioPalette.Dark);

        Assert.Multiple(() =>
        {
            Assert.That(ColorOf((Brush)values["ContextMenuBackgroundBrush"]), Is.EqualTo(StudioPalette.Dark.Surface));
            Assert.That(ColorOf((Brush)values["ContextMenuBorderBrush"]), Is.EqualTo(StudioPalette.Dark.SeparatorStrong));
            Assert.That(ColorOf((Brush)values["ContextMenuSeparatorBrush"]), Is.EqualTo(StudioPalette.Dark.Separator));
            Assert.That(ColorOf((Brush)values["ContextMenuTextBrush"]), Is.EqualTo(StudioPalette.Dark.Foreground));
        });
    }

    // --- Применение словаря к ресурсам (одна точка записи, без накопления словарей). ---

    [Test]
    public void ApplyToApplication_NullArguments_DoNotThrow()
    {
        Assert.DoesNotThrow(() => WfDesignerTheme.ApplyToApplication(null, StudioPalette.Dark));
        Assert.DoesNotThrow(() => WfDesignerTheme.ApplyToApplication(new ResourceDictionary(), null));
    }

    [Test]
    public void ApplyToApplication_AddsSingleMergedDictionary()
    {
        var target = new ResourceDictionary();

        WfDesignerTheme.ApplyToApplication(target, StudioPalette.Dark);
        WfDesignerTheme.ApplyToApplication(target, StudioPalette.Dark);

        Assert.That(target.MergedDictionaries, Has.Count.EqualTo(1), "повторное применение не должно добавлять словарь");
        Assert.That(ColorOf(GetBrush(target, WfDesignerTheme.DesignerViewBackgroundKey)), Is.EqualTo(StudioPalette.Dark.Surface));
    }

    [Test]
    public void ApplyToApplication_UpdatesValuesInPlaceOnThemeChange()
    {
        var target = new ResourceDictionary();

        WfDesignerTheme.ApplyToApplication(target, StudioPalette.Dark);
        WfDesignerTheme.ApplyToApplication(target, StudioPalette.Light);

        Assert.That(target.MergedDictionaries, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(ColorOf(GetBrush(target, WfDesignerTheme.DesignerViewBackgroundKey)), Is.EqualTo(StudioPalette.Light.Surface));
            Assert.That(ColorOf(GetBrush(target, WorkflowDesignerColors.PropertyInspectorTextBrushKey)), Is.EqualTo(StudioPalette.Light.Foreground));
        });
    }

    // --- F-04: темизация поверхности дизайнера. ---

    [Test]
    public void Apply_NullRoot_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => WfDesignerTheme.Apply(null));
        Assert.DoesNotThrow(() => WfDesignerTheme.Apply(null, StudioPalette.Dark));
        Assert.DoesNotThrow(() => WfDesignerTheme.Apply(new Grid(), null));
    }

    [Test]
    public void Apply_SetsSurfaceOnRootAndDesignerView()
    {
        (Grid root, UserControl designerView, _, _, _, _, _) = BuildDesignerViewTree();

        WfDesignerTheme.Apply(root, StudioPalette.Dark);

        Assert.Multiple(() =>
        {
            Assert.That(ColorOf(root.Background), Is.EqualTo(StudioPalette.Dark.Surface));
            Assert.That(ColorOf(designerView.Background), Is.EqualTo(StudioPalette.Dark.Surface));
        });
    }

    [Test]
    public void Apply_MergesPaletteIntoDesignerViewResources()
    {
        (Grid root, UserControl designerView, _, _, _, _, _) = BuildDesignerViewTree();

        WfDesignerTheme.Apply(root, StudioPalette.Dark);
        WfDesignerTheme.Apply(root, StudioPalette.Light);

        Assert.That(designerView.Resources.MergedDictionaries, Has.Count.EqualTo(1),
            "словарь палитры должен переиспользоваться, а не накапливаться");
        Assert.That(ColorOf(GetBrush(designerView.Resources, WfDesignerTheme.DesignerViewBackgroundKey)),
            Is.EqualTo(StudioPalette.Light.Surface),
            "локальные ColorResources дизайнера перекрывают словарь приложения — значение должно обновиться");
    }

    [Test]
    public void Apply_ReplacesWorkflowDefaultSurfaceColors()
    {
        (Grid root, _, Border designerSurface, _, _, _, _) = BuildDesignerViewTree();

        // Дефолтные цвета WF приходят из StaticResource и не пересчитываются при смене темы,
        // поэтому темизатор заменяет их по значению (белая поверхность дизайнера, фон активности).
        designerSurface.Background = new SolidColorBrush(WorkflowDesignerColors.DesignerViewBackgroundColor);
        var activityBorder = new Border { Background = new SolidColorBrush(WorkflowDesignerColors.WorkflowViewElementBackgroundColor) };
        root.Children.Add(activityBorder);

        WfDesignerTheme.Apply(root, StudioPalette.Dark);

        Assert.Multiple(() =>
        {
            Assert.That(ColorOf(designerSurface.Background), Is.EqualTo(StudioPalette.Dark.Surface));
            Assert.That(ColorOf(activityBorder.Background), Is.EqualTo(StudioPalette.Dark.Surface));
        });
    }

    [Test]
    public void Apply_RestoresWorkflowDefaultsAfterReturningToLight()
    {
        // Регресс: пустая активность в дизайнере — плита Border 440x440 вокруг подсказки
        // «Перетащите сюда действие» — после light → dark → light оставалась цвета Dark.Surface
        // (#272727): на светлой теме вокруг подсказки был виден чёрный прямоугольник толщиной
        // с бордер плиты. Причина: дефолт WF подменялся один раз, а цвет палитры предыдущей темы
        // в списке дефолтов отсутствовал, поэтому обратная замена не срабатывала никогда.
        (Grid root, _, _, _, _, _, _) = BuildDesignerViewTree();
        Border plate = AddActivityPlate(root, out Label caption);
        var placeholder = (TextBlock)caption.Content;

        WfDesignerTheme.Apply(root, StudioPalette.Light);
        Assert.That(ColorOf(plate.Background), Is.EqualTo(StudioPalette.Light.Surface),
            "свежий документ в светлой теме: плита на поверхности");

        WfDesignerTheme.Apply(root, StudioPalette.Dark);
        Assert.That(ColorOf(plate.Background), Is.EqualTo(StudioPalette.Dark.Surface),
            "тёмная тема: плита на поверхности");

        WfDesignerTheme.Apply(root, StudioPalette.Light);

        Assert.Multiple(() =>
        {
            Assert.That(ColorOf(plate.Background), Is.EqualTo(StudioPalette.Light.Surface),
                "плита осталась на цвете тёмной палитры — вокруг подсказки виден чёрный прямоугольник");
            Assert.That(ColorOf(plate.BorderBrush), Is.EqualTo(StudioPalette.Light.SeparatorStrong),
                "рамка плиты осталась на цвете тёмной палитры");
            Assert.That(ColorOf(caption.Foreground), Is.EqualTo(StudioPalette.Light.Foreground),
                "подпись осталась на цвете тёмной палитры");
            Assert.That(ColorOf(placeholder.Foreground), Is.EqualTo(StudioPalette.Light.Foreground));
        });
    }

    [Test]
    public void Apply_RestoresWorkflowDefaultsWhenDocumentOpensInDarkTheme()
    {
        // Обратный порядок: документ открыт в тёмной теме, затем переключение на светлую (без
        // предварительного светлого прохода, поэтому «дефолт WF» сразу подменяется на тёмный).
        (Grid root, _, _, _, _, _, _) = BuildDesignerViewTree();
        Border plate = AddActivityPlate(root, out Label caption);

        WfDesignerTheme.Apply(root, StudioPalette.Dark);
        Assert.That(ColorOf(plate.Background), Is.EqualTo(StudioPalette.Dark.Surface));

        WfDesignerTheme.Apply(root, StudioPalette.Light);

        Assert.Multiple(() =>
        {
            Assert.That(ColorOf(plate.Background), Is.EqualTo(StudioPalette.Light.Surface));
            Assert.That(ColorOf(plate.BorderBrush), Is.EqualTo(StudioPalette.Light.SeparatorStrong));
            Assert.That(ColorOf(caption.Foreground), Is.EqualTo(StudioPalette.Light.Foreground));
        });
    }

    [Test]
    public void Apply_RoundTrip_KeepsCustomBrushes()
    {
        // Второе правило замены (цвет противоположной палитры → токен активной) не должно
        // захватывать пользовательские кисти: они не токены палитры.
        (Grid root, _, _, _, _, _, _) = BuildDesignerViewTree();
        var custom = new Border { Background = Brushes.Magenta, BorderBrush = Brushes.Magenta };
        root.Children.Add(custom);

        WfDesignerTheme.Apply(root, StudioPalette.Light);
        WfDesignerTheme.Apply(root, StudioPalette.Dark);
        WfDesignerTheme.Apply(root, StudioPalette.Light);

        Assert.Multiple(() =>
        {
            Assert.That(ColorOf(custom.Background), Is.EqualTo(Colors.Magenta));
            Assert.That(ColorOf(custom.BorderBrush), Is.EqualTo(Colors.Magenta));
        });
    }


    [Test]
    public void Apply_DoesNotTouchCustomBackgrounds()
    {
        (Grid root, _, _, _, _, _, _) = BuildDesignerViewTree();
        var custom = new Border { Background = Brushes.Magenta };
        root.Children.Add(custom);

        WfDesignerTheme.Apply(root, StudioPalette.Dark);

        Assert.That(ColorOf(custom.Background), Is.EqualTo(Colors.Magenta));
    }

    [Test]
    public void Apply_ThemesShellBar()
    {
        (Grid root, _, _, StatusBar shellBar, _, _, _) = BuildDesignerViewTree();

        WfDesignerTheme.Apply(root, StudioPalette.Dark);

        Assert.Multiple(() =>
        {
            Assert.That(ColorOf(shellBar.Background), Is.EqualTo(StudioPalette.Dark.Surface), "shell bar на том же фоне, что и поверхность");
            Assert.That(ColorOf(shellBar.BorderBrush), Is.EqualTo(StudioPalette.Dark.Separator), "вместо акцентной полосы — hairline");
            Assert.That(ColorOf(shellBar.Foreground), Is.EqualTo(StudioPalette.Dark.Foreground));
            Assert.That(shellBar.FontFamily.Source, Does.Contain("Roboto"));
        });
    }
    // Регресс F-04: свёрнутый комбобокс масштаба в shell bar показывал белые буквы на белом фоне.
    // Причина: WF-стиль statusBarComboBoxStyle задаёт только шрифт, Foreground и прозрачный фон, а шаблон
    // Aero2 рисует под текстом опаковую белую плашку (Border#templateRoot кнопки раскрытия и
    // Border#Border редактируемой части); текст комбобокс наследует от shell bar, то есть в тёмной теме
    // он светлый. Темизация комбобоксов идёт тем же проходом, что и shell bar.

    [Test]
    public void Apply_ThemesShellBarZoomCombo()
    {
        (Grid root, _, _, StatusBar shellBar, _, _, _) = BuildDesignerViewTree();
        ComboBox zoom = AddZoomPicker(shellBar);

        var window = new Window { Width = 400, Height = 200, ShowInTaskbar = false, Content = root };
        window.Show();
        try
        {
            Pump(window);
            WfDesignerTheme.Apply(root, StudioPalette.Dark);
            Pump(window);

            Assert.Multiple(() =>
            {
                Assert.That(ColorOf(zoom.Background), Is.EqualTo(StudioPalette.Dark.Surface));
                Assert.That(ColorOf(zoom.Foreground), Is.EqualTo(StudioPalette.Dark.Foreground));
                Assert.That(ColorOf(zoom.BorderBrush), Is.EqualTo(StudioPalette.Dark.Separator));
                Assert.That(zoom.FontFamily.Source, Does.Contain("Roboto"));
                Assert.That(zoom.ItemContainerStyle, Is.Not.Null, "элементы списка остались на системном стиле");
                Assert.That(ColorOf(EditableTextOf(zoom).Foreground), Is.EqualTo(StudioPalette.Dark.Foreground),
                    "текст свёрнутого масштаба остался системным (белым)");
                Assert.That(LightPlatesIn(zoom, StudioPalette.Dark), Is.Empty,
                    "под текстом осталась светлая плашка шаблона Aero2 — белые буквы на белом фоне");
                Assert.That(ColorOf(ArrowOf(zoom).Fill), Is.EqualTo(StudioPalette.Dark.ForegroundMuted),
                    "стрелка раскрытия не перекрашена и не видна на тёмной поверхности");
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void Apply_ShellBarZoomComboIsReadableInBothThemes()
    {
        (Grid root, _, _, StatusBar shellBar, _, _, _) = BuildDesignerViewTree();
        ComboBox zoom = AddZoomPicker(shellBar);

        var window = new Window { Width = 400, Height = 200, ShowInTaskbar = false, Content = root };
        window.Show();
        try
        {
            Pump(window);

            foreach (StudioThemeColors palette in new[] { StudioPalette.Dark, StudioPalette.Light })
            {
                WfDesignerTheme.Apply(root, palette);
                Pump(window);

                Assert.Multiple(() =>
                {
                    Assert.That(ColorOf(zoom.Background), Is.EqualTo(palette.Surface));
                    Assert.That(ColorOf(EditableTextOf(zoom).Foreground), Is.EqualTo(palette.Foreground),
                        $"текст масштаба не читается на поверхности палитры ({palette.IsDark}): цвет не из палитры");
                    Assert.That(
                        Math.Abs(Luminance(ColorOf(EditableTextOf(zoom).Foreground)) - Luminance(ColorOf(zoom.Background))),
                        Is.GreaterThanOrEqualTo(0.3),
                        $"контраст текста и плашки масштаба ниже порога (dark={palette.IsDark})");
                });
            }
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Раскрытый список масштаба: элементы списка и popup не должны оставаться на кистях Aero2.</summary>
    [Test]
    public void Apply_ThemesShellBarZoomComboList()
    {
        (Grid root, _, _, StatusBar shellBar, _, _, _) = BuildDesignerViewTree();
        ComboBox zoom = AddZoomPicker(shellBar);

        var window = new Window { Width = 400, Height = 200, ShowInTaskbar = false, Content = root };
        window.Show();
        try
        {
            Pump(window);
            WfDesignerTheme.Apply(root, StudioPalette.Dark);
            Pump(window);

            zoom.IsDropDownOpen = true;
            Pump(window);

            var item = zoom.ItemContainerGenerator.ContainerFromIndex(0) as ComboBoxItem;
            var popup = zoom.Template.FindName("PART_Popup", zoom) as Popup;

            Assert.Multiple(() =>
            {
                Assert.That(item, Is.Not.Null, "элементы списка масштаба не созданы");
                Assert.That(item.Style, Is.SameAs(zoom.ItemContainerStyle), "элемент списка остался на системном стиле");
                Assert.That(ColorOf(item.Foreground), Is.EqualTo(StudioPalette.Dark.Foreground));
                Assert.That(popup, Is.Not.Null, "у комбобокса нет popup-части");
                Assert.That(LightPlatesIn(popup.Child, StudioPalette.Dark), Is.Empty,
                    "в раскрытом списке остались светлые плашки шаблона Aero2");
            });
        }
        finally
        {
            window.Close();
        }
    }



    [Test]
    public void Apply_ThemesBreadCrumbAndMiniMap()
    {
        (Grid root, _, _, _, ListBox breadCrumb, UserControl miniMap, _) = BuildDesignerViewTree();

        WfDesignerTheme.Apply(root, StudioPalette.Dark);

        Assert.Multiple(() =>
        {
            Assert.That(ColorOf(breadCrumb.Background), Is.EqualTo(StudioPalette.Dark.Surface));
            Assert.That(ColorOf(breadCrumb.Foreground), Is.EqualTo(StudioPalette.Dark.Foreground));
            Assert.That(breadCrumb.BorderThickness, Is.EqualTo(new Thickness(0)));
            Assert.That(ColorOf(miniMap.Background), Is.EqualTo(StudioPalette.Dark.Surface));
            Assert.That(ColorOf(miniMap.BorderBrush), Is.EqualTo(StudioPalette.Dark.Separator));
        });
    }

    [Test]
    public void Apply_ThemesText()
    {
        (Grid root, _, _, _, _, _, TextBlock caption) = BuildDesignerViewTree();

        WfDesignerTheme.Apply(root, StudioPalette.Dark);

        Assert.That(ColorOf(caption.Foreground), Is.EqualTo(StudioPalette.Dark.Foreground));
    }

    [Test]
    public void Apply_SwitchesPalette()
    {
        (Grid root, UserControl designerView, _, _, _, _, _) = BuildDesignerViewTree();

        WfDesignerTheme.Apply(root, StudioPalette.Dark);
        Assert.That(ColorOf(designerView.Background), Is.EqualTo(StudioPalette.Dark.Surface));

        WfDesignerTheme.Apply(root, StudioPalette.Light);
        Assert.That(ColorOf(designerView.Background), Is.EqualTo(StudioPalette.Light.Surface));
    }

    // --- F-06: ошибки (красный только для ошибок) и контекстные меню дизайнера. ---

    [Test]
    public void Apply_SetsErrorAdornerTemplate()
    {
        (Grid root, _, _, _, _, _, _) = BuildDesignerViewTree();
        var textBox = new TextBox();
        root.Children.Add(textBox);

        WfDesignerTheme.Apply(root, StudioPalette.Dark);

        ControlTemplate template = Validation.GetErrorTemplate(textBox);

        Assert.Multiple(() =>
        {
            Assert.That(template, Is.Not.Null, "адаптер ошибки должен быть нашим, а не системным красным прямоугольником");
            Assert.That(template.VisualTree, Is.Not.Null);
            Assert.That(template.IsSealed, Is.True);
            Assert.That(ColorOf(WfDesignerTheme.ErrorBrush(StudioPalette.Dark)), Is.EqualTo(StudioPalette.Dark.CloseHoverBackground),
                "красный зарезервирован под ошибки (системная семантика, не акцент)");
        });
    }

    [Test]
    public void ErrorColor_IsRedAndNotUsedElsewhere()
    {
        Color error = WfDesignerTheme.ErrorBrush(StudioPalette.Dark).Color;

        Assert.That(error.R, Is.GreaterThan(error.G + 64), "токен ошибки должен быть красным");
        CollectionAssert.DoesNotContain(
            WfDesignerTheme.CreateResourceValues(StudioPalette.Dark).Values.OfType<SolidColorBrush>().Select(b => b.Color).ToList(),
            error,
            "красный не должен попадать в обычные кисти дизайнера");
    }

    [Test]
    public void CreateEditingContextMenu_UsesPalette()
    {
        ContextMenu menu = WfDesignerTheme.CreateEditingContextMenu(StudioPalette.Dark);

        Assert.Multiple(() =>
        {
            Assert.That(menu, Is.Not.Null);
            Assert.That(ColorOf(menu.Background), Is.EqualTo(StudioPalette.Dark.Surface));
            Assert.That(ColorOf(menu.Foreground), Is.EqualTo(StudioPalette.Dark.Foreground));
            Assert.That(ColorOf(menu.BorderBrush), Is.EqualTo(StudioPalette.Dark.SeparatorStrong));
            Assert.That(menu.FontFamily.Source, Does.Contain("Roboto"));
            Assert.That(menu.HasDropShadow, Is.False, "тень даёт «светлый» прямоугольник поверх тёмной темы");
            Assert.That(menu.Items, Has.Count.EqualTo(3), "Вырезать/Копировать/Вставить");
        });

        foreach (MenuItem item in menu.Items.OfType<MenuItem>())
        {
            // Текст пункта приходит из стиля контейнера: основной, либо приглушённый — команда без
            // выделения в буфере недоступна, и WPF сам делает пункт IsEnabled = false.
            Assert.That(
                new[] { StudioPalette.Dark.Foreground, StudioPalette.Dark.ForegroundMuted },
                Does.Contain(ColorOf(item.Foreground)),
                "текст пункта меню обязан быть на палитре");
            Assert.That(WfThemeStyles.IsThemed(item.Style), Is.True,
                "текст пункта меню обязан быть на палитре");
        }
    }

    [Test]
    public void CreateEditingContextMenu_NullPalette_FallsBackToDark()
    {
        ContextMenu menu = null;

        Assert.DoesNotThrow(() => menu = WfDesignerTheme.CreateEditingContextMenu(null));
        Assert.That(ColorOf(menu.Background), Is.EqualTo(StudioPalette.Dark.Surface));
    }

    // --- Стражи по исходникам: точки подключения и отсутствие зашитых цветов. ---

    [Test]
    public void Sources_AreWiredIntoStudio()
    {
        string app = ReadSource(Path.Combine("AiRpaStudio", "App.xaml.cs"));
        Assert.That(app, Does.Contain("WfDesignerTheme.ApplyToApplication"),
            "ключи DesignerViewBackground/Foreground должны быть объявлены до создания первого дизайнера");

        string settings = ReadSource(Path.Combine("AiRpaStudio", "Models", "AppSettings.cs"));
        Assert.That(settings, Does.Contain("WfDesignerTheme.ApplyToApplication"), "смена темы должна обновлять ключи дизайнера");

        string docGroup = ReadSource(Path.Combine("AiRpaStudio", "Helpers", "DocGroupHelper.cs"));
        Assert.That(docGroup, Does.Contain("WfDesignerTheme.Apply"), "поверхность дизайнера темизируется при открытии документа");

        string mainView = ReadSource(Path.Combine("AiRpaStudio", "Views", "MainView.xaml.cs"));
        Assert.That(mainView, Does.Contain("WfDesignerTheme.Apply"), "уже открытые дизайнеры перекрашиваются при смене темы");

        string service = ReadSource(Path.Combine("AiRpaStudio", "Core", "Services", "DesignerVariableContextMenuService.cs"));
        Assert.Multiple(() =>
        {
            Assert.That(service, Does.Contain("WfDesignerTheme.CreateEditingContextMenu"));
            Assert.That(service, Does.Not.Contain("Colors.Black"), "зашитый чёрный фон в контекстном меню запрещён");
            Assert.That(service, Does.Not.Contain("Colors.Green"));
            Assert.That(Regex.IsMatch(service, @"\bDebug\.WriteLine", RegexOptions.None, TimeSpan.FromSeconds(2)), Is.False,
                "отладочные WriteLine не должны оставаться в темизации");
        });
    }

    [Test]
    public void Sources_UseNoHardcodedColors()
    {
        string source = ReadSource(Path.Combine("AiRpaStudio", "Helpers", "WfDesignerTheme.cs"));

        Assert.That(Regex.IsMatch(source, @"#[0-9A-Fa-f]{6}", RegexOptions.None, TimeSpan.FromSeconds(2)), Is.False,
            "цвета берутся только из StudioPalette");
        Assert.That(source, Does.Not.Contain("MaterialThemePalette"));
    }

    [Test]
    public void RoslynExpressionEditor_ReferencesDeclaredKeys()
    {
        string xaml = ReadSource(Path.Combine("AiRpaStudio", "CSharpExpressionEditor", "RoslynExpressionEditor.xaml"));
        Assert.That(xaml, Does.Contain(WfDesignerTheme.DesignerViewBackgroundKey));

        Dictionary<string, object> values = WfDesignerTheme.CreateResourceValues(StudioPalette.Dark);
        Assert.That(values.ContainsKey(WfDesignerTheme.DesignerViewBackgroundKey), Is.True,
            "ключ, на который ссылается RoslynExpressionEditor.xaml, обязан быть объявлен");
    }

    [Test]
    public void ProjectFile_CompilesDesignerTheme()
    {
        string csproj = ReadSource(Path.Combine("AiRpaStudio", "AiRpaStudio.csproj"));
        Assert.That(csproj, Does.Contain("Helpers\\WfDesignerTheme.cs"));
    }

    // --- Хлебные крошки: имена активностей (Main › Sequence › Assign) на палитре. ---
    // Регресс: к названиям активностей тема не применялась — белые плашки поверх тёмной поверхности.
    // Причина: крошка — это ListBox, чьи элементы WF строит своим DataTemplate (кнопка с chrome Aero2
    // и TextBlock) на системных кистях, а новые крошки (провал в Sequence/Assign) создаются уже после
    // применения темы, поэтому разовое присваивание ListBox.Background до них не доходило.

    [Test]
    public void Apply_ThemesBreadCrumbItems()
    {
        (Grid root, _, _, _, ListBox breadCrumb, _, _) = BuildDesignerViewTree();
        (ListBoxItem item, Button button, _, TextBlock text) = AddBreadCrumbItem(breadCrumb, "Sequence");

        WfDesignerTheme.Apply(root, StudioPalette.Dark);

        Assert.Multiple(() =>
        {
            Assert.That(ColorOf(item.Background), Is.EqualTo(Colors.Transparent),
                "фон контейнера крошки должен быть прозрачным: состояния рисует триггер стиля");
            Assert.That(ColorOf(item.Foreground), Is.EqualTo(StudioPalette.Dark.Foreground));
            Assert.That(item.FontFamily.Source, Does.Contain("Roboto"));
            Assert.That(ColorOf(text.Foreground), Is.EqualTo(StudioPalette.Dark.Foreground),
                "имя активности осталось системного цвета");
            Assert.That(ColorOf(button.Background), Is.EqualTo(Colors.Transparent),
                "кнопка крошки осталась на светлом chrome Aero2");
            Assert.That(ColorOf(button.BorderBrush), Is.EqualTo(Colors.Transparent));
            Assert.That(ColorOf(button.Foreground), Is.EqualTo(StudioPalette.Dark.Foreground));
            Assert.That(button.FontFamily.Source, Does.Contain("Roboto"));
        });
    }

    [Test]
    public void Apply_BreadCrumbItemContainersUseThemedStyle()
    {
        (Grid root, _, _, _, ListBox breadCrumb, _, _) = BuildDesignerViewTree();

        WfDesignerTheme.Apply(root, StudioPalette.Dark);
        Style first = breadCrumb.ItemContainerStyle;

        WfDesignerTheme.Apply(root, StudioPalette.Dark);

        Assert.That(first, Is.Not.Null,
            "элементы крошки остались на системном стиле: hover и выделение не из палитры");
        Assert.That(first.TargetType, Is.EqualTo(typeof(ListBoxItem)));
        Assert.That(breadCrumb.ItemContainerStyle, Is.SameAs(first),
            "повторная темизация не должна наращивать цепочку BasedOn");
        Assert.That(SetterColorOf(first, Control.BackgroundProperty), Is.EqualTo(Colors.Transparent));

        Trigger hover = TriggersOf(first).OfType<Trigger>().FirstOrDefault(t => t.Property == UIElement.IsMouseOverProperty);
        Trigger selected = TriggersOf(first).OfType<Trigger>().FirstOrDefault(t => t.Property == ListBoxItem.IsSelectedProperty);

        Assert.Multiple(() =>
        {
            Assert.That(hover, Is.Not.Null, "нет триггера IsMouseOver");
            Assert.That(selected, Is.Not.Null, "нет триггера IsSelected — выделение останется системным");
            Assert.That(SetterColor(hover, Control.BackgroundProperty), Is.EqualTo(StudioPalette.Dark.Hover));
            Assert.That(SetterColor(selected, Control.BackgroundProperty), Is.EqualTo(StudioPalette.Dark.Selected));
        });
    }

    /// <summary>
    /// Главный страж бага: внутри крошки не должно остаться ни одной светлой плашки, а текст —
    /// «проваливаться» в фон тёмной темы. Проверяется всё поддерево, включая содержимое DataTemplate.
    /// </summary>
    [Test]
    public void Apply_BreadCrumbHasNoLightBackgrounds()
    {
        (Grid root, _, _, _, ListBox breadCrumb, _, _) = BuildDesignerViewTree();
        (ListBoxItem item, Button button, StackPanel content, TextBlock text) = AddBreadCrumbItem(breadCrumb, "Assign");

        // Так крошку отдаёт WF: контейнер, кнопка перехода и содержимое на светлых системных кистях.
        item.Background = Brushes.White;
        button.Background = Brushes.White;
        button.BorderBrush = Brushes.LightGray;
        text.Foreground = Brushes.Black;
        content.Background = Brushes.WhiteSmoke;

        WfDesignerTheme.Apply(root, StudioPalette.Dark);

        List<string> lightPlates = LightPlatesIn(breadCrumb, StudioPalette.Dark);

        Assert.That(lightPlates, Is.Empty,
            "белая плашка поверх тёмной темы: " + string.Join(", ", lightPlates));
        Assert.That(Luminance(ColorOf(text.Foreground)), Is.GreaterThan(0.5),
            "имя активности осталось тёмным на тёмном фоне");
    }

    [Test]
    public void Apply_BreadCrumbNeutralizesSystemBrushes()
    {
        (Grid root, _, _, _, ListBox breadCrumb, _, _) = BuildDesignerViewTree();
        (ListBoxItem item, Button button, StackPanel content, TextBlock text) = AddBreadCrumbItem(breadCrumb, "Main");

        item.Background = Brushes.White;
        button.Background = Brushes.White;
        button.Foreground = Brushes.Black;
        text.Foreground = Brushes.Black;
        content.Background = Brushes.WhiteSmoke;

        WfDesignerTheme.Apply(root, StudioPalette.Dark);

        Assert.Multiple(() =>
        {
            Assert.That(ColorOf(item.Background), Is.EqualTo(Colors.Transparent));
            Assert.That(ColorOf(button.Background), Is.EqualTo(Colors.Transparent));
            Assert.That(ColorOf(content.Background), Is.EqualTo(StudioPalette.Dark.Surface),
                "светлая площадь гасится в цвет поверхности: Transparent показал бы белого предка полосы");
            Assert.That(ColorOf(button.Foreground), Is.EqualTo(StudioPalette.Dark.Foreground));
            Assert.That(ColorOf(text.Foreground), Is.EqualTo(StudioPalette.Dark.Foreground));
        });
    }

    /// <summary>
    /// Крошки «Main › Sequence › Assign» появляются в момент провала в активность, то есть после
    /// применения темы: контейнеры создаёт генератор элементов, а не разовый проход при загрузке.
    /// </summary>
    [Test]
    public void Apply_ThemesBreadCrumbItemsCreatedAfterApply()
    {
        var breadCrumb = new ListBox
        {
            Name = WfDesignerTheme.BreadCrumbElementName,
            ItemTemplate = BuildBreadCrumbTemplate()
        };

        var window = new Window { Width = 240, Height = 120, ShowInTaskbar = false, Content = breadCrumb };

        window.Show();
        try
        {
            WfDesignerTheme.Apply(window, StudioPalette.Dark);

            breadCrumb.Items.Add("Assign");
            Pump(window);

            var container = breadCrumb.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
            Button button = container == null ? null : FindDescendant<Button>(container);
            TextBlock text = container == null ? null : FindDescendant<TextBlock>(container);

            Assert.That(container, Is.Not.Null, "контейнер крошки не создан");
            Assert.Multiple(() =>
            {
                Assert.That(ColorOf(container.Background), Is.EqualTo(Colors.Transparent),
                    "контейнер, созданный после темизации, остался на системном фоне");
                Assert.That(button, Is.Not.Null, "в крошке нет кнопки перехода");
                Assert.That(ColorOf(button.Background), Is.EqualTo(Colors.Transparent),
                    "содержимое крошки, созданное после темизации, осталось белым");
                Assert.That(ColorOf(text.Foreground), Is.EqualTo(StudioPalette.Dark.Foreground));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void Apply_BreadCrumbFollowsThemeSwitch()
    {
        (Grid root, _, _, _, ListBox breadCrumb, _, _) = BuildDesignerViewTree();
        (ListBoxItem item, Button button, _, TextBlock text) = AddBreadCrumbItem(breadCrumb, "Sequence");
        button.Background = Brushes.White;
        text.Foreground = Brushes.Black;

        WfDesignerTheme.Apply(root, StudioPalette.Dark);
        WfDesignerTheme.Apply(root, StudioPalette.Light);

        Assert.Multiple(() =>
        {
            Assert.That(ColorOf(breadCrumb.Background), Is.EqualTo(StudioPalette.Light.Surface));
            Assert.That(ColorOf(item.Foreground), Is.EqualTo(StudioPalette.Light.Foreground));
            Assert.That(ColorOf(button.Foreground), Is.EqualTo(StudioPalette.Light.Foreground));
            Assert.That(ColorOf(text.Foreground), Is.EqualTo(StudioPalette.Light.Foreground),
                "после смены темы текст крошки остался от прежней палитры");
        });
    }

    // --- Регресс «один белый пиксель»: артефакт внизу справа от имени активности, на hairline
    // «крошки/дизайнер». Остаток мог прийти любым свойством типа Brush (прежний проход перебирал три
    // фиксированных фона и только SolidColorBrush), из ItemsPanel/ScrollViewer с {StaticResource} на
    // белый DesignerViewBackground (StaticResource не пересчитывается) и из элементов, созданных уже
    // после применения темы.

    /// <summary>
    /// Главный страж: внутри крошки не должно остаться ни одной светлой кисти — каким бы свойством она
    /// ни пришла. В тест положены ровно те источники, которых не было в прежнем списке.
    /// </summary>
    [Test]
    public void Apply_BreadCrumbHasNoLightBrushAnywhere()
    {
        (Grid root, _, _, _, ListBox breadCrumb, _, _) = BuildDesignerViewTree();
        (_, _, StackPanel content, _) = AddBreadCrumbItem(breadCrumb, "Assign");

        content.Children.Add(new Border { Width = 1, Height = 1, Background = Brushes.White });
        content.Children.Add(new TextBlock { Text = string.Empty, Background = Brushes.WhiteSmoke });
        content.Children.Add(new Border
        {
            Width = 1,
            Height = 1,
            Background = new LinearGradientBrush(Colors.White, Colors.White, 90)
        });
        content.Children.Add(new Separator { Background = Brushes.White, Height = 1 });

        WfDesignerTheme.Apply(root, StudioPalette.Dark);

        List<string> light = LightBrushesIn(breadCrumb, StudioPalette.Dark);

        Assert.That(light, Is.Empty,
            "светлая кисть внутри крошки даёт белый пиксель на разделителе: " + string.Join(", ", light));
    }

    /// <summary>
    /// Пиксель лежал на hairline между крошками и дизайнером, то есть за границей полосы крошек:
    /// содержимое крошки обязано обрезаться, а дробные края — не давать полупрозрачную полоску.
    /// </summary>
    [Test]
    public void Apply_BreadCrumbClipsArtifactsOverflowingOnTheDivider()
    {
        (Grid root, _, _, _, ListBox breadCrumb, _, _) = BuildDesignerViewTree();

        WfDesignerTheme.Apply(root, StudioPalette.Dark);

        Assert.Multiple(() =>
        {
            Assert.That(breadCrumb.ClipToBounds, Is.True,
                "содержимое крошки рисует поверх hairline «крошки/дизайнер»");
            Assert.That(breadCrumb.Padding, Is.EqualTo(new Thickness(0)));
            Assert.That(breadCrumb.UseLayoutRounding, Is.True,
                "дробные координаты дают полупрозрачную полоску на стыке");
        });
    }

    /// <summary>
    /// Крошки и их шаблон содержимого создаются генератором элементов уже после темизации, а
    /// <c>ItemsPanel</c> WF объявляет с <c>{StaticResource}</c> на белый <c>DesignerViewBackground</c>.
    /// Поэтому светлые кисти обязаны гаситься и на последующих раскладках.
    /// </summary>
    [Test]
    public void Apply_BreadCrumbArtifactsCreatedAfterApplyAreNeutralized()
    {
        var itemsPanel = new FrameworkElementFactory(typeof(StackPanel));
        itemsPanel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        itemsPanel.SetValue(Panel.BackgroundProperty, Brushes.White);

        var breadCrumb = new ListBox
        {
            Name = WfDesignerTheme.BreadCrumbElementName,
            ItemTemplate = BuildBreadCrumbTemplateWithArtifact(),
            ItemsPanel = new ItemsPanelTemplate(itemsPanel)
        };

        var window = new Window { Width = 240, Height = 120, ShowInTaskbar = false, Content = breadCrumb };

        window.Show();
        try
        {
            WfDesignerTheme.Apply(window, StudioPalette.Dark);

            breadCrumb.Items.Add("Assign");
            Pump(window);

            List<string> light = LightBrushesIn(breadCrumb, StudioPalette.Dark);

            Assert.That(light, Is.Empty,
                "светлая кисть, созданная после темизации (ItemsPanel/шаблон крошки): " + string.Join(", ", light));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Страж реализации: обход обязан покрывать все свойства типа Brush и обрезать полосу крошек.
    /// </summary>
    [Test]
    public void Sources_BreadCrumbSweepsAllBrushProperties()
    {
        string theme = ReadSource(Path.Combine("AiRpaStudio", "Helpers", "WfDesignerTheme.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(theme, Does.Contain("BrushPropertiesOf"),
                "обход должен покрывать все свойства типа Brush, а не фиксированный список фонов");
            Assert.That(theme, Does.Contain("ClipToBounds"),
                "крошка должна обрезать содержимое, иначе артефакт ложится на разделитель");
            Assert.That(theme, Does.Contain("PaintBreadCrumbBand"),
                "подложка полосы крошек обязана быть непрозрачной (Surface), иначе сквозь неё виден белый хост");
            Assert.That(theme, Does.Contain("LayoutUpdated"),
                "элементы шаблона создаются после темизации — нужен повторный проход по раскладке");
            Assert.That(theme, Does.Not.Contain("Brushes.White"));
        });
    }

    [Test]
    public void Sources_BreadCrumbThemingIsInPlace()
    {
        string theme = ReadSource(Path.Combine("AiRpaStudio", "Helpers", "WfDesignerTheme.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(theme, Does.Contain("ThemeBreadCrumb"),
                "крошки обязаны темизироваться отдельным проходом (контейнеры + содержимое)");
            Assert.That(theme, Does.Contain("CreateListItemStyle"),
                "контейнеры крошек должны получать стиль палитры");
            Assert.That(theme, Does.Contain("ItemContainerGenerator"),
                "крошки, созданные после применения темы, должны достраиваться по генератору элементов");
            Assert.That(theme, Does.Not.Contain("Brushes.White"));
        });

        string load = ReadSource(Path.Combine("AiRpaStudio", "Helpers", "LoadActivityToDesignerHelper.cs"));
        Assert.That(load, Does.Not.Contain("Brushes.Gray"),
            "серый фон shell bar не следует теме — цвет обязан приходить из палитры");
    }

    /// <summary>
    /// Регресс: после правки «одного пикселя» полоса крошек стала белой целиком. Причина — светлый фон
    /// гасился в <c>Transparent</c>, и сквозь него показывался белый хост полосы, который WF объявляет
    /// через <c>{StaticResource}</c> и который не пересчитывается при смене темы. Подложка полосы
    /// обязана быть непрозрачной, а не прозрачной.
    /// </summary>
    [Test]
    public void Apply_BreadCrumbBandStaysOpaqueSurfaceUnderWhiteHost()
    {
        var host = new Grid { Background = Brushes.White };
        var designerView = new UserControl { Name = WfDesignerTheme.DesignerViewElementName };

        // Шаблон ListBox, который игнорирует Background и рисует собственный белый Border —
        // так ведёт себя шаблон полосы WF со {StaticResource} на белый DesignerViewBackground.
        var chrome = new FrameworkElementFactory(typeof(Border));
        chrome.SetValue(Panel.BackgroundProperty, Brushes.White);
        chrome.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));

        var itemsPanel = new FrameworkElementFactory(typeof(StackPanel));
        itemsPanel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        itemsPanel.SetValue(Panel.BackgroundProperty, Brushes.White);

        var breadCrumb = new ListBox
        {
            Name = WfDesignerTheme.BreadCrumbElementName,
            ItemTemplate = BuildBreadCrumbTemplate(),
            ItemsPanel = new ItemsPanelTemplate(itemsPanel),
            Template = new ControlTemplate(typeof(ListBox)) { VisualTree = chrome }
        };

        host.Children.Add(breadCrumb);
        designerView.Content = host;

        var window = new Window { Width = 240, Height = 120, ShowInTaskbar = false, Content = designerView };

        window.Show();
        try
        {
            breadCrumb.Items.Add("Sequence");
            Pump(window);

            WfDesignerTheme.Apply(window, StudioPalette.Dark);
            Pump(window);

            Border stripChrome = Walk(breadCrumb).OfType<Border>()
                .FirstOrDefault(border => VisualTreeHelper.GetParent(border) == breadCrumb);
            StackPanel stripPanel = Walk(breadCrumb).OfType<StackPanel>().FirstOrDefault();
            TextBlock text = FindDescendant<TextBlock>(breadCrumb);

            Assert.Multiple(() =>
            {
                Assert.That(ColorOf(breadCrumb.Background), Is.EqualTo(StudioPalette.Dark.Surface));
                Assert.That(stripChrome, Is.Not.Null, "не найдена часть шаблона полосы крошек");
                Assert.That(ColorOf(stripChrome.Background), Is.EqualTo(StudioPalette.Dark.Surface),
                    "белый Border шаблона ListBox остался: полоса крошек белая");
                Assert.That(stripPanel, Is.Not.Null, "не найден ItemsPanel полосы крошек");
                Assert.That(ColorOf(stripPanel.Background), Is.EqualTo(StudioPalette.Dark.Surface),
                    "белый ItemsPanel остался: полоса крошек белая");
                Assert.That(ColorOf(host.Background), Is.EqualTo(StudioPalette.Dark.Surface),
                    "белый хост полосы (BreadCrumbBarLayout) остался: полоса крошек белая");
                Assert.That(text, Is.Not.Null);
                Assert.That(ColorOf(OpaqueBackdropOf(text)), Is.EqualTo(StudioPalette.Dark.Surface),
                    "за именем активности просвечивает белый предок");
            });
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Страж исходного бага: за именем активности не должно быть белого фона. Первая непрозрачная кисть
    /// вверх по дереву обязана быть поверхностью палитры — иначе белый предок полосы виден сквозь
    /// прозрачные слои крошки (так появлялись и белая плашка, и «один пиксель» на hairline).
    /// </summary>
    [Test]
    public void Apply_BreadCrumbBackdropBehindNamesIsSurface()
    {
        (Grid root, _, _, _, ListBox breadCrumb, _, _) = BuildDesignerViewTree();
        (_, Button button, StackPanel content, TextBlock text) = AddBreadCrumbItem(breadCrumb, "Assign");

        // Крошка приходит на светлых системных кистях, а хост полосы — на белом {StaticResource}.
        var host = (Panel)breadCrumb.Parent;
        host.Background = Brushes.White;
        button.Background = Brushes.White;
        content.Background = Brushes.WhiteSmoke;
        text.Foreground = Brushes.Black;

        WfDesignerTheme.Apply(root, StudioPalette.Dark);

        double surface = Luminance(StudioPalette.Dark.Surface);
        var violations = new List<string>();

        foreach (DependencyObject element in Walk(breadCrumb)
            .Where(element => element is TextBlock || element is ButtonBase || element is Panel))
        {
            Brush backdrop = OpaqueBackdropOf(element);

            if (!(backdrop is SolidColorBrush solid)
                || solid.Color.A == 0
                || Luminance(solid.Color) > surface + 0.25)
            {
                violations.Add($"{element.GetType().Name} <- {backdrop}");
            }
        }

        Assert.That(violations, Is.Empty,
            "за содержимым крошки просвечивает светлый фон: " + string.Join(", ", violations));
    }

    /// <summary>Первая непрозрачная кисть фона вверх по дереву — то, что реально видно за элементом.</summary>
    private static Brush OpaqueBackdropOf(DependencyObject element)
    {
        for (DependencyObject current = element; current != null; current = ParentOfElement(current))
        {
            Brush background = BackgroundOf(current);

            if (background is SolidColorBrush solid)
            {
                if (solid.Color.A == 0)
                {
                    continue;
                }

                return background;
            }

            if (background != null)
            {
                return background;
            }
        }

        return null;
    }

    /// <summary>Фон элемента, если он вообще способен рисовать фон.</summary>
    private static Brush BackgroundOf(DependencyObject element)
    {
        switch (element)
        {
            case Panel panel:
                return panel.Background;

            case Border border:
                return border.Background;

            case Control control:
                return control.Background;

            default:
                return null;
        }
    }

    /// <summary>Родитель в визуальном дереве (для не-визуальных узлов — логический).</summary>
    private static DependencyObject ParentOfElement(DependencyObject element)
    {
        return element is Visual
            ? VisualTreeHelper.GetParent(element) ?? LogicalTreeHelper.GetParent(element)
            : LogicalTreeHelper.GetParent(element);

    }
    // --- F-04: «один белый пиксель» на стыке полосы крошек и дизайнера. ---

    /// <summary>
    /// Страж «одного белого пикселя». Полоса строится как у WF: <see cref="ListBox"/> с настоящим
    /// шаблоном Aero2, внутри которого <c>ScrollViewer</c> держит угловой <c>Rectangle</c>
    /// (<c>Fill="#FFF0F0F0"</c>), вырождающийся в 1×1 при скрытых полосах прокрутки. Именно этот
    /// пиксель горел справа внизу от имени последней активности и сдвигался вместе с полосой, когда
    /// крошек становилось больше (баг F-04). Проверка рендером: в нижних строках полосы, где лежит
    /// стык с дизайнером, не должно остаться ни одного пикселя светлее поверхности.
    /// </summary>
    [Test]
    public void Apply_BreadCrumbJunctionHasNoLightPixel()
    {
        var root = new Grid { Name = WfDesignerTheme.DesignerViewElementName };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var breadCrumb = new ListBox
        {
            Name = WfDesignerTheme.BreadCrumbElementName,
            HorizontalAlignment = HorizontalAlignment.Left,
            BorderThickness = new Thickness(0),
            Background = Brushes.White,
            ItemTemplate = BuildBreadCrumbTemplate()
        };
        root.Children.Add(breadCrumb);

        var window = new Window { Width = 240, Height = 120, ShowInTaskbar = false, Content = root };
        window.Show();
        try
        {
            breadCrumb.Items.Add("Main");
            breadCrumb.Items.Add("Sequence");
            breadCrumb.Items.Add("Assign");
            Pump(window);

            WfDesignerTheme.Apply(root, StudioPalette.Dark);
            Pump(window);

            int width = (int)Math.Ceiling(root.ActualWidth);
            int height = (int)Math.Ceiling(root.ActualHeight);
            Assert.That(width, Is.GreaterThan(50), "полоса не разложилась — проверять нечего");

            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);

            int stride = width * 4;
            var pixels = new byte[stride * height];
            bitmap.CopyPixels(pixels, stride, 0);

            // Стык «крошки/дизайнер» — нижние строки полосы: там текста нет, любой светлый пиксель
            // здесь и есть артефакт (в WF он приходится на hairline под крошками).
            double bandBottom = breadCrumb.TranslatePoint(new Point(0, 0), root).Y + breadCrumb.ActualHeight;
            int lastBandRow = (int)Math.Ceiling(bandBottom) - 1;
            double surface = Luminance(StudioPalette.Dark.Surface);

            var violations = new List<string>();
            for (int y = Math.Max(0, lastBandRow - 2); y <= lastBandRow; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = (y * stride) + (x * 4);
                    byte alpha = pixels[index + 3];
                    if (alpha == 0)
                    {
                        continue;
                    }

                    var color = Color.FromArgb(alpha, pixels[index + 2], pixels[index + 1], pixels[index]);
                    if (Luminance(color) > surface + 0.25)
                    {
                        violations.Add($"(x={x}, y={y}) {color}");
                    }
                }
            }

            Assert.That(violations, Is.Empty,
                "на стыке полосы крошек и дизайнера остался светлый пиксель: " + string.Join(", ", violations));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Угловой <c>Rectangle</c> шаблона (тот самый 1×1) обязан сливаться с поверхностью полосы:
    /// он не разделитель, поэтому не получает ни цвет разделителя, ни цвет «приглушённого» шеврона.
    /// </summary>
    [Test]
    public void Apply_BreadCrumbTemplateCornerDotMergesWithSurface()
    {
        (Grid root, _, _, _, ListBox breadCrumb, _, _) = BuildDesignerViewTree();

        var window = new Window { Width = 320, Height = 140, ShowInTaskbar = false, Content = root };
        window.Show();
        try
        {
            breadCrumb.Items.Add("Assign");
            Pump(window);

            WfDesignerTheme.Apply(root, StudioPalette.Dark);
            Pump(window);

            List<System.Windows.Shapes.Rectangle> corners = Walk(breadCrumb)
                .OfType<System.Windows.Shapes.Rectangle>()
                .Where(rectangle => rectangle.ActualWidth <= 1 && rectangle.ActualHeight <= 1)
                .ToList();

            Assert.That(corners, Is.Not.Empty,
                "в шаблоне ListBox нет углового Rectangle 1×1 — страж потерял предмет проверки");

            foreach (System.Windows.Shapes.Rectangle corner in corners)
            {
                Assert.That(corner.Fill, Is.Not.Null, "угловой пиксель шаблона остался без кисти");
                Assert.That(ColorOf(corner.Fill), Is.EqualTo(StudioPalette.Dark.Surface),
                    "угловой пиксель шаблона обязан сливаться с поверхностью полосы крошек");
            }
        }
        finally
        {
            window.Close();
        }
    }





    // --- Построители и помощники. ---
    /// <summary>
    /// Плита пустой активности в том виде, в каком её строит WF: <c>Border</c> с локальными
    /// (пришедшими через <c>{StaticResource}</c>) фоном и рамкой и подсказкой «Перетащите сюда
    /// действие» внутри. Локальные кисти не пересчитываются при смене темы — их перекрашивает
    /// только <c>ReplaceWorkflowDefaultColors</c>.
    /// </summary>
    private static Border AddActivityPlate(Grid root, out Label caption)
    {
        caption = new Label
        {
            Foreground = new SolidColorBrush(WorkflowDesignerColors.WorkflowViewElementCaptionColor),
            Content = new TextBlock { Text = "Перетащите сюда действие" }
        };

        var plate = new Border
        {
            Background = new SolidColorBrush(WorkflowDesignerColors.WorkflowViewElementBackgroundColor),
            BorderBrush = new SolidColorBrush(WorkflowDesignerColors.WorkflowViewElementBorderColor),
            Child = caption
        };

        root.Children.Add(plate);
        return plate;
    }


    // --- Пункт «кнопки Развернуть/Свернуть в заголовке дизайнера» (P-10). ---

    [Test]
    public void Apply_HeaderButtonsFitTheirCaptions()
    {
        (Grid root, ToggleButton expand, ToggleButton collapse) = BuildHeaderButtons();

        var window = new Window { Width = 420, Height = 120, ShowInTaskbar = false, Content = root };
        window.Show();
        try
        {
            Pump(window);
            WfDesignerTheme.Apply(root, StudioPalette.Dark);
            Pump(window);

            TextBlock expandCaption = HeaderCaptionOf(expand);
            TextBlock collapseCaption = HeaderCaptionOf(collapse);

            Assert.Multiple(() =>
            {
                // Регресс: кнопки WF получают неявный стиль MaterialDesign с Width=34 — подпись
                // «Развернуть все» (≈83 px) вылезала за кнопку и обрезалась краем панели.
                Assert.That(expand.Width, Is.EqualTo(double.NaN), "Width=34 из стиля MaterialDesign должен быть снят");
                Assert.That(collapse.Width, Is.EqualTo(double.NaN), "Width=34 из стиля MaterialDesign должен быть снят");
                Assert.That(expand.ActualWidth, Is.GreaterThanOrEqualTo(CaptionWidth(expandCaption) + 10d),
                    "подпись «Развернуть все» должна целиком влезать в кнопку");
                Assert.That(collapse.ActualWidth, Is.GreaterThanOrEqualTo(CaptionWidth(collapseCaption) + 10d),
                    "подпись «Свернуть все» должна целиком влезать в кнопку");
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void Apply_HeaderButtonsKeepFivePixelRightInset()
    {
        (Grid root, ToggleButton expand, ToggleButton collapse) = BuildHeaderButtons();

        var window = new Window { Width = 420, Height = 120, ShowInTaskbar = false, Content = root };
        window.Show();
        try
        {
            Pump(window);
            WfDesignerTheme.Apply(root, StudioPalette.Dark);

            // Смена темы не должна накапливать отступ: проход идемпотентен.
            WfDesignerTheme.Apply(root, StudioPalette.Light);
            WfDesignerTheme.Apply(root, StudioPalette.Dark);
            Pump(window);

            Assert.Multiple(() =>
            {
                Assert.That(collapse.Margin.Right, Is.EqualTo(WfDesignerTheme.HeaderButtonsRightInset),
                    "пара кнопок должна отступать от правого края на 5 px дальше, чем задано шаблоном WF");
                Assert.That(expand.Margin.Right, Is.Zero,
                    "отступ добавляется один раз на пару: у левой кнопки своего отступа нет");
            });
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Пара «Развернуть все»/«Свернуть все» в том виде, в каком её строит WF: правый край шапки
    /// (колонка <c>Auto</c>) и горизонтальная панель с <c>HorizontalAlignment=Right</c>.
    /// </summary>
    private static (Grid Root, ToggleButton Expand, ToggleButton Collapse) BuildHeaderButtons()
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        Grid.SetColumn(panel, 1);

        ToggleButton expand = CreateHeaderButton(WfDesignerTheme.ExpandAllButtonName, "Развернуть все");
        ToggleButton collapse = CreateHeaderButton(WfDesignerTheme.CollapseAllButtonName, "Свернуть все");

        _ = panel.Children.Add(expand);
        _ = panel.Children.Add(collapse);
        _ = root.Children.Add(panel);

        return (root, expand, collapse);
    }

    /// <summary>
    /// Кнопка заголовка как в <c>DesignerView.xaml</c>: собственный шаблон растягивает подпись
    /// с отступом 5, а размер задаёт неявный стиль MaterialDesign (<c>Width=34</c>).
    /// </summary>
    private static ToggleButton CreateHeaderButton(string name, string caption)
    {
        var button = new ToggleButton { Name = name, Tag = caption, Width = 34, Focusable = false };
        button.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
            "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ToggleButton}'>"
            + "<Grid><TextBlock x:Name='" + WfDesignerTheme.HeaderCaptionPartName
            + "' Margin='5,5,5,5' Text='{Binding Path=Tag, RelativeSource={RelativeSource AncestorType={x:Type ToggleButton}}}' /></Grid>"
            + "</ControlTemplate>");
        return button;
    }

    /// <summary>Подпись кнопки заголовка (часть шаблона <c>collapseAllText</c>).</summary>
    private static TextBlock HeaderCaptionOf(ToggleButton button)
    {
        button.ApplyTemplate();

        if (button.Template.FindName(WfDesignerTheme.HeaderCaptionPartName, button) is TextBlock caption)
        {
            return caption;
        }

        TextBlock found = FindDescendant<TextBlock>(button);
        Assert.That(found, Is.Not.Null, "в шаблоне кнопки заголовка нет подписи");
        return found;
    }

    /// <summary>Ширина подписи в пикселях — по ней считается минимальная ширина кнопки.</summary>
    private static double CaptionWidth(TextBlock caption)
    {
        var formatted = new FormattedText(
            caption.Text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(caption.FontFamily, caption.FontStyle, caption.FontWeight, caption.FontStretch),
            caption.FontSize,
            Brushes.Black,
            1.0);

        return formatted.WidthIncludingTrailingWhitespace;
    }


    /// <summary>
    /// Синтетическое дерево, повторяющее структуру <c>DesignerView</c>:
    /// корень (WorkflowDesigner.View) → <c>xnDesignerView</c> → поверхность дизайнера,
    /// shell bar, breadcrumb, MiniMap и подпись.
    /// </summary>
    private static (Grid Root, UserControl DesignerView, Border DesignerSurface, StatusBar ShellBar, ListBox BreadCrumb, UserControl MiniMap, TextBlock Caption) BuildDesignerViewTree()
    {
        var root = new Grid { Name = "root" };
        var designerView = new UserControl { Name = WfDesignerTheme.DesignerViewElementName };
        var surface = new Border();
        var shellBar = new StatusBar { Name = WfDesignerTheme.ShellBarElementName };
        var breadCrumb = new ListBox { Name = WfDesignerTheme.BreadCrumbElementName, BorderThickness = new Thickness(1) };
        var miniMap = new UserControl { Name = WfDesignerTheme.MiniMapElementName };
        var caption = new TextBlock { Text = "Sequence" };

        var layout = new Grid();
        layout.Children.Add(surface);
        layout.Children.Add(shellBar);
        layout.Children.Add(breadCrumb);
        layout.Children.Add(miniMap);
        layout.Children.Add(caption);

        designerView.Content = layout;
        root.Children.Add(designerView);

        return (root, designerView, surface, shellBar, breadCrumb, miniMap, caption);
    }

    /// <summary>
    /// Комбобокс масштаба в shell bar дизайнера (<c>zoomPicker</c> из Reference Source): editable,
    /// поэтому свёрнутый текст приходит из <c>PART_EditableTextBox</c>.
    /// </summary>
    private static ComboBox AddZoomPicker(StatusBar shellBar)
    {
        var zoom = new ComboBox { Name = "zoomPicker", Width = 80, IsEditable = true };
        _ = zoom.Items.Add("100%");
        _ = zoom.Items.Add("75%");
        zoom.SelectedIndex = 0;
        _ = shellBar.Items.Add(zoom);
        return zoom;
    }

    /// <summary>Редактируемая часть свёрнутого комбобокса — по ней судится видимый цвет текста.</summary>
    private static TextBox EditableTextOf(ComboBox comboBox)
    {
        Assert.That(comboBox.Template, Is.Not.Null, "шаблон комбобокса не применён");
        Assert.That(comboBox.Template.FindName("PART_EditableTextBox", comboBox), Is.InstanceOf<TextBox>(),
            "у комбобокса масштаба нет редактируемой части");
        return (TextBox)comboBox.Template.FindName("PART_EditableTextBox", comboBox);
    }

    /// <summary>Стрелка раскрытия шаблона комбобокса (в Aero2 — <c>Path#Arrow</c> внутри ToggleButton).</summary>
    private static System.Windows.Shapes.Path ArrowOf(ComboBox comboBox)
    {
        System.Windows.Shapes.Path arrow = FindDescendant<System.Windows.Shapes.Path>(comboBox);
        Assert.That(arrow, Is.Not.Null, "в шаблоне комбобокса нет стрелки раскрытия");
        return arrow;
    }

    private static Brush GetBrush(ResourceDictionary resources, string key)
    {
        Assert.That(resources, Is.Not.Null);
        Assert.That(resources.Contains(key), Is.True, $"в ресурсах нет ключа {key}");
        Assert.That(resources[key], Is.InstanceOf<Brush>(), $"{key}: ожидалась кисть");
        return (Brush)resources[key];
    }

    private static Color ColorOf(Brush brush)
    {
        Assert.That(brush, Is.InstanceOf<SolidColorBrush>());
        return ((SolidColorBrush)brush).Color;
    }

    private static string ReadSource(string relativePath)
    {
        string path = Path.Combine(FindRepositoryRoot(), relativePath);
        Assert.That(File.Exists(path), Is.True, $"не найден файл {relativePath}");
        return File.ReadAllText(path);
    }

    /// <summary>
    /// Крошка в том виде, в каком её строит WF: контейнер списка → кнопка перехода → содержимое
    /// (имя активности и шеврон). Возвращает части, чтобы тест мог задать «системные» кисти.
    /// </summary>
    private static (ListBoxItem Item, Button Button, StackPanel Content, TextBlock Text) AddBreadCrumbItem(ListBox breadCrumb, string name)
    {
        var text = new TextBlock { Text = name };
        var content = new StackPanel();
        content.Children.Add(text);

        var button = new Button { Content = content, Background = Brushes.White, BorderBrush = Brushes.LightGray };
        var item = new ListBoxItem { Content = button };
        breadCrumb.Items.Add(item);

        return (item, button, content, text);
    }

    /// <summary>DataTemplate крошки, как у WF: кнопка на светлой системной кисти с чёрным текстом.</summary>
    private static DataTemplate BuildBreadCrumbTemplate()
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding());
        text.SetValue(TextBlock.ForegroundProperty, Brushes.Black);

        var button = new FrameworkElementFactory(typeof(Button));
        button.SetValue(Control.BackgroundProperty, Brushes.White);
        button.SetValue(Control.BorderBrushProperty, Brushes.LightGray);
        button.SetValue(Control.ForegroundProperty, Brushes.Black);
        button.AppendChild(text);

        return new DataTemplate { VisualTree = button };
    }

    /// <summary>Протягивает очередь диспетчера до отложенных проходов темизации (крошки достраиваются в Background).</summary>
    private static void Pump(Window window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
        window.UpdateLayout();
    }

    private static T FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        return Walk(root).OfType<T>().FirstOrDefault();
    }

    /// <summary>Обход поддерева: логические дети (до применения шаблона) + визуальные (после).</summary>
    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        return Walk(root, new HashSet<DependencyObject>());
    }

    private static IEnumerable<DependencyObject> Walk(DependencyObject root, HashSet<DependencyObject> seen)
    {
        if (root == null)
        {
            yield break;
        }

        foreach (object child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is DependencyObject logicalChild && seen.Add(logicalChild))
            {
                yield return logicalChild;

                foreach (DependencyObject descendant in Walk(logicalChild, seen))
                {
                    yield return descendant;
                }
            }
        }

        if (!(root is Visual))
        {
            yield break;
        }

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            DependencyObject visualChild = VisualTreeHelper.GetChild(root, index);
            if (!seen.Add(visualChild))
            {
                continue;
            }

            yield return visualChild;

            foreach (DependencyObject descendant in Walk(visualChild, seen))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>
    /// Светлые плашки внутри крошки: любой фон ярче поверхности палитры больше чем на порог.
    /// Именно так выглядел баг — белый прямоугольник имени активности в тёмной теме.
    /// </summary>
    private static List<string> LightPlatesIn(DependencyObject root, StudioThemeColors palette)
    {
        var violations = new List<string>();
        double surface = Luminance(palette.Surface);

        foreach (DependencyObject element in Walk(root))
        {
            foreach (DependencyProperty property in new[] { Panel.BackgroundProperty, Control.BackgroundProperty, Border.BackgroundProperty })
            {
                if (element.GetValue(property) is SolidColorBrush brush
                    && brush.Color.A != 0
                    && Luminance(brush.Color) > surface + 0.25)
                {
                    violations.Add($"{element.GetType().Name}.{property.Name} = {brush.Color}");
                }
            }
        }

        return violations;
    }

    /// <summary>Относительная яркость цвета (WCAG): 0 — чёрный, 1 — белый.</summary>
    private static double Luminance(Color color)
    {
        return (0.2126 * LinearChannel(color.R)) + (0.7152 * LinearChannel(color.G)) + (0.0722 * LinearChannel(color.B));
    }

    private static double LinearChannel(byte value)
    {
        double channel = value / 255d;
        return channel <= 0.03928 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }

    private static IList<TriggerBase> TriggersOf(Style style)
    {
        Assert.That(style, Is.Not.Null);
        return style.Triggers;
    }

    private static Color SetterColor(Trigger trigger, DependencyProperty property)
    {
        Assert.That(trigger, Is.Not.Null);
        Setter setter = trigger.Setters.OfType<Setter>().FirstOrDefault(s => s.Property == property);
        Assert.That(setter, Is.Not.Null, $"в триггере {property.Name} нет сэттера");
        return ColorOf((Brush)setter.Value);
    }

    private static Color SetterColorOf(Style style, DependencyProperty property)
    {
        Setter setter = style.Setters.OfType<Setter>().FirstOrDefault(s => s.Property == property);
        Assert.That(setter, Is.Not.Null, $"в стиле {property.Name} нет сэттера");
        return ColorOf((Brush)setter.Value);
    }


    /// <summary>
    /// Шаблон крошки с «артефактами»: белый фон панели содержимого, белый фон текста и элемент 1×1.
    /// Так выглядит содержимое, которое строит WF из <c>{StaticResource}</c> уже после темизации.
    /// </summary>
    private static DataTemplate BuildBreadCrumbTemplateWithArtifact()
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding());
        text.SetValue(TextBlock.ForegroundProperty, Brushes.Black);
        text.SetValue(TextBlock.BackgroundProperty, Brushes.WhiteSmoke);

        var pixel = new FrameworkElementFactory(typeof(Border));
        pixel.SetValue(FrameworkElement.WidthProperty, 1d);
        pixel.SetValue(FrameworkElement.HeightProperty, 1d);
        pixel.SetValue(Panel.BackgroundProperty, Brushes.White);

        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        panel.SetValue(Panel.BackgroundProperty, Brushes.White);
        panel.AppendChild(text);
        panel.AppendChild(pixel);

        return new DataTemplate { VisualTree = panel };
    }

    /// <summary>
    /// Светлые кисти внутри поддерева по всем свойствам типа <see cref="Brush"/>. Кисти палитры
    /// разрешены (они и есть результат темизации), <c>Foreground</c> проверяется отдельно —
    /// светлый текст на тёмной поверхности это норма.
    /// </summary>
    private static List<string> LightBrushesIn(DependencyObject root, StudioThemeColors palette)
    {
        var violations = new List<string>();
        double surface = Luminance(palette.Surface);
        HashSet<Color> paletteColors = new(palette.Tokens.Values);

        foreach (DependencyObject element in Walk(root))
        {
            foreach (DependencyProperty property in BrushDpsOf(element.GetType()))
            {
                if (property.Name == "Foreground" || !(element.GetValue(property) is Brush brush)
                    || ReferenceEquals(brush, Brushes.Transparent))
                {
                    continue;
                }

                if (brush is SolidColorBrush solid)
                {
                    if (IsLightArtifact(solid.Color, surface, paletteColors))
                    {
                        violations.Add($"{element.GetType().Name}.{property.Name} = {solid.Color}");
                    }

                    continue;
                }

                if (brush is GradientBrush gradient
                    && gradient.GradientStops.Any(stop => IsLightArtifact(stop.Color, surface, paletteColors)))
                {
                    violations.Add($"{element.GetType().Name}.{property.Name} = gradient");
                }
            }
        }

        return violations;
    }

    private static bool IsLightArtifact(Color color, double surfaceLuminance, HashSet<Color> paletteColors)
    {
        return color.A != 0
            && !paletteColors.Contains(color)
            && Luminance(color) > surfaceLuminance + 0.25;
    }

    /// <summary>Свойства типа Brush объявленные в типе и его базовых типах (независимо от реализации).</summary>
    private static IEnumerable<DependencyProperty> BrushDpsOf(Type type)
    {
        for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
        {
            foreach (System.Reflection.FieldInfo field in current.GetFields(
                System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.DeclaredOnly))
            {
                if (field.FieldType == typeof(DependencyProperty)
                    && field.GetValue(null) is DependencyProperty property
                    && typeof(Brush).IsAssignableFrom(property.PropertyType))
                {
                    yield return property;
                }
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AiRpa.sln")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null, "не найден корень репозитория (AiRpa.sln)");
        return directory.FullName;
    }
}
