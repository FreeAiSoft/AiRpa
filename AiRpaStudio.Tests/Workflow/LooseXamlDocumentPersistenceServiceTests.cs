using System;
using System.Activities;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RPA.Core.Workflow;

namespace AiRpaStudio.Tests.Workflow
{
    /// <summary>
    /// Тесты loose-XAML-загрузки: <c>Main.xaml</c> со ссылками <c>&lt;local:SubProcessN/&gt;</c>
    /// превращается в <see cref="ActivityBuilder"/>, где каждая ссылка — <see cref="DynamicActivity"/>
    /// с заполненным <c>Name</c>. Без сборки и без MSBuild.
    /// </summary>
    [TestFixture]
    public class LooseXamlDocumentPersistenceServiceTests
    {
        private const string ActivityNs = "http://schemas.microsoft.com/netfx/2009/xaml/activities";
        private const string XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";

        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "airpa_loose_" + Guid.NewGuid().ToString("N"));
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
        public void Load_ResolvesLocalReferenceToDynamicActivity()
        {
            string sub = Write("SubProcess1.xaml", Activity("RPA_1.SubProcess1", "<Sequence />"));
            string main = Write("Main.xaml",
                "<Activity x:Class=\"RPA_1.Main\" xmlns=\"" + ActivityNs + "\" xmlns:x=\"" + XamlNs +
                "\" xmlns:local=\"clr-namespace:RPA_1\">" +
                "<Sequence><local:SubProcess1 /></Sequence>" +
                "</Activity>");

            WorkflowTypeRegistry registry = WorkflowTypeRegistry.Scan(new[] { sub });
            var service = new LooseXamlDocumentPersistenceService(registry);

            object root = service.Load(main);

            Assert.That(root, Is.InstanceOf<ActivityBuilder>(), "loose-загрузка Main.xaml должна вернуть ActivityBuilder");

            ActivityBuilder builder = (ActivityBuilder)root;
            Assert.That(builder.Name, Is.EqualTo("RPA_1.Main"));
            Assert.That(builder.Implementation, Is.Not.Null);

            DynamicActivity facade = WorkflowInspectionServices.GetActivities(builder.Implementation)
                .OfType<DynamicActivity>()
                .SingleOrDefault(a => a.Name == "RPA_1.SubProcess1");

            Assert.That(facade, Is.Not.Null,
                "вложенная ссылка local:SubProcess1 обязана стать DynamicActivity с Name = x:Class");
            Assert.That(facade.DisplayName, Is.EqualTo("SubProcess1"),
                "блок в конструкторе должен подписываться именем класса, а не CLR-типом DynamicActivity");
        }

        [Test]
        public void Load_PopulatesFacadeArguments()
        {
            string sub = Write("SubProcess1.xaml",
                Activity("RPA_1.SubProcess1",
                    "<x:Members>" +
                    "<x:Property Name=\"Input\" Type=\"InArgument(x:String)\" />" +
                    "<x:Property Name=\"Count\" Type=\"InArgument(x:Int32)\" />" +
                    "</x:Members>" +
                    "<Sequence />"));
            string main = Write("Main.xaml",
                "<Activity x:Class=\"RPA_1.Main\" xmlns=\"" + ActivityNs + "\" xmlns:x=\"" + XamlNs +
                "\" xmlns:local=\"clr-namespace:RPA_1\">" +
                "<Sequence><local:SubProcess1 /></Sequence>" +
                "</Activity>");

            WorkflowTypeRegistry registry = WorkflowTypeRegistry.Scan(new[] { sub });
            var service = new LooseXamlDocumentPersistenceService(registry);

            ActivityBuilder builder = (ActivityBuilder)service.Load(main);

            DynamicActivity facade = WorkflowInspectionServices.GetActivities(builder.Implementation)
                .OfType<DynamicActivity>()
                .Single(a => a.Name == "RPA_1.SubProcess1");

            Assert.That(facade.Properties, Has.Count.EqualTo(2));
            Assert.That(facade.Properties["Input"].Type, Is.EqualTo(typeof(string)));
            Assert.That(facade.Properties["Count"].Type, Is.EqualTo(typeof(int)));
        }


        [Test]
        public void Load_ResolvesAllRegisteredReferences()
        {
            string sub1 = Write("SubProcess1.xaml", Activity("RPA_1.SubProcess1", "<Sequence />"));
            string sub2 = Write("SubProcess2.xaml", Activity("RPA_1.SubProcess2", "<Sequence />"));
            string main = Write("Main.xaml",
                "<Activity x:Class=\"RPA_1.Main\" xmlns=\"" + ActivityNs + "\" xmlns:x=\"" + XamlNs +
                "\" xmlns:local=\"clr-namespace:RPA_1\">" +
                "<Sequence><local:SubProcess1 /><local:SubProcess2 /></Sequence>" +
                "</Activity>");

            WorkflowTypeRegistry registry = WorkflowTypeRegistry.Scan(new[] { sub1, sub2 });
            var service = new LooseXamlDocumentPersistenceService(registry);

            ActivityBuilder builder = (ActivityBuilder)service.Load(main);

            string[] names = WorkflowInspectionServices.GetActivities(builder.Implementation)
                .OfType<DynamicActivity>()
                .Select(a => a.Name)
                .OrderBy(n => n)
                .ToArray();

            Assert.That(names, Is.EqualTo(new[] { "RPA_1.SubProcess1", "RPA_1.SubProcess2" }),
                "все зарегистрированные вложенные ссылки обязаны резолвиться");
        }


