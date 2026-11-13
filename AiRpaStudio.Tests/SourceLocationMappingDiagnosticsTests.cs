using System;
using System.Activities.Debugger;
using System.IO;
using NUnit.Framework;
using RPA.Logging;

namespace AiRpaStudio.Tests
{
    /// <summary>
    /// Юнит-тесты файловой диагностики SourceLocationMapping. Диагностика не должна падать сама
    /// и обязана оставлять машиночитаемый след: по нему разбирается хрупкий дефект «точка останова
    /// не ставится» (пустой/перезаписанный SourceLocationMapping в loose-пути открытия).
    /// </summary>
    public class SourceLocationMappingDiagnosticsTests
    {
        private string _originalPath;
        private bool _originalEnabled;

        [SetUp]
        public void SetUp()
        {
            _originalPath = SourceLocationMappingDiagnostics.LogPath;
            _originalEnabled = SourceLocationMappingDiagnostics.Enabled;

            SourceLocationMappingDiagnostics.LogPath = Path.Combine(
                Path.GetTempPath(),
                "airpa-slm-test-" + Guid.NewGuid().ToString("N") + ".log");
            SourceLocationMappingDiagnostics.Enabled = true;
            SourceLocationMappingDiagnostics.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (File.Exists(SourceLocationMappingDiagnostics.LogPath))
                {
                    File.Delete(SourceLocationMappingDiagnostics.LogPath);
                }
            }
            finally
            {
                SourceLocationMappingDiagnostics.LogPath = _originalPath;
                SourceLocationMappingDiagnostics.Enabled = _originalEnabled;
            }
        }

        [Test]
        public void Write_AppendsTimestampedLineToFile()
        {
            SourceLocationMappingDiagnostics.Write("hello-mapping");

            string text = File.ReadAllText(SourceLocationMappingDiagnostics.LogPath);

            Assert.That(text, Does.Contain("hello-mapping"));
            Assert.That(text, Does.Match(@"\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}\] hello-mapping"));
        }

        [Test]
        public void Reset_ClearsFile()
        {
            SourceLocationMappingDiagnostics.Write("first");

            SourceLocationMappingDiagnostics.Reset();

            Assert.That(File.ReadAllText(SourceLocationMappingDiagnostics.LogPath), Is.EqualTo(string.Empty));
        }

        [Test]
        public void Disabled_DoesNotWrite()
        {
            SourceLocationMappingDiagnostics.Enabled = false;

            SourceLocationMappingDiagnostics.Write("should-not-appear");

            Assert.That(File.ReadAllText(SourceLocationMappingDiagnostics.LogPath), Is.EqualTo(string.Empty));
        }

        [Test]
        public void DescribeSourceLocation_FormatsCoordinates()
        {
            var location = new SourceLocation(@"C:\tmp\Main.xaml", 10, 5, 12, 7);

            string text = SourceLocationMappingDiagnostics.Describe(location);

            Assert.That(text, Does.Contain("MAIN.XAML"));
            Assert.That(text, Does.Contain("L10C5-L12C7"));
        }

        [Test]
        public void DescribeActivity_FormatsTypeAndDisplayName()
        {
            var sequence = new System.Activities.Statements.Sequence { DisplayName = "Seq" };

            string text = SourceLocationMappingDiagnostics.Describe(sequence);

            Assert.That(text, Does.Contain("Sequence"));
            Assert.That(text, Does.Contain("DisplayName=Seq"));
        }

        [Test]
        public void DescribeSourceLocation_Null_ReturnsMarker()
        {
            Assert.That(
                SourceLocationMappingDiagnostics.Describe((SourceLocation)null),
                Is.EqualTo("<null>"));
        }

        [Test]
        public void DescribeActivity_Null_ReturnsMarker()
        {
            Assert.That(
                SourceLocationMappingDiagnostics.Describe((System.Activities.Activity)null),
                Is.EqualTo("<null>"));
        }

        [Test]
        public void WriteException_LogsContextAndStackTrace()
        {
            Exception exception = CaptureException();

            SourceLocationMappingDiagnostics.WriteException(exception, "ctx");

            string text = File.ReadAllText(SourceLocationMappingDiagnostics.LogPath);
            Assert.That(text, Does.Contain("ctx: boom"));
            Assert.That(text, Does.Contain("InvalidOperationException"));
            Assert.That(text, Does.Contain("CaptureException"), "стек должен попадать в лог.");
        }

        [Test]
        public void WriteException_Null_DoesNotWrite()
        {
            SourceLocationMappingDiagnostics.WriteException(null, "ctx");

            Assert.That(File.ReadAllText(SourceLocationMappingDiagnostics.LogPath), Is.EqualTo(string.Empty));
        }

        [Test]
        public void LogPath_BlankValue_ResetsToDefault()
        {
            SourceLocationMappingDiagnostics.LogPath = "   ";

            Assert.That(SourceLocationMappingDiagnostics.LogPath, Does.Contain("airpa-sourcelocation-mapping.log"));
        }

        private static Exception CaptureException()
        {
            try
            {
                throw new InvalidOperationException("boom");
            }
            catch (InvalidOperationException ex)
            {
                return ex;
            }
        }
    }
}
