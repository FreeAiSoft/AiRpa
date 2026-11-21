using System;
using System.Collections.Generic;
using System.Activities;
using System.Data;
using ActivitiesTracker.Entities;
using NUnit.Framework;
using RPA.Locals;

namespace AiRpaStudio.Tests.Locals;

public class ValueInspectorTests
{
    private ValueInspector inspector;

    [SetUp]
    public void SetUp()
    {
        inspector = new ValueInspector();
    }

    [Test]
    public void BuildNode_Null_IsNullKind()
    {
        LocalsNode node = inspector.BuildNode("x", null);
        Assert.That(node.Kind, Is.EqualTo(LocalsNodeKind.Null));
        Assert.That(node.DisplayValue, Is.EqualTo("null"));
        Assert.That(node.TypeName, Is.Null);
        Assert.That(node.IsExpandable, Is.False);
    }

    [Test]
    public void BuildNode_DBNull_IsNullKind()
    {
        LocalsNode node = inspector.BuildNode("x", DBNull.Value);
        Assert.That(node.Kind, Is.EqualTo(LocalsNodeKind.Null));
        Assert.That(node.DisplayValue, Is.EqualTo("DBNull"));
    }

    [Test]
    public void BuildNode_String_IsScalar()
    {
        LocalsNode node = inspector.BuildNode("name", "hello");
        Assert.That(node.Kind, Is.EqualTo(LocalsNodeKind.Scalar));
        Assert.That(node.DisplayValue, Is.EqualTo("hello"));
        Assert.That(node.TypeName, Is.EqualTo("String"));
    }

    [Test]
    public void BuildNode_Int_IsScalar()
    {
        LocalsNode node = inspector.BuildNode("n", 42);
        Assert.That(node.Kind, Is.EqualTo(LocalsNodeKind.Scalar));
        Assert.That(node.DisplayValue, Is.EqualTo("42"));
        Assert.That(node.TypeName, Is.EqualTo("Int32"));
    }

    [Test]
    public void BuildNode_Enum_IsScalarWithName()
    {
        LocalsNode node = inspector.BuildNode("d", DayOfWeek.Friday);
        Assert.That(node.Kind, Is.EqualTo(LocalsNodeKind.Scalar));
        Assert.That(node.DisplayValue, Is.EqualTo("Friday"));
        Assert.That(node.TypeName, Is.EqualTo("DayOfWeek"));
    }

    [Test]
    public void BuildNode_DateTime_IsScalar()
    {
        DateTime value = new DateTime(2024, 1, 2, 3, 4, 5);
        LocalsNode node = inspector.BuildNode("d", value);
        Assert.That(node.Kind, Is.EqualTo(LocalsNodeKind.Scalar));
        Assert.That(node.TypeName, Is.EqualTo("DateTime"));
    }

    [Test]
    public void BuildNode_List_IsCollectionWithIndexedChildren()
    {
        LocalsNode node = inspector.BuildNode("list", new List<int> { 1, 2, 3 });
        Assert.That(node.Kind, Is.EqualTo(LocalsNodeKind.Collection));
        Assert.That(node.DisplayValue, Is.EqualTo("Count = 3"));
        Assert.That(node.TypeName, Is.EqualTo("List<Int32>"));
        Assert.That(node.IsExpandable, Is.True);

        Assert.That(node.Children.Count, Is.EqualTo(3));
        Assert.That(node.Children[0].Name, Is.EqualTo("[0]"));
        Assert.That(node.Children[0].DisplayValue, Is.EqualTo("1"));
        Assert.That(node.Children[2].Name, Is.EqualTo("[2]"));
        Assert.That(node.Children[2].DisplayValue, Is.EqualTo("3"));
    }

    [Test]
    public void BuildNode_Dictionary_HasKeyValueChildren()
    {
        LocalsNode node = inspector.BuildNode("map", new Dictionary<string, int> { ["a"] = 1 });
        Assert.That(node.Kind, Is.EqualTo(LocalsNodeKind.Dictionary));
        Assert.That(node.DisplayValue, Is.EqualTo("Count = 1"));

        Assert.That(node.Children.Count, Is.EqualTo(1));
        LocalsNode entry = node.Children[0];
        Assert.That(entry.Name, Is.EqualTo("[a]"));
        Assert.That(entry.Kind, Is.EqualTo(LocalsNodeKind.Dictionary));

        Assert.That(entry.Children.Count, Is.EqualTo(2));
        Assert.That(entry.Children[0].Name, Is.EqualTo("Key"));
        Assert.That(entry.Children[0].DisplayValue, Is.EqualTo("a"));
        Assert.That(entry.Children[1].Name, Is.EqualTo("Value"));
        Assert.That(entry.Children[1].DisplayValue, Is.EqualTo("1"));
    }

    [Test]
    public void BuildNode_DataTable_IsTableWithRowChildren()
    {
        DataTable table = new DataTable();
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("Age", typeof(int));
        table.Rows.Add("Ann", 30);
        table.Rows.Add("Bob", 40);

        LocalsNode node = inspector.BuildNode("dt", table);
        Assert.That(node.Kind, Is.EqualTo(LocalsNodeKind.Table));
        Assert.That(node.DisplayValue, Is.EqualTo("Rows = 2, Columns = 2"));

        Assert.That(node.Children.Count, Is.EqualTo(2));
        LocalsNode row = node.Children[0];
        Assert.That(row.Name, Is.EqualTo("[0]"));
        Assert.That(row.Children.Count, Is.EqualTo(2));
        Assert.That(row.Children[0].Name, Is.EqualTo("Name"));
        Assert.That(row.Children[0].DisplayValue, Is.EqualTo("Ann"));
        Assert.That(row.Children[1].Name, Is.EqualTo("Age"));
        Assert.That(row.Children[1].DisplayValue, Is.EqualTo("30"));
    }

