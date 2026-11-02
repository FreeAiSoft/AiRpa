using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Controls;
using AvalonDock.Layout;
using NUnit.Framework;
using RPA.Helpers;

namespace AiRpaStudio.Tests;

/// <summary>
/// Регрессия на <c>NullReferenceException</c> в
/// <c>RPA.Helpers.DocGroupHelper.DocDocument_IsSelectedChanged</c>: панель документов содержит не только
/// вкладки дизайнера (<c>DocGroupHelper.AddDocument</c>, у них <c>View.Tag</c> = путь к файлу), но и
/// редакторы кода/XAML (<c>MainView.AddCodeDocumentTab</c>, <c>Tag</c> не заполняется). AvalonDock поднимает
/// <c>IsSelectedChanged</c> и при снятии выбора, поэтому обработчик обязан фильтровать «чужие» вкладки и не
/// разыменовывать <c>Tag</c> / снятие выбора без проверки.
/// </summary>
[TestFixture]
[Apartment(ApartmentState.STA)]
public class DocGroupHelperSelectionTests
{
    private const string DesignerFile = @"C:\workflow\Main.xaml";
    private const string CodeContentId = @"code:C:\workflow\Main.xaml";

    [Test]
    public void IsSelectedChanged_CodeDocumentWithoutTag_DoesNotThrowAndKeepsActiveDesigner()
    {
        List<string> activated = new List<string>();
        object helper = CreateHelper(activated, new List<string>());

        // Так выглядит вкладка редактора кода: Content — UserControl без Tag, ContentId — "code:<path>".
        LayoutDocument codeDocument = new LayoutDocument
        {
            Content = new Border(),
            ContentId = CodeContentId,
            IsSelected = true
        };

        Assert.DoesNotThrow(() => RaiseSelectionChanged(helper, codeDocument),
            "вкладка редактора кода не должна ронять обработчик выбора вкладки");
        Assert.That(activated, Is.Empty,
            "переключение на вкладку кода не должно менять активного дизайнера");
    }

    [Test]
    public void IsSelectedChanged_DesignerDocumentWithoutSelection_DoesNotThrow()
    {
        List<string> activated = new List<string>();
        object helper = CreateHelper(activated, new List<string>());

        LayoutDocument designerDocument = new LayoutDocument
        {
            Content = new Border { Tag = DesignerFile },
            IsSelected = false
        };

        Assert.DoesNotThrow(() => RaiseSelectionChanged(helper, designerDocument));
        Assert.That(activated, Is.Empty,
            "снятие выбора (IsSelected = false) не переключает активного дизайнера");
    }

    [Test]
    public void IsSelectedChanged_DesignerDocumentWithEmptyTag_DoesNotThrow()
    {
        List<string> activated = new List<string>();
        object helper = CreateHelper(activated, new List<string>());

        LayoutDocument designerDocument = new LayoutDocument
        {
            Content = new Border { Tag = string.Empty },
            IsSelected = true
        };

        Assert.DoesNotThrow(() => RaiseSelectionChanged(helper, designerDocument));
        Assert.That(activated, Is.Empty, "пустой путь не должен уходить в переключение дизайнера");
    }

    [Test]
    public void IsSelectedChanged_NotALayoutDocument_DoesNotThrow()
    {
        List<string> activated = new List<string>();
        object helper = CreateHelper(activated, new List<string>());

        Assert.DoesNotThrow(() => Invoke(helper, "DocDocument_IsSelectedChanged", new Border()));
        Assert.That(activated, Is.Empty);
    }

    [Test]
    public void Closed_CodeDocumentWithoutTag_DoesNotThrow()
    {
        List<string> activated = new List<string>();
        List<string> removed = new List<string>();
        object helper = CreateHelper(activated, removed);

        LayoutDocument codeDocument = new LayoutDocument
        {
            Content = new Border(),
            ContentId = CodeContentId
        };

        Assert.DoesNotThrow(() => Invoke(helper, "DocClosed", codeDocument));
        Assert.That(removed, Is.Empty, "закрытие вкладки кода не должно снимать дизайнер по пустому пути");
    }

