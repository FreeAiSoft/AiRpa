using System;
using System.IO;
using NUnit.Framework;

namespace AiRpaStudio.Tests;

/// <summary>
/// Стражи по исходникам для инварианта «выбрана ровно одна вкладка» в панели документов.
/// <para>
/// Весь баг «после перезапуска активна не та вкладка и две вкладки подсвечены» держался на двух
/// строках вендорного AvalonDock, которые отключали штатную логику выбора для
/// <c>LayoutDocumentPane</c>. Они не должны вернуться: без них панель документов ведёт себя как
/// панель анкоарабов (и как в Visual Studio), а <c>SelectedContent</c> снова становится валидным.
/// </para>
/// </summary>
[TestFixture]
public class DocumentPaneSelectionWiringTests
{
    [Test]
    public void LayoutDocumentPane_KeepsAutoFixSelectedContentEnabled()
    {
        string source = ReadSource(@"AvalonDock\Layout\LayoutDocumentPane.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Not.Contain("_autoFixSelectedContent = false"),
                "отключение авто-выбора оставляет панель без выбранной вкладки (SelectedContent == null)");
            Assert.That(source, Does.Contain("_autoFixSelectedContent = true"),
                "у панели документов авто-выбор выбранной вкладки включён, как у LayoutAnchorablePane");
        });
    }

    [Test]
    public void LayoutContent_IsSelected_DoesNotResetPaneSelection()
    {
        string method = MethodBody(ReadSource(@"AvalonDock\Layout\LayoutContent.cs"), "public bool IsSelected");

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Not.Contain("Parent is LayoutDocumentPane"),
                "у панели документов не должно быть отдельной ветки выбора: она и накапливала IsSelected");
            Assert.That(method, Does.Not.Contain("SelectedContentIndex = -1;"),
                "выбор вкладки не должен обнулять индекс панели: SelectedContent остаётся null");
            Assert.That(method, Does.Contain("parentSelector.SelectedContentIndex = _isSelected ? parentSelector.IndexOf(this) : -1;"),
                "выбор вкладки переносит индекс панели на себя (штатное поведение AvalonDock)");
        });
    }

    [Test]
    public void AddDocument_StillSelectsOnlyTheNewTab()
    {
        string method = MethodBody(ReadSource(@"AiRpaStudio\Helpers\DocGroupHelper.cs"), "internal void AddDocument(WorkflowDesigner");

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("docDocument.IsSelected = true"),
                "новая вкладка дизайнера показывается сразу");
            Assert.That(method, Does.Not.Contain("IsActive = true"),
                "активным документ делает выбор в панели (LayoutDocumentPaneControl.OnSelectionChanged), а не сам себя");
        });
    }

    [Test]
    public void GetActiveDocumentPath_ReadsPanelSelection()
    {
        string method = MethodBody(ReadSource(@"AiRpaStudio\Views\MainView.xaml.cs"), "internal string GetActiveDocumentPath()");

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("DocumentTabsCloseCoordinator.FindActive"),
                "активную вкладку знает только панель документов");
            Assert.That(method, Does.Contain("DocumentTabsCloseCoordinator.PathOf"),
                "путь активной вкладки — PathOf (дизайнер или редактор кода)");
        });
    }

    /// <summary>
    /// Тело метода по уникальному фрагменту сигнатуры: от его открывающей скобки до парной закрывающей.
    /// Так проверка не захватывает соседние методы (вызовы того же метода идут ниже по файлу).
    /// </summary>
    private static string MethodBody(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"не найден фрагмент «{signature}»");

        int open = source.IndexOf('{', start);
        Assert.That(open, Is.GreaterThanOrEqualTo(0), $"не найдено тело «{signature}»");

        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(open, i - open + 1);
                }
            }
        }

        Assert.Fail($"не закрыто тело «{signature}»");
        return null;
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
