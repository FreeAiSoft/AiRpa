using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using NUnit.Framework;
using RPA.NuGet.Services;
using RPA.NuGet.ViewModels;

namespace AiRpaStudio.Tests;

/// <summary>
/// Четвёртая приёмка панели «NuGet»: шеврон блока «Options» раскрывает два флажка, но обязан вести
/// себя как кнопка **без фона** — наведение мыши не меняет ни фон, ни цвет подписи, ни цвет иконки,
/// а сам шеврон — иконка MaterialDesign (<c>ChevronRight</c> → <c>ChevronDown</c>), а не два
/// самодельных <c>Path</c>.
/// </summary>
[TestFixture]
public class NuGetOptionsChevronTests
{
    [Test]
    public void OptionsExpander_UsesMaterialChevronWithoutHoverBackground()
    {
        string block = OptionsExpanderBlock();

        Assert.Multiple(() =>
        {
            Assert.That(block, Does.Contain("<materialIcons:MaterialIcon"),
                "шеврон обязан быть иконкой MaterialDesign");
            Assert.That(block, Does.Contain("Kind=\"ChevronRight\""), "свёрнутое состояние — ChevronRight");
            Assert.That(block, Does.Contain("Property=\"Kind\" Value=\"ChevronDown\""), "раскрытое состояние — ChevronDown");
            Assert.That(block, Does.Contain("x:Name=\"ChevronIcon\""), "иконка одна: глиф переключается триггером");

            // Кнопка без фона: наведение и нажатие ничего не красят.
            Assert.That(block, Does.Not.Contain("IsMouseOver"),
                "наведение не должно менять оформление шапки (замечание приёмки)");
            Assert.That(block, Does.Not.Contain("IsPressed"), "нажатие не должно менять оформление шапки");
            Assert.That(block, Does.Not.Contain("Studio.Hover"), "фон наведения в шапке запрещён");
            Assert.That(block, Does.Contain("Background=\"Transparent\""), "шапка — прозрачная кнопка");
            Assert.That(block, Does.Contain("CornerRadius=\"0\""), "углы панели прямые");

            // Самодельных шевронов больше нет.
            Assert.That(block, Does.Not.Contain("<Path"),
                "шеврон рисуется иконкой MaterialDesign, а не Path со Stroke");

            // Осталось именно раскрытие блока полей.
            Assert.That(block, Does.Contain("<ToggleButton"), "шапка остаётся кликабельной кнопкой");
            Assert.That(block, Does.Contain("Cursor=\"Hand\""), "по шапке видно, что она нажимается");
            Assert.That(block, Does.Contain("IsExpanded, Mode=TwoWay"), "кнопка раскрывает сам Expander");
            Assert.That(block, Does.Contain("NuGet.StretchedToggleButtonStyle"),
                "внутренний ToggleButton обязан задавать Style явно (иначе неявный стиль MaterialDesign даёт Width=34)");
        });
    }

