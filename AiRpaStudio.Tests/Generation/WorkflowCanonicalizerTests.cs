using System;
using System.Activities;
using System.Activities.Expressions;
using System.Activities.Presentation.Expressions;
using System.Activities.Presentation.ViewState;
using System.Collections.Generic;
using System.Linq;
using RPA.Core.Generation;

namespace AiRpaStudio.Tests.Generation;

/// <summary>
/// Этап 2 конвейера генерации XAML: канонизация объектной модели
/// в формат студии (п. 1.1) — <c>x:Class</c>, <c>sap2010:ExpressionActivityEditor="C#"</c>,
/// <c>TextExpression.NamespacesForImplementation</c>/<c>ReferencesForImplementation</c> со ссылкой на сборку
/// проекта, <c>sap2010:WorkflowViewState.IdRef</c> на корне и на каждой активности.
/// <para>
/// Проверки идут по объектной модели (вложенные свойства, которые потом пишет сериализатор WF),
/// а не по тексту XAML: так тест не зависит от форматирования и от версии сериализатора.
/// </para>
/// </summary>
[TestFixture]
public class WorkflowCanonicalizerTests
{
    // --- Имя класса и редактор выражений. ---

    [Test]
    public void Canonicalize_SetsQualifiedClassName()
    {
        ActivityBuilder builder = WorkflowCanonicalizer.Canonicalize(
            WorkflowTestSamples.SimpleWorkflow(), WorkflowTestSamples.Options());

        Assert.That(builder.Name, Is.EqualTo("RPA_1.Main"),
            "x:Class собирается из пространства имён проекта и имени файла (как RPA-1\\Main.xaml → RPA_1.Main)");
    }

    [Test]
    public void Canonicalize_SetsCSharpExpressionEditor()
    {
        ActivityBuilder builder = WorkflowCanonicalizer.Canonicalize(
            WorkflowTestSamples.SimpleWorkflow(), WorkflowTestSamples.Options());

        Assert.That(ExpressionActivityEditor.GetExpressionActivityEditor(builder), Is.EqualTo("C#"),
            "без sap2010:ExpressionActivityEditor=\"C#\" выражения в конструкторе не редактируются");
    }

    // --- Пространства имён и ссылки. ---

    [Test]
    public void Canonicalize_AddsDefaultNamespacesForExpressions()
    {
        ActivityBuilder builder = WorkflowCanonicalizer.Canonicalize(
            WorkflowTestSamples.SimpleWorkflow(), WorkflowTestSamples.Options());

        IList<string> namespaces = TextExpression.GetNamespacesForImplementation(builder);

        Assert.Multiple(() =>
        {
            Assert.That(namespaces, Is.Not.Null.And.Not.Empty,
                "без TextExpression.NamespacesForImplementation выражения не компилируются");
            Assert.That(namespaces, Does.Contain("System"));
            Assert.That(namespaces, Does.Contain("System.Linq"));
        });
    }

