using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using AiRpaStudio.Tests.Design;
using NUnit.Framework;
using RPA.Core.Workflow;

namespace AiRpaStudio.Tests.Workflow
{
    /// <summary>
    /// Регрессия: при пересохранении loose-XAML (открытие → раскладка конструктора → Flush →
    /// WorkflowDesigner.Save) в активность дописывался второй
    /// <c>sap:VirtualizedContainerService.HintSize</c>, из-за чего конструктор выдавал предупреждение
    /// «'HintSize' property has already been set on 'Assign'».
    /// <para>
    /// Канонический формат студии держит HintSize только в <c>sap2010:ViewStateData</c>, а не прямым
    /// атрибутом/элементом на активностях: иначе штатный загрузчик WF (конвертация ViewState в attached
    /// properties) выставляет HintSize второй раз и получает дубль.
    /// </para>
    /// </summary>
    [TestFixture]
    public class WorkflowSaveHintSizeRegressionTests
    {
        private const string SapNs = "http://schemas.microsoft.com/netfx/2009/xaml/activities/presentation";

        [Test]
        public void Rpa1_DesignerFlush_DoesNotEmitDirectHintSize()
        {
            string rpa1 = FindRpa1();
            string[] xamlFiles = Directory.GetFiles(rpa1, "*.xaml", SearchOption.TopDirectoryOnly);

            LooseFlushReport report = DesignerBenchProxy.Shared.LooseFlush(
                Path.Combine(rpa1, "Main.xaml"),
                xamlFiles);

            Assert.That(report.Error, Is.Null.Or.Empty, report.Description);
            Assert.That(report.FlushedText, Is.Not.Null.And.Not.Empty, report.Description);

            AssertNoDirectHintSize(report.FlushedText);
        }

        [Test]
        public void Rpa1_MainRoundTrip_DoesNotEmitDirectHintSize()
        {
            string rpa1 = FindRpa1();
            string[] xamlFiles = Directory.GetFiles(rpa1, "*.xaml", SearchOption.TopDirectoryOnly);

            WorkflowTypeRegistry registry = WorkflowTypeRegistry.Scan(xamlFiles);
            var service = new LooseXamlDocumentPersistenceService(registry);
            var builder = (System.Activities.ActivityBuilder)service.Load(Path.Combine(rpa1, "Main.xaml"));

            string flushed = null;
            var flushingService = new LooseXamlDocumentPersistenceService(registry, text => flushed = text);
            flushingService.Flush(builder);

            Assert.That(flushed, Is.Not.Null.And.Not.Empty, "Flush обязан вернуть XAML");
            AssertNoDirectHintSize(flushed);
        }

        private static void AssertNoDirectHintSize(string xaml)
        {
            XDocument document = XDocument.Parse(xaml);

            var directHintSize = document.Root.DescendantsAndSelf()
                .Where(e => CountDirectHintSize(e) > 0 && e.Name.LocalName != "ViewStateData")
                .Select(e => e.Name.LocalName)
                .Distinct()
                .ToList();

            Assert.That(directHintSize, Is.Empty,
                "HintSize обязан жить только в sap2010:ViewStateData, а не прямым атрибутом на активностях. " +
                "Найдено на: " + string.Join(", ", directHintSize));

            bool viewStateKeepsHintSize = document.Root.DescendantsAndSelf()
                .Any(e => e.Name.LocalName == "ViewStateData" && CountDirectHintSize(e) > 0);

            Assert.That(viewStateKeepsHintSize, Is.True,
                "размеры активностей обязаны сохраняться в ViewStateData");
        }

        private static int CountDirectHintSize(XElement element)
        {
            int count = element.Attributes()
                .Count(a => a.Name.NamespaceName == SapNs
                    && a.Name.LocalName == "VirtualizedContainerService.HintSize");

            count += element.Elements()
                .Count(e => e.Name.NamespaceName == SapNs
                    && e.Name.LocalName == "VirtualizedContainerService.HintSize");

            return count;
        }

        private static string FindRpa1()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AiRpa.sln")))
            {
                directory = directory.Parent;
            }

            if (directory == null)
            {
                throw new InvalidOperationException("AiRpa.sln не найден — RPA-1 негде искать.");
            }

            string rpa1 = Path.Combine(directory.FullName, "RPA-1");
            Assert.That(Directory.Exists(rpa1), Is.True, "нет эталонного проекта RPA-1: " + rpa1);
            return rpa1;
        }
    }
}