    [Test, Apartment(ApartmentState.STA)]
    public void Panel_OptionsChevronFlipsGlyphAndKeepsTransparentBackground()
    {
        var model = new NuGetPanelViewModel(new NuGetSearchService("https://127.0.0.1:1/index.json"));
        var panel = new RPA.NuGet.Views.NuGetPanel(model);

        panel.Measure(new Size(1100, 700));
        panel.Arrange(new Rect(0, 0, 1100, 700));
        panel.UpdateLayout();

        Expander expander = FindExpander(panel);
        Assert.That(expander, Is.Not.Null, "в панели деталей нет Expander «Options»");

        expander.ApplyTemplate();
        var toggle = expander.Template.FindName("HeaderToggle", expander) as ToggleButton;
        Assert.That(toggle, Is.Not.Null, "шапка Expander — ToggleButton");
        toggle.ApplyTemplate();

        var toggleTemplate = (ControlTemplate)toggle.Template;
        Assert.That(toggleTemplate.FindName("HeaderBorder", toggle), Is.Not.Null, "нет бордера шапки");

        Assert.Multiple(() =>
        {
            // Ни одного триггера наведения/нажатия: шапка не меняет цвет.
            foreach (TriggerBase trigger in toggleTemplate.Triggers)
            {
                var triggerObject = trigger as Trigger;
                if (triggerObject == null)
                    continue;

                Assert.That(triggerObject.Property, Is.Not.EqualTo(UIElement.IsMouseOverProperty),
                    "у шапки не должно быть hover-триггера");
                Assert.That(triggerObject.Property, Is.Not.EqualTo(ButtonBase.IsPressedProperty),
                    "у шапки не должно быть pressed-триггера");
            }

            var border = toggleTemplate.FindName("HeaderBorder", toggle) as Border;
            Assert.That(border, Is.Not.Null);
            Assert.That(IsTransparent(border.Background), Is.True,
                "фон шапки прозрачный — это кнопка без фона");
        });

        object icon = toggleTemplate.FindName("ChevronIcon", toggle);
        Assert.That(icon, Is.Not.Null, "шеврон — именованная иконка");
        Assert.That(icon.GetType().FullName, Is.EqualTo("Material.Icons.WPF.MaterialIcon"),
            "шеврон обязан быть MaterialIcon");

        Assert.That(KindOf(icon), Is.EqualTo(KindAlias(icon, "ChevronRight")), "свёрнутый блок — ChevronRight");
        expander.IsExpanded = true;
        panel.UpdateLayout();
        Assert.That(KindOf(icon), Is.EqualTo(KindAlias(icon, "ChevronDown")), "раскрытый блок — ChevronDown");
    }

    // ================= Служебное =================

    private static string KindOf(object icon)
    {
        PropertyInfo kind = icon.GetType().GetProperty("Kind");
        Assert.That(kind, Is.Not.Null, "у MaterialIcon нет свойства Kind");
        object value = kind.GetValue(icon);
        return value == null ? null : value.ToString();
    }

    /// <summary>
    /// Имя значения <c>Kind</c> для XAML-алиаса: в Material.Icons 2.4.1 у одного значения бывает
    /// несколько имён (<c>ChevronRight</c> = <c>NavigateNext</c>, <c>ChevronDown</c> = <c>ExpandMore</c>),
    /// а <c>ToString()</c> отдаёт каноническое имя. Сравнение идёт по имени, а не по строке из XAML.
    /// </summary>
    private static string KindAlias(object icon, string alias)
    {
        Type kindType = icon.GetType().GetProperty("Kind")?.PropertyType;
        Assert.That(kindType, Is.Not.Null, "у MaterialIcon нет свойства Kind");
        Assert.That(Enum.IsDefined(kindType, alias), Is.True, $"в этой версии Material.Icons нет иконки {alias}");
        return Enum.GetName(kindType, Enum.Parse(kindType, alias));
    }

    private static bool IsTransparent(Brush brush)
    {
        if (brush == null)
            return true;

        return brush is SolidColorBrush solid && solid.Color.A == 0;
    }

    private static Expander FindExpander(DependencyObject root)
    {
        if (root is Expander found)
            return found;

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            Expander child = FindExpander(VisualTreeHelper.GetChild(root, i));
            if (child != null)
                return child;
        }

        return null;
    }

    /// <summary>Текст стиля <c>NuGet.OptionsExpanderStyle</c> (до следующего раздела файла стилей).</summary>
    private static string OptionsExpanderBlock()
    {
        string styles = Read(@"RPA.NuGet\Views\NuGetPanel.Styles.xaml");
        int start = styles.IndexOf("<Style x:Key=\"NuGet.OptionsExpanderStyle\"", StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThan(-1), "в стилях нет NuGet.OptionsExpanderStyle");

        int end = styles.IndexOf("<!-- ======================= Список пакетов", start, StringComparison.Ordinal);
        Assert.That(end, Is.GreaterThan(start), "не найден конец стиля NuGet.OptionsExpanderStyle");

        return styles.Substring(start, end - start);
    }

    private static string Read(string relativePath)
    {
        string path = Path.Combine(FindRepositoryRoot(), relativePath);
        Assert.That(File.Exists(path), Is.True, $"нет файла {relativePath}");
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AiRpa.sln")))
            directory = directory.Parent;

        Assert.That(directory, Is.Not.Null, "не найден корень репозитория (AiRpa.sln)");
        return directory.FullName;
    }
}
