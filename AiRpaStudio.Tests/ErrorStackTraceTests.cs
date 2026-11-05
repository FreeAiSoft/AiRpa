using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using RPA.Helpers;

namespace AiRpaStudio.Tests;

/// <summary>
/// Стек вызовов в тексте ошибки.
/// <para>
/// Инцидент: окно ошибки показало «Не удалось загрузить в дизайнер: «System.Windows.Controls.RowDefinition»
/// не является Visual или Visual3D» — по такой строке невозможно понять, где именно упало. Правило
/// студии (AGENTS.md): любая ошибка, которую видит пользователь или читает разработчик, содержит полный
/// <see cref="Exception.ToString()"/> со стеком. Форматирование живёт в <see cref="ErrorText"/>, а
/// этот файл держит стражи по исходникам: ни одно место показа/логирования ошибки не должно снова
/// печатать только <c>ex.Message</c>.
/// </para>
/// </summary>
public class ErrorStackTraceTests
{
    /// <summary>Куда уходит текст ошибки: окно ошибки, лог, диагностика, ответ MCP.</summary>
    private static readonly Regex SinkPattern = new Regex(
        "OnSendMessage|ErrorDialog\\.Show|Log\\.Write|Log\\(|Debug\\.WriteLine|Console\\.|Errors\\.Add|new Error\\(|SendErrorResponse|Data =|Error =",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(2));

    /// <summary>Файлы, где формируется голова текста ошибки; им можно читать ex.Message.</summary>
    private static readonly string[] ErrorTextBuilders = { "Helpers\\ErrorDialog", "Helpers\\ErrorText" };

    [Test]
    public void Describe_AppendsFullException()
    {
        Exception captured = CaptureException();

        string text = ErrorText.Describe("Не удалось загрузить в дизайнер", captured);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.StartWith("Не удалось загрузить в дизайнер: "), "контекст должен остаться первым");
            Assert.That(text, Does.Contain(captured.Message), "сообщение исключения не должно теряться");
            Assert.That(text, Does.Contain(nameof(InvalidOperationException)), "нужен тип исключения");
            Assert.That(text, Does.Contain(nameof(CaptureException)), "в тексте нет стека — именно этого не хватало");
        });
    }

    [Test]
    public void Describe_WithoutException_ReturnsContext()
    {
        Assert.That(ErrorText.Describe("контекст:", null), Is.EqualTo("контекст"), "конечное двоеточие убирается");
    }

    [Test]
    public void Describe_WithoutContext_StartsWithExceptionMessage()
    {
        Exception captured = CaptureException();

        string text = ErrorText.Describe(null, captured);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.StartWith(captured.Message));
            Assert.That(text, Does.Contain(nameof(CaptureException)));
        });
    }

    [Test]
    public void Of_Null_IsEmpty()
    {
        Assert.That(ErrorText.Of(null), Is.Empty);
    }

    [Test]
    public void ErrorDialog_BuildExceptionMessage_KeepsStackTrace()
    {
        Exception captured = CaptureException();

        string text = ErrorDialog.BuildExceptionMessage(captured, "Failed to save workspace layout");

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.StartWith("Failed to save workspace layout: "));
            Assert.That(text, Does.Contain(nameof(CaptureException)), "окно ошибки обязано показывать стек");
        });
    }

    [Test]
    public void Sources_ReportExceptionsWithStackTrace()
    {
        var violations = new List<string>();

        foreach (string file in StudioSources())
        {
            if (ErrorTextBuilders.Any(name => file.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                continue;
            }

            string[] lines = File.ReadAllLines(file);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                bool interpolatesMessage = line.IndexOf("{ex.Message}", StringComparison.Ordinal) >= 0;
                bool reportsMessageOnly = line.IndexOf("ex.Message", StringComparison.Ordinal) >= 0 && SinkPattern.IsMatch(line);

                if (interpolatesMessage || reportsMessageOnly)
                {
                    violations.Add($"{Path.GetFileName(file)}:{i + 1}: {line.Trim()}");
                }
            }
        }

        Assert.That(violations, Is.Empty,
            "ошибки показываются и логируются только полным исключением (стек обязателен):"
            + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Test]
    public void ProjectFile_CompilesErrorText()
    {
        // OR-02: `ErrorText` переехал в общий headless-рантайм (`Rpa.Workflow.Runtime`), чтобы раннер
        // печатал ошибки в том же формате со стеком. Проверяются оба конца связи: файл на месте
        // и студия ссылается на рантайм — иначе формат ошибок студии и раннера разъедется.
        string source = ReadSource(@"Rpa.Workflow.Runtime\Helpers\ErrorText.cs");

        Assert.That(source, Does.Contain("public static string Of(Exception ex)"),
            "хелпер формата ошибок обязан остаться единственным источником текста ошибки со стеком");

        string studio = ReadSource(@"AiRpaStudio\AiRpaStudio.csproj");

        Assert.That(studio, Does.Contain(@"Rpa.Workflow.Runtime"),
            "студия обязана ссылаться на общий рантайм прогона, иначе формат ошибок разъедется с раннером");
    }

    private static Exception CaptureException()
    {
        try
        {
            throw new InvalidOperationException(
                "«System.Windows.Controls.RowDefinition» не является Visual или Visual3D.");
        }
        catch (InvalidOperationException ex)
        {
            return ex;
        }
    }

    /// <summary>Все исходники студии (<c>*.cs</c>) без <c>bin</c>/<c>obj</c>.</summary>
    private static IEnumerable<string> StudioSources()
    {
        foreach (string file in Directory.GetFiles(StudioDirectory(), "*.cs", SearchOption.AllDirectories))
        {
            if (file.IndexOf(@"\bin\", StringComparison.OrdinalIgnoreCase) >= 0
                || file.IndexOf(@"\obj\", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                continue;
            }

            yield return file;
        }
    }

    private static string StudioDirectory()
    {
        string directory = TestContext.CurrentContext.TestDirectory;

        while (directory != null)
        {
            string candidate = Path.Combine(directory, "AiRpaStudio");

            if (File.Exists(Path.Combine(candidate, "AiRpaStudio.csproj")))
            {
                return candidate;
            }

            directory = Path.GetDirectoryName(directory);
        }

        throw new DirectoryNotFoundException("не найден проект AiRpaStudio");
    }

    private static string ReadSource(string relativePath)
    {
        string directory = TestContext.CurrentContext.TestDirectory;

        while (directory != null)
        {
            string candidate = Path.Combine(directory, relativePath);

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = Path.GetDirectoryName(directory);
        }

        throw new FileNotFoundException($"не найден файл {relativePath}");
    }
}
