using System;
using System.Activities;
using System.Activities.Debugger;
using System.IO;
using System.Linq;

using NUnit.Framework;
using RPA.Core;
using RPA.Helpers;

namespace AiRpaStudio.Tests.Workflow
{
    /// <summary>
    /// Шаг 6 плана <c>LooseWorkflowRun</c>: рантайм-маппинг <see cref="RuntimeSourceLocationMap.Build"/>
    /// строит <c>Activity → SourceLocation</c> по отладочным символам скомпилированного XAML и покрывает
    /// вложенные процессы, а не только верхний уровень.
    /// </summary>
    [TestFixture]
    public class RuntimeSourceLocationMapTests
    {
        [Test]
        public void Build_BuiltRpa1_CoversMainAndNestedProcesses()
        {
            string rpa1 = FindRpa1();
            string csproj = Path.Combine(rpa1, "RPA-1.csproj");
            string mainXaml = Path.Combine(rpa1, "Main.xaml");

            Assert.That(File.Exists(csproj), Is.True, "нет RPA-1.csproj: " + csproj);
            Assert.That(File.Exists(mainXaml), Is.True, "нет Main.xaml: " + mainXaml);

            Activity activity = ActivityRunner.TryLoadFromBuiltAssembly(csproj, mainXaml, out string reason);

            Assert.That(activity, Is.Not.Null, reason);
            Assert.That(activity.GetType().FullName, Is.EqualTo("RPA_1.Main"),
                "эталон обязан грузиться из собранной RPA-1.dll по x:Class Main.xaml");

            System.Collections.Generic.Dictionary<object, SourceLocation> mapping =
                RuntimeSourceLocationMap.Build(activity);

            Assert.That(mapping, Is.Not.Null);
            Assert.That(mapping.Count, Is.GreaterThan(0),
                "маппинг по DebugSymbol не должен быть пустым для собранного эталона");

            string[] files = mapping.Values
                .Select(location => location?.FileName ?? string.Empty)
                .Select(Path.GetFileName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Assert.That(files.Any(f => string.Equals(f, "Main.xaml", StringComparison.OrdinalIgnoreCase)),
                Is.True, "в маппинге должен быть Main.xaml; есть: " + string.Join(", ", files));
            Assert.That(files.Any(f => string.Equals(f, "SubProcess1.xaml", StringComparison.OrdinalIgnoreCase)),
                Is.True, "в маппинге должен быть SubProcess1.xaml; есть: " + string.Join(", ", files));
            Assert.That(files.Any(f => string.Equals(f, "SubProcess2.xaml", StringComparison.OrdinalIgnoreCase)),
                Is.True, "в маппинге должен быть SubProcess2.xaml (тело вложенного процесса); есть: " + string.Join(", ", files));
            Assert.That(files.Any(f => string.Equals(f, "SubProcess3.xaml", StringComparison.OrdinalIgnoreCase)),
                Is.True, "в маппинге должен быть SubProcess3.xaml (иначе точка останова на его теле не сработает); есть: " + string.Join(", ", files));
        }

        [Test]
        public void Build_NullActivity_ReturnsEmptyMapping()
        {
            System.Collections.Generic.Dictionary<object, SourceLocation> mapping =
                RuntimeSourceLocationMap.Build(null);

            Assert.That(mapping, Is.Not.Null);
            Assert.That(mapping.Count, Is.Zero);
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
                throw new InvalidOperationException("AiRpa.sln не найден.");
            }

            string rpa1 = Path.Combine(directory.FullName, "RPA-1");
            Assert.That(Directory.Exists(rpa1), Is.True, "нет эталонного проекта RPA-1: " + rpa1);
            return rpa1;
        }
    }
}
