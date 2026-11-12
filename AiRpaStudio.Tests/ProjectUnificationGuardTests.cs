using System.IO;

namespace AiRpaStudio.Tests;

/// <summary>
/// Стражи унификации проекта: нативный (project.json) и VS (.csproj/.sln) пути обязаны сходиться
/// в один доменный объект <see cref="RPA.Entities.Project"/> и один pipeline загрузки/дизайнера.
/// Проверяются по исходникам, потому что сами загрузки дизайнера требуют STA/WPF.
/// </summary>
public class ProjectUnificationGuardTests
{
    [Test]
    public void ProjectManager_UsesSingleProjectLoaderPath()
    {
        string source = ReadSource(@"AiRpaStudio\Models\ProjectManager.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("ProjectLoader.Load(path)"),
                "ProjectManager обязан строить дерево через единый ProjectLoader");
            Assert.That(source, Does.Contain("internal ProjectTreeBuildResult BuildTree(string path)"),
                "должен быть единый BuildTree");
            Assert.That(source, Does.Contain("return BuildTree(dirPath);"),
                "BuildFileTree должен делегировать единому BuildTree");
            Assert.That(source, Does.Contain("return BuildTree(path);"),
                "BuildVsProjectTree должен делегировать единому BuildTree");
        });
    }

    [Test]
    public void VsProjectLoader_AttachesUnifiedProject()
    {
        string source = ReadSource(@"AiRpaStudio\VsProject\VsProjectLoader.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("Project = project"),
                "узел VS-проекта обязан нести единый доменный объект Project");
        });
    }

    [Test]
    public void CustomWfDesigner_LoadsUnifiedProjectInBothModes()
    {
        string designer = ReadSource(@"AiRpaStudio\Core\CustomWfDesigner.cs");
        string main = ReadSource(@"AiRpaStudio\Models\Main.cs");

        Assert.Multiple(() =>
        {
            Assert.That(designer, Does.Contain("internal void LoadProject(Project project)"),
                "дизайнер обязан уметь принимать единый Project");
            Assert.That(main, Does.Contain("CustomWfDesigner.LoadProject(tree.Root?.Project)"),
                "Main обязан передавать единый Project в дизайнер (оба режима)");
        });
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