    [Test]
    public void Canonicalize_AddsProjectAssemblyReferenceLast()
    {
        ActivityBuilder builder = WorkflowCanonicalizer.Canonicalize(
            WorkflowTestSamples.SimpleWorkflow(), WorkflowTestSamples.Options());

        IList<AssemblyReference> references = TextExpression.GetReferencesForImplementation(builder);
        List<string> names = references
            .Select(reference => reference.AssemblyName?.Name)
            .Where(name => !string.IsNullOrEmpty(name))
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(names, Does.Contain("mscorlib"));
            Assert.That(names, Does.Contain("Microsoft.CSharp"),
                "язык выражений C# требует ссылки на Microsoft.CSharp");
            Assert.That(names.Last(), Is.EqualTo("RPA-1"),
                "сборка проекта добавляется последней — как в эталоне RPA-1\\Main.xaml");
        });
    }

    [Test]
    public void Canonicalize_MergesExtraNamespacesAndReferencesWithoutDuplicates()
    {
        WorkflowCanonicalizationOptions options = WorkflowTestSamples.Options();
        options.Namespaces.Add("System.Text.RegularExpressions");
        options.Namespaces.Add("System");                      // дубликат значения по умолчанию
        options.References.Add("Activities.Flow");
        options.References.Add("mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089");

        ActivityBuilder builder = WorkflowCanonicalizer.Canonicalize(
            WorkflowTestSamples.SimpleWorkflow(), options);

        IList<string> namespaces = TextExpression.GetNamespacesForImplementation(builder);
        IList<string> references = TextExpression.GetReferencesForImplementation(builder)
            .Select(reference => reference.AssemblyName?.Name)
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(namespaces, Does.Contain("System.Text.RegularExpressions"),
                "дополнительные пространства имён нужны конкретному процессу");
            Assert.That(namespaces.Count(name => name == "System"), Is.EqualTo(1),
                "дубликат пространства имён не должен попадать в XAML: конструктор показывает список как есть");
            Assert.That(references, Does.Contain("Activities.Flow"));
            Assert.That(references.Count(name => string.Equals(name, "mscorlib", StringComparison.OrdinalIgnoreCase)),
                Is.EqualTo(1), "ссылка на сборку сравнивается по простому имени, а не по полному");
        });
    }

    // --- Идентификаторы конструктора. ---

    [Test]
    public void Canonicalize_AssignsIdRefToRootAndEveryActivity()
    {
        ActivityBuilder builder = WorkflowCanonicalizer.Canonicalize(
            WorkflowTestSamples.SimpleWorkflow(), WorkflowTestSamples.Options());

        var activities = new List<Activity> { builder.Implementation };
        activities.AddRange(WorkflowInspectionServices.GetActivities(builder.Implementation));

        List<string> idRefs = activities.Select(WorkflowViewState.GetIdRef).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(WorkflowViewState.GetIdRef(builder), Is.EqualTo("RPA_1.Main_1"),
                "у корня IdRef = «полное имя класса_1», как в эталоне");
            Assert.That(idRefs, Has.Count.EqualTo(3), "Sequence + Assign + WriteLine");
            Assert.That(idRefs, Has.None.Empty, "каждая активность обязана получить IdRef");
            Assert.That(idRefs.Distinct().Count(), Is.EqualTo(idRefs.Count),
                "IdRef обязан быть уникальным, иначе конструктор путает элементы диаграммы");
        });
    }

    [Test]
    public void Canonicalize_StripsGenericArityFromIdRef()
    {
        ActivityBuilder builder = WorkflowCanonicalizer.Canonicalize(
            WorkflowTestSamples.NestedWorkflow(), WorkflowTestSamples.Options());

        var activities = new List<Activity> { builder.Implementation };
        activities.AddRange(WorkflowInspectionServices.GetActivities(builder.Implementation));

        List<string> idRefs = activities.Select(WorkflowViewState.GetIdRef).Where(id => !string.IsNullOrEmpty(id)).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(idRefs.Any(id => id.StartsWith("ForEach_", StringComparison.Ordinal)), Is.True,
                "IdRef обобщённой активности — ForEach_1, а не ForEach`1_1: " + string.Join(", ", idRefs));
            Assert.That(idRefs.Any(id => id.IndexOf("`", StringComparison.Ordinal) >= 0), Is.False,
                "арность типа не должна попадать в IdRef: " + string.Join(", ", idRefs));
            Assert.That(idRefs.Distinct().Count(), Is.EqualTo(idRefs.Count),
                "вложенные активности тоже получают уникальные IdRef: " + string.Join(", ", idRefs));
        });
    }

    [Test]
    public void Canonicalize_IsIdempotent()
    {
        ActivityBuilder builder = WorkflowCanonicalizer.Canonicalize(
            WorkflowTestSamples.SimpleWorkflow(), WorkflowTestSamples.Options());

        string namespacesBefore = string.Join("|", TextExpression.GetNamespacesForImplementation(builder));
        string referencesBefore = string.Join("|", TextExpression.GetReferencesForImplementation(builder)
            .Select(reference => reference.AssemblyName?.Name));
        string idRefsBefore = string.Join("|", WorkflowInspectionServices.GetActivities(builder.Implementation)
            .Select(WorkflowViewState.GetIdRef));

        WorkflowCanonicalizer.Canonicalize(builder, WorkflowTestSamples.Options());

        Assert.Multiple(() =>
        {
            Assert.That(string.Join("|", TextExpression.GetNamespacesForImplementation(builder)),
                Is.EqualTo(namespacesBefore), "повторная канонизация не должна размножать пространства имён");
            Assert.That(string.Join("|", TextExpression.GetReferencesForImplementation(builder)
                    .Select(reference => reference.AssemblyName?.Name)),
                Is.EqualTo(referencesBefore), "повторная канонизация не должна размножать ссылки");
            Assert.That(string.Join("|", WorkflowInspectionServices.GetActivities(builder.Implementation)
                    .Select(WorkflowViewState.GetIdRef)),
                Is.EqualTo(idRefsBefore), "IdRef обязан быть детерминированным, иначе diff «плавает»");
        });
    }

    // --- Параметры канонизации. ---

    [Test]
    public void Options_ForProject_DerivesClassAndNamespace()
    {
        WorkflowCanonicalizationOptions options = WorkflowCanonicalizationOptions.ForProject(
            "RPA-1", @"C:\work\RPA-1\Main.xaml");

        Assert.Multiple(() =>
        {
            Assert.That(options.QualifiedClassName, Is.EqualTo("RPA_1.Main"),
                "недопустимые в идентификаторе символы заменяются: RPA-1 → RPA_1");
            Assert.That(options.ProjectAssemblyName, Is.EqualTo("RPA-1"),
                "имя сборки проекта остаётся как есть: это имя сборки, а не идентификатор");
        });
    }

    [Test]
    public void Options_SanitizeIdentifier_HandlesUnusualNames()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WorkflowCanonicalizationOptions.SanitizeIdentifier("RPA-1"), Is.EqualTo("RPA_1"));
            Assert.That(WorkflowCanonicalizationOptions.SanitizeIdentifier("2-й проект"), Is.EqualTo("_2_й_проект"),
                "имя не может начинаться с цифры — добавляется «_»");
            Assert.That(WorkflowCanonicalizationOptions.SanitizeIdentifier("a..b"), Is.EqualTo("a.b"),
                "пустая часть между точками вырождается: две точки подряд не идентификатор");
            Assert.That(WorkflowCanonicalizationOptions.ClassNameFromFilePath(@"C:\x\RPA-1\Main.xaml"), Is.EqualTo("Main"));
        });
    }

    [Test]
    public void Options_RejectInvalidArguments()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new WorkflowCanonicalizationOptions("RPA_1", string.Empty, "RPA-1"),
                Throws.ArgumentException, "имя класса обязательно");
            Assert.That(() => new WorkflowCanonicalizationOptions("RPA_1", "1Main", "RPA-1"),
                Throws.ArgumentException, "имя класса обязано быть идентификатором C#");
            Assert.That(() => new WorkflowCanonicalizationOptions("RPA_1", "Main", string.Empty),
                Throws.ArgumentException,
                "без сборки проекта типы проекта не резолвятся — параметр не может быть пустым");
            Assert.That(() => new WorkflowCanonicalizationOptions("RPA_1", "Main", "RPA 1"),
                Throws.ArgumentException, "«RPA 1» не имя сборки");
        });
    }

    [Test]
    public void Canonicalize_RejectsNullModel()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => WorkflowCanonicalizer.Canonicalize((Activity)null, WorkflowTestSamples.Options()),
                Throws.ArgumentNullException);
            Assert.That(() => WorkflowCanonicalizer.Canonicalize((ActivityBuilder)null, WorkflowTestSamples.Options()),
                Throws.ArgumentNullException);
            Assert.That(() => WorkflowCanonicalizer.Canonicalize(new ActivityBuilder(), null),
                Throws.ArgumentNullException);
        });
    }

    /// <summary>
    /// Процесс без реализации не должен ронять канонизацию: подсказка нужна агенту, иначе он получит
    /// пустой XAML без объяснения.
    /// </summary>
    [Test]
    public void Canonicalize_ReportsMissingImplementationInHints()
    {
        var hints = new List<string>();
        ActivityBuilder builder = WorkflowCanonicalizer.Canonicalize(new ActivityBuilder(), WorkflowTestSamples.Options(), hints);

        Assert.Multiple(() =>
        {
            Assert.That(WorkflowViewState.GetIdRef(builder), Is.EqualTo("RPA_1.Main_1"));
            Assert.That(hints, Is.Not.Empty, "отсутствие Implementation обязано попасть в подсказки");
        });
    }
}
