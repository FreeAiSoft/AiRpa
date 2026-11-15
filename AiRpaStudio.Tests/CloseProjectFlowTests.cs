using System;
using System.IO;
using RPA.Core;
using RPA.Enums;
using RPA.Models;

namespace AiRpaStudio.Tests;

/// <summary>
/// Поведенческие тесты закрытия проекта на реальной модели <see cref="Main"/>:
/// очистка вкладок, сохранение сессии в per-project store и захват несохранённых буферов.
/// </summary>
public class CloseProjectFlowTests
{
    private string tempDir;
    private string savedLastProject;
    private string savedSessions;
    private string savedBuffers;

    [SetUp]
    public void SetUp()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "CloseProjectFlow_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        savedLastProject = RPA.Properties.Settings.Default.LastLoadedProjectPath;
        savedSessions = RPA.Properties.Settings.Default.ProjectSessions;
        savedBuffers = RPA.Properties.Settings.Default.ProjectUnsavedBuffers;

        CustomWfDesigner studio = CustomWfDesigner.GetInstance();
        studio.UnsavedDocumentBuffers.Clear();
        studio.WorkflowDesigners.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        RPA.Properties.Settings.Default.LastLoadedProjectPath = savedLastProject;
        RPA.Properties.Settings.Default.ProjectSessions = savedSessions;
        RPA.Properties.Settings.Default.ProjectUnsavedBuffers = savedBuffers;
        RPA.Properties.Settings.Default.Save();

        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public void CloseProjectCore_NoProject_ReturnsTrue()
    {
        RPA.Properties.Settings.Default.LastLoadedProjectPath = string.Empty;
        var main = new RPA.Models.Main();

        Assert.That(main.CloseProjectCore(null), Is.True);
    }

    [Test]
    public void CloseProjectCore_ClearsCodeDocumentsAndProjectPath()
    {
        var main = new RPA.Models.Main();
        string file = Path.Combine(tempDir, "sample.txt");
        File.WriteAllText(file, "disk");

        main.OpenCodeDocuments.Add(CodeDocument.FromText(file, "disk", isDirty: false));
        RPA.Properties.Settings.Default.LastLoadedProjectPath = tempDir;

        bool closed = main.CloseProjectCore(null);

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.True);
            Assert.That(main.OpenCodeDocuments, Is.Empty, "вкладки редактора кода/XAML закрываются");
            Assert.That(RPA.Properties.Settings.Default.LastLoadedProjectPath, Is.Empty,
                "после закрытия проекта последний открытый путь очищается");
        });
    }

    [Test]
    public void SaveSessionState_WritesPerProjectSession()
    {
        var main = new RPA.Models.Main();
        string file = Path.Combine(tempDir, "sample.txt");
        File.WriteAllText(file, "disk");

        main.OpenCodeDocuments.Add(CodeDocument.FromText(file, "disk", isDirty: false));
        RPA.Properties.Settings.Default.LastLoadedProjectPath = tempDir;

        main.SaveSessionState();

        Assert.That(ProjectSessionStore.TryGet(RPA.Properties.Settings.Default.ProjectSessions, tempDir, out string session), Is.True,
            "сессия проекта должна попасть в per-project store");
        Assert.That(session, Does.Contain("sample.txt"));
    }

    [Test]
    public void CloseProjectCore_SaveToBuffer_CapturesDirtyCodeBuffer()
    {
        var main = new RPA.Models.Main();
        string file = Path.Combine(tempDir, "sample.txt");
        File.WriteAllText(file, "disk");

        main.OpenCodeDocuments.Add(CodeDocument.FromText(file, "edited", isDirty: true));
        RPA.Properties.Settings.Default.LastLoadedProjectPath = tempDir;

        bool closed = main.CloseProjectCore(CloseProjectChangesResult.SaveToBuffer);

        System.Collections.Generic.IReadOnlyList<ProjectUnsavedBufferEntry> buffers =
            ProjectUnsavedBuffersStore.Get(RPA.Properties.Settings.Default.ProjectUnsavedBuffers, tempDir);

        Assert.Multiple(() =>
        {
            Assert.That(closed, Is.True);
            Assert.That(buffers.Count, Is.EqualTo(1));
            Assert.That(buffers[0].FilePath, Is.EqualTo(file));
            Assert.That(buffers[0].Text, Is.EqualTo("edited"));
            Assert.That(buffers[0].Kind, Is.EqualTo("code"));
        });
    }
}
