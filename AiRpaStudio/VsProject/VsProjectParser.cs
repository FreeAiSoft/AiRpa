using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace RPA.VsProject
{
    /// <summary>Parses Visual Studio project (.csproj) files (legacy and SDK-style).</summary>
    public static class VsProjectParser
    {
        public static VsProjectInfo Parse(string projectPath)
        {
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                throw new ArgumentException("Project path is required.", nameof(projectPath));
            }

            string fullPath = Path.GetFullPath(projectPath);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("Project file not found.", fullPath);
            }

            XDocument document = XDocument.Load(fullPath);
            XElement root = document.Root ?? throw new InvalidDataException("Project file is empty.");
            bool isSdkStyle = string.Equals(root.Name.LocalName, "Project", StringComparison.OrdinalIgnoreCase)
                              && (string)root.Attribute("Sdk") != null;

            VsProjectInfo info = new VsProjectInfo
            {
                ProjectPath = fullPath,
                IsSdkStyle = isSdkStyle,
                RootNamespace = FirstElementValue(root, "RootNamespace"),
                AssemblyName = FirstElementValue(root, "AssemblyName"),
                TargetFrameworkVersion = FirstElementValue(root, "TargetFrameworkVersion"),
                TargetFramework = FirstElementValue(root, "TargetFramework"),
                ProjectGuid = FirstElementValue(root, "ProjectGuid"),
                OutputType = FirstElementValue(root, "OutputType")
            };

            foreach (XElement itemGroup in root.Descendants().Where(e => e.Name.LocalName == "ItemGroup"))
            {
                foreach (XElement item in itemGroup.Elements())
                {
                    string itemType = item.Name.LocalName;
                    string include = (string)item.Attribute("Include");
                    if (string.IsNullOrWhiteSpace(include))
                    {
                        include = (string)item.Attribute("Update");
                    }

                    if (string.IsNullOrWhiteSpace(include))
                    {
                        continue;
                    }

                    XElement hintPath = item.Elements().FirstOrDefault(e => e.Name.LocalName == "HintPath");

                    info.Items.Add(new VsProjectItem
                    {
                        ItemType = itemType,
                        Include = include,
                        HintPath = hintPath?.Value
                    });
                }
            }

            info.Name = string.IsNullOrWhiteSpace(info.AssemblyName)
                ? Path.GetFileNameWithoutExtension(fullPath)
                : info.AssemblyName;

            return info;
        }

        private static string FirstElementValue(XElement root, string localName)
        {
            XElement element = root.Descendants().FirstOrDefault(e => e.Name.LocalName == localName);
            return element?.Value.Trim();
        }
    }
}
