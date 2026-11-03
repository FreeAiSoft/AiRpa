using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

using NUnit.Framework;

namespace AiRpaStudio.Tests;

/// <summary>
/// `CP-04` — the toolchain setting must stay consistent across the three places that describe it:
/// `Settings.settings` (designer source), `Settings.Designer.cs` (generated property) and `App.config`
/// (value that ships with the app). Drift between them silently breaks the "pin VS 2022" feature.
/// </summary>
public class MsBuildToolchainSettingTests
{
    private const string SettingName = "MsBuildToolchain";

    [Test]
    public void SettingsFile_DeclaresToolchainWithAutoDefault()
    {
        var document = XDocument.Load(Path.Combine(StudioDirectory, "Properties", "Settings.settings"));
        var setting = FindSetting(document);

        Assert.That(setting, Is.Not.Null, "В Settings.settings нет настройки " + SettingName);
        Assert.That(setting.Attribute("Type")?.Value, Is.EqualTo("System.String"));
        Assert.That(ValueOf(setting), Is.EqualTo("Auto"));
    }

    [Test]
    public void DesignerClass_ExposesToolchainProperty()
    {
        string designer = File.ReadAllText(Path.Combine(StudioDirectory, "Properties", "Settings.Designer.cs"));

        Assert.That(designer, Does.Contain("public string " + SettingName));
        Assert.That(designer, Does.Contain("DefaultSettingValueAttribute(\"Auto\")"));
    }

    [Test]
    public void AppConfig_ShipsToolchainValueForTheSettingsClass()
    {
        var document = XDocument.Load(Path.Combine(StudioDirectory, "App.config"));
        var setting = FindSetting(document);

        Assert.That(setting, Is.Not.Null, "В App.config нет значения настройки " + SettingName);
        Assert.That(ValueOf(setting), Is.EqualTo("Auto"));
    }

    private static XElement FindSetting(XDocument document)
    {
        return document
            .Descendants()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, "setting", StringComparison.OrdinalIgnoreCase)
                                 && string.Equals(GetName(e), SettingName, StringComparison.Ordinal));
    }

    private static string GetName(XElement element)
    {
        var attribute = element.Attributes()
            .FirstOrDefault(a => string.Equals(a.Name.LocalName, "Name", StringComparison.OrdinalIgnoreCase));
        return attribute?.Value;
    }

    private static string ValueOf(XElement setting)
    {
        var value = setting?.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, "value", StringComparison.OrdinalIgnoreCase));
        return value?.Value.Trim();
    }

    private static string StudioDirectory
    {
        get
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null)
            {
                string candidate = Path.Combine(dir, "AiRpaStudio", "Properties", "Settings.settings");
                if (File.Exists(candidate))
                    return Path.Combine(dir, "AiRpaStudio");

                dir = Path.GetDirectoryName(dir);
            }

            Assert.Fail("Could not locate the AiRpaStudio source folder.");
            return null;
        }
    }
}
