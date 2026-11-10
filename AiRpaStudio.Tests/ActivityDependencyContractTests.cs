using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

using NUnit.Framework;

using RPA.Core.Workflow;
using RPA.VsProject;
using AiRpaStudio.Tests.Workflow;

namespace AiRpaStudio.Tests;

/// <summary>
/// Р С™Р С•Р Р…РЎвЂљРЎР‚Р В°Р С”РЎвЂљ Р В·Р В°Р Р†Р С‘РЎРѓР С‘Р СР С•РЎРѓРЎвЂљР ВµР в„– Р В±Р С‘Р В±Р В»Р С‘Р С•РЎвЂљР ВµР С” Р В°Р С”РЎвЂљР С‘Р Р†Р Р…Р С•РЎРѓРЎвЂљР ВµР в„– (`ru/activity-development.md`).
/// <para>
/// Р РЋРЎвЂљРЎС“Р Т‘Р С‘РЎРЏ Р С‘ РЎР‚Р С•Р В±Р С•РЎвЂљ Р Т‘Р ВµР В»РЎРЏРЎвЂљ Р С•Р Т‘Р С‘Р Р… AppDomain, Р С—Р С•РЎРЊРЎвЂљР С•Р СРЎС“ Р С—Р ВµРЎР‚Р ВµРЎРѓР ВµР С”Р В°РЎР‹РЎвЂ°Р С‘Р ВµРЎРѓРЎРЏ РЎРѓР С• РЎРѓРЎвЂљРЎС“Р Т‘Р С‘Р ВµР в„– Р В·Р В°Р Р†Р С‘РЎРѓР С‘Р СР С•РЎРѓРЎвЂљР С‘ Р В°Р С”РЎвЂљР С‘Р Р†Р Р…Р С•РЎРѓРЎвЂљР С‘
/// Р С•Р В±РЎРЏР В·Р В°Р Р…РЎвЂ№ Р С‘Р СР ВµРЎвЂљРЎРЉ РЎвЂљРЎС“ Р В¶Р Вµ Р Р†Р ВµРЎР‚РЎРѓР С‘РЎР‹, РЎвЂЎРЎвЂљР С• Р С‘ РЎвЂ¦Р С•РЎРѓРЎвЂљ. Р ВР Р…Р В°РЎвЂЎР Вµ Р Р† Р Т‘Р С•Р СР ВµР Р…Р Вµ Р С•Р С”Р В°Р В·РЎвЂ№Р Р†Р В°РЎР‹РЎвЂљРЎРѓРЎРЏ Р Т‘Р Р†Р Вµ Р С”Р С•Р С—Р С‘Р С‘ Р С•Р Т‘Р Р…Р С•Р в„– РЎРѓР В±Р С•РЎР‚Р С”Р С‘ РІР‚вЂќ
/// <c>FileLoadException</c>/<c>FileNotFoundException</c> Р Р† Р С—Р В°Р В»Р С‘РЎвЂљРЎР‚Р Вµ, Р Т‘Р С‘Р В·Р В°Р в„–Р Р…Р ВµРЎР‚Р Вµ Р С‘ Р С—РЎР‚Р С‘ Р С•РЎвЂљР В»Р В°Р Т‘Р С”Р Вµ РЎР‚Р С•Р В±Р С•РЎвЂљР В°.
/// </para>
/// </summary>
[TestFixture]
public class ActivityDependencyContractTests
{
    /// <summary>
    /// Р СџР С‘Р Р…-Р Р†Р ВµРЎР‚РЎРѓР С‘Р С‘ РІР‚вЂќ Р Р†Р ВµРЎР‚РЎРѓР С‘Р С‘ РЎвЂљР ВµРЎвЂ¦ Р В¶Р Вµ Р С—Р В°Р С”Р ВµРЎвЂљР С•Р Р†, РЎвЂЎРЎвЂљР С• Р В·Р В°Р С”РЎР‚Р ВµР С—Р В»Р ВµР Р…РЎвЂ№ Р Р† <c>AiRpaStudio\packages.config</c>.
    /// Р СџР В°Р С”Р ВµРЎвЂљРЎвЂ№, Р С”Р С•РЎвЂљР С•РЎР‚РЎвЂ№РЎвЂ¦ Р Р…Р ВµРЎвЂљ Р Р† РЎРЊРЎвЂљР С•Р С РЎРѓР В»Р С•Р Р†Р В°РЎР‚Р Вµ (Р Р…Р В°Р С—РЎР‚Р С‘Р СР ВµРЎР‚, <c>PuppeteerSharp</c>), РЎРѓРЎвЂЎР С‘РЎвЂљР В°РЎР‹РЎвЂљРЎРѓРЎРЏ РЎС“Р Р…Р С‘Р С”Р В°Р В»РЎРЉР Р…РЎвЂ№Р СР С‘
    /// Р В·Р В°Р Р†Р С‘РЎРѓР С‘Р СР С•РЎРѓРЎвЂљРЎРЏР СР С‘ Р В°Р С”РЎвЂљР С‘Р Р†Р Р…Р С•РЎРѓРЎвЂљР С‘ Р С‘ Р Р…Р Вµ Р С—РЎР‚Р С•Р Р†Р ВµРЎР‚РЎРЏРЎР‹РЎвЂљРЎРѓРЎРЏ.
    // РџРёРЅ-РІРµСЂСЃРёРё вЂ” РµРґРёРЅС‹Р№ РёСЃС‚РѕС‡РЅРёРє: RPA.VsProject.ActivityCompatibilityContract.PinnedVersions.

