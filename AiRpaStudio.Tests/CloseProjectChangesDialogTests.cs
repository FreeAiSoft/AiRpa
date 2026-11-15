using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using RPA.Enums;
using RPA.Views;

namespace AiRpaStudio.Tests;

/// <summary>
/// Темизированный диалог «Закрыть проект с несохранёнными изменениями»: кнопки возвращают
/// <see cref="CloseProjectChangesResult"/>, тема/локализация/скругление и точки интеграции.
/// </summary>
[Apartment(ApartmentState.STA)]
public class CloseProjectChangesDialogTests
{
    [Test]
    public void View_DefaultResultIsCancel()
    {
        var view = new CloseProjectChangesView();
        try
        {
            Assert.That(view.Result, Is.EqualTo(CloseProjectChangesResult.Cancel));
        }
        finally
        {
            view.Close();
        }
    }

    [Test]
    public void View_SaveButton_SetsSave()
    {
        var view = new CloseProjectChangesView();
        try
        {
            ButtonOf(view, "SaveButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(view.Result, Is.EqualTo(CloseProjectChangesResult.Save));
        }
        finally
        {
            view.Close();
        }
    }

    [Test]
    public void View_BufferButton_SetsSaveToBuffer()
    {
        var view = new CloseProjectChangesView();
        try
        {
            ButtonOf(view, "BufferButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(view.Result, Is.EqualTo(CloseProjectChangesResult.SaveToBuffer));
        }
        finally
        {
            view.Close();
        }
    }

    [Test]
    public void View_CancelButton_SetsCancel()
    {
        var view = new CloseProjectChangesView();
        try
        {
            ButtonOf(view, "CancelButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.That(view.Result, Is.EqualTo(CloseProjectChangesResult.Cancel));
        }
        finally
        {
            view.Close();
        }
    }

    [Test]
    public void View_Xaml_IsThemed()
    {
        string xaml = ReadSource(@"AiRpaStudio\Views\CloseProjectChangesView.xaml");

        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Contain("Background=\"{DynamicResource Studio.Surface}\""));
            Assert.That(xaml, Does.Contain("Foreground=\"{DynamicResource Studio.Foreground}\""));
            Assert.That(xaml, Does.Contain("<controls:StudioTitleBar"));
            Assert.That(xaml, Does.Contain("ShowMinimize=\"False\""));
            Assert.That(xaml, Does.Contain("CornerRadius=\"8\""));
            Assert.That(xaml, Does.Not.Match("#[0-9A-Fa-f]{6,8}"), "зашитые цвета запрещены");
        });
    }

    [Test]
    public void View_EnablesWindowCorner()
    {
        string codeBehind = ReadSource(@"AiRpaStudio\Views\CloseProjectChangesView.xaml.cs");
        Assert.Multiple(() =>
        {
            Assert.That(codeBehind, Does.Contain("OnSourceInitialized"));
            Assert.That(codeBehind, Does.Contain("WindowCorner.Enable(this)"));
        });
    }

    [Test]
    public void Dialog_DoesNotUseMessageBox()
    {
        string helper = ReadSource(@"AiRpaStudio\Helpers\CloseProjectChangesDialog.cs");
        string view = ReadSource(@"AiRpaStudio\Views\CloseProjectChangesView.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(helper, Does.Not.Contain("MessageBox.Show"));
            Assert.That(view, Does.Not.Contain("MessageBox.Show"));
        });
    }

    [Test]
    public void ProjectFile_IncludesDialog()
    {
        string csproj = ReadSource(@"AiRpaStudio\AiRpaStudio.csproj");

        Assert.Multiple(() =>
        {
            Assert.That(csproj, Does.Contain(@"Views\CloseProjectChangesView.xaml"));
            Assert.That(csproj, Does.Contain(@"Views\CloseProjectChangesView.xaml.cs"));
            Assert.That(csproj, Does.Contain(@"Helpers\CloseProjectChangesDialog.cs"));
            Assert.That(csproj, Does.Contain(@"Enums\CloseProjectChangesResult.cs"));
        });
    }

    private static Button ButtonOf(CloseProjectChangesView view, string name)
    {
        var button = view.FindName(name) as Button;
        Assert.That(button, Is.Not.Null, $"в CloseProjectChangesView нет кнопки с именем {name}");
        return button;
    }

    private static string ReadSource(string relativePath)
    {
        string path = Path.Combine(FindRepositoryRoot(), relativePath);
        Assert.That(File.Exists(path), Is.True, "не найден файл " + relativePath);
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
