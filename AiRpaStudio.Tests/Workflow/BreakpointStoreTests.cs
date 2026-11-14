using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using RPA.Core.Services;

namespace AiRpaStudio.Tests.Workflow
{
    /// <summary>
    /// Тесты <see cref="BreakpointStore"/>: round-trip координат и типа, пустой store,
    /// восстановление из резервной копии и отказ от относительных путей (несохранённые документы).
    /// </summary>
    [TestFixture]
    public class BreakpointStoreTests
    {
        private string _previousRoot;
        private string _tempRoot;

        [SetUp]
        public void SetUp()
        {
            _previousRoot = BreakpointStore.RootOverride;
            _tempRoot = Path.Combine(Path.GetTempPath(), "airpa-bp-" + Guid.NewGuid().ToString("N"));
            BreakpointStore.RootOverride = _tempRoot;
        }

        [TearDown]
        public void TearDown()
        {
            BreakpointStore.RootOverride = _previousRoot;
            TryDeleteDirectory(_tempRoot);
        }

        [Test]
        public void Save_Load_RoundTripsCoordinatesAndType()
        {
            string workflow = Path.Combine(Path.GetTempPath(), "wf-" + Guid.NewGuid().ToString("N"), "Main.xaml");
            var records = new List<BreakpointStore.BreakpointRecord>
            {
                new BreakpointStore.BreakpointRecord { StartLine = 10, StartColumn = 4, EndLine = 12, EndColumn = 8, BreakpointType = 1 },
                new BreakpointStore.BreakpointRecord { StartLine = 20, StartColumn = 2, EndLine = 22, EndColumn = 6, BreakpointType = 3, ActivityId = "1.6", DisplayName = "Sequence" }
            };

            BreakpointStore.Save(workflow, records);
            List<BreakpointStore.BreakpointRecord> loaded = BreakpointStore.Load(workflow);

            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.Count, Is.EqualTo(2));
            Assert.That(loaded[0].StartLine, Is.EqualTo(10));
            Assert.That(loaded[0].BreakpointType, Is.EqualTo(1));
            Assert.That(loaded[1].ActivityId, Is.EqualTo("1.6"));
            Assert.That(loaded[1].DisplayName, Is.EqualTo("Sequence"));
        }

        [Test]
        public void Save_EmptyRecords_WritesEmptyStore()
        {
            string workflow = Path.Combine(Path.GetTempPath(), "wf-" + Guid.NewGuid().ToString("N"), "Main.xaml");
            BreakpointStore.Save(workflow, new List<BreakpointStore.BreakpointRecord>());

            Assert.That(BreakpointStore.TryGetStorePath(workflow, out string path), Is.True);
            Assert.That(File.Exists(path), Is.True);

            List<BreakpointStore.BreakpointRecord> loaded = BreakpointStore.Load(workflow);
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.Count, Is.EqualTo(0));
        }

        [Test]
        public void Load_FallsBackToBackup_WhenMainFileIsCorrupted()
        {
            string workflow = Path.Combine(Path.GetTempPath(), "wf-" + Guid.NewGuid().ToString("N"), "Main.xaml");
            var v1 = new List<BreakpointStore.BreakpointRecord>
            {
                new BreakpointStore.BreakpointRecord { StartLine = 7, StartColumn = 3, EndLine = 7, EndColumn = 5, BreakpointType = 3 }
            };
            var v2 = new List<BreakpointStore.BreakpointRecord>
            {
                new BreakpointStore.BreakpointRecord { StartLine = 99, StartColumn = 1, EndLine = 99, EndColumn = 2, BreakpointType = 3 }
            };

            BreakpointStore.Save(workflow, v1);
            BreakpointStore.Save(workflow, v2); // main=v2, bak=v1

            Assert.That(BreakpointStore.TryGetStorePath(workflow, out string path), Is.True);
            Assert.That(File.Exists(path + ".bak"), Is.True);
            File.WriteAllText(path, "{ not valid json");

            List<BreakpointStore.BreakpointRecord> loaded = BreakpointStore.Load(workflow);
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.Count, Is.EqualTo(1));
            Assert.That(loaded[0].StartLine, Is.EqualTo(7));
        }

        [Test]
        public void TryGetStorePath_RejectsRelativePath()
        {
            Assert.That(BreakpointStore.TryGetStorePath("Activity1.xaml", out string _), Is.False);
            Assert.That(BreakpointStore.TryGetStorePath("", out string _), Is.False);
            Assert.That(BreakpointStore.TryGetStorePath(null, out string _), Is.False);
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch (Exception)
            {
                // временный каталог может быть занят — не критично для теста
            }
        }
    }
}
