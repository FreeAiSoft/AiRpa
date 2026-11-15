using System;
using System.IO;
using RPA.Core;
using RPA.Models;

namespace AiRpaStudio.Tests;

/// <summary>
/// Поведенческие тесты полного цикла несохранённых буферов закрытых документов на реальной
/// модели <see cref="Main"/>: закрытие изменённой вкладки кладёт буфер в коллекцию, повторное
/// открытие переносит его во вкладку, «Сохранить всё» пишет буфер в файл.
/// </summary>
public class UnsavedDocumentBufferFlowTests
{
    private string tempDir;
    private string tempFile;

    [SetUp]
    public void SetUp()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "UnsavedBufferFlowTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        tempFile = Path.Combine(tempDir, "sample.txt");

        // Синглтон дизайнера живёт между тестами: сбрасываем общее состояние, чтобы тесты
        // не влияли друг на друга через несохранённые буферы и открытые вкладки дизайнера.
        CustomWfDesigner studio = CustomWfDesigner.GetInstance();
        studio.UnsavedDocumentBuffers.Clear();
        studio.WorkflowDesigners.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public void CloseCodeDocument_StashesDirtyBuffer()
    {
        File.WriteAllText(tempFile, "disk");
        var main = new RPA.Models.Main();
        var document = CodeDocument.FromText(tempFile, "edited", isDirty: true);
        main.OpenCodeDocuments.Add(document);

        main.CmdCloseCodeDocument(tempFile);

        Assert.Multiple(() =>
        {
            Assert.That(main.OpenCodeDocuments, Is.Empty, "закрытая вкладка убирается из открытых документов");
            Assert.That(main.CustomWfDesigner.UnsavedDocumentBuffers.Contains(tempFile), Is.True,
                "изменённый буфер попадает в коллекцию несохранённых");
            Assert.That(main.CustomWfDesigner.UnsavedDocumentBuffers.TryGet(tempFile, out UnsavedDocumentBuffer buffer), Is.True);
            Assert.That(buffer.Text, Is.EqualTo("edited"), "в коллекции лежит текст на момент закрытия");
        });
    }

    [Test]
    public void CloseCodeDocument_CleanDocument_DoesNotStash()
    {
        File.WriteAllText(tempFile, "disk");
        var main = new RPA.Models.Main();
        var document = CodeDocument.FromText(tempFile, "disk", isDirty: false);
        main.OpenCodeDocuments.Add(document);

        main.CmdCloseCodeDocument(tempFile);

        Assert.That(main.CustomWfDesigner.UnsavedDocumentBuffers.Count, Is.Zero,
            "чистый документ не должен попадать в коллекцию");
    }

    [Test]
    public void CmdDiscardCodeDocument_DropsDocumentAndStash()
    {
        File.WriteAllText(tempFile, "disk");
        var main = new RPA.Models.Main();
        var document = CodeDocument.FromText(tempFile, "edited", isDirty: true);
        main.OpenCodeDocuments.Add(document);
        main.CustomWfDesigner.UnsavedDocumentBuffers.Set(tempFile, "stashed", UnsavedDocumentBufferKind.Code);

        main.CmdDiscardCodeDocument(tempFile);

        Assert.Multiple(() =>
        {
            Assert.That(main.OpenCodeDocuments, Is.Empty);
            Assert.That(main.CustomWfDesigner.UnsavedDocumentBuffers.Count, Is.Zero,
                "discard (удаление файла) отбрасывает и открытый документ, и буфер");
        });
    }

    [Test]
    public void CmdOpenInCodeEditor_RestoresStashedBufferAsDirty()
    {
        File.WriteAllText(tempFile, "disk");
        var main = new RPA.Models.Main();
        main.CustomWfDesigner.UnsavedDocumentBuffers.Set(tempFile, "buffer", UnsavedDocumentBufferKind.Code);

        main.CmdOpenInCodeEditor(tempFile);

        Assert.Multiple(() =>
        {
            CodeDocument document = main.OpenCodeDocuments.Count == 1 ? main.OpenCodeDocuments[0] : null;
            Assert.That(document, Is.Not.Null, "повторное открытие создаёт вкладку редактора кода");
            Assert.That(document.Text, Is.EqualTo("buffer"), "текст берётся из коллекции, а не с диска");
            Assert.That(document.IsDirty, Is.True, "признак «изменён» переносится в открытый документ");
            Assert.That(main.CustomWfDesigner.UnsavedDocumentBuffers.Count, Is.Zero,
                "после переноса буфер удаляется из коллекции");
        });
    }

