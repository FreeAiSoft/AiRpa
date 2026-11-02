using System;
using System.IO;
using RPA.Core.Services;

namespace AiRpaStudio.Tests;

/// <summary>
/// Баг «ошибки валидации отсутствуют при загрузке и в покое, появляются только после правки».
/// <para>
/// Иконка ошибки живёт на <c>Border x:Name="validationVisuals"</c> внутри вьюхи активности, поэтому
/// проход <c>DesignerValidator</c> результативен только для <b>реализованных</b> вьюх
/// (<c>ModelItem.View != null</c>). При открытии документа поверхность дизайнера строится позже
/// <c>Load</c>: документ попадает в панель, затем идут раскладка и тема. Единственный
/// «инициализационный» проход отрабатывал до этого момента, флаг инициализации снимался — и до первой
/// правки модели иконок не было.
/// </para>
/// <para>
/// Правило: полный проход (с компиляцией выражений Roslyn) выполняется при изменении модели, при
/// невыполненной инициализации и при смене подписи реализованных вьюх. Между событиями таймер только
/// пересчитывает подпись, поэтому «каждую секунду» валидация, как раньше, не идёт.
/// </para>
/// </summary>
public class ValidationScheduleTests
{
    // --- Решение «пора выполнять полный проход». ---

    [Test]
    public void ShouldValidate_InitPending_RunsEvenWithoutViews()
    {
        // Первый проход нужен всегда: он же инициализирует состояние иконок.
        Assert.That(ValidationSchedule.ShouldValidate(false, true, null, string.Empty), Is.True);
    }

    [Test]
    public void ShouldValidate_ModelChanged_Runs()
    {
        Assert.That(ValidationSchedule.ShouldValidate(true, false, "a|b", "a|b"), Is.True);
    }

    [Test]
    public void ShouldValidate_ViewsAppeared_Runs()
    {
        // Главный сценарий бага: загрузка документа, вьюхи построились позже первого прохода.
        Assert.That(ValidationSchedule.ShouldValidate(false, false, string.Empty, "0:Sequence:42"), Is.True);
    }

    [Test]
    public void ShouldValidate_NothingChanged_Skips()
    {
        // Иначе возвращается прежняя проблема: полная валидация с Roslyn каждую секунду.
        Assert.That(ValidationSchedule.ShouldValidate(false, false, "0:Sequence:42", "0:Sequence:42"), Is.False);
    }

    [Test]
    public void ShouldValidate_ModelNotLoaded_Skips()
    {
        // currentSignature == null — модели ещё нет: проход бессмысленен, таймер повторит проверку.
        Assert.Multiple(() =>
        {
            Assert.That(ValidationSchedule.ShouldValidate(true, true, null, null), Is.False);
            Assert.That(ValidationSchedule.ShouldValidate(false, false, "a", null), Is.False);
        });
    }

    // --- Подпись реализованных вьюх. ---

    [Test]
    public void BuildSignature_IsOrderIndependent()
    {
        string first = ValidationSchedule.BuildSignature(new[] { "1:Assign:7", "0:Sequence:9" });
        string second = ValidationSchedule.BuildSignature(new[] { "0:Sequence:9", "1:Assign:7" });

        Assert.That(first, Is.EqualTo(second), "порядок обхода модели не должен выглядеть как изменение поверхности");
    }

    [Test]
    public void BuildSignature_SkipsEmptyKeys()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ValidationSchedule.BuildSignature(null), Is.EqualTo(string.Empty));
            Assert.That(ValidationSchedule.BuildSignature(Array.Empty<string>()), Is.EqualTo(string.Empty));
            Assert.That(ValidationSchedule.BuildSignature(new[] { null, string.Empty }), Is.EqualTo(string.Empty));
        });
    }

    [Test]
    public void BuildSignature_ChangesWhenViewRecreated()
    {
        // WF пересоздал вьюху — локальные значения иконки потеряны, проход обязан повториться.
        string before = ValidationSchedule.BuildSignature(new[] { ValidationSchedule.BuildViewKey(0, "Assign", 1) });
        string after = ValidationSchedule.BuildSignature(new[] { ValidationSchedule.BuildViewKey(0, "Assign", 2) });

        Assert.That(before, Is.Not.EqualTo(after));
    }

    [Test]
    public void BuildSignature_ChangesWhenActivityRealized()
    {
        // Активность развернули (или доскроллили) — её вьюха появилась.
        string collapsed = ValidationSchedule.BuildSignature(new[] { ValidationSchedule.BuildViewKey(0, "Sequence", 1) });
        string expanded = ValidationSchedule.BuildSignature(new[]
        {
            ValidationSchedule.BuildViewKey(0, "Sequence", 1),
            ValidationSchedule.BuildViewKey(1, "Assign", 2)
        });

        Assert.That(collapsed, Is.Not.EqualTo(expanded));
    }

    // --- Стражи исходников: правило не должно откатиться. ---

    [Test]
    public void DesignerValidator_UsesValidationSchedule()
    {
        string source = ReadSource(@"AiRpaStudio\CSharpExpressionEditor\DesignerValidator.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("ValidationSchedule.ShouldValidate"),
                "без сверки подписи вьюх проход отрабатывает до построения поверхности и иконок нет");
            Assert.That(source, Does.Contain("BuildRealizedSignature"),
                "подпись реализованных вьюх — входные данные для решения о проходе");
            Assert.That(source, Does.Not.Contain("(_isModelChanged || _isNeedInitValidation) && _isModelServiceAvailable"),
                "старое условие запускало валидацию только по модели и не замечало появление вьюх");
        });
    }

    [Test]
    public void DesignerValidator_CountsViewIdentityInSignature()
    {
        string source = ReadSource(@"AiRpaStudio\CSharpExpressionEditor\DesignerValidator.cs");

        Assert.That(source, Does.Contain("BuildViewKey"),
            "идентичность вьюхи в подписи нужна, чтобы пересозданная вьюха снова получила иконку");
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
