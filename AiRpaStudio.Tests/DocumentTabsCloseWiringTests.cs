using System;
using System.IO;
using NUnit.Framework;

namespace AiRpaStudio.Tests;

/// <summary>
/// Стражи точек сборки правила «соседняя вкладка при закрытии» (баг «при закрытии открывается не та
/// вкладка»). Само правило проверяется в <see cref="DocumentTabPolicyTests"/>, поведение панели — в
/// <see cref="DocumentTabsCloseTests"/>; здесь — что студия спрашивает правило, а не выбирает вкладку
/// сама.
/// </summary>
public class DocumentTabsCloseWiringTests
{
    [Test]
    public void CustomWfDesigner_DoesNotActivateFirstDesignerAfterClose()
    {
        string method = MethodBody(ReadSource(@"AiRpaStudio\Core\CustomWfDesigner.cs"), "RemoveDesignerByLoadedFile");

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Not.Contain("WorkflowDesigners[0]"),
                "workflowDesigners[0] — это и был дефект: активной всегда становилась первая вкладка");
            Assert.That(method, Does.Not.Contain("WorkflowDesigners.Count > 0 ?"),
                "активную вкладку нельзя выбирать «наугад» по количеству вкладок");
            Assert.That(method, Does.Contain("ReferenceEquals(activeBeforeClose, designer)"),
                "активного дизайнера меняет только закрытие активной вкладки: фоновую можно закрыть, не переключая видимую");
            Assert.That(method, Does.Contain("ActiveWorkflowDesignerEx = null"),
                "закрытый дизайнер не может остаться активным: иначе SyncWFControls и сохранение работали бы с закрытым документом");
        });
    }

    [Test]
    public void CustomWfDesigner_TakesActiveDesignerBeforeClosingTab()
    {
        string method = MethodBody(ReadSource(@"AiRpaStudio\Core\CustomWfDesigner.cs"), "RemoveDesignerByLoadedFile");

        int snapshot = method.IndexOf("WorkflowDesignerEx activeBeforeClose = ActiveWorkflowDesignerEx;", StringComparison.Ordinal);
        int close = method.IndexOf("CloseDocumentByPath(loadedFile)", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot, Is.GreaterThanOrEqualTo(0),
                "активный дизайнер читается до закрытия вкладки");
            Assert.That(close, Is.GreaterThan(snapshot),
                "активного дизайнера фиксируют ДО закрытия вкладки в UI: закрытие поднимает IsSelectedChanged");
        });
    }

    [Test]
    public void CustomWfDesigner_HandlesReentrantClose()
    {
        string method = MethodBody(ReadSource(@"AiRpaStudio\Core\CustomWfDesigner.cs"), "RemoveDesignerByLoadedFile");

        Assert.That(method, Does.Contain("!WorkflowDesigners.Contains(designer)"),
            "закрытие UI-вкладки поднимает Closed → CmdRemoveDesigner: повторный вход не должен выбирать вкладку заново");
    }

    [Test]
    public void MainView_ActivatesNeighbourTabWhenClosing()
    {
        string source = ReadSource(@"AiRpaStudio\Views\MainView.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("new DocumentTabsCloseCoordinator(docGroup)"),
                "панель документов обязана следить за закрытием вкладок (и дизайнеров, и редактора кода)");
            Assert.That(source, Does.Contain("ApplyPendingActivation()"),
                "соседняя вкладка активируется после того, как модель убрала документ");
            Assert.That(source, Does.Contain("WorkflowDesigners.CollectionChanged += WorkflowDesigners_CollectionChanged_ActivateNeighbour"),
                "для вкладки дизайнера последнее слово за панелью документов, а не за моделью дизайнеров");
        });
    }

    [Test]
    public void MainView_SubscribesNeighbourActivationAfterDocGroupHelper()
    {
        string source = ReadSource(@"AiRpaStudio\Views\MainView.xaml.cs");

        int docGroupHelper = source.IndexOf(
            "WorkflowDesigners.CollectionChanged += docGroupHelper.WorkflowDesigners_CollectionChanged",
            StringComparison.Ordinal);
        int activation = source.IndexOf(
            "WorkflowDesigners.CollectionChanged += WorkflowDesigners_CollectionChanged_ActivateNeighbour",
            StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(docGroupHelper, Is.GreaterThanOrEqualTo(0));
            Assert.That(activation, Is.GreaterThan(docGroupHelper),
                "обработчик панели документов должен выполняться после обработчика DocGroupHelper, иначе выбор вкладки перезапишется");
        });
    }

    [Test]
    public void MainView_ReportsActiveDocumentFromTabs()
    {
        string source = ReadSource(@"AiRpaStudio\Views\MainView.xaml.cs");

        Assert.That(source, Does.Contain("internal string GetActiveDocumentPath()"),
            "активную вкладку (дизайнер или редактор кода) знает только панель документов");
    }

    [Test]
    public void Main_ActiveDocumentComesFromDocumentPane()
    {
        string method = MethodBody(ReadSource(@"AiRpaStudio\Models\Main.cs"), "ActiveDocumentPath");

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("GetActiveDocumentPath()"),
                "активная вкладка сессии — та, что выбрана в панели документов");
            Assert.That(method, Does.Not.Contain("LastOrDefault()"),
                "«последняя открытая вкладка кода» — не активная вкладка: из-за этого восстанавливалась не та вкладка");
        });
    }

    [Test]
    public void DocGroupHelper_HandlesDesignerMoveForSessionRestore()
    {
        string source = ReadSource(@"AiRpaStudio\Helpers\DocGroupHelper.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("NotifyCollectionChangedAction.Move"),
                "восстановление порядка вкладок (WorkflowDesigners.Move) не должно падать с NotImplementedException");
            Assert.That(source, Does.Contain("ReorderDesignerTabsToModelOrder()"),
                "визуальный порядок вкладок дизайнера обязан следовать за моделью после восстановления сессии");
        });
    }

    /// <summary>Тело метода по имени (грубая, но устойчивая к форматированию выборка).</summary>
    private static string MethodBody(string source, string methodName)
    {
        // Берём последнее вхождение: у метода есть определение и место вызова, тело нужно от определения.
        int signature = source.LastIndexOf(methodName + "(", StringComparison.Ordinal);
        Assert.That(signature, Is.GreaterThanOrEqualTo(0), $"не найден метод {methodName}");

        int start = source.LastIndexOf('\n', signature);
        return source.Substring(start, Math.Min(2600, source.Length - start));
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
