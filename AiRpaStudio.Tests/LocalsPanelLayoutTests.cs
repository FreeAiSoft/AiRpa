using System;
using System.IO;
using NUnit.Framework;

namespace AiRpaStudio.Tests;

/// <summary>
/// Стражи интеграции панели «Локальные переменные» в docking-раскладку (этап 3):
/// контрол создаётся в <c>InitializeControls</c>, восстанавливается по ContentId "Locals",
/// попадает в правую группу дефолтной раскладки, есть пункт меню и ключ локализации.
/// </summary>
public class LocalsPanelLayoutTests
{
    [Test]
    public void MainView_CreatesLocalsControlAndBorder()
    {
        string code = ReadSource(@"AiRpaStudio\Views\MainView.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("new LocalsControl"), "панель Locals не создаётся");
            Assert.That(code, Does.Contain("localsBorder"), "контрол Locals не оборачивается в Border");
            Assert.That(code, Does.Contain("localsModel = new LocalsModel()"), "модель Locals не создаётся");
            Assert.That(code, Does.Contain("DataContext = localsModel"), "модель не подключается к контролу");
        });
    }

    [Test]
    public void MainView_RestoresLocalsByContentId()
    {
        string code = ReadSource(@"AiRpaStudio\Views\MainView.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("contentId == \"Locals\""), "нет ветки восстановления панели Locals");
            Assert.That(code, Does.Contain("args.Content = localsBorder;"), "ветка Locals не восстанавливает контент");
            Assert.That(code, Does.Contain("FindResource(\"lang_main_locals\")"), "Title панели не локализован");
        });
    }

    [Test]
    public void MainView_AddsLocalsAnchorableToRightGroup()
    {
        string code = ReadSource(@"AiRpaStudio\Views\MainView.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("ContentId = \"Locals\""), "нет anchorable Locals в дефолтной раскладке");
            Assert.That(code, Does.Contain("rightLayoutAnchorablePaneGroup.Children.Add(localsPane);"),
                "панель Locals не добавлена в правую боковую группу");
            Assert.That(code, Does.Contain("Content = localsBorder"), "anchorable Locals не содержит контрол");
        });
    }

    [Test]
    public void MainView_ToolsMenu_HasLocalsItem()
    {
        string xaml = ReadSource(@"AiRpaStudio\Views\MainView.xaml");

        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Contain("Header=\"{DynamicResource lang_main_locals}\""),
                "пункт меню «Локальные переменные» не локализован");
            Assert.That(xaml, Does.Contain("Click=\"LocalsPanelMenuItem_Click\""),
                "клик по пункту меню не обрабатывается");
        });
    }

    [Test]
    public void MainView_ShowLocalsPanel_ActivatesByContentId()
    {
        string code = ReadSource(@"AiRpaStudio\Views\MainView.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("private void ShowLocalsPanel()"), "нет метода открытия панели Locals");
            Assert.That(code, Does.Contain("string.Equals(a.ContentId, \"Locals\", StringComparison.Ordinal)"), "поиск панели не идёт по ContentId");
            Assert.That(code, Does.Contain("anchorable.Show()"), "найденная панель не показывается");
        });
    }

    [Test]
    public void MainView_ShowLocalsPanel_RecreatesAnchorableWhenMissing()
    {
        string code = ReadSource(@"AiRpaStudio\Views\MainView.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("anchorable = new LayoutAnchorable"), "нет ветки пересоздания отсутствующей панели Locals");
            Assert.That(code, Does.Contain("AddToLayout(dockManager, AnchorableShowStrategy.Right)"), "пересозданная панель не докается вправо");
        });
    }


    [Test]
    public void Resources_ContainLocalsKey()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ReadSource(@"AiRpaStudio\Resources\Main.resx"), Does.Contain("lang_main_locals"));
            Assert.That(ReadSource(@"AiRpaStudio\Resources\Main.ru-RU.resx"), Does.Contain("lang_main_locals"));
        });
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
