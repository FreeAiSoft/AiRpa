using System;
using System.Activities.Statements;
using System.Linq;
using RPA.Core.Generation;

namespace AiRpaStudio.Tests.Generation;

/// <summary>
/// Этап 4 конвейера генерации XAML: реестр палитры
/// (<c>ActivityPaletteRegistry</c>) — метаданные активностей по объектной модели WF.
/// <para>
/// Реестр — единственный источник правды о палитре: из него строится и панель «Активности», и описания
/// аргументов для инструментов <c>xaml.*</c>. Поэтому тесты фиксируют и аргументы (тип, направление,
/// обязательность), и контейнеры, и то, что все встроенные категории панели в реестре есть.
/// </para>
/// </summary>
[TestFixture]
public class ActivityPaletteRegistryTests
{
    private ActivityPaletteRegistry _registry;

    [SetUp]
    public void CreateRegistry()
    {
        _registry = ActivityPaletteRegistry.Default;
    }

    [Test]
    public void Find_WriteLine_DescribesTextArgumentAsStringIn()
    {
        PaletteActivityDescriptor descriptor = _registry.Find("WriteLine");

        Assert.That(descriptor, Is.Not.Null, "WriteLine — базовая активность палитры");
        Assert.That(descriptor.Type, Is.EqualTo(typeof(WriteLine)));

        PaletteArgumentDescriptor text = descriptor.FindArgument("text");

        Assert.Multiple(() =>
        {
            Assert.That(text, Is.Not.Null, "имя аргумента ищется без учёта регистра");
            Assert.That(text.Direction, Is.EqualTo(PaletteArgumentDirection.In));
            Assert.That(text.TypeName, Is.EqualTo("String"));
            Assert.That(text.Required, Is.False, "у WriteLine.Text нет [RequiredArgument]");
            Assert.That(text.AcceptsExpression, Is.True);
        });
    }

    [Test]
    public void Find_Assign_KeepsRequiredOutAndInArguments()
    {
        PaletteActivityDescriptor descriptor = _registry.Find("Assign");

        Assert.That(descriptor, Is.Not.Null);

        PaletteArgumentDescriptor to = descriptor.FindArgument("To");
        PaletteArgumentDescriptor value = descriptor.FindArgument("Value");

        Assert.Multiple(() =>
        {
            Assert.That(to.Direction, Is.EqualTo(PaletteArgumentDirection.Out));
            Assert.That(to.Required, Is.True, "Assign.To помечен [RequiredArgument]: без него WF даёт ошибку");
            Assert.That(to.IsReference, Is.True, "To — ссылка, значение обязано быть CSharpReference");
            Assert.That(value.Direction, Is.EqualTo(PaletteArgumentDirection.In));
            Assert.That(value.Required, Is.True);
        });
    }

    [Test]
    public void Find_If_DescribesThenAndElseAsActivityChildren()
    {
        PaletteActivityDescriptor descriptor = _registry.Find("If");

        Assert.That(descriptor, Is.Not.Null);
        Assert.That(descriptor.FindArgument("Condition"), Is.Not.Null);
        Assert.That(descriptor.FindArgument("Condition").Required, Is.True);

        PaletteContainerDescriptor then = descriptor.FindContainer("Then");
        PaletteContainerDescriptor otherwise = descriptor.FindContainer("Else");

        Assert.Multiple(() =>
        {
            Assert.That(then, Is.Not.Null);
            Assert.That(then.Kind, Is.EqualTo(PaletteContainerKind.ActivityChild));
            Assert.That(otherwise, Is.Not.Null);
            Assert.That(descriptor.CanContainActivities, Is.True);
        });
    }

    [Test]
    public void Find_Sequence_ExposesActivitiesCollection()
    {
        PaletteContainerDescriptor activities = _registry.Find("Sequence").FindContainer("Activities");

        Assert.Multiple(() =>
        {
            Assert.That(activities, Is.Not.Null, "Sequence принимает активности только в Activities");
            Assert.That(activities.Kind, Is.EqualTo(PaletteContainerKind.ActivityCollection));
            Assert.That(activities.NeedsFlowStep, Is.False);
        });
    }

    [Test]
    public void Find_ForEach_RecognisesGenericActivityInAnySpelling()
    {
        PaletteActivityDescriptor byShortName = _registry.Find("ForEach");
        PaletteActivityDescriptor byBrackets = _registry.Find("ForEach<>");
        PaletteActivityDescriptor byArity = _registry.Find("ForEach`1");
        PaletteActivityDescriptor byFullName = _registry.Find("System.Activities.Statements.ForEach`1");

        Assert.Multiple(() =>
        {
            Assert.That(byShortName, Is.Not.Null);
            Assert.That(byShortName.Type, Is.EqualTo(typeof(ForEach<>)));
            Assert.That(byShortName.IsGeneric, Is.True);
            Assert.That(byShortName.GenericArity, Is.EqualTo(1));
            Assert.That(byBrackets, Is.SameAs(byShortName));
            Assert.That(byArity, Is.SameAs(byShortName));
            Assert.That(byFullName, Is.SameAs(byShortName));
            Assert.That(byShortName.DisplayName, Is.EqualTo("ForEach<T>"), "подпись как в панели «Активности»");
        });
    }

    [Test]
    public void Find_ForEach_DescribesBodyAsActivityAction()
    {
        PaletteContainerDescriptor body = _registry.Find("ForEach").FindContainer("Body");

        Assert.Multiple(() =>
        {
            Assert.That(body, Is.Not.Null);
            Assert.That(body.Kind, Is.EqualTo(PaletteContainerKind.ActivityAction));
            Assert.That(body.NeedsFlowStep, Is.False);
        });
    }

