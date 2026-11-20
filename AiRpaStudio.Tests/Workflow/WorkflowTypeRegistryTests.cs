using System;
using System.IO;
using System.Collections.Generic;
using NUnit.Framework;
using RPA.Core.Workflow;
using RPA.Entities;
using RPA.ProjectModel;

namespace AiRpaStudio.Tests.Workflow
{
    /// <summary>
    /// Тесты реестра вложенных XAML-активностей: сканирование только шапки файла,
    /// пропуск недоступных/битых/без-x:Class файлов, lookup по полному имени.
    /// </summary>
    [TestFixture]
    public class WorkflowTypeRegistryTests
    {
        private const string ActivityNs = "http://schemas.microsoft.com/netfx/2009/xaml/activities";
        private const string XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";

        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "airpa_registry_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }

        [Test]
        public void Scan_BuildsMap_FromXamlFiles()
        {
            string a = Write("A.xaml", Xaml("RPA_1.A", null));
            string b = Write("B.xaml", Xaml("RPA_1.B", "<x:Property Name=\"Input\" Type=\"InArgument(x:String)\" />"));

            WorkflowTypeRegistry registry = WorkflowTypeRegistry.Scan(new[] { a, b });

            Assert.That(registry.Count, Is.EqualTo(2));
            Assert.That(registry.TryGet("RPA_1.A", out XamlTypeFacade facadeA), Is.True);
            Assert.That(facadeA.Name, Is.EqualTo("A"));
            Assert.That(registry.TryGet("RPA_1.B", out XamlTypeFacade facadeB), Is.True);
            Assert.That(facadeB.Arguments, Has.Count.EqualTo(1));
        }

        [Test]
        public void Scan_SkipsMissingAndNonXamlFiles()
        {
            string a = Write("A.xaml", Xaml("RPA_1.A", null));
            string missing = Path.Combine(_tempDir, "missing.xaml");
            string txt = Write("note.txt", "not xaml");

            WorkflowTypeRegistry registry = WorkflowTypeRegistry.Scan(new[] { a, missing, txt });

            Assert.That(registry.Count, Is.EqualTo(1));
            Assert.That(registry.TryGet("RPA_1.A", out _), Is.True);
        }

        [Test]
        public void Scan_SkipsFileWithoutClass()
        {
            string noClass = Write("NoClass.xaml", "<Activity xmlns=\"" + ActivityNs + "\" xmlns:x=\"" + XamlNs + "\"><Sequence /></Activity>");

            WorkflowTypeRegistry registry = WorkflowTypeRegistry.Scan(new[] { noClass });

            Assert.That(registry.Count, Is.EqualTo(0));
        }

        [Test]
        public void Scan_FirstDuplicateWins()
        {
            string first = Write("First.xaml", Xaml("RPA_1.Dup", null));
            string second = Write("Second.xaml", Xaml("RPA_1.Dup", "<x:Property Name=\"X\" Type=\"InArgument(x:String)\" />"));

            WorkflowTypeRegistry registry = WorkflowTypeRegistry.Scan(new[] { first, second });

            Assert.That(registry.Count, Is.EqualTo(1));
            Assert.That(registry.TryGet("RPA_1.Dup", out XamlTypeFacade facade), Is.True);
            Assert.That(facade.Arguments, Is.Empty, "первый зарегистрированный x:Class должен побеждать");
            Assert.That(registry.TryGetSourceFile("RPA_1.Dup", out string source), Is.True);
            Assert.That(source, Is.EqualTo(Path.GetFullPath(first)));
        }

        [Test]
        public void Scan_SkipsMalformedXml()
        {
            string bad = Write("Bad.xaml", "<Activity x:Class=\"RPA_1.Bad\"");
            string good = Write("Good.xaml", Xaml("RPA_1.Good", null));

            WorkflowTypeRegistry registry = WorkflowTypeRegistry.Scan(new[] { bad, good });

            Assert.That(registry.Count, Is.EqualTo(1));
            Assert.That(registry.TryGet("RPA_1.Good", out _), Is.True);
            Assert.That(registry.TryGet("RPA_1.Bad", out _), Is.False);
        }

        [Test]
        public void TryGet_UnknownReturnsFalse()
        {
            WorkflowTypeRegistry registry = WorkflowTypeRegistry.Scan(Array.Empty<string>());

            Assert.That(registry.TryGet("RPA_1.Nope", out _), Is.False);
            Assert.That(registry.TryGetSourceFile("RPA_1.Nope", out _), Is.False);
        }

        [Test]
        public void TryGetCodeType_WithCatalog_ResolvesCodeBehindType()
        {
            Write("Code.cs", "namespace RPA_1 { public class Code { } }");
            Project project = MakeProject(Compile("Code.cs"));

            ProjectCodeTypeCatalog catalog = ProjectCodeTypeCatalog.Build(project);
            WorkflowTypeRegistry registry = WorkflowTypeRegistry.Scan(Array.Empty<string>());
            registry.CodeTypes = catalog;

            Assert.Multiple(() =>
            {
                Assert.That(registry.TryGetCodeType("RPA_1.Code", out Type code), Is.True);
                Assert.That(code.FullName, Is.EqualTo("RPA_1.Code"));
            });
        }

        [Test]
        public void TryGetCodeType_WithoutCatalog_ReturnsFalse()
        {
            WorkflowTypeRegistry registry = WorkflowTypeRegistry.Scan(Array.Empty<string>());

            Assert.Multiple(() =>
            {
                Assert.That(registry.TryGetCodeType("RPA_1.Code", out _), Is.False,
                    "реестр без code-каталога не должен резолвить code-behind типы");
                Assert.That(registry.TryGetCodeType(null, out _), Is.False);
            });
        }

        private Project MakeProject(params VsProjectItem[] items)
        {
            var project = new Project { RootNamespace = "RPA_1" };
            project.SetProjectDir(_tempDir);
            project.Items = new List<VsProjectItem>(items);
            return project;
        }

        private static VsProjectItem Compile(string include)
        {
            return new VsProjectItem { ItemType = "Compile", Include = include };
        }

        private string Write(string fileName, string content)
        {
            string path = Path.Combine(_tempDir, fileName);
            File.WriteAllText(path, content);
            return path;
        }

        private static string Xaml(string xClass, string members)
        {
            string membersBlock = members == null ? string.Empty : "<x:Members>" + members + "</x:Members>";

            return "<Activity x:Class=\"" + xClass + "\" xmlns=\"" + ActivityNs + "\" xmlns:x=\"" + XamlNs + "\">" +
                   membersBlock +
                   "<Sequence />" +
                   "</Activity>";
        }
    }
}
