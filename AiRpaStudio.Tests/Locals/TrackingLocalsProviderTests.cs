using System;
using System.Activities.Tracking;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ActivitiesTracker.Core;
using NUnit.Framework;

namespace AiRpaStudio.Tests.Locals;

public class TrackingLocalsProviderTests
{
    [Test]
    public void GetSnapshot_NullRecord_ReturnsEmpty()
    {
        var provider = new TrackingLocalsProvider();
        var snapshot = provider.GetSnapshot(null);

        Assert.That(snapshot, Is.Not.Null);
        Assert.That(snapshot.Locals, Is.Empty);
        Assert.That(snapshot.ActivityDisplayName, Is.Null);
    }

    [Test]
    public void GetSnapshot_CollectsVariablesAndArguments_DoesNotFilterStandardArgs()
    {
        var provider = new TrackingLocalsProvider();
        var record = CreateRecord(
            new Dictionary<string, object> { ["userName"] = "max", ["retries"] = 3 },
            new Dictionary<string, object> { ["InputText"] = "hello", ["IsSafe"] = true, ["IsNeedTrace"] = false });

        var snapshot = provider.GetSnapshot(record);

        Assert.That(snapshot.ActivityDisplayName, Is.EqualTo("MyActivity"));

        Dictionary<string, ActivitiesTracker.Entities.LocalVar> byName = snapshot.Locals.ToDictionary(l => l.VarName);
        Assert.That(byName.Count, Is.EqualTo(5));

        Assert.That(byName["userName"].Data, Is.EqualTo("max"));
        Assert.That(byName["userName"].TypeName, Is.EqualTo("String"));
        Assert.That(byName["userName"].Scope, Is.EqualTo("MyActivity"));

        Assert.That(byName["retries"].Data, Is.EqualTo(3));
        Assert.That(byName["retries"].TypeName, Is.EqualTo("Int32"));

        Assert.That(byName["InputText"].Data, Is.EqualTo("hello"));
        Assert.That(byName["InputText"].TypeName, Is.EqualTo("String"));

        Assert.That(byName["IsSafe"].Data, Is.EqualTo(true));
        Assert.That(byName["IsSafe"].TypeName, Is.EqualTo("Boolean"));

        Assert.That(byName["IsNeedTrace"].Data, Is.EqualTo(false));
        Assert.That(byName["IsNeedTrace"].TypeName, Is.EqualTo("Boolean"));
    }

    [Test]
    public void GetSnapshot_NullVariableValue_HasNullTypeName()
    {
        var provider = new TrackingLocalsProvider();
        var record = CreateRecord(
            new Dictionary<string, object> { ["nothing"] = null },
            new Dictionary<string, object>());

        var snapshot = provider.GetSnapshot(record);

        Assert.That(snapshot.Locals.Count, Is.EqualTo(1));
        Assert.That(snapshot.Locals[0].VarName, Is.EqualTo("nothing"));
        Assert.That(snapshot.Locals[0].Data, Is.Null);
        Assert.That(snapshot.Locals[0].TypeName, Is.Null);
    }

    [Test]
    public void GetSnapshot_EmptyRecord_ReturnsEmptyLocals()
    {
        var provider = new TrackingLocalsProvider();
        var record = CreateRecord(new Dictionary<string, object>(), new Dictionary<string, object>());

        var snapshot = provider.GetSnapshot(record);

        Assert.That(snapshot.Locals, Is.Empty);
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