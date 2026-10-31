using System;
using System.IO;
using System.Xml.Linq;

namespace RPA.VsTemplate
{
    /// <summary>Parses .vstemplate files (Visual Studio project/item templates).</summary>
    public static class VsTemplateParser
    {
        private static readonly XNamespace Ns = "http://schemas.microsoft.com/developer/vstemplate/2005";

        public static VsTemplate Parse(string templatePath)
        {
            if (string.IsNullOrWhiteSpace(templatePath))
            {
                throw new ArgumentException("Template path is required.", nameof(templatePath));
            }

            string fullPath = Path.GetFullPath(templatePath);
            XDocument document = XDocument.Load(fullPath);

            XElement root = document.Root;
            if (root == null || root.Name.LocalName != "VSTemplate")
            {
                throw new InvalidDataException("File is not a valid .vstemplate: missing 'VSTemplate' root element.");
            }

            VsTemplate template = new VsTemplate
            {
                TemplatePath = fullPath,
                TemplateDirectory = Path.GetDirectoryName(fullPath),
                Version = (string)root.Attribute("Version"),
                Type = ParseType((string)root.Attribute("Type"))
            };

            XElement templateData = root.Element(Ns + "TemplateData");
            if (templateData != null)
            {
                template.Name = ReadText(templateData.Element(Ns + "Name"));
                template.DefaultName = ReadText(templateData.Element(Ns + "DefaultName"));
                template.TemplateId = ReadText(templateData.Element(Ns + "TemplateID"));
                template.ProjectType = ReadText(templateData.Element(Ns + "ProjectType"));
                template.RequiredFrameworkVersion = ReadText(templateData.Element(Ns + "RequiredFrameworkVersion"));
            }

            XElement content = root.Element(Ns + "TemplateContent");
            if (content != null)
            {
                ParseContent(content, template);
            }

            return template;
        }

        private static VsTemplateType ParseType(string type)
        {
            return string.Equals(type, "Project", StringComparison.OrdinalIgnoreCase)
                ? VsTemplateType.Project
                : VsTemplateType.Item;
        }

        private static void ParseContent(XElement content, VsTemplate template)
        {
            // Assembly references (item templates).
            XElement references = content.Element(Ns + "References");
            if (references != null)
            {
                foreach (XElement reference in references.Elements(Ns + "Reference"))
                {
                    string assembly = ReadText(reference.Element(Ns + "Assembly"));
                    if (!string.IsNullOrWhiteSpace(assembly))
                    {
                        template.References.Add(assembly);
                    }
                }
            }

            // Project file (project templates).
            XElement project = content.Element(Ns + "Project");
            if (project != null)
            {
                template.Files.Add(new VsTemplateFile
                {
                    SourceFile = (string)project.Attribute("File"),
                    TargetFileName = (string)project.Attribute("TargetFileName"),
                    ReplaceParameters = ReadBool(project.Attribute("ReplaceParameters")),
                    IsProjectFile = true
                });

                foreach (XElement projectItem in project.Elements(Ns + "ProjectItem"))
                {
                    AddProjectItem(projectItem, template, null);
                }

                foreach (XElement folder in project.Elements(Ns + "Folder"))
                {
                    string targetFolder = (string)folder.Attribute("TargetFolderName")
                                          ?? (string)folder.Attribute("Name");

                    foreach (XElement projectItem in folder.Elements(Ns + "ProjectItem"))
                    {
                        AddProjectItem(projectItem, template, targetFolder);
                    }
                }
            }

            // Direct project items (item templates).
            foreach (XElement projectItem in content.Elements(Ns + "ProjectItem"))
            {
                AddProjectItem(projectItem, template, null);
            }
        }

        private static void AddProjectItem(XElement element, VsTemplate template, string targetFolder)
        {
            string source = ReadText(element);
            if (string.IsNullOrWhiteSpace(source))
            {
                return;
            }

            template.Files.Add(new VsTemplateFile
            {
                SourceFile = source.Trim(),
                TargetFileName = (string)element.Attribute("TargetFileName"),
                ReplaceParameters = ReadBool(element.Attribute("ReplaceParameters")),
                OpenInEditor = ReadBool(element.Attribute("OpenInEditor")),
                TargetFolder = targetFolder
            });
        }

        private static string ReadText(XElement element)
        {
            return element == null ? null : (string.IsNullOrWhiteSpace(element.Value) ? null : element.Value.Trim());
        }

        private static bool ReadBool(XAttribute attribute)
        {
            return attribute != null && bool.TryParse(attribute.Value, out bool value) && value;
        }
    }
}
