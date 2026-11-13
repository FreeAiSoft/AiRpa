using System.Activities;
using System.Activities.Debugger;
using System.Activities.Statements;
using System.Collections.Generic;
using NUnit.Framework;
using RPA.Helpers;

namespace AiRpaStudio.Tests;

[TestFixture]
public class TrackHandlerDesignerLocationTests
{
    [TestCase("1.6", "6")]
    [TestCase("1.1", "1")]
    [TestCase("1.5.3.1", "1")]
    [TestCase("6", "6")]
    [TestCase(null, null)]
    [TestCase("", null)]
    public void LastIdComponent_ReturnsLastComponent(string id, string expected)
    {
        Assert.That(TrackHandler.LastIdComponent(id), Is.EqualTo(expected));
    }

    [Test]
    public void ResolveDesignerLocation_MatchesByLastComponent()
    {
        // Дизайнер открыт loose: у корня Sequence плоский Id "1".
        Activity designerActivity = new Sequence();
        WorkflowInspectionServices.CacheMetadata(designerActivity);

        var designerMapping = new Dictionary<object, SourceLocation>
        {
            { designerActivity, new SourceLocation("Main.xaml", 28, 3, 51, 14) }
        };

        // Рантайм: активность с тем же последним компонентом QualifiedId ("1").
        Activity runtimeActivity = new Sequence();
        WorkflowInspectionServices.CacheMetadata(runtimeActivity);

        SourceLocation resolved = TrackHandler.ResolveDesignerLocation(designerMapping, runtimeActivity);

        Assert.That(resolved, Is.Not.Null);
        Assert.That(resolved.StartLine, Is.EqualTo(28));
        Assert.That(resolved.StartColumn, Is.EqualTo(3));
    }

    [Test]
    public void ResolveDesignerLocation_NoMatchByLastComponent_ReturnsNull()
    {
        // Ключ дизайнера — корень с Id "1".
        Activity designerActivity = new Sequence();
        WorkflowInspectionServices.CacheMetadata(designerActivity);

        var designerMapping = new Dictionary<object, SourceLocation>
        {
            { designerActivity, new SourceLocation("Main.xaml", 1, 1, 1, 1) }
        };

        // Рантайм-ребёнок с Id "1.2" (последний компонент "2") не совпадает с ключом "1".
        var root = new Sequence
        {
            Activities =
            {
                new WriteLine { Text = "a" },
                new WriteLine { Text = "b" }
            }
        };
        WorkflowInspectionServices.CacheMetadata(root);

        Activity runtimeChild = null;
        foreach (Activity a in WorkflowInspectionServices.GetActivities(root))
        {
            if (a is WriteLine && a.Id != null && a.Id.EndsWith("2"))
            {
                runtimeChild = a;
                break;
            }
        }

        Assert.That(runtimeChild, Is.Not.Null, "не найден рантайм-ребёнок с Id, оканчивающимся на 2");

        SourceLocation resolved = TrackHandler.ResolveDesignerLocation(designerMapping, runtimeChild);

        Assert.That(resolved, Is.Null);
    }
}
