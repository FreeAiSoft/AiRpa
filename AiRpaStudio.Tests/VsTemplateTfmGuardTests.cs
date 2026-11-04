using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

using NUnit.Framework;

using IOPath = System.IO.Path;

namespace AiRpaStudio.Tests;

/// <summary>
/// `CP-07` — шаблон проекта VS (ActivityLibrary2010): плейсхолдер TFM вместо захардкоженной версии,
/// полный набор ссылок WF/дизайнера и умолчание «4.7.2» для создаваемых роботов (CP-10).
/// </summary>
[TestFixture]
public class VsTemplateTfmGuardTests
{
    /// <summary>Сборки, без которых активность с дизайнером не собирается сразу после создания проекта.</summary>
    private static readonly string[] RequiredReferences =
    {
        "System",
        "System.Activities",
        "System.Activities.Core.Presentation",
        "System.Activities.Presentation",
        "System.ComponentModel.Composition",
        "System.ServiceModel.Activities",
        "System.Xaml",
        "PresentationCore",
        "PresentationFramework",
        "WindowsBase"
    };

    [Test]
    public void DefaultTargetFrameworkVersion_IsNet472()
    {
        Assert.That(RPA.Consts.defaultTargetFrameworkVersion, Is.EqualTo("4.7.2"),
            "CP-07/CP-10: генерируемые роботы целятся в 4.7.2");

        Assert.That(RPA.Consts.targetFrameworkVersions.First(), Is.EqualTo("4.7.2"),
            "4.7.2 должен стоять первым в списке — он выбран по умолчанию");
    }

    [Test]
    public void TemplateProject_KeepsTargetFrameworkPlaceholder()
    {
        string text = File.ReadAllText(TemplateProjectPath);

        Assert.That(text, Does.Contain("$targetframeworkversion$"),
            "TFM в шаблоне подставляется при создании проекта");
        Assert.That(text, Does.Not.Match(@"<TargetFrameworkVersion>v4\."),
            "в шаблоне не должно быть захардкоженной версии .NET Framework");
    }

    [Test]
    public void TemplateProject_ReferencesWorkflowAndDesignerAssemblies()
    {
        XDocument document = XDocument.Load(TemplateProjectPath);

        var references = new HashSet<string>(
            document.Descendants()
                .Where(e => e.Name.LocalName == "Reference")
                .Select(e => ((string)e.Attribute("Include") ?? string.Empty).Trim())
                .Where(v => v.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        var missing = RequiredReferences.Where(r => !references.Contains(r)).ToList();

        Assert.That(missing, Is.Empty,
            "в шаблоне проекта не хватает ссылок (активность с дизайнером иначе не собирается): " +
            string.Join(", ", missing));
    }

    [Test]
    public void TemplateProject_IsWorkflowProject()
    {
        XDocument document = XDocument.Load(TemplateProjectPath);

        string guids = document.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "ProjectTypeGuids")?.Value ?? string.Empty;

        Assert.That(guids, Does.Contain(RPA.VsProject.VsProjectParser.WorkflowProjectTypeGuid.Trim('{', '}')),
            "шаблон создаёт WF-проект (библиотека активностей)");
    }

    [Test]
    public void Template_DeclaresMinimumFrameworkVersion()
    {
        XDocument document = XDocument.Load(TemplatePath);

        string required = document.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "RequiredFrameworkVersion")?.Value?.Trim();

        Assert.That(required, Is.Not.Null.And.Not.Empty,
            "в .vstemplate должно быть указано минимальное требование к .NET Framework");

        Version requiredVersion = Version.Parse(required);
        Version robotVersion = Version.Parse(RPA.Consts.defaultTargetFrameworkVersion);

        Assert.That(requiredVersion, Is.LessThanOrEqualTo(robotVersion),
            "минимальная версия шаблона не может быть выше TFM роботов (" + robotVersion + ")");
    }

    private static string TemplatePath
    {
        get
        {
            DirectoryInfo directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

            while (directory != null)
            {
                string candidate = IOPath.Combine(directory.FullName, "AiRpaStudio", "VSTemplates", "ActivityLibrary2010", "ActivityLibrary.vstemplate");

                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            Assert.Fail("не найден шаблон AiRpaStudio\\VSTemplates\\ActivityLibrary2010\\ActivityLibrary.vstemplate");
            return null;
        }
    }

    private static string TemplateProjectPath
    {
        get { return IOPath.Combine(IOPath.GetDirectoryName(TemplatePath), "ActivityLibrary.csproj"); }
    }
}
