using System;
using System.IO;
using System.Linq;
using RPA.Chat.Services.Workflow;

namespace AiRpaStudio.Tests.Workflow;

/// <summary>
/// AG-07: нормализация XAML — чистые правила, без WF и без WPF.
/// <para>
/// Нормализатор обязан давать канонический вид студии (UTF-8 без BOM, CRLF, без XML-декларации,
/// <c>xmlns:mc/sap/sap2010/mca</c> + <c>mc:Ignorable</c>) и при этом <b>не переиндентировать</b>
/// документ: текст выражений (<c>mca:CSharpValue</c>) и списки <c>TextExpression.*</c> лежат внутри
/// элементов, и добавленный отступ стал бы частью значимого текста. Отступ файла — только читается
/// и показывается агенту в шапке <c>read_files</c>.
/// </para>
/// </summary>
[TestFixture]
public class WorkflowXamlTextTests
{
    private const string ActivitiesNs = "http://schemas.microsoft.com/netfx/2009/xaml/activities";
    private const string XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string McNs = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private const string SapNs = "http://schemas.microsoft.com/netfx/2009/xaml/activities/presentation";
    private const string Sap2010Ns = "http://schemas.microsoft.com/netfx/2010/xaml/activities/presentation";
    private const string McaNs = "clr-namespace:Microsoft.CSharp.Activities;assembly=System.Activities";

    private static string Workflow(string inner, string extraRootAttributes = null)
    {
        return "<Activity xmlns=\"" + ActivitiesNs + "\" xmlns:x=\"" + XamlNs + "\""
               + (extraRootAttributes ?? string.Empty) + ">\r\n  " + inner + "\r\n</Activity>";
    }

    private static int CountOf(string text, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    [Test]
    public void Normalize_AddsStudioNamespacesAndMarkupCompatibility()
    {
        WorkflowXamlNormalizeResult result = WorkflowXamlText.Normalize(Workflow("<Sequence />"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Kind, Is.EqualTo(WorkflowXamlKind.Workflow));
            Assert.That(result.Text, Does.Contain("xmlns:mc=\"" + McNs + "\""));
            Assert.That(result.Text, Does.Contain("xmlns:sap=\"" + SapNs + "\""));
            Assert.That(result.Text, Does.Contain("xmlns:sap2010=\"" + Sap2010Ns + "\""));
            Assert.That(result.Text, Does.Contain("xmlns:mca=\"" + McaNs + "\""));
            Assert.That(result.Text, Does.Contain("mc:Ignorable=\"sap sap2010\""));
            Assert.That(result.Changed, Is.True, "файл без этих объявлений обязан измениться");
            Assert.That(string.Join("; ", result.Notes), Does.Contain("mca"),
                "в отчёте нормализации должно быть видно, что добавлено");
        });
    }

    [Test]
    public void Normalize_RemovesXmlDeclarationAndUsesCrLf()
    {
        string input =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<Activity xmlns=\"" + ActivitiesNs + "\">\n  <Sequence />\n</Activity>\n";

        WorkflowXamlNormalizeResult result = WorkflowXamlText.Normalize(input);

        Assert.Multiple(() =>
        {
            Assert.That(result.Text, Does.Not.StartWith("<?xml"), "файлы студии — без XML-декларации");
            Assert.That(result.Text.Replace("\r\n", string.Empty), Does.Not.Contain("\n"), "переводы строк обязаны стать CRLF");
            Assert.That(result.Text.Replace("\r\n", string.Empty), Does.Not.Contain("\r"));
        });
    }

    [Test]
    public void Normalize_DoesNotDuplicateAlreadyDeclaredPrefixes()
    {
        string input =
            "<Activity xmlns=\"" + ActivitiesNs + "\" xmlns:mc=\"" + McNs + "\" mc:Ignorable=\"sap sap2010\"" +
            " xmlns:sap=\"" + SapNs + "\" xmlns:sap2010=\"" + Sap2010Ns + "\" xmlns:mca=\"" + McaNs + "\">\r\n" +
            "  <Sequence />\r\n</Activity>";

        WorkflowXamlNormalizeResult result = WorkflowXamlText.Normalize(input);

        Assert.Multiple(() =>
        {
            Assert.That(CountOf(result.Text, "xmlns:mc="), Is.EqualTo(1));
            Assert.That(CountOf(result.Text, "xmlns:sap="), Is.EqualTo(1));
            Assert.That(CountOf(result.Text, "xmlns:sap2010="), Is.EqualTo(1));
            Assert.That(CountOf(result.Text, "xmlns:mca="), Is.EqualTo(1));
            Assert.That(CountOf(result.Text, "Ignorable="), Is.EqualTo(1));
            Assert.That(result.Changed, Is.False, "канонический документ не должен меняться");
        });
    }

    [Test]
    public void Normalize_MergesIgnorablePrefixes()
    {
        string input = Workflow("<Sequence />",
            " xmlns:mc=\"" + McNs + "\" mc:Ignorable=\"d\"");

        WorkflowXamlNormalizeResult result = WorkflowXamlText.Normalize(input);

        Assert.That(result.Text, Does.Contain("mc:Ignorable=\"d sap sap2010\""),
            "существующие префиксы mc:Ignorable сохраняются, sap/sap2010 добавляются");
    }

