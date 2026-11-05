using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RPA.Core.Services;

namespace AiRpaStudio.Tests;

/// <summary>
/// Обход модели дизайнера (<see cref="DesignerValidationScope"/>) — замена прежнего
/// <c>DesignerValidator.GetModelItemChilds</c>, где в коллекции стоял <c>else { break; }</c>.
/// <para>
/// Тот <c>break</c> означал: первый элемент коллекции «незнакомого» типа обрывает обход всей
/// коллекции. А незнакомым был, например, <c>ActivityAction</c> — тело <c>ForEach</c>/<c>While</c>;
/// <c>PickBranch</c> — ветка <c>Pick</c>; <c>FlowStep</c> за пределами первого элемента;
/// <c>Catch</c>. Поэтому тела циклов, ветки <c>Pick</c> и содержимое кастомных контейнеров вообще
/// не валидировались — карточка «валидация кастомных активностей» (UI-02).
/// </para>
/// <para>
/// Тесты идут по синтетическому дереву через <see cref="IDesignerScopeNode"/>: правила обхода —
/// чистая логика, а настоящий <c>ModelItem</c> требует живого конструктора с STA-потоком.
/// Типы в дереве — настоящие типы WF, поэтому проверяется именно то, что встретится в дизайнере.
/// </para>
/// </summary>
[TestFixture]
public class DesignerValidationScopeTests
{
    // --- Негатив исходного бага: незнакомый элемент коллекции не обрывает обход. ---

    [Test]
    public void Collect_UnknownCollectionItem_DoesNotStopCollection()
    {
        Node root = new Node(typeof(System.Activities.Statements.Sequence))
            .Items(
                "Activities",
                new Node(typeof(string)),
                new Node(typeof(System.Activities.Statements.Assign)));

        IReadOnlyList<DesignerScopeEntry> entries = DesignerValidationScope.Collect(root);

        Assert.That(
            entries.Select(e => e.Node.ItemType),
            Does.Contain(typeof(System.Activities.Statements.Assign)),
            "активность после элемента «незнакомого типа» обязана попасть в обход: " +
            "прежний else { break; } обрывал коллекцию на первом же таком элементе");
    }

    // --- Тела ActivityAction: ForEach/While, Pick, Catch, Flowchart. ---

    [Test]
    public void Collect_ForEachBody_IsDiscovered()
    {
        Node forEach = new Node(typeof(System.Activities.Statements.ForEach<object>))
            .Value(
                "Body",
                new Node(typeof(System.Activities.ActivityAction<object>))
                    .Value("Handler", new Node(typeof(System.Activities.Statements.WriteLine))));

        Node root = new Node(typeof(System.Activities.Statements.Sequence)).Items("Activities", forEach);

        IReadOnlyList<DesignerScopeEntry> entries = DesignerValidationScope.Collect(root);

        Assert.Multiple(() =>
        {
            Assert.That(
                entries.Select(e => e.Node.ItemType),
                Does.Contain(typeof(System.Activities.Statements.WriteLine)),
                "тело ForEach — это ActivityAction, а не активность: без спуска в него содержимое цикла не проверяется");
            Assert.That(
                entries.Select(e => e.Address),
                Does.Contain("root/Activities[0]/Body/Handler"),
                "адрес нужен, чтобы результат фоновой проверки попал на нужную активность");
        });
    }

