using System;
using System.Activities;
using System.Activities.Debugger;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AiRpaStudio.Tests.Design;
using NUnit.Framework;
using RPA.Core;
using RPA.Core.Workflow;
using RPA.Helpers;

namespace AiRpaStudio.Tests.Workflow
{
    /// <summary>
    /// Диагностика Ф2: сравнение координат <see cref="SourceLocation"/> дизайнерского (loose
    /// <c>CollectMapping</c>) и рантаймного (<c>DebugSymbol</c> собранной сборки) маппингов для одного
    /// и того же файла. <c>DebuggerService.CurrentLocation</c> резолвится по точному
    /// <see cref="SourceLocation.Equals"/>, который учитывает <c>StartColumn</c>/<c>EndColumn</c>,
    /// поэтому расхождение колонок объясняет исчезновение жёлтой рамки при работающих точках останова
    /// (те сравниваются только по <c>StartLine</c>/<c>EndLine</c>).
    /// </summary>
    [TestFixture]
    public class RuntimeVsDesignerLocationDiagTests
    {
        [Test]
        public void Compare_MainXaml_RuntimeVsDesignerCoordinates()
        {
            string rpa1 = FindRpa1();
            string csproj = Path.Combine(rpa1, "RPA-1.csproj");
            string mainXaml = Path.Combine(rpa1, "Main.xaml");

            Dictionary<string, SourceLocation> designerById = DesignerTestHost.Shared.Invoke(() =>
            {
                var registry = WorkflowTypeRegistry.Scan(Directory.GetFiles(rpa1, "*.xaml", SearchOption.TopDirectoryOnly));
                var designer = new WorkflowDesignerEx();
                WfDesignerEnvironment.Configure(designer);
                LooseXamlDesignerLoader.Open(designer, mainXaml, registry);
                DebuggerMappingsHelper.RegenerateSourceDebuggerMappings(designer);
                return ById(designer.DesignerSourceLocationMapping);
            });

            Activity activity = ActivityRunner.TryLoadFromBuiltAssembly(csproj, mainXaml, out string reason);
            Assert.That(activity, Is.Not.Null, reason);
            Dictionary<string, SourceLocation> runtimeById = ById(RuntimeSourceLocationMap.Build(activity));

            Console.WriteLine("=== RUNTIME vs DESIGNER (Main.xaml) ===");
            Console.WriteLine("designerCount=" + designerById.Count + " runtimeCount=" + runtimeById.Count);

            var ids = designerById.Keys.Union(runtimeById.Keys)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();

            foreach (string id in ids)
            {
                designerById.TryGetValue(id, out SourceLocation d);
                runtimeById.TryGetValue(id, out SourceLocation r);

                Console.WriteLine(
                    "Id=" + id
                    + " designer=" + Fmt(d)
                    + " runtime=" + Fmt(r)
                    + " equal=" + SameLocation(d, r));
            }
        }

        private static Dictionary<string, SourceLocation> ById(Dictionary<object, SourceLocation> mapping)
        {
            var result = new Dictionary<string, SourceLocation>();
            foreach (KeyValuePair<object, SourceLocation> entry in mapping)
            {
                if (entry.Key is Activity a && !string.IsNullOrEmpty(a.Id))
                {
                    result[a.Id] = entry.Value;
                }
            }
            return result;
        }

        private static string Fmt(SourceLocation l)
        {
            if (l == null)
            {
                return "<null>";
            }

            return Path.GetFileName(l.FileName) + " L" + l.StartLine + "C" + l.StartColumn
                + "-L" + l.EndLine + "C" + l.EndColumn;
        }

        private static bool SameLocation(SourceLocation a, SourceLocation b)
        {
            if (a == null || b == null)
            {
                return false;
            }

            return a.StartLine == b.StartLine
                && a.StartColumn == b.StartColumn
                && a.EndLine == b.EndLine
                && a.EndColumn == b.EndColumn
                && string.Equals(Path.GetFileName(a.FileName), Path.GetFileName(b.FileName), StringComparison.OrdinalIgnoreCase);
        }

        private static string FindRpa1()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AiRpa.sln")))
            {
                directory = directory.Parent;
            }

            Assert.That(directory, Is.Not.Null, "AiRpa.sln не найден");
            string rpa1 = Path.Combine(directory.FullName, "RPA-1");
            Assert.That(Directory.Exists(rpa1), Is.True, "нет эталонного RPA-1: " + rpa1);
            return rpa1;
        }
    }
}
