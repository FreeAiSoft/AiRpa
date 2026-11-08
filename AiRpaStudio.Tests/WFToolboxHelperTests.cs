using System;
using System.Activities.Presentation.Toolbox;
using System.Activities.Statements;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using RPA.Core.Generation;
using RPA.Helpers;

namespace AiRpaStudio.Tests;

/// <summary>
/// Сортировка панели «Активности»: активности внутри категории должны идти по алфавиту
/// независимо от порядка перечисления типов в источнике (встроенный список задаётся вручную).
/// </summary>
[Apartment(ApartmentState.STA)]
public class WFToolboxHelperTests
{
    [Test]
    public void AddToolboxCategory_SortsToolsAlphabeticallyByDisplayName()
    {
        var toolbox = new ToolboxControl();
        var helper = new WFToolboxHelper();

        // Намеренно не по алфавиту: While, If, Sequence, DoWhile.
        helper.AddToolboxCategory(toolbox, new List<Type>
        {
            typeof(While),
            typeof(If),
            typeof(Sequence),
            typeof(DoWhile)
        }, "Flow");

        ToolboxCategory category = toolbox.Categories[0];

        string[] names = category.Tools.Select(tool => tool.DisplayName).ToArray();
        string[] expected = { "DoWhile", "If", "Sequence", "While" };

        Assert.That(names, Is.EqualTo(expected),
            "активности в категории обязаны идти по алфавиту отображаемого имени");
    }

    [Test]
    public void CategoryDisplayName_UsesKeyWhenNotLocalized()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WFToolboxHelper.CategoryDisplayName("Activities.Browser"), Is.EqualTo("Activities.Browser"),
                "assembly name without localization must remain the category name");

            Assert.That(WFToolboxHelper.CategoryDisplayName(StudioActivityPalette.ControlFlowCategory),
                Is.Not.EqualTo(StudioActivityPalette.ControlFlowCategory),
                "built-in categories must stay localized");
        });
    }
}
