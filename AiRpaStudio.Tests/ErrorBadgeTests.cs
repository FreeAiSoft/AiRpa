using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AiRpaStudio.Tests;

/// <summary>
/// Баг «ошибка C#-выражения показывается красной рамкой без иконки».
/// <para>
/// Бейдж ошибки в <c>RoslynExpressionEditor.xaml</c> был <c>Rectangle</c> с <c>Fill = null</c> и
/// hairline-рамкой (этап 7 темы), то есть геометрической рамкой без глифа. Пользователь ожидает
/// иконку ошибки, как в остальной студии (<c>materialIcons:MaterialIcon</c>), поэтому бейдж — глиф
/// <c>Kind="Error"</c> в единственном не-нейтральном цвете палитры.
/// </para>
/// <para>
/// Второй дефект того же бага — задержка: проверка выражения не реагировала на набор текста
/// (см. <see cref="ExpressionValidationTimingTests"/>).
/// </para>
/// </summary>
[Apartment(ApartmentState.STA)]
public class ErrorBadgeTests
{
    private const string EditorXamlPath = @"AiRpaStudio\CSharpExpressionEditor\RoslynExpressionEditor.xaml";
    private const string EditorSourcePath = @"AiRpaStudio\CSharpExpressionEditor\RoslynExpressionEditor.xaml.cs";

    // --- Бейдж: иконка, а не пустая рамка. ---

    [Test]
    public void ErrorBadge_IsMaterialIconInsteadOfEmptyRectangle()
    {
        string xaml = WithoutComments(Read(EditorXamlPath));

        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Contain("xmlns:materialIcons=\"clr-namespace:Material.Icons.WPF;assembly=Material.Icons.WPF\""),
                "иконки студии берутся из Material.Icons.WPF");
            Assert.That(xaml, Does.Contain("TargetType=\"{x:Type materialIcons:MaterialIcon}\""),
                "стиль бейджа должен быть стилем иконки, а не Rectangle");
            Assert.That(xaml, Does.Not.Contain("<Rectangle"),
                "рамка без глифа — это и есть «красная рамка без иконки»");
        });
    }

    [Test]
    public void ErrorBadge_HasErrorGlyphAndPaletteColor()
    {
        string xaml = WithoutComments(Read(EditorXamlPath));
        string style = ExtractErrorStyle(xaml);

        Assert.Multiple(() =>
        {
            Assert.That(style, Does.Contain("Property=\"Kind\""), "у иконки должен быть задан глиф");
            Assert.That(style, Does.Contain("Value=\"Error\""), "глиф ошибки — залитый круг с «!» (Kind=Error)");
            Assert.That(style, Does.Contain("Studio.CloseHoverBackground"),
                "красный — единственный не-нейтральный цвет палитры, семантика ошибки");
            Assert.That(style, Does.Contain("Property=\"Width\""));
            Assert.That(style, Does.Contain("Property=\"Height\""),
                "MaterialIcon без явных размеров схлопывается в 0 — иконки не видно");
        });
    }

    [Test]
    public void ErrorBadge_IsVisibleOnlyForErrors_AndKeepsToolTip()
    {
        string style = ExtractErrorStyle(WithoutComments(Read(EditorXamlPath)));

        Assert.Multiple(() =>
        {
            Assert.That(style, Does.Contain("HasValidationError"),
                "видимость бейджа управляется состоянием проверки выражения");
            Assert.That(style, Does.Contain("ValidationErrorMessage"),
                "текст ошибки должен быть в подсказке бейджа");
            Assert.That(style, Does.Contain("ToolTipService.InitialShowDelay"),
                "подсказка должна появляться сразу: ошибку ищут наведением на бейдж");
        });
    }

    [Test]
    public void ErrorBadge_HasNoHardcodedColors()
    {
        string style = ExtractErrorStyle(WithoutComments(Read(EditorXamlPath)));

        Assert.That(style, Does.Not.Match("#[0-9a-fA-F]{3,8}"), "цвета бейджа — только ключами палитры");
    }

    // --- Проверка по изменению текста. ---

    [Test]
    public void ExpressionEditor_ValidatesOnTextChangeWithDebounce()
    {
        string source = Read(EditorSourcePath);

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("TextChanged += OnExpressionEditorTextChanged"),
                "без подписки на изменение текста бейдж появляется только по фоновому циклу (до 10 с)");
            Assert.That(source, Does.Contain("ExpressionValidationTiming.DebounceMilliseconds"),
                "проверка по набору должна идти через паузу-дребезг, а не на каждое нажатие");
            Assert.That(source, Does.Contain("ExpressionValidationTiming.ShouldValidate"),
                "неизменённый текст не должен перекомпилироваться");
            Assert.That(source, Does.Not.Contain("validationWaitTime = 10000"),
                "десятисекундная пауза фонового цикла и была задержкой появления ошибки");
        });
    }

    [Test]
    public void ExpressionEditor_StopsValidationTimerAndSubscriptions()
    {
        string source = Read(EditorSourcePath);
        string closing = ExtractMethod(source, "private void OnEditorClosing");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("TextChanged -= OnExpressionEditorTextChanged"),
                "подписку на изменённый редактор надо снимать при закрытии вкладки");
            Assert.That(closing, Does.Contain("StopExpressionValidationTimer"),
                "таймер проверки не должен тикать после закрытия документа");
        });
    }

    [Test]
    public void ExpressionEditor_ClearsBadgeWhenExpressionBecomesValid()
    {
        string source = Read(EditorSourcePath);
        string validate = ExtractMethod(source, "ValidateExpression");

        Assert.That(validate, Does.Contain("UpdateValidationError"),
            "раньше ошибка снималась только при новой ошибке: исправленное выражение оставляло бейдж");
        Assert.That(validate, Does.Not.Contain("if (etb.HasError"), "ветка без сброса бейджа — это тот же баг");
    }

    // --- Разбор исходников. ---

    private static string ExtractErrorStyle(string xaml)
    {
        Match match = Regex.Match(xaml, "x:Key=\"errorStyle\"[\\s\\S]*?</Style>");
        Assert.That(match.Success, Is.True, "не найден стиль errorStyle");
        return match.Value;
    }

    private static string ExtractMethod(string source, string name)
    {
        int start = source.IndexOf(name, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"не найден метод {name}");
        int end = source.IndexOf("\n        private", start, StringComparison.Ordinal);
        return end > start ? source.Substring(start, end - start) : source.Substring(start);
    }

    private static string WithoutComments(string text)
    {
        return Regex.Replace(text, "<!--[\\s\\S]*?-->", string.Empty);
    }

    private static string Read(string relativePath)
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
