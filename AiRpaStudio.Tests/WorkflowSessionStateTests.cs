using System;
using System.Collections.Generic;
using System.IO;
using RPA.Core;

namespace AiRpaStudio.Tests;

/// <summary>
/// Баг «не персистится состояние вкладок»: при закрытии вкладки были, при следующем запуске — нет.
/// <para>
/// Причина лежала в персистентности: обработчик <c>WorkflowDesigners_CollectionChanged</c> подписывался
/// только внутри <c>CustomWfDesigner.LoadProject</c> (классический проект), а основной режим — проект
/// Visual Studio (<c>.sln</c>/<c>.csproj</c>) — не подписывался и не восстанавливал ничего.
/// Восстановление к тому же не знало ни про порядок вкладок, ни про активную вкладку, ни про вкладки
/// редактора кода, и хранило пути без привязки к проекту.
/// </para>
/// <para>
/// Здесь проверяется чистая логика <see cref="WorkflowSessionState"/>: сериализация, чтение старого
/// формата, фильтрация чужих/исчезнувших файлов и выбор активной вкладки.
/// </para>
/// </summary>
public class WorkflowSessionStateTests
{
    private const string ProjectRoot = @"C:\Projects\Demo";

    private static readonly string MainActivity = Path.Combine(ProjectRoot, "Activity1.xaml");
    private static readonly string SecondActivity = Path.Combine(ProjectRoot, "Activity2.xaml");
    private static readonly string CodeFile = Path.Combine(ProjectRoot, "Program.cs");
    private static readonly string ForeignActivity = @"C:\Projects\Other\Activity9.xaml";

    private static Func<string, bool> Existing(params string[] files)
    {
        HashSet<string> set = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
        return path => set.Contains(path);
    }

    // --- Сохранение и восстановление полного состояния. ---

    [Test]
    public void Serialize_Deserialize_RoundTrip_KeepsOrderAndActiveTab()
    {
        WorkflowSessionState state = WorkflowSessionState.Create(
            ProjectRoot,
            new[] { MainActivity, SecondActivity },
            new[] { CodeFile },
            SecondActivity);

        string json = state.Serialize();
        WorkflowSessionState restored = WorkflowSessionState.Deserialize(
            json, ProjectRoot, Existing(MainActivity, SecondActivity, CodeFile));

        Assert.Multiple(() =>
        {
            Assert.That(restored.Documents.Select(d => d.FilePath),
                Is.EqualTo(new[] { MainActivity, SecondActivity, CodeFile }),
                "порядок вкладок должен сохраняться: иначе вкладки «переезжают» после перезапуска");
            Assert.That(restored.Documents.Select(d => d.Kind),
                Is.EqualTo(new[]
                {
                    WorkflowSessionDocument.WorkflowKind,
                    WorkflowSessionDocument.WorkflowKind,
                    WorkflowSessionDocument.CodeKind
                }),
                "тип вкладки нужен, чтобы открыть её дизайнером или редактором кода");
            Assert.That(restored.ActiveDocument, Is.EqualTo(SecondActivity),
                "активная вкладка должна восстанавливаться, а не всегда первая");
        });
    }

    [Test]
    public void Create_InfersKindFromExtension()
    {
        WorkflowSessionState state = WorkflowSessionState.Create(
            ProjectRoot, null, new[] { CodeFile, Path.Combine(ProjectRoot, "robot.json") }, null);

        Assert.Multiple(() =>
        {
            Assert.That(state.Documents[0].Kind, Is.EqualTo(WorkflowSessionDocument.CodeKind));
            Assert.That(state.Documents[1].Kind, Is.EqualTo(WorkflowSessionDocument.CodeKind));
        });
    }

    // --- Совместимость со старым форматом (JSON-массив путей). ---

    [Test]
    public void Deserialize_LegacyPathArray_IsRead()
    {
        string legacy = "[\"" + MainActivity.Replace("\\", "\\\\") + "\",\"" + CodeFile.Replace("\\", "\\\\") + "\"]";

        WorkflowSessionState restored = WorkflowSessionState.Deserialize(
            legacy, ProjectRoot, Existing(MainActivity, CodeFile));

        Assert.Multiple(() =>
        {
            Assert.That(restored.Documents.Select(d => d.FilePath),
                Is.EqualTo(new[] { MainActivity, CodeFile }),
                "старая настройка DesignerFiles — просто массив путей, её нельзя терять");
            Assert.That(restored.Documents[0].Kind, Is.EqualTo(WorkflowSessionDocument.WorkflowKind));
            Assert.That(restored.Documents[1].Kind, Is.EqualTo(WorkflowSessionDocument.CodeKind));
            Assert.That(restored.ActiveDocument, Is.EqualTo(MainActivity),
                "в старом формате активной вкладки нет — берём первую");
        });
    }

    // --- Фильтрация: чужие проекту и исчезнувшие файлы. ---

    [Test]
    public void Deserialize_DropsMissingFiles()
    {
        WorkflowSessionState state = WorkflowSessionState.Create(
            ProjectRoot, new[] { MainActivity, SecondActivity }, null, SecondActivity);

        WorkflowSessionState restored = WorkflowSessionState.Deserialize(
            state.Serialize(), ProjectRoot, Existing(MainActivity));

        Assert.Multiple(() =>
        {
            Assert.That(restored.Documents.Select(d => d.FilePath), Is.EqualTo(new[] { MainActivity }));
            Assert.That(restored.ActiveDocument, Is.EqualTo(MainActivity),
                "активная вкладка исчезла — берём первую из оставшихся");
        });
    }

