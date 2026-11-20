using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using RPA.Core.Workflow;
using RPA.Entities;
using RPA.ProjectModel;

namespace AiRpaStudio.Tests.Workflow
{
    /// <summary>
    /// Тесты <see cref="ProjectCodeTypeCatalog"/>: компиляция только code-behind
    /// (C#-файлов проекта) в in-memory сборку, построение карты <c>Type.FullName → Type</c>
    /// и безопасное поведение при отсутствии/ошибках исходников.
    /// </summary>
    [TestFixture]
    public class ProjectCodeTypeCatalogTests
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "airpa_codecat_" + Guid.NewGuid().ToString("N"));
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
        public void Build_CompilesCodeBehindType()
        {
            Write("Code.cs", "namespace RPA_1 { public class Code { public static void Run() { } } }");
            Project project = MakeProject(Compile("Code.cs"));

            ProjectCodeTypeCatalog catalog = ProjectCodeTypeCatalog.Build(project);

            Assert.Multiple(() =>
            {
                Assert.That(catalog.Count, Is.EqualTo(1));
                Assert.That(catalog.TryGetType("RPA_1.Code", out Type code), Is.True);
                Assert.That(code.FullName, Is.EqualTo("RPA_1.Code"));
                Assert.That(catalog.Diagnostics, Is.Empty);
            });
        }

        [Test]
        public void Build_NoCsFiles_ReturnsEmptyCatalog()
        {
            Project project = MakeProject();

            ProjectCodeTypeCatalog catalog = ProjectCodeTypeCatalog.Build(project);

            Assert.Multiple(() =>
            {
                Assert.That(catalog.Count, Is.EqualTo(0));
                Assert.That(catalog.TryGetType("RPA_1.Code", out _), Is.False);
                Assert.That(catalog.Diagnostics, Is.Empty);
            });
        }

        [Test]
        public void Build_SkipsAssemblyInfo()
        {
            Write(
                Path.Combine("Properties", "AssemblyInfo.cs"),
                "using System.Reflection;\r\n" +
                "[assembly: AssemblyTitle(\"x\")]\r\n" +
                "namespace RPA_1 { public class Fake { } }");

            Project project = MakeProject(Compile(Path.Combine("Properties", "AssemblyInfo.cs")));

            ProjectCodeTypeCatalog catalog = ProjectCodeTypeCatalog.Build(project);

            Assert.Multiple(() =>
            {
                Assert.That(catalog.Count, Is.EqualTo(0));
                Assert.That(catalog.TryGetType("RPA_1.Fake", out _), Is.False,
                    "AssemblyInfo.cs не должен компилироваться в каталог code-behind типов");
            });
        }

        [Test]
        public void Build_IgnoresBinAndObj()
        {
            Write(Path.Combine("bin", "Generated.cs"), "namespace RPA_1 { public class FromBin { } }");
            Write(Path.Combine("obj", "Generated.cs"), "namespace RPA_1 { public class FromObj { } }");

            Project project = MakeProject(
                Compile(Path.Combine("bin", "Generated.cs")),
                Compile(Path.Combine("obj", "Generated.cs")));

            ProjectCodeTypeCatalog catalog = ProjectCodeTypeCatalog.Build(project);

            Assert.Multiple(() =>
            {
                Assert.That(catalog.Count, Is.EqualTo(0));
                Assert.That(catalog.TryGetType("RPA_1.FromBin", out _), Is.False);
                Assert.That(catalog.TryGetType("RPA_1.FromObj", out _), Is.False);
            });
        }

        [Test]
        public void Build_CompileError_ReturnsEmptyWithDiagnostics()
        {
            Write("Broken.cs", "namespace RPA_1 { public class Broken { public void M() { int x = ; } } }");
            Project project = MakeProject(Compile("Broken.cs"));

            ProjectCodeTypeCatalog catalog = ProjectCodeTypeCatalog.Build(project);

            Assert.Multiple(() =>
            {
                Assert.That(catalog.Count, Is.EqualTo(0));
                Assert.That(catalog.Diagnostics, Is.Not.Empty,
                    "ошибка компиляции должна остаться в Diagnostics, а не бросаться");
            });
        }

        [Test]
        public void Build_ReferencesHintPath_CompilesCodeUsingReferencedAssembly()
        {
            string xmlLocation = typeof(System.Xml.XmlDocument).Assembly.Location;
            string hintPath = Path.Combine("Refs", "System.Xml.dll");
            string fullHintPath = Path.Combine(_tempDir, hintPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullHintPath));
            File.Copy(xmlLocation, fullHintPath, overwrite: true);

            Write(
                "Code.cs",
                "namespace RPA_1 { public class Code { public static object Create() { return new System.Xml.XmlDocument(); } } }");

            var reference = new VsProjectItem
            {
                ItemType = "Reference",
                Include = "System.Xml, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089",
                HintPath = hintPath
            };

            Project project = MakeProject(Compile("Code.cs"), reference);

            ProjectCodeTypeCatalog catalog = ProjectCodeTypeCatalog.Build(project);

            Assert.Multiple(() =>
            {
                Assert.That(catalog.Count, Is.EqualTo(1),
                    "code-behind, использующий сборку по HintPath, должен компилироваться");
                Assert.That(catalog.TryGetType("RPA_1.Code", out _), Is.True);
                Assert.That(catalog.Diagnostics, Is.Empty,
                    "HintPath-ссылка должна попадать в compilation");
            });
        }

        [Test]
        public void Build_MissingReference_DoesNotThrow()
        {
            Write("Code.cs", "namespace RPA_1 { public class Code { } }");

            var reference = new VsProjectItem
            {
                ItemType = "Reference",
                Include = "NoSuchAssembly, Version=1.0.0.0",
                HintPath = "NoSuchAssembly.dll"
            };

            Project project = MakeProject(Compile("Code.cs"), reference);

            ProjectCodeTypeCatalog catalog = ProjectCodeTypeCatalog.Build(project);

            Assert.Multiple(() =>
            {
                Assert.That(catalog.Count, Is.EqualTo(1),
                    "отсутствующая ссылка не должна мешать компиляции остального code-behind");
                Assert.That(catalog.TryGetType("RPA_1.Code", out _), Is.True);
                Assert.That(catalog.Diagnostics, Is.Empty);
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

        private string Write(string relativePath, string content)
        {
            string path = Path.Combine(_tempDir, relativePath);
            string directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, content);
            return path;
        }
    }
}