    [Test]
    public void Collect_PickBranches_AreDiscovered()
    {
        Node branch = new Node(typeof(System.Activities.Statements.PickBranch))
            .Value("Trigger", new Node(typeof(System.Activities.Statements.WriteLine)))
            .Value(
                "Action",
                new Node(typeof(System.Activities.ActivityAction<object>))
                    .Value("Handler", new Node(typeof(System.Activities.Statements.WriteLine))));

        Node pick = new Node(typeof(System.Activities.Statements.Pick)).Items("Branches", branch);
        Node root = new Node(typeof(System.Activities.Statements.Sequence)).Items("Activities", pick);

        List<string> addresses = DesignerValidationScope.Collect(root).Select(e => e.Address).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(addresses, Does.Contain("root/Activities[0]/Branches[0]/Trigger"));
            Assert.That(addresses, Does.Contain("root/Activities[0]/Branches[0]/Action/Handler"));
        });
    }

    [Test]
    public void Collect_FlowchartNodes_AreDiscovered()
    {
        Node flowStep = new Node(typeof(System.Activities.Statements.FlowStep))
            .Value("Content", new Node(typeof(System.Activities.Statements.WriteLine)));
        Node flowchart = new Node(typeof(System.Activities.Statements.Flowchart)).Items("Nodes", flowStep);
        Node root = new Node(typeof(System.Activities.Statements.Sequence)).Items("Activities", flowchart);

        List<string> addresses = DesignerValidationScope.Collect(root).Select(e => e.Address).ToList();

        Assert.That(
            addresses,
            Does.Contain("root/Activities[0]/Nodes[0]/Content"),
            "узел блок-схемы — это FlowStep (не активность), содержимое лежит в Content");
    }

    [Test]
    public void Collect_TryCatchHandlers_AreDiscovered()
    {
        Node handler = new Node(typeof(System.Activities.Statements.Catch<Exception>))
            .Value(
                "Action",
                new Node(typeof(System.Activities.ActivityAction<Exception>))
                    .Value("Handler", new Node(typeof(System.Activities.Statements.WriteLine))));

        Node tryCatch = new Node(typeof(System.Activities.Statements.TryCatch)).Items("Catches", handler);
        Node root = new Node(typeof(System.Activities.Statements.Sequence)).Items("Activities", tryCatch);

        List<string> addresses = DesignerValidationScope.Collect(root).Select(e => e.Address).ToList();

        Assert.That(addresses, Does.Contain("root/Activities[0]/Catches[0]/Action/Handler"));
    }

    [Test]
    public void Collect_CustomContainerActivity_IsTraversed()
    {
        // Кастомная активность-контейнер: своя коллекция активностей с произвольным именем свойства.
        Node custom = new Node(typeof(FakeContainerActivity))
            .Items("Steps", new Node(typeof(System.Activities.Statements.WriteLine)));

        Node root = new Node(typeof(System.Activities.Statements.Sequence)).Items("Activities", custom);

        List<string> addresses = DesignerValidationScope.Collect(root).Select(e => e.Address).ToList();

        Assert.That(
            addresses,
            Does.Contain("root/Activities[0]/Steps[0]"),
            "обход не должен знать имена свойств заранее: содержимое кастомного контейнера валидируется");
    }

    // --- Границы обхода. ---

    [Test]
    public void Collect_VariableSubtree_IsSkipped()
    {
        Node variable = new Node(typeof(System.Activities.Variable<string>))
            .Value("Default", new Node(typeof(System.Activities.Expressions.Literal<string>)));

        Node root = new Node(typeof(System.Activities.Statements.Sequence)).Items("Variables", variable);

        IReadOnlyList<DesignerScopeEntry> entries = DesignerValidationScope.Collect(root);

        Assert.That(
            entries.Select(e => e.Node.ItemType),
            Does.Not.Contain(typeof(System.Activities.Expressions.Literal<string>)),
            "переменная не является точкой валидации: спуск в её значение давал бы «активности», " +
            "которых пользователь не видит в дереве");
    }

    [Test]
    public void Collect_Cycle_DoesNotHang()
    {
        Node state = new Node(typeof(System.Activities.Statements.State));
        Node transition = new Node(typeof(System.Activities.Statements.Transition)).Value("To", state);
        state.Items("Transitions", transition);

        Node root = new Node(typeof(System.Activities.Statements.StateMachine)).Items("States", state);

        IReadOnlyList<DesignerScopeEntry> entries = DesignerValidationScope.Collect(root);

        Assert.That(entries.Count, Is.LessThan(20),
            "переходы между состояниями ссылаются друг на друга: обход обязан отсекать циклы");
    }

    [Test]
    public void Collect_IncludesRootWithStableAddress()
    {
        Node root = new Node(typeof(System.Activities.Statements.Sequence))
            .Items("Activities", new Node(typeof(System.Activities.Statements.Assign)));

        IReadOnlyList<DesignerScopeEntry> entries = DesignerValidationScope.Collect(root);

        Assert.Multiple(() =>
        {
            Assert.That(entries, Is.Not.Empty);
            Assert.That(entries[0].Address, Is.EqualTo(DesignerValidationScope.RootAddress),
                "корень (реализация процесса) — тоже активность, у неё бывают свои аргументы");
            Assert.That(entries[0].Node.ItemType, Is.EqualTo(typeof(System.Activities.Statements.Sequence)));
        });
    }

    [Test]
    public void Collect_NullRoot_ReturnsEmpty()
    {
        Assert.That(DesignerValidationScope.Collect(null), Is.Empty);
    }

    [Test]
    public void Collect_AddressesAreUnique()
    {
        Node root = new Node(typeof(System.Activities.Statements.Sequence))
            .Items(
                "Activities",
                new Node(typeof(System.Activities.Statements.Assign)),
                new Node(typeof(System.Activities.Statements.Assign)));

        List<string> addresses = DesignerValidationScope.Collect(root).Select(e => e.Address).ToList();

        Assert.That(addresses, Is.Unique, "по адресу результат фоновой проверки сопоставляется с активностью");
    }

    // --- Классификация типов. ---

    [Test]
    public void IsContainerType_RecognizesWorkflowWrappers()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DesignerValidationScope.IsContainerType(typeof(System.Activities.ActivityAction<object>)), Is.True);
            Assert.That(DesignerValidationScope.IsContainerType(typeof(System.Activities.Statements.FlowStep)), Is.True);
            Assert.That(DesignerValidationScope.IsContainerType(typeof(System.Activities.Statements.Catch<Exception>)), Is.True);
            Assert.That(DesignerValidationScope.IsContainerType(typeof(System.Activities.Statements.State)), Is.True);
            Assert.That(DesignerValidationScope.IsContainerType(typeof(System.Activities.Statements.PickBranch)), Is.True);
        });
    }

    [Test]
    public void IsContainerType_RejectsVariablesArgumentsAndPlainTypes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DesignerValidationScope.IsContainerType(typeof(System.Activities.Variable<string>)), Is.False);
            Assert.That(DesignerValidationScope.IsContainerType(typeof(System.Activities.Argument)), Is.False);
            Assert.That(DesignerValidationScope.IsContainerType(typeof(string)), Is.False);
            Assert.That(DesignerValidationScope.IsContainerType(null), Is.False);
        });
    }

    /// <summary>Кастомная активность-контейнер (только для типа в дереве).</summary>
    public sealed class FakeContainerActivity : System.Activities.Activity
    {
    }

    // --- Синтетическое дерево модели. ---

    private sealed class Node : IDesignerScopeNode
    {
        private readonly List<Property> _properties = new List<Property>();

        internal Node(Type itemType)
        {
            ItemType = itemType;
        }

        public Type ItemType { get; }

        public object Identity => this;

        public IEnumerable<IDesignerScopeProperty> Properties => _properties;

        internal Node Value(string name, Node value)
        {
            _properties.Add(new Property(name, value, null));
            return this;
        }

        internal Node Items(string name, params Node[] items)
        {
            _properties.Add(new Property(name, null, items));
            return this;
        }
    }

    private sealed class Property : IDesignerScopeProperty
    {
        internal Property(string name, Node value, Node[] items)
        {
            Name = name;
            Value = value;
            Items = items ?? Array.Empty<Node>();
        }

        public string Name { get; }

        public IDesignerScopeNode Value { get; }

        public IEnumerable<IDesignerScopeNode> Items { get; }
    }
}
