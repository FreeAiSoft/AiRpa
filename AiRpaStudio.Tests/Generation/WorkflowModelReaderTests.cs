using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RPA.Core.Generation;

namespace AiRpaStudio.Tests.Generation;

/// <summary>
/// Этап 4 конвейера генерации XAML: чтение модели процесса
/// (<c>WorkflowModelReader</c>, инструмент <c>xaml.get_workflow</c>).
/// <para>
/// Идентификаторы активностей — пути в дереве (<c>root/Activities[1]</c>). Именно эти строки агент
/// передаёт в <c>xaml.set_argument</c>, поэтому тесты фиксируют и сам путь, и то, что он совпадает
/// с путём <c>ModelItem</c> (по нему ищет <c>DesignerModelSession</c>).
/// </para>
/// </summary>
[TestFixture]
public class WorkflowModelReaderTests
{
    private readonly System.Collections.Generic.List<string> _temporaryDirectories = new();

    [TearDown]
    public void Cleanup()
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
    public void Read_CanonicalWorkflow_ReturnsActivityTree()
    {
        WorkflowSnapshot snapshot = ReadSample();

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Ok, Is.True, string.Join(Environment.NewLine, snapshot.Errors));
            Assert.That(snapshot.WorkflowId, Is.EqualTo(WorkflowTestSamples.WorkflowNamespace + "." + WorkflowTestSamples.WorkflowClass));
            Assert.That(snapshot.Root, Is.Not.Null);
            Assert.That(snapshot.Root.Id, Is.EqualTo(WorkflowModelReader.RootId));
            Assert.That(snapshot.Root.TypeName, Is.EqualTo("Sequence"));
            Assert.That(snapshot.ActivityCount, Is.EqualTo(3), "Sequence + Assign + WriteLine");
            Assert.That(snapshot.Root.Children.Select(child => child.TypeName),
                Is.EqualTo(new[] { "Assign", "WriteLine" }));
        });
    }

    [Test]
    public void Read_BuildsStablePathIds()
    {
        WorkflowSnapshot snapshot = ReadSample();

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Root.Children[0].Id, Is.EqualTo("root/Activities[0]"));
            Assert.That(snapshot.Root.Children[1].Id, Is.EqualTo("root/Activities[1]"));
            Assert.That(snapshot.Root.Children[0].ParentId, Is.EqualTo(WorkflowModelReader.RootId));
        });
    }

    [Test]
    public void Read_ReturnsArgumentExpressions()
    {
        WorkflowSnapshot snapshot = ReadSample();

        WorkflowActivitySnapshot assign = snapshot.Find("root/Activities[0]");
        WorkflowArgumentSnapshot to = assign.Arguments.Single(argument => argument.Name == "To");
        WorkflowArgumentSnapshot value = assign.Arguments.Single(argument => argument.Name == "Value");

        WorkflowActivitySnapshot writeLine = snapshot.Find("root/Activities[1]");
        WorkflowArgumentSnapshot text = writeLine.Arguments.Single(argument => argument.Name == "Text");

        Assert.Multiple(() =>
        {
            Assert.That(to.Expression, Is.EqualTo("output"));
            Assert.That(to.Direction, Is.EqualTo("Out"));
            Assert.That(value.Expression, Is.EqualTo("\"Hello from RPA-1\""));
            Assert.That(text.Expression, Is.EqualTo("output"));
            Assert.That(text.TypeName, Is.EqualTo("String"));
        });
    }

    [Test]
    public void Find_AcceptsDisplayNameWhenUnique()
    {
        WorkflowSnapshot snapshot = ReadSample();

        WorkflowActivitySnapshot byName = snapshot.Find("Вывод");

        Assert.Multiple(() =>
        {
            Assert.That(byName, Is.Not.Null, "агенту удобно адресовать активность по подписи");
            Assert.That(byName.Id, Is.EqualTo("root/Activities[1]"));
            Assert.That(snapshot.Find("НетТакой"), Is.Null);
        });
    }

    [Test]
    public void Read_MissingFile_ReturnsErrorWithHint()
    {
        WorkflowSnapshot snapshot = WorkflowModelReader.Read(Path.Combine(Path.GetTempPath(), "нет-файла-" + Guid.NewGuid().ToString("N") + ".xaml"));

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Ok, Is.False);
            Assert.That(snapshot.Errors.Single(), Does.Contain("не найден"));
            Assert.That(snapshot.Hints, Is.Not.Empty);
        });
    }

    [Test]
    public void Read_EmptyPath_ReturnsPathError()
    {
        WorkflowSnapshot snapshot = WorkflowModelReader.Read(null);

        Assert.That(snapshot.Errors.Single(), Does.Contain("xaml.path"));
    }

    [Test]
    public void Read_BrokenXaml_ReturnsLoadErrorInsteadOfException()
    {
        string path = WorkflowTestSamples.WriteTempXaml("<Activity><Sequence></Activity>");
        _temporaryDirectories.Add(Path.GetDirectoryName(path));

        WorkflowSnapshot snapshot = WorkflowModelReader.Read(path);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Ok, Is.False);
            Assert.That(snapshot.Errors, Is.Not.Empty);
        });
    }

    private WorkflowSnapshot ReadSample()
    {
        string path = WorkflowTestSamples.WriteTempXaml(WorkflowTestSamples.CanonicalXaml());
        _temporaryDirectories.Add(Path.GetDirectoryName(path));

        WorkflowSnapshot snapshot = WorkflowModelReader.Read(path);
        Assert.That(snapshot.Ok, Is.True, string.Join(Environment.NewLine, snapshot.Errors));

        return snapshot;
    }
}
