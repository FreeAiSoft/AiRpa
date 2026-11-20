using System;
using System.Activities;
using System.Collections.Generic;
using System.IO;
using System.Xaml;
using System.Xaml.Schema;
using NUnit.Framework;
using RPA.Core.Workflow;
using RPA.Entities;
using RPA.ProjectModel;

namespace AiRpaStudio.Tests.Workflow
{
    /// <summary>
    /// Тесты <see cref="LooseActivityXamlSchemaContext"/>: резолв code-behind CLR-типов
    /// (<c>local:Code</c>) в настоящий <see cref="Type"/> при сохранении приоритета XAML-фасадов
    /// (<c>local:SubProcessN</c> → <see cref="DynamicActivity"/>).
    /// </summary>
    [TestFixture]
    public class LooseActivityXamlSchemaContextTests
    {
        private const string ActivityNs = "http://schemas.microsoft.com/netfx/2009/xaml/activities";
        private const string XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";
        private const string LocalNs = "clr-namespace:RPA_1";

        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "airpa_schema_" + Guid.NewGuid().ToString("N"));
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
        public void GetXamlType_ResolvesCodeBehindType()
        {
            Write("Code.cs", "namespace RPA_1 { public class Code { } }");
            Project project = MakeProject(Compile("Code.cs"));

            WorkflowTypeRegistry registry = WorkflowTypeRegistry.Scan(Array.Empty<string>());
            registry.CodeTypes = ProjectCodeTypeCatalog.Build(project);

            var schema = new LooseActivityXamlSchemaContext(registry);

            XamlType xamlType = schema.GetXamlType(new XamlTypeName(LocalNs, "Code"));

            Assert.Multiple(() =>
            {
                Assert.That(xamlType, Is.Not.Null);
                Assert.That(xamlType.UnderlyingType, Is.Not.Null);
                Assert.That(xamlType.UnderlyingType.FullName, Is.EqualTo("RPA_1.Code"),
                    "local:Code обязан резолвиться в реальный System.Type из in-memory сборки");
            });
        }

        [Test]
        public void GetXamlType_FacadeHasPriorityOverCodeType()
        {
            string sub = Write("SubProcess1.xaml", Activity("RPA_1.SubProcess1", "<Sequence />"));
            Write("Code.cs", "namespace RPA_1 { public class SubProcess1 { } }");
            Project project = MakeProject(Compile("Code.cs"));

            WorkflowTypeRegistry registry = WorkflowTypeRegistry.Scan(new[] { sub });
            registry.CodeTypes = ProjectCodeTypeCatalog.Build(project);

            var schema = new LooseActivityXamlSchemaContext(registry);

            XamlType xamlType = schema.GetXamlType(new XamlTypeName(LocalNs, "SubProcess1"));

            Assert.Multiple(() =>
            {
                Assert.That(xamlType, Is.InstanceOf<LooseActivityXamlType>(),
                    "совпадение имени с XAML-фасадом должно побеждать code-behind тип");
                Assert.That(xamlType.UnderlyingType, Is.EqualTo(typeof(DynamicActivity)),
                    "вложенный процесс обязан остаться DynamicActivity-фасадом, а не CLR-типом");
            });
        }

        [Test]
        public void GetXamlType_UnknownType_DelegatesToBase()
        {
            WorkflowTypeRegistry registry = WorkflowTypeRegistry.Scan(Array.Empty<string>());
            var schema = new LooseActivityXamlSchemaContext(registry);

            XamlType xamlType = schema.GetXamlType(new XamlTypeName(ActivityNs, "NoSuchActivity"));

            Assert.That(xamlType, Is.Null,
                "неизвестный тип обязан уходить в base.GetXamlType и не перехватываться code-каталогом");
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

        private static string Activity(string xClass, string body)
        {
            return "<Activity x:Class=\"" + xClass + "\" xmlns=\"" + ActivityNs + "\" xmlns:x=\"" + XamlNs + "\">" +
                   body +
                   "</Activity>";
        }
    }
}
