using System;
using System.IO;
using System.Text.RegularExpressions;
using AiRpaStudio.Tests.Design;
using NUnit.Framework;

namespace AiRpaStudio.Tests;

/// <summary>
/// Отслеживание правок дизайнера (баг «добавил аргумент — кнопки сохранения не активны»).
/// <para>
/// Причина: loose-XAML путь публикует <c>IDocumentPersistenceService</c>, и
/// <c>WorkflowDesigner.NotifyModelChanged()</c> вместо <c>ModelChanged</c> вызывает
/// <c>IDocumentPersistenceService.OnModelChanged</c>. Значит подписка только на
/// <c>ModelChanged</c> «не видит» правки. Фикс — дополнительное отслеживание
/// <c>ModelTreeManager.EditingScopeCompleted</c> с проверкой <c>HasEffectiveChanges</c>.
/// </para>
/// </summary>
public class WorkflowDesignerEditTrackingTests
{
    [Test]
    public void WorkflowDesignerEx_TracksEditingScopeCompleted()
    {
        string source = ReadSource(@"AiRpaStudio\Core\WorkflowDesignerEx.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("Context.Services.Subscribe<ModelTreeManager>(OnModelTreeManagerAvailable)"),
                "подписка на ModelTreeManager обязана быть: сервис публикуется в WorkflowDesigner.Load");
            Assert.That(source, Does.Contain("treeManager.EditingScopeCompleted += OnEditingScopeCompleted"),
                "«изменён» отслеживается по EditingScopeCompleted — ModelChanged не поднимается при IDocumentPersistenceService");
            Assert.That(source, Does.Contain("e.EditingScope.HasEffectiveChanges"),
                "пустой scope не должен помечать вкладку изменённой");
            Assert.That(source, Does.Contain("_modelTreeManager.EditingScopeCompleted -= OnEditingScopeCompleted"),
                "при закрытии вкладки подписка снимается вместе с валидатором");
        });
    }

    [Test]
    public void WorkflowDesignerEx_EditingScopeCompleted_SetsEdited()
    {
        string method = MethodBody(ReadSource(@"AiRpaStudio\Core\WorkflowDesignerEx.cs"), "OnEditingScopeCompleted");

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("IsEdited = true;"),
                "реальная правка модели обязана помечать вкладку «изменённой»");
            Assert.That(method, Does.Contain("if (e.EditingScope != null && e.EditingScope.HasEffectiveChanges)"),
                "проверка эффективных изменений — как у самого WorkflowDesigner.OnEditingScopeCompleted");
        });
    }

    [Test]
    public void LooseXaml_ModelEdit_MarksDesignerEdited()
    {
        string rpa1 = FindRpa1();
        string[] xamlFiles = Directory.GetFiles(rpa1, "*.xaml", SearchOption.TopDirectoryOnly);

        DesignerEditTrackingReport report = DesignerBenchProxy.Shared.EditTrackingScenario(
            Path.Combine(rpa1, "Main.xaml"),
            xamlFiles);

        Assert.Multiple(() =>
        {
            Assert.That(report.Error, Is.Null.Or.Empty,
                "сценарий отслеживания правок упал." + Environment.NewLine + report.Description);
            Assert.That(report.LoadSucceeded, Is.True, report.Description);
            Assert.That(report.CleanAfterLoad, Is.True,
                "после загрузки вкладка не должна быть «изменённой»." + Environment.NewLine + report.Description);
            Assert.That(report.EditedAfterModelChange, Is.True,
                "реальная правка модели обязана включить кнопку сохранения." + Environment.NewLine + report.Description);
            Assert.That(report.EditedAfterEmptyScope, Is.False,
                "пустой ModelEditingScope не должен помечать вкладку «изменённой»." + Environment.NewLine + report.Description);
        });
    }

    /// <summary>Тело метода по имени (грубая, но устойчивая к форматированию выборка).</summary>
    private static string MethodBody(string source, string methodName)
    {
        Match match = Regex.Match(
            source,
            @"(override|internal|public|private|protected)[^\r\n]*\b" + Regex.Escape(methodName) + @"\s*\(");

        Assert.That(match.Success, Is.True, $"не найден метод {methodName}");
        return source.Substring(match.Index, Math.Min(2000, source.Length - match.Index));
    }

    private static string ReadSource(string relativePath)
    {
        string path = Path.Combine(FindRepositoryRoot(), relativePath);
        Assert.That(File.Exists(path), Is.True, $"не найден файл {relativePath}");
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AiRpa.sln")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null, "не найден корень репозитория (AiRpa.sln)");
        return directory.FullName;
    }

    private static string FindRpa1()
    {
        string root = FindRepositoryRoot();
        string rpa1 = Path.Combine(root, "RPA-1");
        Assert.That(Directory.Exists(rpa1), Is.True, "нет эталонного проекта RPA-1: " + rpa1);
        return rpa1;
    }
}
