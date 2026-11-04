using System;
using System.Collections.Generic;
using System.IO;
using AiRpaStudio.Tests.Design;
using RPA.Core.Generation;

namespace AiRpaStudio.Tests.Generation;

/// <summary>
/// Этап 6 плана <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c> на настоящем конструкторе:
/// процесс, собранный из JSON-описания (переменные, присваивание, ветвление, цикл), открывается
/// в дизайнере без ошибок валидации и без модальных окон.
/// <para>
/// Это п. 1.3 плана: «валидный XAML» проверяется прогоном через настоящий <c>WorkflowDesigner</c>
/// на стенде этапа 1, а не разбором текста.
/// </para>
/// </summary>
[TestFixture]
public class JsonAstDesignerTests
{
    private const string Document = @"{
  ""variables"": [
    { ""name"": ""counter"", ""type"": ""Int32"", ""default"": ""0"" },
    { ""name"": ""message"", ""type"": ""String"" }
  ],
  ""activities"": [
    { ""type"": ""Assign"", ""displayName"": ""Текст"",
      ""arguments"": { ""To"": ""message"", ""Value"": ""\""значение\"""" } },
    { ""type"": ""Assign"", ""displayName"": ""Счётчик"",
      ""arguments"": { ""To"": ""counter"", ""Value"": """ + "1" + @""" } },
    { ""type"": ""If"", ""displayName"": ""Проверка"",
      ""arguments"": { ""Condition"": ""counter > 0"" },
      ""children"": {
        ""Then"": [ { ""type"": ""WriteLine"", ""displayName"": ""Тогда"",
                      ""arguments"": { ""Text"": ""message"" } } ],
        ""Else"": [ { ""type"": ""WriteLine"", ""displayName"": ""Иначе"",
                      ""arguments"": { ""Text"": ""\""пусто\"""" } } ]
      } },
    { ""type"": ""ForEach"", ""typeArguments"": [ ""String"" ], ""displayName"": ""Обход"",
      ""arguments"": { ""Values"": { ""expression"": ""new string[0]"", ""type"": ""String[]"" } },
      ""children"": {
        ""Body"": [ { ""type"": ""WriteLine"", ""displayName"": ""Вывод в цикле"",
                      ""arguments"": { ""Text"": ""\""элемент\"""" } } ]
      } }
  ]
}";

    private readonly List<string> _temporaryDirectories = new List<string>();

    [TearDown]
    public void CleanupTemporaryDirectories()
    {
        foreach (string directory in _temporaryDirectories)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
            catch (Exception)
            {
                // Каталог мог остаться заблокированным — на результат теста это не влияет.
            }
        }

        _temporaryDirectories.Clear();
    }

    [Test]
    public void JsonWorkflow_RendersInRealDesigner()
    {
        string root = WorkflowTestSamples.CreateTempDirectory();
        _temporaryDirectories.Add(root);

        string directory = Path.Combine(root, "RPA-1");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "Main.xaml");

        JsonWorkflowResult created = new JsonWorkflowService().Create(new JsonWorkflowRequest
        {
            Json = Document,
            FilePath = path
        });

        Assert.That(created.Ok, Is.True,
            "JSON-описание не прошло конвейер генерации." + Environment.NewLine + created.Describe());

        DesignerBenchReport report = DesignerBenchProxy.Shared.Render(path);

        Assert.Multiple(() =>
        {
            Assert.That(report.Ok, Is.True, "сгенерированный из JSON процесс не отрисовался.\n" + report.Description);
            Assert.That(report.RenderedActivities, Is.Not.Empty, "в дереве конструктора есть активности");
            Assert.That(report.UnrecognizedActivities, Is.Empty, "нераспознанных активностей быть не должно");
            Assert.That(report.ValidationErrors, Is.Empty, "валидация WF обязана быть без ошибок");
            Assert.That(report.ModalWindows, Is.Empty, "модальные окна конструктора блокируют студию");
        });
    }
}