    [Test]
    public void Closed_DesignerDocument_RemovesDesignerByTag()
    {
        List<string> activated = new List<string>();
        List<string> removed = new List<string>();
        object helper = CreateHelper(activated, removed);

        LayoutDocument designerDocument = new LayoutDocument
        {
            Content = new Border { Tag = DesignerFile }
        };

        Invoke(helper, "DocClosed", designerDocument);
        Assert.That(removed, Is.EqualTo(new[] { DesignerFile }));
    }

    [Test]
    public void Handler_GuardsTagAndSelectionInSource()
    {
        string source = ReadSource(Path.Combine("AiRpaStudio", "Helpers", "DocGroupHelper.cs"));

        Assert.That(source, Does.Contain("view.Tag is string loadedFile"),
            "признак вкладки дизайнера — путь в Tag; разыменовывать Tag без проверки нельзя");
        Assert.That(source, Does.Contain("ActiveWorkflowDesigner?.View"),
            "активного дизайнера нельзя разыменовывать без проверки на null");

        Match handler = Regex.Match(source,
            @"private void DocDocument_IsSelectedChanged.*?private void DocClosed",
            RegexOptions.Singleline, TimeSpan.FromSeconds(2));
        Assert.That(handler.Success, Is.True, "не найден обработчик DocDocument_IsSelectedChanged");

        string body = handler.Value;
        Assert.Multiple(() =>
        {
            Assert.That(body, Does.Not.Contain(".ToString()"),
                "в обработчике не должно быть прямого view.Tag.ToString()");
            Assert.That(body, Does.Contain("document.IsSelected"),
                "обработчик должен отбрасывать событие снятия выбора (IsSelected = false)");
            Assert.That(body, Does.Contain("sender is LayoutDocument"),
                "вкладка берётся из sender — искать первый выбранный Child не нужно");
        });
    }

    private static object CreateHelper(List<string> activated, List<string> removed)
    {
        // Конструктор DocGroupHelper требует MainViewModel (подписка на PropertyChanged) и реальный
        // LayoutDocumentPane, поэтому создаём экземпляр без вызова конструктора и заполняем только
        // те поля, которые нужны обработчикам.
        Type helperType = typeof(DocGroupHelper);
        object helper = FormatterServices.GetUninitializedObject(helperType);

        FieldInfo setActiveDesignerField = helperType.GetField("setActiveDesignerDelegate",
            BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo removeDesignerField = helperType.GetField("removeDesignerDelegate",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.Multiple(() =>
        {
            Assert.That(setActiveDesignerField, Is.Not.Null, "изменилось поле setActiveDesignerDelegate");
            Assert.That(removeDesignerField, Is.Not.Null, "изменилось поле removeDesignerDelegate");
        });

        setActiveDesignerField.SetValue(helper, new Action<string>(activated.Add));
        removeDesignerField.SetValue(helper, new Action<string>(removed.Add));
        return helper;
    }

    private static void RaiseSelectionChanged(object helper, LayoutDocument document)
    {
        Invoke(helper, "DocDocument_IsSelectedChanged", document);
    }

    private static void Invoke(object helper, string handlerName, object sender)
    {
        MethodInfo handler = typeof(DocGroupHelper).GetMethod(handlerName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(handler, Is.Not.Null, $"не найден обработчик {handlerName}");

        try
        {
            _ = handler.Invoke(helper, new[] { sender, EventArgs.Empty });
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            // Пробрасываем исходное исключение, чтобы Assert.DoesNotThrow видел настоящий тип.
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
        }
    }

    private static string ReadSource(string relativePath)
    {
        string path = Path.Combine(FindRepositoryRoot(), relativePath);
        Assert.That(File.Exists(path), Is.True, $"не найден файл {relativePath}");
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AiRpa.sln")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null, "не найден корень репозитория (AiRpa.sln)");
        return directory.FullName;
    }
}
