using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

using NUnit.Framework;

namespace AiRpaStudio.Tests;

/// <summary>
/// Ф1 — the start-script setting must stay consistent across the three places that describe it:
/// `Settings.settings` (designer source), `Settings.Designer.cs` (generated property) and `App.config`
/// (value that ships with the app). Drift between them silently breaks the "Стартовый скрипт" feature.
/// </summary>
public class DefaultRunScriptSettingTests
{
    private const string SettingName = "DefaultRunScript";

    [Test]
    public void SettingsFile_DeclaresDefaultRunScriptWithMainXamlDefault()
    {
        var document = XDocument.Load(Path.Combine(StudioDirectory, "Properties", "Settings.settings"));
        var setting = FindSetting(document);

        Assert.That(setting, Is.Not.Null, "В Settings.settings нет настройки " + SettingName);
        Assert.That(setting.Attribute("Type")?.Value, Is.EqualTo("System.String"));
        Assert.That(ValueOf(setting), Is.EqualTo("Main.xaml"));
    }

    [Test]
    public void DesignerClass_ExposesDefaultRunScriptProperty()
    {
        string designer = File.ReadAllText(Path.Combine(StudioDirectory, "Properties", "Settings.Designer.cs"));

        Assert.That(designer, Does.Contain("public string " + SettingName));
        Assert.That(designer, Does.Contain("DefaultSettingValueAttribute(\"Main.xaml\")"));
    }

    [Test]
    public void AppConfig_ShipsDefaultRunScriptValueForTheSettingsClass()
    {
        var document = XDocument.Load(Path.Combine(StudioDirectory, "App.config"));
        var setting = FindSetting(document);

        Assert.That(setting, Is.Not.Null, "В App.config нет значения настройки " + SettingName);
        Assert.That(ValueOf(setting), Is.EqualTo("Main.xaml"));
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
