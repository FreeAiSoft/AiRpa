using System;
using System.Windows;
using System.Windows.Media;
using RPA.Helpers;

namespace AiRpaStudio.Tests;

/// <summary>
/// Бейдж ошибки поля C#-выражения — голубой, а не красный (запрос: «иконку валидации сделай голубой»).
/// <para>
/// Красный в студии закреплён за системной семантикой ошибки: иконка валидации на заголовке активности
/// (родной рисунок WF, <c>ValidationIconState.ErrorIconBrush</c>) и кружок адаптера
/// <c>WfDesignerTheme.CreateValidationErrorTemplate</c>. Бейдж поля — отдельный сигнал «проверка поля»,
/// он получил собственный семантический цвет <c>Studio.Semantic.Validation</c>.
/// </para>
/// <para>
/// Почему семантика, а не палитра: <see cref="StudioPalette"/> обязан оставаться нейтральным
/// (chroma ≤ 6, docs\Vs2026_Theme_Plan.md §1 п.2), а зашитый hex запрещён стражем
/// <c>ThemeAcceptanceTests.ThemedXaml_HaveNoHardcodedColors</c>. Семантические кисти
/// (<see cref="StudioBrushes.SemanticKeys"/>) — единственное место с насыщенным цветом.
/// </para>
/// </summary>
[Apartment(ApartmentState.STA)]
public class ValidationBadgeColorTests
{
    [Test]
    public void ValidationSemanticKey_IsDeclaredAndFilled()
    {
        Assert.That(StudioBrushes.SemanticKeys, Does.Contain(StudioBrushes.SemanticValidationKey),
            "ключ бейджа обязан входить в SemanticKeys — иначе StudioBrushes.Apply его не заполнит");
        Assert.That(StudioBrushes.SemanticValidationKey, Does.StartWith("Studio.Semantic."),
            "семантические кисти живут в пространстве Studio.Semantic.*");

        var target = new ResourceDictionary();
        StudioBrushes.Apply(target);

        Assert.Multiple(() =>
        {
            Assert.That(target.Contains(StudioBrushes.SemanticValidationKey), Is.True,
                "семантический алиас бейджа не заполнен — DynamicResource в разметке поля останется пустым");
            Assert.That(target[StudioBrushes.SemanticValidationKey], Is.InstanceOf<SolidColorBrush>(),
                "бейдж — обычная кисть палитры, а не градиент");
            Assert.That(((Brush)target[StudioBrushes.SemanticValidationKey]).IsFrozen, Is.True,
                "кисть ставится на десятки полей выражений: изменяемый экземпляр не нужен");
        });
    }

    [Test]
    public void ValidationSemanticBrush_IsLightBlue()
    {
        var target = new ResourceDictionary();
        StudioBrushes.Apply(target);

        Color color = ColorOf(target, StudioBrushes.SemanticValidationKey);

        Assert.Multiple(() =>
        {
            Assert.That(color.A, Is.EqualTo(0xFF), "глиф обязан читаться на поверхности: цвет непрозрачный");
            Assert.That(color.B, Is.GreaterThan(color.G), "голубой: синяя составляющая больше зелёной");
            Assert.That(color.G, Is.GreaterThan(color.R), "голубой: зелёная составляющая больше красной");
            Assert.That(color.B, Is.GreaterThanOrEqualTo(0xE0), "тон светлый, а не тёмно-синий");
            Assert.That(StudioPalette.Chroma(color), Is.GreaterThan(6),
                "семантика кодируется цветом: chroma выше нейтрального порога палитры");
        });
    }

    [Test]
    public void ValidationSemanticBrush_DoesNotDependOnTheme()
    {
        var dark = new ResourceDictionary();
        var light = new ResourceDictionary();
        StudioBrushes.ApplyPalette(dark, StudioPalette.Dark);
        StudioBrushes.ApplyPalette(light, StudioPalette.Light);

        Assert.That(
            ColorOf(light, StudioBrushes.SemanticValidationKey),
            Is.EqualTo(ColorOf(dark, StudioBrushes.SemanticValidationKey)),
            "цвет бейджа — семантика, а не оформление: в обеих темах один и тот же голубой");
    }

    [Test]
    public void ValidationSemanticBrush_IsNotTheSharedErrorRed()
    {
        var target = new ResourceDictionary();
        StudioBrushes.Apply(target);

        Color validation = ColorOf(target, StudioBrushes.SemanticValidationKey);

        Assert.Multiple(() =>
        {
            Assert.That(validation, Is.Not.EqualTo(StudioPalette.Dark.CloseHoverBackground),
                "бейдж поля больше не совпадает с общим красным ошибки");
            Assert.That(validation, Is.Not.EqualTo(ColorOf(target, StudioBrushes.SemanticStopKey)),
                "«стоп» остаётся красным: голубым он стал бы неотличим от бейджа проверки");
        });
    }

    private static Color ColorOf(ResourceDictionary dictionary, string key)
    {
        Assert.That(dictionary.Contains(key), Is.True, $"в ресурсах нет кисти {key}");
        return ((SolidColorBrush)dictionary[key]).Color;
    }
}
