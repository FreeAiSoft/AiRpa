using System.Collections.Generic;
using ActivitiesTracker.Entities;
using RPA.Locals;
using RPA.Models;

namespace AiRpaStudio.Tests.Locals;

public class LocalsModelTests
{
    [Test]
    public void Update_SetsRootsAndDisplayName_AndClearsSelection()
    {
        var model = new LocalsModel();
        var snapshot = new LocalsSnapshot(
            new List<LocalVar> { new LocalVar("a", 1, "Int32", "scope") },
            "MyActivity");

        model.Update(snapshot);

        Assert.That(model.Roots.Count, Is.EqualTo(1));
        Assert.That(model.Roots[0].Name, Is.EqualTo("a"));
        Assert.That(model.Roots[0].IsExpanded, Is.True);
        Assert.That(model.ActivityDisplayName, Is.EqualTo("MyActivity"));
        Assert.That(model.SelectedNode, Is.Null);
    }

    [Test]
    public void Update_NullSnapshot_Clears()
    {
        var model = new LocalsModel();
        model.Update(new LocalsSnapshot(
            new List<LocalVar> { new LocalVar("a", 1, "Int32", "s") },
            "X"));
        model.SelectedNode = model.Roots[0];

        model.Update(null);

        Assert.That(model.Roots, Is.Empty);
        Assert.That(model.ActivityDisplayName, Is.Null);
        Assert.That(model.SelectedNode, Is.Null);
    }

    [Test]
    public void Clear_RemovesRootsAndSelection()
    {
        var model = new LocalsModel();
        model.Update(new LocalsSnapshot(
            new List<LocalVar> { new LocalVar("a", 1, "Int32", "s") },
            "X"));
        model.SelectedNode = model.Roots[0];

        model.Clear();

        Assert.That(model.Roots, Is.Empty);
        Assert.That(model.ActivityDisplayName, Is.Null);
        Assert.That(model.SelectedNode, Is.Null);
    }

    [Test]
    public void SelectedNode_RaisesPropertyChanged()
    {
        var model = new LocalsModel();
        var node = new LocalsNode("n", "v", "t", LocalsNodeKind.Scalar, false, null);
        var raised = new List<string>();
        model.PropertyChanged += (s, e) => raised.Add(e.PropertyName);

        model.SelectedNode = node;

        Assert.That(model.SelectedNode, Is.SameAs(node));
        Assert.That(raised, Does.Contain("SelectedNode"));
    }
}