    [Test]
    public void Deserialize_DropsFilesOutsideProject()
    {
        // Проект сменили: старые вкладки другого проекта не подтягиваются.
        WorkflowSessionState state = WorkflowSessionState.Create(
            ProjectRoot, new[] { MainActivity, ForeignActivity }, null, ForeignActivity);

        WorkflowSessionState restored = WorkflowSessionState.Deserialize(
            state.Serialize(), ProjectRoot, Existing(MainActivity, ForeignActivity));

        Assert.That(restored.Documents.Select(d => d.FilePath), Is.EqualTo(new[] { MainActivity }));
    }

    [Test]
    public void IsInProject_SolutionFile_UsesItsFolder()
    {
        string solution = Path.Combine(ProjectRoot, "Demo.sln");

        Assert.Multiple(() =>
        {
            Assert.That(WorkflowSessionState.IsInProject(solution, MainActivity), Is.True,
                "для .sln корень проекта — папка решения");
            Assert.That(WorkflowSessionState.IsInProject(solution, ForeignActivity), Is.False);
            Assert.That(WorkflowSessionState.IsInProject(null, MainActivity), Is.False);
        });
    }

    [Test]
    public void Deserialize_DuplicatesAndEmptyPaths_AreDropped()
    {
        string json = "{\"version\":1,\"project\":\"" + ProjectRoot.Replace("\\", "\\\\") + "\","
                      + "\"active\":null,\"documents\":["
                      + "{\"path\":\"" + MainActivity.Replace("\\", "\\\\") + "\",\"kind\":\"workflow\"},"
                      + "{\"path\":\"\",\"kind\":\"workflow\"},"
                      + "{\"path\":\"" + MainActivity.ToUpperInvariant().Replace("\\", "\\\\") + "\",\"kind\":\"code\"}]}";

        WorkflowSessionState restored = WorkflowSessionState.Deserialize(json, ProjectRoot, Existing(MainActivity));

        Assert.Multiple(() =>
        {
            Assert.That(restored.Documents, Has.Count.EqualTo(1), "один файл — одна вкладка");
            Assert.That(restored.Documents[0].Kind, Is.EqualTo(WorkflowSessionDocument.WorkflowKind),
                "побеждает первое вхождение: вкладка дизайнера не должна превращаться во вкладку кода");
        });
    }

    // --- Устойчивость к мусору в настройках. ---

    [Test]
    public void Deserialize_BrokenJson_ReturnsEmptyStateWithoutThrowing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WorkflowSessionState.Deserialize("{\"documents\": [", ProjectRoot, Existing()).Documents, Is.Empty);
            Assert.That(WorkflowSessionState.Deserialize(null, ProjectRoot, Existing()).Documents, Is.Empty);
            Assert.That(WorkflowSessionState.Deserialize("   ", ProjectRoot, Existing()).Documents, Is.Empty);
            Assert.That(WorkflowSessionState.Deserialize("42", ProjectRoot, Existing()).Documents, Is.Empty);
        });
    }

    [Test]
    public void Serialize_EmptySession_WritesEmptyString()
    {
        // Пустая сессия — пустая настройка: при старте ничего не восстанавливаем и не трогаем старый формат.
        Assert.That(WorkflowSessionState.Empty(ProjectRoot).Serialize(), Is.Empty);
    }

    [Test]
    public void Normalize_NoDocuments_ClearsActiveTab()
    {
        WorkflowSessionState state = WorkflowSessionState.Empty(ProjectRoot);
        state.ActiveDocument = MainActivity;

        state.Normalize(Existing());

        Assert.That(state.ActiveDocument, Is.Null, "активной вкладки без документов быть не может");
    }

    [Test]
    public void Deserialize_ActiveTabFromOtherList_FallsBackToFirst()
    {
        WorkflowSessionState state = WorkflowSessionState.Create(
            ProjectRoot, new[] { MainActivity, SecondActivity }, null, ForeignActivity);

        WorkflowSessionState restored = WorkflowSessionState.Deserialize(
            state.Serialize(), ProjectRoot, Existing(MainActivity, SecondActivity));

        Assert.That(restored.ActiveDocument, Is.EqualTo(MainActivity));
    }
    [Test]
    public void Serialize_Deserialize_WithRealFiles_OnDisk()
    {
        // Сквозная проверка формата на настоящих файлах: без подмены File.Exists.
        string root = Path.Combine(Path.GetTempPath(), "RpaSession_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            string first = Path.Combine(root, "Activity1.xaml");
            string second = Path.Combine(root, "Activity2.xaml");
            string code = Path.Combine(root, "Program.cs");
            File.WriteAllText(first, "<Activity />");
            File.WriteAllText(second, "<Activity />");
            File.WriteAllText(code, "class Program { }");

            string json = WorkflowSessionState
                .Create(root, new[] { first, second }, new[] { code }, second)
                .Serialize();

            // Вторую вкладку закрыли, а файл удалили: в восстановленной сессии её быть не должно.
            File.Delete(second);

            WorkflowSessionState restored = WorkflowSessionState.Deserialize(json, root);

            Assert.Multiple(() =>
            {
                Assert.That(restored.Documents.Select(d => d.FilePath), Is.EqualTo(new[] { first, code }));
                Assert.That(restored.Documents[0].Kind, Is.EqualTo(WorkflowSessionDocument.WorkflowKind));
                Assert.That(restored.Documents[1].Kind, Is.EqualTo(WorkflowSessionDocument.CodeKind));
                Assert.That(restored.ActiveDocument, Is.EqualTo(first),
                    "удалённой активной вкладки нет — активной становится первая");
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

}
