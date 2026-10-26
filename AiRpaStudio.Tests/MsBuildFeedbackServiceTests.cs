using NUnit.Framework;
using RPA.Chat.Services;

namespace RPA.Chat.Tests
{
    [TestFixture]
    public class MsBuildFeedbackServiceTests
    {
        [Test]
        public void ParseBuildErrors_ParsesErrorAndWarning()
        {
            var output =
                "C:\\foo\\Program.cs(12,5): error CS1002: ; expected\r\n" +
                "C:\\foo\\Program.cs(3,1): warning CS0168: The variable 'x' is declared but never used\r\n";

            var errors = MsBuildFeedbackService.ParseBuildErrors(output);

            Assert.That(errors.Count, Is.EqualTo(2));

            Assert.That(errors[0].Code, Is.EqualTo("CS1002"));
            Assert.That(errors[0].Severity, Is.EqualTo("Error"));
            Assert.That(errors[0].File, Is.EqualTo("C:\\foo\\Program.cs"));
            Assert.That(errors[0].Line, Is.EqualTo(12));
            Assert.That(errors[0].Column, Is.EqualTo(5));

            Assert.That(errors[1].Code, Is.EqualTo("CS0168"));
            Assert.That(errors[1].Severity, Is.EqualTo("Warning"));
        }

        [Test]
        public void ParseBuildErrors_IgnoresNonDiagnosticLines()
        {
            var output = "Microsoft (R) Build Engine version 18.0.0.0\r\nBuild succeeded.\r\n";

            var errors = MsBuildFeedbackService.ParseBuildErrors(output);

            Assert.That(errors.Count, Is.EqualTo(0));
        }

        [Test]
        public void HasSolutionConfiguration_AcceptsAnyNonEmptyConfiguration()
        {
            var service = new MsBuildFeedbackService(() => "C:\\foo\\test.sln");
            Assert.That(service.HasSolutionConfigurationAsync("Debug").Result, Is.True);
            Assert.That(service.HasSolutionConfigurationAsync("").Result, Is.False);
        }
    }
}