    [Test]
    public void Find_UnknownActivity_ReturnsNull()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_registry.Find("НетТакойАктивности"), Is.Null);
            Assert.That(_registry.Find(null), Is.Null);
            Assert.That(_registry.Find("  "), Is.Null);
        });
    }

    [Test]
    public void Find_Flowchart_MarksNodesAsUnsupportedMvpContainer()
    {
        PaletteActivityDescriptor flowchart = _registry.Find("Flowchart");

        Assert.That(flowchart, Is.Not.Null);

        PaletteContainerDescriptor nodes = flowchart.FindContainer("Nodes");

        Assert.That(nodes, Is.Not.Null, "узлы блок-схемы должны быть видны агенту как отдельный контейнер");
        Assert.That(nodes.Kind, Is.EqualTo(PaletteContainerKind.FlowchartNodes));
        Assert.That(nodes.NeedsFlowStep, Is.True, "в MVP узлы Flowchart не поддерживаются — это видно из метаданных");
    }

    [Test]
    public void Describe_NotAnActivity_ReturnsNull()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_registry.Describe(typeof(string)), Is.Null);

            // FlowDecision/State — элементы диаграммы, а не активности: отдельно их создать нельзя.
            Assert.That(_registry.Describe(typeof(System.Activities.Statements.FlowDecision)), Is.Null);
            Assert.That(_registry.Describe(typeof(System.Activities.Statements.State)), Is.Null);
            Assert.That(_registry.Describe(null), Is.Null);
        });
    }

    [Test]
    public void Default_CoversEveryBuiltinToolboxCategoryType()
    {
        Type[] toolboxTypes = StudioActivityPalette.BuiltinCategories()
            .SelectMany(category => category.Types)
            .Where(type => typeof(System.Activities.Activity).IsAssignableFrom(type))
            .ToArray();

        Assert.That(toolboxTypes, Is.Not.Empty, "панель «Активности» не может быть пустой");

        string missing = string.Join(", ", toolboxTypes
            .Where(type => _registry.Find(ActivityPaletteRegistry.KeyOf(type)) == null)
            .Select(type => type.Name));

        Assert.That(missing, Is.Empty,
            "реестр инструментов обязан знать всё, что есть в панели «Активности»: " + missing);
    }

    [Test]
    public void Default_CategoriesComeFromToolboxKeys()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_registry.Categories, Contains.Item(StudioActivityPalette.ControlFlowCategory));
            Assert.That(_registry.Categories, Contains.Item(StudioActivityPalette.PrimitivesCategory));
            Assert.That(_registry.InCategory(StudioActivityPalette.PrimitivesCategory).Count, Is.GreaterThan(0));
            Assert.That(_registry.Containers.Select(descriptor => descriptor.Key),
                Has.Some.EqualTo("Sequence").And.Some.EqualTo("If"));
        });
    }
    [Test]
    public void BuiltinCategories_IncludeParallelForEachPickBranchAndFinalState()
    {
        var controlFlow = StudioActivityPalette.BuiltinCategories()
            .Where(category => category.Key == StudioActivityPalette.ControlFlowCategory)
            .SelectMany(category => category.Types)
            .ToArray();

        var stateMachine = StudioActivityPalette.BuiltinCategories()
            .Where(category => category.Key == StudioActivityPalette.StateMachineCategory)
            .SelectMany(category => category.Types)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(controlFlow,
                Does.Contain(typeof(ParallelForEach<>)).And.Contains(typeof(PickBranch)));
            Assert.That(stateMachine,
                Does.Contain(typeof(System.Activities.Core.Presentation.FinalState)));
        });
    }

    [Test]
    public void BuiltinCategories_InvokeDelegateInPrimitives_CollectionsOwnCategory()
    {
        var primitives = StudioActivityPalette.BuiltinCategories()
            .Where(category => category.Key == StudioActivityPalette.PrimitivesCategory)
            .SelectMany(category => category.Types)
            .ToArray();

        var collection = StudioActivityPalette.BuiltinCategories()
            .Where(category => category.Key == StudioActivityPalette.CollectionCategory)
            .SelectMany(category => category.Types)
            .ToArray();

        var flowchart = StudioActivityPalette.BuiltinCategories()
            .Where(category => category.Key == StudioActivityPalette.FlowchartCategory)
            .SelectMany(category => category.Types)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(primitives, Does.Contain(typeof(InvokeDelegate)));
            Assert.That(collection, Does.Contain(typeof(AddToCollection<>))
                .And.Contains(typeof(RemoveFromCollection<>)));
            Assert.That(flowchart, Does.Not.Contain(typeof(AddToCollection<>)));
        });
    }

    [Test]
    public void Categories_IncludeBrowserActivities_WhenBrowserAssemblyIsLoaded()
    {
        System.Reflection.Assembly browser = null;
        try
        {
            browser = System.Reflection.Assembly.Load("Activities.Browser");
        }
        catch (Exception)
        {
        }

        Assume.That(browser, Is.Not.Null, "Activities.Browser не загружена в тестовый домен");


        string[] keys = StudioActivityPalette.Categories()
            .Select(category => category.Key)
            .ToArray();

        Assert.That(keys, Does.Contain(StudioActivityPalette.BrowserCategory),
            "браузерные активности должны попадать в каталог/реестр из общего источника палитры");
    }


}
