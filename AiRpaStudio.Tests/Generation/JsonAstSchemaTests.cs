using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RPA.Core.Generation;

namespace AiRpaStudio.Tests.Generation;

/// <summary>
/// Этап 6 плана <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c>: строгая JSON-схема процесса
/// и её проверка <b>до</b> построения модели WF.
/// <para>
/// Проверяется главное обещание этапа: неизвестное поле, неизвестная активность и неверный тип
/// переменной не доходят до объектной модели, а возвращаются агенту как список ошибок с путём
/// до поля и подсказкой. Whitelist'ы берутся из палитры студии, а не из второго списка.
/// </para>
/// </summary>
[TestFixture]
public class JsonAstSchemaTests
{
    private const string SimpleDocument = @"{
  ""project"": ""RPA-1"",
  ""variables"": [ { ""name"": ""output"", ""type"": ""String"" } ],
  ""activities"": [
    { ""type"": ""Assign"", ""arguments"": { ""To"": ""output"", ""Value"": ""\""Hello\"""" } },
    { ""type"": ""WriteLine"", ""arguments"": { ""Text"": ""output"" } }
  ]
}";

    [Test]
    public void Schema_IsStrict_AndDeclaresKnownActivityAndTypes()
    {
        JObject schema = WorkflowJsonSchema.ToJsonSchema();

        var activitySchema = (JObject)schema["definitions"]["activity"];
        var activityEnum = (JArray)activitySchema["properties"]["type"]["enum"];
        var typeEnum = (JArray)schema["definitions"]["variable"]["properties"]["type"]["enum"];

        Assert.Multiple(() =>
        {
            Assert.That(schema["additionalProperties"].Value<bool>(), Is.False,
                "лишние поля в описании процесса отклоняются");
            Assert.That(activitySchema["additionalProperties"].Value<bool>(), Is.False,
                "лишние поля в описании активности отклоняются");
            Assert.That(activityEnum.Select(token => token.Value<string>()), Does.Contain("WriteLine"));
            Assert.That(activityEnum.Select(token => token.Value<string>()), Does.Contain("Sequence"));
            Assert.That(typeEnum.Select(token => token.Value<string>()), Does.Contain("String"));
            Assert.That(typeEnum.Select(token => token.Value<string>()), Does.Contain("Int32"));
        });
    }

    [Test]
    public void Schema_ActivityWhitelist_IsTheStudioPalette()
    {
        IReadOnlyList<string> keys = WorkflowJsonSchema.ActivityKeys();
        IReadOnlyList<string> paletteKeys = ActivityPaletteRegistry.Default.All
            .Select(descriptor => descriptor.Key)
            .OrderBy(key => key, System.StringComparer.Ordinal)
            .ToArray();

        Assert.That(keys, Is.EqualTo(paletteKeys),
            "whitelist JSON обязан совпадать с палитрой: второй список рано или поздно разойдётся");
    }

    [Test]
    public void Schema_Example_IsValid()
    {
        WorkflowJsonValidation validation = Validate(WorkflowJsonSchema.Example());

        Assert.That(validation.Ok, Is.True,
            "пример из схемы обязан проходить валидацию.\n" + validation.Describe());
    }

    [Test]
    public void Validate_SimpleDocument_ReturnsDocument()
    {
        WorkflowJsonValidation validation = Validate(SimpleDocument);

        Assert.That(validation.Ok, Is.True, validation.Describe());

        Assert.Multiple(() =>
        {
            Assert.That(validation.Document.ProjectName, Is.EqualTo("RPA-1"));
            Assert.That(validation.Document.ActivityCount, Is.EqualTo(2), "посчитаны описанные активности");
            Assert.That(validation.Document.VariableNames, Is.EqualTo(new[] { "output" }));
            Assert.That(validation.Document.Root, Is.Not.Null);
            Assert.That(validation.Document.Root.ActivityType, Is.EqualTo("Sequence"),
                "форма activities даёт корневой Sequence");
        });
    }

    [Test]
    public void Validate_UnknownDocumentField_ReportsPathAndAllowedFields()
    {
        WorkflowJsonValidation validation = Validate(@"{
  ""activities"": [ { ""type"": ""WriteLine"", ""arguments"": { ""Text"": ""\""x\"""" } } ],
  ""unknown"": 1
}");

        string errors = Join(validation.Errors);

        Assert.Multiple(() =>
        {
            Assert.That(validation.Ok, Is.False);
            Assert.That(errors, Does.Contain("$.unknown"), "путь до поля обязателен");
            Assert.That(errors, Does.Contain("допустимые"), "агент видит список разрешённых полей");
        });
    }

    [Test]
    public void Validate_UnknownActivity_ReportsWhitelistHint()
    {
        WorkflowJsonValidation validation = Validate(@"{
  ""activities"": [ { ""type"": ""Teleport"" } ]
}");

        Assert.Multiple(() =>
        {
            Assert.That(validation.Ok, Is.False);
            Assert.That(Join(validation.Errors), Does.Contain("$.activities[0].type"));
            Assert.That(Join(validation.Errors), Does.Contain("whitelist"));
            Assert.That(Join(validation.Hints), Does.Contain("Sequence"),
                "подсказка перечисляет доступные активности");
        });
    }

    [Test]
    public void Validate_UnknownVariableType_ReportsError()
    {
        WorkflowJsonValidation validation = Validate(@"{
  ""variables"": [ { ""name"": ""data"", ""type"": ""MyCustomClass"" } ],
  ""activities"": [ { ""type"": ""WriteLine"", ""arguments"": { ""Text"": ""\""x\"""" } } ]
}");

        Assert.Multiple(() =>
        {
            Assert.That(validation.Ok, Is.False);
            Assert.That(Join(validation.Errors), Does.Contain("$.variables[0].type"));
            Assert.That(Join(validation.Hints), Does.Contain("String"), "подсказка перечисляет допустимые типы");
        });
    }

    [Test]
    public void Validate_VariableWithoutType_ReportsError()
    {
        WorkflowJsonValidation validation = Validate(@"{
  ""variables"": [ { ""name"": ""data"" } ],
  ""activities"": [ { ""type"": ""WriteLine"", ""arguments"": { ""Text"": ""\""x\"""" } } ]
}");

        Assert.That(Join(validation.Errors), Does.Contain("$.variables[0].type: тип переменной не задан"));
    }

    [Test]
    public void Validate_DuplicateVariables_ReportsError()
    {
        WorkflowJsonValidation validation = Validate(@"{
  ""variables"": [
    { ""name"": ""data"", ""type"": ""String"" },
    { ""name"": ""data"", ""type"": ""Int32"" }
  ],
  ""activities"": [ { ""type"": ""WriteLine"", ""arguments"": { ""Text"": ""\""x\"""" } } ]
}");

        Assert.That(Join(validation.Errors), Does.Contain("объявлена дважды"));
    }

    [Test]
    public void Validate_VariablesOnNonContainer_ReportsError()
    {
        WorkflowJsonValidation validation = Validate(@"{
  ""activities"": [
    { ""type"": ""WriteLine"", ""variables"": [ { ""name"": ""data"", ""type"": ""String"" } ],
      ""arguments"": { ""Text"": ""\""x\"""" } }
  ]
}");

        Assert.Multiple(() =>
        {
            Assert.That(validation.Ok, Is.False);
            Assert.That(Join(validation.Errors), Does.Contain("не объявляет переменные"));
        });
    }

    [Test]
    public void Validate_ReferenceArgumentWithLiteral_ReportsError()
    {
        WorkflowJsonValidation validation = Validate(@"{
  ""variables"": [ { ""name"": ""output"", ""type"": ""String"" } ],
  ""activities"": [
    { ""type"": ""Assign"", ""arguments"": {
        ""To"": { ""value"": ""output"", ""type"": ""String"" },
        ""Value"": ""\""x\"""" } }
  ]
}");

        Assert.Multiple(() =>
        {
            Assert.That(validation.Ok, Is.False);
            Assert.That(Join(validation.Errors), Does.Contain("$.activities[0].arguments.To"));
        });
    }

    [Test]
    public void Validate_AssignWithoutTypes_ReportsError()
    {
        WorkflowJsonValidation validation = Validate(@"{
  ""activities"": [
    { ""type"": ""Assign"", ""arguments"": { ""To"": ""unknown"", ""Value"": ""1"" } }
  ]
}");

        Assert.Multiple(() =>
        {
            Assert.That(validation.Ok, Is.False);
            Assert.That(Join(validation.Errors), Does.Contain("тип To/Value не определён"),
                "молчаливый object в XAML недопустим");
            Assert.That(Join(validation.Hints), Does.Contain("type"));
        });
    }

    [Test]
    public void Validate_AssignInfersTypesFromVariable()
    {
        WorkflowJsonValidation validation = Validate(SimpleDocument);

        Assert.That(validation.Ok, Is.True, validation.Describe());

        PaletteActivitySpec assign = validation.Document.Root.Children["Activities"].First();
        PaletteValue to = assign.Arguments["To"];
        PaletteValue value = assign.Arguments["Value"];

        Assert.Multiple(() =>
        {
            Assert.That(to.TypeName, Is.EqualTo("String"), "тип To выведен из объявленной переменной");
            Assert.That(value.TypeName, Is.EqualTo("String"),
                "тип Value унаследован от To: иначе в XAML попал бы object");
        });
    }

    [Test]
    public void Validate_GenericActivityWithoutTypeArguments_ReportsError()
    {
        WorkflowJsonValidation validation = Validate(@"{
  ""activities"": [
    { ""type"": ""ForEach"", ""arguments"": {
        ""Values"": { ""expression"": ""new string[0]"", ""type"": ""String[]"" } },
      ""children"": { ""Body"": [ { ""type"": ""WriteLine"", ""arguments"": { ""Text"": ""\""x\"""" } } ] } }
  ]
}");

        Assert.Multiple(() =>
        {
            Assert.That(validation.Ok, Is.False);
            Assert.That(Join(validation.Errors), Does.Contain("typeArguments"));
        });
    }

    [Test]
    public void Validate_GenericActivityWithUnknownTypeArgument_ReportsError()
    {
        WorkflowJsonValidation validation = Validate(@"{
  ""activities"": [
    { ""type"": ""ForEach"", ""typeArguments"": [ ""MyType"" ],
      ""children"": { ""Body"": [ { ""type"": ""WriteLine"", ""arguments"": { ""Text"": ""\""x\"""" } } ] } }
  ]
}");

        Assert.Multiple(() =>
        {
            Assert.That(validation.Ok, Is.False);
            Assert.That(Join(validation.Errors), Does.Contain("MyType"));
            Assert.That(Join(validation.Hints), Does.Contain("String"));
        });
    }

    [Test]
    public void Validate_UnknownContainer_ReportsError()
    {
        WorkflowJsonValidation validation = Validate(@"{
  ""activities"": [
    { ""type"": ""If"", ""arguments"": { ""Condition"": ""true"" },
      ""children"": { ""Branches"": [ { ""type"": ""WriteLine"", ""arguments"": { ""Text"": ""\""x\"""" } } ] } }
  ]
}");

        Assert.Multiple(() =>
        {
            Assert.That(validation.Ok, Is.False);
            Assert.That(Join(validation.Errors), Does.Contain("нет контейнера"));
        });
    }

    [Test]
    public void Validate_NestedError_ReportsFullPath()
    {
        WorkflowJsonValidation validation = Validate(@"{
  ""activities"": [
    { ""type"": ""If"", ""arguments"": { ""Condition"": ""true"" },
      ""children"": { ""Then"": [ { ""type"": ""Teleport"" } ] } }
  ]
}");

        Assert.That(Join(validation.Errors), Does.Contain("$.activities[0].children.Then[0].type"),
            "путь до вложенного поля — единственный способ найти ошибку в большом описании");
    }

    [Test]
    public void Validate_UnknownValueField_ReportsError()
    {
        WorkflowJsonValidation validation = Validate(@"{
  ""activities"": [
    { ""type"": ""WriteLine"", ""arguments"": { ""Text"": { ""expr"": ""\""x\"""", ""lambda"": ""() => 1"" } } }
  ]
}");

        Assert.Multiple(() =>
        {
            Assert.That(validation.Ok, Is.False);
            Assert.That(Join(validation.Errors), Does.Contain("$.activities[0].arguments.Text.lambda"));
        });
    }

    [Test]
    public void Validate_BrokenJson_ReportsPosition()
    {
        WorkflowJsonValidation validation = Validate("{ \"activities\": [ ");

        Assert.Multiple(() =>
        {
            Assert.That(validation.Ok, Is.False);
            Assert.That(Join(validation.Errors), Does.Contain("JSON не разобран"));
            Assert.That(Join(validation.Hints), Does.Contain("activities"), "подсказка напоминает схему");
        });
    }

    [Test]
    public void Validate_ActivitiesAndRootTogether_ReportsError()
    {
        WorkflowJsonValidation validation = Validate(@"{
  ""root"": { ""type"": ""Sequence"" },
  ""activities"": [ { ""type"": ""WriteLine"", ""arguments"": { ""Text"": ""\""x\"""" } } ]
}");

        Assert.That(Join(validation.Errors), Does.Contain("не оба"));
    }

    [Test]
    public void Validate_EmptyDocument_ReportsError()
    {
        WorkflowJsonValidation validation = Validate("{}");

        Assert.Multiple(() =>
        {
            Assert.That(validation.Ok, Is.False);
            Assert.That(Join(validation.Errors), Does.Contain("нет ни activities, ни root"));
            Assert.That(Join(validation.Hints), Does.Contain("activities"));
        });
    }

    [Test]
    public void Validate_TooLargeDocument_ReportsError()
    {
        string huge = @"{ ""description"": """ + new string('x', WorkflowJsonSchema.MaxJsonChars + 100) + @""" }";

        WorkflowJsonValidation validation = Validate(huge);

        Assert.That(Join(validation.Errors), Does.Contain("символов"));
    }

    private static WorkflowJsonValidation Validate(string json)
    {
        return new WorkflowJsonValidator().Validate(json);
    }

    private static string Join(IEnumerable<string> items)
    {
        return string.Join(" | ", items);
    }
}
