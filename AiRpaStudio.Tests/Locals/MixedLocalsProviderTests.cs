using System;
using System.Activities.Tracking;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ActivitiesTracker.Core;
using ActivitiesTracker.Entities;
using NUnit.Framework;

namespace AiRpaStudio.Tests.Locals;

public class MixedLocalsProviderTests
{
    [Test]
    public void GetSnapshot_NullRecord_ReturnsEmpty()
    {
        var provider = new MixedLocalsProvider();

        LocalsSnapshot snapshot = provider.GetSnapshot(null);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot, Is.Not.Null);
            Assert.That(snapshot.Locals, Is.Empty);
            Assert.That(snapshot.ActivityDisplayName, Is.Null);
        });
    }

    [Test]
    public void GetSnapshot_WithoutActivityInstance_FallsBackToTracking()
    {
        var provider = new MixedLocalsProvider();
        var record = CreateRecord(
            new Dictionary<string, object> { ["userName"] = "max" },
            new Dictionary<string, object> { ["InputText"] = "hello" });

        LocalsSnapshot snapshot = provider.GetSnapshot(record);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.ActivityDisplayName, Is.EqualTo("MyActivity"));

            Dictionary<string, LocalVar> byName = snapshot.Locals.ToDictionary(l => l.VarName);
            Assert.That(byName.Count, Is.EqualTo(2));
            Assert.That(byName["userName"].Data, Is.EqualTo("max"));
            Assert.That(byName["InputText"].Data, Is.EqualTo("hello"));
        });
    }

    private static ActivityStateRecord CreateRecord(
        IDictionary<string, object> variables,
        IDictionary<string, object> arguments)
    {
        var activity = new ActivityInfo("MyActivity", "1", "1", "MyActivity");
        var record = new ActivityStateRecord(Guid.NewGuid(), 0, activity, "Closed");

        // ActivityStateRecord.Variables/Arguments имеют internal setter и вычисляются лениво
        // из ActivityInstance; для юнит-теста коллектор подставляем через приватные поля.
        SetField(record, "variables", variables);
        SetField(record, "arguments", arguments);
        return record;
    }

    private static void SetField(object target, string name, object value)
    {
        FieldInfo field = typeof(ActivityStateRecord).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, "внутреннее поле ActivityStateRecord '{0}' не найдено", name);
        field.SetValue(target, value);
    }
}
