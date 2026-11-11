using System;
using System.Activities.Presentation.Toolbox;
using System.Activities.Statements;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    public void BuildModelThenBuildControl_PreloadsToolsAndKeepsPaletteUsable()
    {
        var helper = new WFToolboxHelper();

        List<ActivityPaletteCategory> palette = new List<ActivityPaletteCategory>
        {
            new ActivityPaletteCategory(StudioActivityPalette.ControlFlowCategory, new[]
            {
                typeof(While),
                typeof(If),
                typeof(Sequence),
                typeof(DoWhile)
            })
        };

        // Фоновая фаза в студии выполняется в Task.Run (MTA): рефлексия и создание
        // System.Drawing.Design.ToolboxItem должны быть потокобезопасны без UI-потока.
        List<ToolboxCategory> model = Task.Run(() => helper.BuildModel(palette)).GetAwaiter().GetResult();
        ToolboxControl toolbox = helper.BuildControl(model);

        ToolboxCategory category = toolbox.Categories[0];

        Assert.Multiple(() =>
        {
            Assert.That(category.Tools.Select(tool => tool.DisplayName).ToArray(),
                Is.EqualTo(new[] { "DoWhile", "If", "Sequence", "While" }),
                "активности в категории обязаны идти по алфавиту отображаемого имени");

            foreach (ToolboxItemWrapper tool in category.Tools)
            {
                Assert.That(tool.IsValid, Is.True, $"{tool.ToolName}: ToolboxItem не разрешён");
                Assert.That(tool.Type, Is.Not.Null, $"{tool.ToolName}: Type не заполнен (drag&drop сломается)");
            }
        });
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