    [Test]
    public void BuildNode_Object_EnumeratesPropertiesAndFields()
    {
        var value = new SampleObject { Text = "hi", Number = 7, PublicField = 9 };
        LocalsNode node = inspector.BuildNode("obj", value);
        Assert.That(node.Kind, Is.EqualTo(LocalsNodeKind.Object));
        Assert.That(node.DisplayValue, Is.EqualTo("{SampleObject}"));

        List<string> names = ChildNames(node);
        Assert.That(names, Does.Contain("Text"));
        Assert.That(names, Does.Contain("Number"));
        Assert.That(names, Does.Contain("PublicField"));
    }

    [Test]
    public void BuildNode_Cycle_IsError()
    {
        Node root = new Node { Name = "root" };
        root.Next = root;

        LocalsNode node = inspector.BuildNode("n", root);
        LocalsNode next = FindChild(node, "Next");
        Assert.That(next, Is.Not.Null);
        Assert.That(next.Kind, Is.EqualTo(LocalsNodeKind.Error));
        Assert.That(next.DisplayValue, Is.EqualTo("(cycle)"));
    }

    [Test]
    public void BuildNode_ThrowingProperty_IsErrorWithMessage()
    {
        LocalsNode node = inspector.BuildNode("t", new ThrowingProperty());
        LocalsNode child = FindChild(node, "Boom");
        Assert.That(child, Is.Not.Null);
        Assert.That(child.Kind, Is.EqualTo(LocalsNodeKind.Error));
        Assert.That(child.DisplayValue, Does.Contain("boom"));
    }

    [Test]
    public void BuildNode_MaxDepth_StopsExpansion()
    {
        var a = new Node { Name = "a", Next = new Node { Name = "b", Next = new Node { Name = "c" } } };

        inspector.MaxDepth = 2;
        LocalsNode node = inspector.BuildNode("n", a);
        LocalsNode b = FindChild(node, "Next");
        LocalsNode c = FindChild(b, "Next");
        Assert.That(c, Is.Not.Null);
        Assert.That(c.DisplayValue, Is.EqualTo("(max depth)"));
        Assert.That(c.IsExpandable, Is.False);
    }

    [Test]
    public void BuildNode_MaxItems_TruncatesCollection()
    {
        var list = new List<int> { 1, 2, 3, 4, 5 };
        inspector.MaxItems = 2;
        LocalsNode node = inspector.BuildNode("list", list);
        Assert.That(node.Children.Count, Is.EqualTo(3));
        Assert.That(node.Children[2].Kind, Is.EqualTo(LocalsNodeKind.Error));
        Assert.That(node.Children[2].DisplayValue, Is.EqualTo("(more items)"));
    }

    [Test]
    public void BuildNodes_MapsLocals()
    {
        var locals = new List<LocalVar>
        {
            new LocalVar("a", 1, "Int32", "scope"),
            new LocalVar("b", null, null, "scope")
        };

        var nodes = inspector.BuildNodes(locals);
        Assert.That(nodes.Count, Is.EqualTo(2));
        Assert.That(nodes[0].Kind, Is.EqualTo(LocalsNodeKind.Scalar));
        Assert.That(nodes[1].Kind, Is.EqualTo(LocalsNodeKind.Null));
    }

    [Test]
    public void BuildNodes_UnassignedArgument_IsMarkedNotAssigned()
    {
        var locals = new List<LocalVar>
        {
            new LocalVar("OutputText", null, "String", "scope", LocalVarKind.Argument, ArgumentDirection.Out, "Executing", true)
        };

        var nodes = inspector.BuildNodes(locals);

        Assert.Multiple(() =>
        {
            Assert.That(nodes.Count, Is.EqualTo(1));
            Assert.That(nodes[0].Name, Is.EqualTo("OutputText"));
            Assert.That(nodes[0].DisplayValue, Is.EqualTo("не присвоен"));
            Assert.That(nodes[0].Kind, Is.EqualTo(LocalsNodeKind.Null));
            Assert.That(nodes[0].TypeName, Is.EqualTo("String"));
            Assert.That(nodes[0].IsExpandable, Is.False);
        });
    }

    [Test]
    public void LocalsNode_Children_AreLazyAndCached()
    {
        int loads = 0;
        var node = new LocalsNode("n", "v", "t", LocalsNodeKind.Object, true,
            () =>
            {
                loads++;
                return new List<LocalsNode>
                {
                    new LocalsNode("c", "1", "Int32", LocalsNodeKind.Scalar, false, null)
                };
            });

        Assert.That(loads, Is.EqualTo(0));
        var first = node.Children;
        Assert.That(loads, Is.EqualTo(1));
        var second = node.Children;
        Assert.That(loads, Is.EqualTo(1));
        Assert.That(first, Is.SameAs(second));
        Assert.That(first.Count, Is.EqualTo(1));
    }

    private static List<string> ChildNames(LocalsNode node)
    {
        var names = new List<string>();
        foreach (LocalsNode child in node.Children)
        {
            names.Add(child.Name);
        }

        return names;
    }

    private static LocalsNode FindChild(LocalsNode node, string name)
    {
        foreach (LocalsNode child in node.Children)
        {
            if (child.Name == name)
            {
                return child;
            }
        }

        return null;
    }

    public class SampleObject
    {
        public string Text { get; set; }
        public int Number { get; set; }
        public int PublicField;
    }

    public class Node
    {
        public string Name { get; set; }
        public Node Next { get; set; }
    }

    public class ThrowingProperty
    {
        public int Boom => throw new InvalidOperationException("boom");
    }
}