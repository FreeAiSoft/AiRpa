using System;
using System.Activities.Debugger;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using RPA.Helpers;
using RPA.Logging;
using Activity = System.Activities.Activity;

namespace AiRpaStudio.Tests
{
    /// <summary>
    /// Юнит-тесты <see cref="DebuggerMappingsHelper"/> для методов, не требующих STA-стенда и
    /// настоящего WorkflowDesigner. Сценарии с живым дизайнером покрыты в
    /// <see cref="AiRpaStudio.Tests.Workflow.LooseBreakpointDiagnosticTests"/>.
    /// </summary>
    [TestFixture]
    public class DebuggerMappingsHelperTests
    {
        [Test]
        public void BuildActivityIdToWfElementMap_MapsActivitiesById()
        {
            var sequence = new System.Activities.Statements.Sequence { DisplayName = "Seq" };
            var assign = new System.Activities.Statements.Assign { DisplayName = "Assign" };
            SetActivityId(sequence, 1);
            SetActivityId(assign, 2);

            var mapping = new Dictionary<object, SourceLocation>
            {
                { sequence, new SourceLocation("f.xaml", 1, 1, 1, 5) },
                { assign, new SourceLocation("f.xaml", 2, 1, 2, 5) },
            };

            Dictionary<string, Activity> result = DebuggerMappingsHelper.BuildActivityIdToWfElementMap(mapping);

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result["1"], Is.SameAs(sequence));
            Assert.That(result["2"], Is.SameAs(assign));
        }

        [Test]
        public void BuildActivityIdToWfElementMap_IgnoresNonActivityKeys()
        {
            var sequence = new System.Activities.Statements.Sequence { DisplayName = "Seq" };
            SetActivityId(sequence, 1);

            var mapping = new Dictionary<object, SourceLocation>
            {
                { sequence, new SourceLocation("f.xaml", 1, 1, 1, 5) },
                { new object(), new SourceLocation("f.xaml", 2, 1, 2, 5) },
            };

            Dictionary<string, Activity> result = DebuggerMappingsHelper.BuildActivityIdToWfElementMap(mapping);

            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result["1"], Is.SameAs(sequence));
        }

        [Test]
        public void RegenerateSourceDebuggerMappings_NullDesigner_WritesLog()
        {
            string originalPath = SourceLocationMappingDiagnostics.LogPath;
            string temp = Path.Combine(Path.GetTempPath(), "airpa-slm-" + Guid.NewGuid().ToString("N") + ".log");
            SourceLocationMappingDiagnostics.LogPath = temp;

            try
            {
                SourceLocationMappingDiagnostics.Reset();

                DebuggerMappingsHelper.RegenerateSourceDebuggerMappings(null);

                Assert.That(File.ReadAllText(temp), Does.Contain("designer=<null>"));
            }
            finally
            {
                SourceLocationMappingDiagnostics.LogPath = originalPath;
                TryDeleteFile(temp);
            }
        }

        private static void SetActivityId(Activity activity, int id)
        {
            PropertyInfo property = typeof(Activity).GetProperty(
                "InternalId",
                BindingFlags.Instance | BindingFlags.NonPublic);
            property.SetValue(activity, id);
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
                // временный файл может быть занят — не критично для теста
            }
        }
    }
}