    /// <summary>
    /// Р СџРЎР‚Р С•Р ВµР С”РЎвЂљРЎвЂ№, Р С”Р С•РЎвЂљР С•РЎР‚РЎвЂ№Р Вµ Р С•Р В±РЎРЏР В·Р В°Р Р…РЎвЂ№ РЎРѓР С•Р В±Р В»РЎР‹Р Т‘Р В°РЎвЂљРЎРЉ Р С”Р С•Р Р…РЎвЂљРЎР‚Р В°Р С”РЎвЂљ. Р Р€РЎРѓРЎвЂљР В°РЎР‚Р ВµР Р†РЎв‚¬Р С‘Р в„– Р С—РЎР‚Р С•Р ВµР С”РЎвЂљ <c>Activities</c> РЎРѓР С•Р В·Р Р…Р В°РЎвЂљР ВµР В»РЎРЉР Р…Р С•
    /// Р Р†Р Р…Р Вµ РЎРѓР С—Р С‘РЎРѓР С”Р В°: Р С•Р Р… РЎвЂљРЎРЏР Р…Р ВµРЎвЂљ РЎРѓРЎвЂљР В°РЎР‚РЎвЂ№Р в„– Р С–РЎР‚Р В°РЎвЂћ Microsoft.Extensions.* 2.x Р С‘ Р С—Р ВµРЎР‚Р ВµР Р†Р С•Р Т‘Р С‘РЎвЂљРЎРѓРЎРЏ Р С•РЎвЂљР Т‘Р ВµР В»РЎРЉР Р…Р С•
    /// (РЎРѓР С. Р’В«Р СџРЎР‚Р С•Р В±Р В»Р ВµР СР Р…РЎвЂ№Р Вµ Р С—РЎР‚Р С‘Р СР ВµРЎР‚РЎвЂ№Р’В» Р Р† <c>ru/activity-development.md</c>).
    /// </summary>
    private static readonly string[] ActivityProjects =
    {
        "Activities.Browser",
        "Activities.Debug",
        "Activities.Files",
        "Activities.Flow",
        "Activities.OS",
        "ActivityCodeTemplate",
        "ActivityLibraryBase",
        "DesignerTemplate",
    };

    [Test]
    public void ActivityProjects_UsePinnedVersionsForSharedDependencies()
    {
        string root = FindRepositoryRoot();
        var problems = new List<string>();

        foreach (string project in ActivityProjects)
        {
            string directory = Path.Combine(root, project);
            if (!Directory.Exists(directory))
            {
                problems.Add(project + ": Р С”Р В°РЎвЂљР В°Р В»Р С•Р С– Р Р…Р Вµ Р Р…Р В°Р в„–Р Т‘Р ВµР Р…");
                continue;
            }

            CheckPackagesConfig(directory, project, problems);
            CheckCsprojHintPaths(directory, project, problems);
            CheckNuspec(directory, project, problems);
        }

        Assert.That(problems, Is.Empty,
            "Р Р†Р ВµРЎР‚РЎРѓР С‘Р С‘ Р С—Р ВµРЎР‚Р ВµРЎРѓР ВµР С”Р В°РЎР‹РЎвЂ°Р С‘РЎвЂ¦РЎРѓРЎРЏ Р В·Р В°Р Р†Р С‘РЎРѓР С‘Р СР С•РЎРѓРЎвЂљР ВµР в„– РЎР‚Р В°РЎРѓРЎвЂ¦Р С•Р Т‘РЎРЏРЎвЂљРЎРѓРЎРЏ РЎРѓР С• РЎРѓРЎвЂљРЎС“Р Т‘Р С‘Р ВµР в„–:" + Environment.NewLine +
            string.Join(Environment.NewLine, problems));
    }

