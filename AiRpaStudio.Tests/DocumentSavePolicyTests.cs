using RPA.Core;

namespace AiRpaStudio.Tests;

/// <summary>
/// Кнопки «Сохранить»/«Сохранить всё» учитывают оба вида вкладок: дизайнеры (IsEdited) и редакторы
/// кода/XAML (IsDirty). Правило — чистые функции <see cref="DocumentSavePolicy"/>.
/// </summary>
public class DocumentSavePolicyTests
{
    [Test]
    public void CanSave_Stopped_EditedDesigner_True()
    {
        Assert.That(DocumentSavePolicy.CanSave(stopped: true, hasActiveDocument: true, activeDesignerEdited: true, activeCodeDirty: false), Is.True);
    }

    [Test]
    public void CanSave_Stopped_DirtyCode_True()
    {
        Assert.That(DocumentSavePolicy.CanSave(stopped: true, hasActiveDocument: true, activeDesignerEdited: false, activeCodeDirty: true), Is.True);
    }

    [Test]
    public void CanSave_Running_False()
    {
        Assert.That(DocumentSavePolicy.CanSave(stopped: false, hasActiveDocument: true, activeDesignerEdited: true, activeCodeDirty: true), Is.False);
    }

    [Test]
    public void CanSave_NoActiveDocument_False()
    {
        Assert.That(DocumentSavePolicy.CanSave(stopped: true, hasActiveDocument: false, activeDesignerEdited: true, activeCodeDirty: true), Is.False);
    }

    [Test]
    public void CanSave_CleanActiveDocument_False()
    {
        Assert.That(DocumentSavePolicy.CanSave(stopped: true, hasActiveDocument: true, activeDesignerEdited: false, activeCodeDirty: false), Is.False);
    }

    [Test]
    public void CanSaveAll_AnyEdited_True()
    {
        Assert.That(DocumentSavePolicy.CanSaveAll(stopped: true, anyDesignerEdited: true, anyCodeDirty: false), Is.True);
        Assert.That(DocumentSavePolicy.CanSaveAll(stopped: true, anyDesignerEdited: false, anyCodeDirty: true), Is.True);
    }

    [Test]
    public void CanSaveAll_Running_False()
    {
        Assert.That(DocumentSavePolicy.CanSaveAll(stopped: false, anyDesignerEdited: true, anyCodeDirty: true), Is.False);
    }

    [Test]
    public void CanSaveAll_NothingEdited_False()
    {
        Assert.That(DocumentSavePolicy.CanSaveAll(stopped: true, anyDesignerEdited: false, anyCodeDirty: false), Is.False);
    }

    [Test]
    public void CanSaveAll_StashedBuffer_True()
    {
        Assert.That(DocumentSavePolicy.CanSaveAll(stopped: true, anyDesignerEdited: false, anyCodeDirty: false, anyStashedBuffer: true), Is.True);
    }

    [Test]
    public void CanSaveAll_StashedBuffer_Running_False()
    {
        Assert.That(DocumentSavePolicy.CanSaveAll(stopped: false, anyDesignerEdited: false, anyCodeDirty: false, anyStashedBuffer: true), Is.False);
    }

    [Test]
    public void CanSaveAll_StashedBuffer_NothingEdited_False()
    {
        Assert.That(DocumentSavePolicy.CanSaveAll(stopped: true, anyDesignerEdited: false, anyCodeDirty: false, anyStashedBuffer: false), Is.False);
    }

}