        [Test]
        public void Flush_RoundTripsLocalReferences()
        {
            string sub1 = Write("SubProcess1.xaml", Activity("RPA_1.SubProcess1", "<Sequence />"));
            string sub2 = Write("SubProcess2.xaml", Activity("RPA_1.SubProcess2", "<Sequence />"));
            string main = Write("Main.xaml",
                "<Activity x:Class=\"RPA_1.Main\" xmlns=\"" + ActivityNs + "\" xmlns:x=\"" + XamlNs +
                "\" xmlns:local=\"clr-namespace:RPA_1\">" +
                "<Sequence><local:SubProcess1 /><local:SubProcess2 /></Sequence>" +
                "</Activity>");

            WorkflowTypeRegistry registry = WorkflowTypeRegistry.Scan(new[] { sub1, sub2 });
            var service = new LooseXamlDocumentPersistenceService(registry);
            ActivityBuilder builder = (ActivityBuilder)service.Load(main);

            string flushed = null;
            var flushingService = new LooseXamlDocumentPersistenceService(registry, text => flushed = text);
            flushingService.Flush(builder);

            Assert.That(flushed, Is.Not.Null);
            Assert.That(flushed, Does.Contain("<local:SubProcess1"));
            Assert.That(flushed, Does.Contain("<local:SubProcess2"));
            Assert.That(flushed, Does.Not.Contain("DynamicActivity"),
                "фасады обязаны сериализоваться обратно в ссылки, а не в DynamicActivity");
            Assert.That(flushed, Does.Not.Contain("DisplayName=\"SubProcess1\""),
                "служебный DisplayName, равный имени класса, не должен попадать в файл");
            Assert.That(flushed, Does.Contain("xmlns:local=\"clr-namespace:RPA_1\""));
        }

        [Test]
        public void Flush_PreservesReferenceViewState()
        {
            string sub = Write("SubProcess1.xaml", Activity("RPA_1.SubProcess1", "<Sequence />"));
            string sap2010 = "http://schemas.microsoft.com/netfx/2010/xaml/activities/presentation";
            string main = Write("Main.xaml",
                "<Activity x:Class=\"RPA_1.Main\" xmlns=\"" + ActivityNs + "\" xmlns:x=\"" + XamlNs +
                "\" xmlns:local=\"clr-namespace:RPA_1\" xmlns:sap2010=\"" + sap2010 + "\">" +
                "<Sequence><local:SubProcess1 sap2010:WorkflowViewState.IdRef=\"SubProcess1_1\" /></Sequence>" +
                "</Activity>");

            WorkflowTypeRegistry registry = WorkflowTypeRegistry.Scan(new[] { sub });
            var service = new LooseXamlDocumentPersistenceService(registry);
            ActivityBuilder builder = (ActivityBuilder)service.Load(main);

            string flushed = null;
            var flushingService = new LooseXamlDocumentPersistenceService(registry, text => flushed = text);
            flushingService.Flush(builder);

            Assert.That(flushed, Does.Contain("sap2010:WorkflowViewState.IdRef=\"SubProcess1_1\""));
            Assert.That(flushed, Does.Contain("<local:SubProcess1"));
            Assert.That(flushed, Does.Not.Contain("DynamicActivity"));
        }

        [Test]
        public void Flush_DoesNotChangeNestedFiles()
        {
            string subContent = Activity("RPA_1.SubProcess1", "<Sequence />");
            string sub = Write("SubProcess1.xaml", subContent);
            string main = Write("Main.xaml",
                "<Activity x:Class=\"RPA_1.Main\" xmlns=\"" + ActivityNs + "\" xmlns:x=\"" + XamlNs +
                "\" xmlns:local=\"clr-namespace:RPA_1\">" +
                "<Sequence><local:SubProcess1 /></Sequence>" +
                "</Activity>");

            WorkflowTypeRegistry registry = WorkflowTypeRegistry.Scan(new[] { sub });
            var service = new LooseXamlDocumentPersistenceService(registry);
            ActivityBuilder builder = (ActivityBuilder)service.Load(main);

            string before = File.ReadAllText(sub);
            var flushingService = new LooseXamlDocumentPersistenceService(registry, text => { });
            flushingService.Flush(builder);

            Assert.That(File.ReadAllText(sub), Is.EqualTo(before),
                "Flush меняет только корневой документ, вложенные файлы остаются нетронутыми");
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