    [Test]
    public void LoadFromProject_KeepsResolverInstalled_AndResolvesPackageAssemblyBySimpleName()
    {
        string directory = MakeTempDir();
        try
        {
            string packageDir = Path.Combine(directory, "packages", "Fake.Activities.1.0.0", "lib", "net472");
            Directory.CreateDirectory(packageDir);
            File.Copy(typeof(PaletteRefreshProbeActivity).Assembly.Location, Path.Combine(packageDir, "Fake.Activities.dll"));

            string csproj = Path.Combine(directory, "Legacy.csproj");
            File.WriteAllText(csproj, LegacyCsproj());
            File.WriteAllText(Path.Combine(directory, "packages.config"), PackagesConfig());

            new NuGetActivityPaletteLoader().LoadFromProject(csproj);

            Assembly resolved = null;
            try
            {
                resolved = Assembly.Load("Fake.Activities");
            }
            catch (Exception)
            {
                // Р С›Р В¶Р С‘Р Т‘Р В°Р ВµРЎвЂљРЎРѓРЎРЏ РЎР‚Р В°Р В·РЎР‚Р ВµРЎв‚¬Р С‘РЎвЂљРЎРЉРЎРѓРЎРЏ Р С—Р С•РЎРѓРЎвЂљР С•РЎРЏР Р…Р Р…РЎвЂ№Р С Р С•Р В±РЎР‚Р В°Р В±Р С•РЎвЂљРЎвЂЎР С‘Р С”Р С•Р С Р С—Р В°Р С”Р ВµРЎвЂљР Р…РЎвЂ№РЎвЂ¦ РЎРѓР В±Р С•РЎР‚Р С•Р С”.
            }

            Assert.That(NuGetActivityPaletteLoader.IsResolverInstalled, Is.True,
                "РЎР‚Р ВµР В·Р С•Р В»Р Р†Р ВµРЎР‚ Р С—Р В°Р С”Р ВµРЎвЂљР Р…РЎвЂ№РЎвЂ¦ РЎРѓР В±Р С•РЎР‚Р С•Р С” РЎРѓР Р…РЎРЏРЎвЂљ Р Т‘Р С• Р С—Р С•РЎРѓРЎвЂљРЎР‚Р С•Р ВµР Р…Р С‘РЎРЏ Р С—Р В°Р В»Р С‘РЎвЂљРЎР‚РЎвЂ№");
            Assert.That(resolved, Is.Not.Null,
                "Assembly.Load(\"Fake.Activities\") Р Р…Р Вµ Р Р…Р В°РЎв‚¬РЎвЂР В» РЎРѓР В±Р С•РЎР‚Р С”РЎС“ Р С—Р В°Р С”Р ВµРЎвЂљР В° Р С—Р С•РЎРѓР В»Р Вµ LoadFromProject");
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    [Test]
    public void WFToolboxHelper_GuardsCategoryAdd()
    {
        string source = ReadSource(@"AiRpaStudio\Helpers\WFToolboxHelper.cs");

        Assert.That(source, Does.Contain("wfToolbox.Categories.Add(controlFlowToolboxCategory);"),
            "Р С”Р В°РЎвЂљР ВµР С–Р С•РЎР‚Р С‘РЎРЏ РЎвЂљРЎС“Р В»Р В±Р С•Р С”РЎРѓР В° Р Т‘Р С•Р В»Р В¶Р Р…Р В° Р Т‘Р С•Р В±Р В°Р Р†Р В»РЎРЏРЎвЂљРЎРЉРЎРѓРЎРЏ Р Р†Р Р…РЎС“РЎвЂљРЎР‚Р С‘ try/catch");
        Assert.That(source, Does.Contain("catch (Exception ex)"),
            "Р Р† WFToolboxHelper Р Р…Р ВµРЎвЂљ try/catch Р Р†Р С•Р С”РЎР‚РЎС“Р С– Р Т‘Р С•Р В±Р В°Р Р†Р В»Р ВµР Р…Р С‘РЎРЏ Р С”Р В°РЎвЂљР ВµР С–Р С•РЎР‚Р С‘Р С‘");
    }

    /// <summary>
    /// Р В Р ВµР С–РЎР‚Р ВµРЎРѓРЎРѓР С‘РЎРЏ Р’В«Р С—РЎС“РЎРѓРЎвЂљР В°РЎРЏ Р С”Р В°РЎвЂљР ВµР С–Р С•РЎР‚Р С‘РЎРЏ <c>Activities.Browser</c>Р’В»: Р В°РЎвЂљРЎР‚Р С‘Р В±РЎС“РЎвЂљРЎвЂ№ Р С‘Р В· Р Т‘Р С‘Р В·Р В°Р в„–Р Р…Р ВµРЎР‚РЎРѓР С”Р С•Р в„– РЎРѓР В±Р С•РЎР‚Р С”Р С‘
    /// <c>Microsoft.Windows.Design.Interaction</c> (<c>[ToolboxBrowsable]</c>, <c>[ToolboxItem]</c>)
    /// РЎвЂЎР С‘РЎвЂљР В°РЎР‹РЎвЂљРЎРѓРЎРЏ Р С—РЎР‚Р С‘ Р В»Р ВµР Р…Р С‘Р Р†Р С•Р в„– Р СР В°РЎвЂљР ВµРЎР‚Р С‘Р В°Р В»Р С‘Р В·Р В°РЎвЂ Р С‘Р С‘ РЎРЊР В»Р ВµР СР ВµР Р…РЎвЂљР С•Р Р† РЎвЂљРЎС“Р В»Р В±Р С•Р С”РЎРѓР В° WF. Р РЋР В°Р СР С•Р в„– РЎРѓР В±Р С•РЎР‚Р С”Р С‘ Р Р…Р ВµРЎвЂљ Р Р…Р С‘ РЎР‚РЎРЏР Т‘Р С•Р С РЎРѓ
    /// Р С—Р В°Р С”Р ВµРЎвЂљР С•Р С (Р Р† <c>.nuspec</c> Р С”Р В»Р В°Р Т‘РЎвЂРЎвЂљРЎРѓРЎРЏ РЎвЂљР С•Р В»РЎРЉР С”Р С• <c>Activities.Browser.dll</c>), Р Р…Р С‘ Р Р† РЎР‚Р В°Р Р…РЎвЂљР В°Р в„–Р СР Вµ
    /// РЎРѓРЎвЂљРЎС“Р Т‘Р С‘Р С‘ РІР‚вЂќ <c>ResolveToolboxItem</c> Р С—Р В°Р Т‘Р В°Р ВµРЎвЂљ РЎРѓ <c>FileNotFoundException</c>, Р С‘ Р С”Р В°РЎвЂљР ВµР С–Р С•РЎР‚Р С‘РЎРЏ
    /// Р С•РЎРѓРЎвЂљР В°РЎвЂРЎвЂљРЎРѓРЎРЏ Р С—РЎС“РЎРѓРЎвЂљР С•Р в„–. Р СџР С•РЎРЊРЎвЂљР С•Р СРЎС“ Р В°Р С”РЎвЂљР С‘Р Р†Р Р…Р С•РЎРѓРЎвЂљРЎРЏР С Р В·Р В°Р С—РЎР‚Р ВµРЎвЂ°Р ВµР Р…РЎвЂ№ <c>[ToolboxBrowsable]</c>,
    /// <c>using Microsoft.Windows.Design;</c> Р С‘ РЎРѓРЎРѓРЎвЂ№Р В»Р С”Р В° Р Р…Р В° РЎРѓР В°Р СРЎС“ РЎРѓР В±Р С•РЎР‚Р С”РЎС“.
    /// </summary>
    [Test]
    public void ActivityProjects_DoNotReferenceWindowsDesignAssembly()
    {
        string root = FindRepositoryRoot();
        var problems = new List<string>();

        foreach (string project in ActivityProjects)
        {
            string directory = Path.Combine(root, project);
            if (!Directory.Exists(directory))
            {
                problems.Add(project + ": Р С”Р В°РЎвЂљР В°Р В»Р С•Р С– Р Р…Р Вµ Р Р…Р В°Р в„–Р Т‘Р ВµР Р…");
                continue;
            }

            CheckSourcesForWindowsDesignAttributes(directory, project, problems);
            CheckCsprojForWindowsDesignReference(directory, project, problems);
        }

        Assert.That(problems, Is.Empty,
            "Р В°Р С”РЎвЂљР С‘Р Р†Р Р…Р С•РЎРѓРЎвЂљР С‘ Р Р…Р Вµ Р Т‘Р С•Р В»Р В¶Р Р…РЎвЂ№ РЎРѓРЎРѓРЎвЂ№Р В»Р В°РЎвЂљРЎРЉРЎРѓРЎРЏ Р Р…Р В° Р Т‘Р С‘Р В·Р В°Р в„–Р Р…Р ВµРЎР‚РЎРѓР С”РЎС“РЎР‹ РЎРѓР В±Р С•РЎР‚Р С”РЎС“ Microsoft.Windows.Design.Interaction:" +
            Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    private static void CheckSourcesForWindowsDesignAttributes(string directory, string project, List<string> problems)
    {
        foreach (string source in Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(source);
            string relative = source.Length > directory.Length + 1
                ? source.Substring(directory.Length + 1)
                : source;

            if (text.Contains("[ToolboxBrowsable"))
            {
                problems.Add(project + ": " + relative + " РЎРѓР С•Р Т‘Р ВµРЎР‚Р В¶Р С‘РЎвЂљ [ToolboxBrowsable]");
            }

            if (text.Contains("using Microsoft.Windows.Design;"))
            {
                problems.Add(project + ": " + relative + " РЎРѓР С•Р Т‘Р ВµРЎР‚Р В¶Р С‘РЎвЂљ using Microsoft.Windows.Design;");
            }
        }
    }

    private static void CheckCsprojForWindowsDesignReference(string directory, string project, List<string> problems)
    {
        string csproj = Path.Combine(directory, project + ".csproj");
        if (!File.Exists(csproj))
        {
            return;
        }

        string text = File.ReadAllText(csproj);
        if (text.Contains("Microsoft.Windows.Design.Interaction"))
        {
            problems.Add(project + ": " + project + ".csproj РЎРѓР С•Р Т‘Р ВµРЎР‚Р В¶Р С‘РЎвЂљ РЎРѓРЎРѓРЎвЂ№Р В»Р С”РЎС“ Р Р…Р В° Microsoft.Windows.Design.Interaction");
        }
    }

    // --- Р СџРЎР‚Р С•Р Р†Р ВµРЎР‚Р С”Р С‘ Р С‘РЎРѓРЎвЂљР С•РЎвЂЎР Р…Р С‘Р С”Р С•Р Р† Р Р†Р ВµРЎР‚РЎРѓР С‘Р в„–. ---

    private static void CheckPackagesConfig(string directory, string project, List<string> problems)
    {
        string packagesConfig = Path.Combine(directory, "packages.config");
        if (!File.Exists(packagesConfig))
        {
            return;
        }

        XDocument document;
        try
        {
            document = XDocument.Load(packagesConfig);
        }
        catch (Exception ex)
        {
            problems.Add(project + ": packages.config Р Р…Р Вµ РЎвЂЎР С‘РЎвЂљР В°Р ВµРЎвЂљРЎРѓРЎРЏ (" + ex.Message + ")");
            return;
        }

        foreach (XElement package in document.Root.Elements("package"))
        {
            CheckVersion(problems, project, (string)package.Attribute("id"), (string)package.Attribute("version"));
        }
    }

    private static void CheckCsprojHintPaths(string directory, string project, List<string> problems)
    {
        string csproj = Path.Combine(directory, project + ".csproj");
        if (!File.Exists(csproj))
        {
            return;
        }

        string text = File.ReadAllText(csproj);
        foreach (Match match in Regex.Matches(text, @"packages\\(?<id>[^\\]+)\.(?<version>[^\\]+)\\lib"))
        {
            CheckVersion(problems, project, match.Groups["id"].Value, match.Groups["version"].Value);
        }
    }

    private static void CheckNuspec(string directory, string project, List<string> problems)
    {
        foreach (string nuspec in Directory.GetFiles(directory, "*.nuspec"))
        {
            XDocument document;
            try
            {
                document = XDocument.Load(nuspec);
            }
            catch (Exception ex)
            {
                problems.Add(project + ": nuspec Р Р…Р Вµ РЎвЂЎР С‘РЎвЂљР В°Р ВµРЎвЂљРЎРѓРЎРЏ (" + ex.Message + ")");
                continue;
            }

            XNamespace ns = document.Root.Name.Namespace;
            foreach (XElement dependency in document.Descendants(ns + "dependency"))
            {
                CheckVersion(problems, project, (string)dependency.Attribute("id"), (string)dependency.Attribute("version"));
            }
        }
    }

    private static void CheckVersion(List<string> problems, string project, string id, string version)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        if (!ActivityCompatibilityContract.PinnedVersions.TryGetValue(id, out string pinned))
        {
            // Р Р€Р Р…Р С‘Р С”Р В°Р В»РЎРЉР Р…Р В°РЎРЏ Р В·Р В°Р Р†Р С‘РЎРѓР С‘Р СР С•РЎРѓРЎвЂљРЎРЉ Р В°Р С”РЎвЂљР С‘Р Р†Р Р…Р С•РЎРѓРЎвЂљР С‘ РІР‚вЂќ Р Р…Р Вµ Р С—Р ВµРЎР‚Р ВµРЎРѓР ВµР С”Р В°Р ВµРЎвЂљРЎРѓРЎРЏ РЎРѓР С• РЎРѓРЎвЂљРЎС“Р Т‘Р С‘Р ВµР в„–, Р Р…Р Вµ Р С—РЎР‚Р С•Р Р†Р ВµРЎР‚РЎРЏР ВµР С.
            return;
        }

        if (!string.Equals(version, pinned, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add(project + ": " + id + " " + (version ?? "<null>") + " Р Р†Р СР ВµРЎРѓРЎвЂљР С• " + pinned);
        }
    }

    // --- Р ВР Р…РЎвЂћРЎР‚Р В°РЎРѓРЎвЂљРЎР‚РЎС“Р С”РЎвЂљРЎС“РЎР‚Р В°. ---

    private static string MakeTempDir()
    {
        string directory = Path.Combine(Path.GetTempPath(), "actdep_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string LegacyCsproj() =>
        "<Project ToolsVersion=\"15.0\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">" +
        "<PropertyGroup><TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion></PropertyGroup></Project>";

    private static string PackagesConfig() =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?><packages>" +
        "<package id=\"Fake.Activities\" version=\"1.0.0\" targetFramework=\"net472\" /></packages>";

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, true);
        }
        catch (Exception)
        {
            // Р вЂ”Р В°Р С–РЎР‚РЎС“Р В¶Р ВµР Р…Р Р…Р В°РЎРЏ DLL Р СР С•Р В¶Р ВµРЎвЂљ Р С•РЎРѓРЎвЂљР В°Р Р†Р В°РЎвЂљРЎРЉРЎРѓРЎРЏ Р В·Р В°Р В±Р В»Р С•Р С”Р С‘РЎР‚Р С•Р Р†Р В°Р Р…Р Р…Р С•Р в„– РІР‚вЂќ Р С”Р В°Р С” Р Р† NuGetPaletteSyncTests.
        }
    }

    private static string ReadSource(string relativePath)
    {
        string path = Path.Combine(FindRepositoryRoot(), relativePath);
        Assert.That(File.Exists(path), Is.True, "Р СњР ВµРЎвЂљ РЎвЂћР В°Р в„–Р В»Р В° " + relativePath);
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AiRpa.sln")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null, "Р Р…Р Вµ Р Р…Р В°Р в„–Р Т‘Р ВµР Р… Р С”Р С•РЎР‚Р ВµР Р…РЎРЉ РЎР‚Р ВµР С—Р С•Р В·Р С‘РЎвЂљР С•РЎР‚Р С‘РЎРЏ Р С—Р С• AiRpa.sln");
        return directory.FullName;
    }
}
