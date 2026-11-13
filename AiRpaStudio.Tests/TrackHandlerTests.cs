using System.Activities;
using System.Activities.Debugger;
using System.Activities.Statements;
using System.Collections.Generic;
using NUnit.Framework;
using RPA.Helpers;

namespace AiRpaStudio.Tests;

[TestFixture]
public class TrackHandlerTests
{
    [Test]
    public void GetSourceLocation_WhenRuntimeIdMatchesDesigner_ReturnsLocation()
    {
        var expected = new SourceLocation("Main.xaml", 3, 4, 5, 6);
        var mapping = new Dictionary<object, SourceLocation>
        {
            { FirstChild(), expected }
        };

        SourceLocation location = TrackHandler.GetSourceLocation(mapping, FirstChild());

        Assert.That(location, Is.SameAs(expected));
    }

    [Test]
    public void GetSourceLocation_WhenRuntimeIdDiffersFromDesigner_ReturnsNull()
    {
        // Ограничение варианта A: дизайнер открыт loose, прогон идёт по собранной сборке.
        // Одинаковая форма дерева не гарантирует совпадение Activity.Id (фасад vs реальные тела),
        // поэтому внутри вложенного SubProcessN подсветка/точка останова не находятся.
        Activity[] designerChildren = Children();
        Activity[] runtimeChildren = Children();
        var mapping = new Dictionary<object, SourceLocation>
        {
            { designerChildren[0], new SourceLocation("Main.xaml", 1, 1, 2, 1) }
        };

        SourceLocation location = TrackHandler.GetSourceLocation(mapping, runtimeChildren[1]);

        Assert.That(location, Is.Null);
    }

    [Test]
    public void GetSourceLocation_OnEmptyMapping_ReturnsNull()
    {
        var mapping = new Dictionary<object, SourceLocation>();

        SourceLocation location = TrackHandler.GetSourceLocation(mapping, FirstChild());

        Assert.That(location, Is.Null);
    }

    private static Activity FirstChild()
    {
        return Children()[0];
    }

    private static Activity[] Children()
    {
        var root = new Sequence
        {
            Activities =
            {
                new WriteLine { Text = "a" },
                new WriteLine { Text = "b" }
            }
        };
        WorkflowInspectionServices.CacheMetadata(root);

        var children = new List<Activity>();
        foreach (Activity activity in WorkflowInspectionServices.GetActivities(root))
        {
            if (activity is WriteLine)
            {
                children.Add(activity);
            }
        }

        return children.ToArray();
    }
}