    [Test]
    public void Normalize_PreservesExpressionTextAndFileIndentation()
    {
        string input =
            "<Activity xmlns=\"" + ActivitiesNs + "\" xmlns:x=\"" + XamlNs + "\" xmlns:mca=\"" + McaNs + "\">\r\n" +
            "    <Sequence>\r\n" +
            "        <WriteLine>\r\n" +
            "            <WriteLine.Text>\r\n" +
            "                <InArgument x:TypeArguments=\"x:String\">\r\n" +
            "                    <mca:CSharpValue x:TypeArguments=\"x:String\">value + 1</mca:CSharpValue>\r\n" +
            "                </InArgument>\r\n" +
            "            </WriteLine.Text>\r\n" +
            "        </WriteLine>\r\n" +
            "    </Sequence>\r\n" +
            "</Activity>";

        WorkflowXamlNormalizeResult result = WorkflowXamlText.Normalize(input);

        Assert.Multiple(() =>
        {
            Assert.That(result.Text, Does.Contain(">value + 1</mca:CSharpValue>"),
                "текст выражения не должен обрастать отступами");
            Assert.That(result.Text, Does.Contain("\r\n    <Sequence>"),
                "отступы файла сохраняются: нормализатор не переиндентирует документ");
        });
    }

    [Test]
    public void Normalize_IsIdempotent()
    {
        string input = "<?xml version=\"1.0\"?>\n" + Workflow("<Sequence />").Replace("\r\n", "\n");

        WorkflowXamlNormalizeResult first = WorkflowXamlText.Normalize(input);
        WorkflowXamlNormalizeResult second = WorkflowXamlText.Normalize(first.Text);

        Assert.Multiple(() =>
        {
            Assert.That(second.Text, Is.EqualTo(first.Text), "повторная нормализация ничего не меняет");
            Assert.That(second.Changed, Is.False);
        });
    }

    [Test]
    public void Normalize_MalformedXmlIsNotPretendedToBeNormalized()
    {
        const string input = "<Activity><Sequence></Activity>";

        WorkflowXamlNormalizeResult result = WorkflowXamlText.Normalize(input);

        Assert.Multiple(() =>
        {
            Assert.That(result.Kind, Is.EqualTo(WorkflowXamlKind.Malformed));
            Assert.That(result.Text, Is.EqualTo(input), "текст битого XML возвращается как есть");
            Assert.That(result.Error, Is.Not.Null.And.Not.Empty, "причина обязана быть названа");
            Assert.That(result.Changed, Is.False);
        });
    }

    [Test]
    public void Normalize_LeavesNonWorkflowXamlAlone()
    {
        const string input =
            "<Window xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Width=\"100\" />";

        WorkflowXamlNormalizeResult result = WorkflowXamlText.Normalize(input);

        Assert.Multiple(() =>
        {
            Assert.That(result.Kind, Is.EqualTo(WorkflowXamlKind.OtherXml));
            Assert.That(result.Text, Is.EqualTo(input), "XAML не рабочего процесса не переписывается");
            Assert.That(result.Changed, Is.False);
            Assert.That(string.Join("; ", result.Notes), Does.Contain("не рабочий процесс"));
        });
    }

    [Test]
    public void Describe_ReportsEncodingEolIndentRootAndNamespaces()
    {
        string header = WorkflowXamlText.Describe(
            Workflow("<Sequence />"),
            hasBom: false,
            source: "файл на диске");

        Assert.Multiple(() =>
        {
            Assert.That(header, Does.Contain("xaml:"));
            Assert.That(header, Does.Contain("Activity"), "корневой тип обязан быть виден");
            Assert.That(header, Does.Contain("UTF-8 без BOM"));
            Assert.That(header, Does.Contain("EOL CRLF"));
            Assert.That(header, Does.Contain("отступ 2 пробела"));
            Assert.That(header, Does.Contain("строк"));
            Assert.That(header, Does.Contain("xaml xmlns: default →"), "объявленные xmlns перечисляются");
            Assert.That(header, Does.Contain("x → http://schemas.microsoft.com/winfx/2006/xaml"), "префикс → URI");
            Assert.That(header, Does.Contain("файл на диске"));
        });
    }

    [Test]
    public void Describe_ReportsBomAndDesignerSource()
    {
        string header = WorkflowXamlText.Describe(
            Workflow("<Sequence />"),
            hasBom: true,
            source: "файл на диске",
            openInDesigner: true);

        Assert.Multiple(() =>
        {
            Assert.That(header, Does.Contain("UTF-8 с BOM"));
            Assert.That(header, Does.Contain("в конструкторе: да"),
                "агент обязан видеть, что правки модели перекроют файл");
        });
    }

    [Test]
    public void Describe_ReportsLoneLineFeeds()
    {
        string header = WorkflowXamlText.Describe(
            Workflow("<Sequence />").Replace("\r\n", "\n"),
            hasBom: false,
            source: "буфер редактора");

        Assert.Multiple(() =>
        {
            Assert.That(header, Does.Contain("EOL LF"));
            Assert.That(header, Does.Contain("буфер редактора"));
            Assert.That(header, Does.Contain("в конструкторе: нет"));
        });
    }

    [Test]
    public void Describe_MalformedXamlSaysSo()
    {
        string header = WorkflowXamlText.Describe("<Activity><Sequence></Activity>", hasBom: false, source: null);

        Assert.Multiple(() =>
        {
            Assert.That(header, Does.Contain("не является корректным XML"));
            Assert.That(header, Does.Contain("UTF-8 без BOM"), "кодировку видно даже у битого файла");
        });
    }

    [TestCase("Main.xaml", true)]
    [TestCase("Core\\Main.XAML", true)]
    [TestCase("Main.xaml.cs", false)]
    [TestCase("Activity.cs", false)]
    [TestCase("main.txt", false)]
    public void IsWorkflowXamlFile_DependsOnExtension(string path, bool expected)
    {
        Assert.That(WorkflowXamlText.IsWorkflowXamlFile(path), Is.EqualTo(expected));
    }
}
