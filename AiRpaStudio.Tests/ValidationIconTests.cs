using System;
using System.IO;
using System.Windows.Media;
using RPA.Core.Services;
using RPA.Helpers;

namespace AiRpaStudio.Tests;

/// <summary>
/// Баг «ошибки валидации исчезают, хотя должны быть».
/// <para>
/// Иконку ошибки на активности рисует WF: в шаблоне <c>WorkflowElementDesignerDefaultTemplate</c>
/// у <c>Border x:Name="validationVisuals"</c> есть триггеры на <c>ModelItem.ValidationState</c>.
/// Студия (<c>DesignerValidator.SetValidationIconDirectly</c>) ставила на этот Border <b>локальные</b>
/// значения и при отсутствии своих ошибок писала <c>Visibility = Collapsed</c> — локальное значение
/// сильнее триггера шаблона, поэтому нативная красная иконка гасла (в проектах Visual Basic иконка
/// оставалась, в C# перекрашивалась в студийную кисть из «Windows Store App» иконок).
/// </para>
/// <para>
/// Правило после исправления проверяется здесь: свои ошибки есть — рисуем свою иконку кистью палитры
/// и объединяем тексты; своих ошибок нет — не трогаем Border и отдаём отрисовку WF.
/// </para>
/// </summary>
public class ValidationIconTests
{
    // --- Решение «кто рисует иконку». ---

    [Test]
    public void ShouldDrawOwn_NoStudioErrors_ReturnsFalse()
    {
        // false = не вмешиваться: триггеры WF рисуют красную иконку ошибки.
        Assert.Multiple(() =>
        {
            Assert.That(ValidationIconState.ShouldDrawOwn(Array.Empty<string>()), Is.False);
            Assert.That(ValidationIconState.ShouldDrawOwn(null), Is.False);
        });
    }

    [Test]
    public void ShouldDrawOwn_StudioErrors_ReturnsTrue()
    {
        Assert.That(ValidationIconState.ShouldDrawOwn(new[] { "Value: выражение не компилируется" }), Is.True);
    }

    // --- Текст подсказки. ---

    [Test]
    public void ComposeToolTip_StudioErrorsOnly()
    {
        string toolTip = ValidationIconState.ComposeToolTip(
            new[] { "Value: ошибка 1", "To: ошибка 2" }, null);

        Assert.That(toolTip, Is.EqualTo("Value: ошибка 1" + Environment.NewLine + "To: ошибка 2"));
    }

    [Test]
    public void ComposeToolTip_KeepsWorkflowMessage()
    {
        // Своя иконка перекрывает подсказку WF, поэтому текст WF обязан попасть в подсказку.
        string toolTip = ValidationIconState.ComposeToolTip(
            new[] { "Value: ошибка выражения" },
            "Свойство DisplayName обязательно");

        Assert.Multiple(() =>
        {
            Assert.That(toolTip, Does.Contain("Value: ошибка выражения"));
            Assert.That(toolTip, Does.Contain("Свойство DisplayName обязательно"));
        });
    }

    [Test]
    public void ComposeToolTip_NoMessages_ReturnsNull()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ValidationIconState.ComposeToolTip(Array.Empty<string>(), null), Is.Null);
            Assert.That(ValidationIconState.ComposeToolTip(Array.Empty<string>(), "   "), Is.Null);
            Assert.That(ValidationIconState.ComposeToolTip(null, null), Is.Null);
        });
    }

    [Test]
    public void ComposeToolTip_DeduplicatesAndTrims()
    {
        string toolTip = ValidationIconState.ComposeToolTip(
            new[] { "  ошибка  ", "", null, "ошибка" },
            "ошибка");

        Assert.That(toolTip, Is.EqualTo("ошибка"), "одна и та же ошибка не должна повторяться в подсказке");
    }

    // --- Кисть иконки. ---

    [Test]
    public void ErrorBrush_ComesFromPalette()
    {
        // Красный — системная семантика ошибки из палитры, а не кисть WF «Windows Store App».
        SolidColorBrush brush = (SolidColorBrush)ValidationIconState.ErrorBrush();

        Assert.That(brush.Color, Is.EqualTo(StudioPalette.Dark.CloseHoverBackground));
    }

    [Test]
    public void TryGetWorkflowMessage_WithoutService_ReturnsNull()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ValidationIconState.TryGetWorkflowMessage(null, null), Is.Null);
        });
    }

    // --- Стражи исходников: правка не должна откатиться. ---

    [Test]
    public void DesignerValidator_ClearsLocalValuesInsteadOfCollapsingIcon()
    {
        string source = ReadSource(@"AiRpaStudio\CSharpExpressionEditor\DesignerValidator.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("border.ClearValue(UIElement.VisibilityProperty)"),
                "без снятия локальных значений триггеры шаблона WF не работают и иконка ошибки гаснет");
            Assert.That(source, Does.Not.Contain("border.Visibility = Visibility.Collapsed"),
                "локальное Collapsed гасит нативную иконку WF");
            Assert.That(source, Does.Not.Contain("ActivityErrorValidation"),
                "кисть иконки берётся из палитры (ValidationIconState.ErrorBrush), а не из иконок WF");
        });
    }

    [Test]
    public void DesignerValidator_StopsInitialValidationAfterFirstRun()
    {
        string source = ReadSource(@"AiRpaStudio\CSharpExpressionEditor\DesignerValidator.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("_isNeedInitValidation = false"),
                "флаг не сбрасывался, и полная валидация шла каждую секунду: иконки WF мигали и гасли");
            Assert.That(source, Does.Contain("ValidationIconState.TryGetWorkflowMessage"),
                "подсказка своей иконки должна включать текст WF");
            Assert.That(source, Does.Contain("expressionModelItem == null"),
                "аргумент без выражения ронял весь проход валидации NullReferenceException");
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