    [Test]
    public void CmdOpenInCodeEditor_WithoutStash_LoadsFromDiskClean()
    {
        File.WriteAllText(tempFile, "disk");
        var main = new RPA.Models.Main();

        main.CmdOpenInCodeEditor(tempFile);

        Assert.Multiple(() =>
        {
            Assert.That(main.OpenCodeDocuments.Count, Is.EqualTo(1));
            Assert.That(main.OpenCodeDocuments[0].Text, Is.EqualTo("disk"));
            Assert.That(main.OpenCodeDocuments[0].IsDirty, Is.False);
        });
    }

    [Test]
    public void SaveAllChanges_WritesStashedBufferToDisk()
    {
        File.WriteAllText(tempFile, "disk");
        var main = new RPA.Models.Main();
        main.CustomWfDesigner.UnsavedDocumentBuffers.Set(tempFile, "buffer", UnsavedDocumentBufferKind.Code);

        main.SaveAllChanges();

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(tempFile), Is.EqualTo("buffer"),
                "«Сохранить всё» пишет буфер закрытого документа в файл");
            Assert.That(main.CustomWfDesigner.UnsavedDocumentBuffers.Count, Is.Zero,
                "сохранённый буфер удаляется из коллекции");
        });
    }

    [Test]
    public void Close_Reopen_Save_WritesBufferToDisk()
    {
        File.WriteAllText(tempFile, "disk");
        var main = new RPA.Models.Main();

        var document = CodeDocument.FromText(tempFile, "edited", isDirty: true);
        main.OpenCodeDocuments.Add(document);
        main.CmdCloseCodeDocument(tempFile);

        Assert.That(main.CustomWfDesigner.UnsavedDocumentBuffers.Contains(tempFile), Is.True,
            "закрытие изменённого документа оставляет буфер в коллекции");

        main.CmdOpenInCodeEditor(tempFile);

        Assert.Multiple(() =>
        {
            Assert.That(main.OpenCodeDocuments.Count, Is.EqualTo(1));
            Assert.That(main.OpenCodeDocuments[0].Text, Is.EqualTo("edited"));
            Assert.That(main.OpenCodeDocuments[0].IsDirty, Is.True);
            Assert.That(main.CustomWfDesigner.UnsavedDocumentBuffers.Count, Is.Zero);
        });

        main.SaveAllChanges();

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(tempFile), Is.EqualTo("edited"),
                "после «Сохранить всё» буфер записан в файл");
            Assert.That(main.CustomWfDesigner.UnsavedDocumentBuffers.Count, Is.Zero);
        });
    }

    [Test]
    public void HasUnsavedChanges_TrueWhenStashedBufferPresent()
    {
        var main = new RPA.Models.Main();
        Assert.That(main.HasUnsavedChanges, Is.False, "без изменений проект чистый");

        main.CustomWfDesigner.UnsavedDocumentBuffers.Set(tempFile, "buffer", UnsavedDocumentBufferKind.Code);

        Assert.That(main.HasUnsavedChanges, Is.True,
            "несохранённый буфер закрытого документа делает проект «изменённым»");
    }

    [Test]
    public void CanSaveAll_TrueWhenStashedBufferPresent()
    {
        var main = new RPA.Models.Main();
        Assert.That(main.CanSaveAll, Is.False, "без изменений «Сохранить всё» неактивно");

        main.CustomWfDesigner.UnsavedDocumentBuffers.Set(tempFile, "buffer", UnsavedDocumentBufferKind.Code);

        Assert.That(main.CanSaveAll, Is.True,
            "наличие несохранённого буфера активирует «Сохранить всё» на остановленном workflow");
    }
}
