using NUnit.Framework;
using RPA.Chat.Services;
using RPA.VsTemplate;

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
        public void MsBuildToolchain_DefaultsToAuto_AndCanBePinned()
        {
            var service = new MsBuildFeedbackService(() => "C:\\foo\\test.sln");

            Assert.That(service.MsBuildToolchain, Is.EqualTo("auto"));

            service.MsBuildToolchain = "vs2022";

            Assert.That(service.MsBuildToolchain, Is.EqualTo("vs2022"));
            Assert.That(service.DescribeToolchain(), Does.Contain("MSBuild"));
        }

        [Test]
        public void DescribeToolchain_ResolvesRealMachineMsBuild()
        {
            var service = new MsBuildFeedbackService(() => "C:\\foo\\test.sln");

            var description = service.DescribeToolchain();

            Assert.That(description, Does.Contain("MSBuild"));
            Assert.That(description, Does.Not.Contain("not found"));
        }

        [Test]
        public void BuildAsync_MissingSolutionPath_Throws()
        {
            var service = new MsBuildFeedbackService(() => "C:\\no\\such\\solution.sln");

            Assert.ThrowsAsync<InvalidOperationException>(() => service.BuildAsync("Debug"));
        }

        [Test]
        public void BuildAsync_RealProject_WritesToolchainLineAndSucceeds()
        {
            string output = Path.Combine(Path.GetTempPath(), "chat_build_" + Guid.NewGuid().ToString("N"));
            try
            {
                var template = VsTemplateParser.Parse(Path.Combine(TemplateRoot, "ActivityLibrary2010", "ActivityLibrary.vstemplate"));
                VsTemplateInstantiator.Instantiate(template, new VsTemplateInstantiationRequest
                {
                    ProjectName = "ChatBuildTest",
                    SafeProjectName = "ChatBuildTest",
                    RootNamespace = "ChatBuildTest",
                    TargetFrameworkVersion = "4.8",
                    OutputDirectory = output
                });

                var project = Path.Combine(output, "ChatBuildTest.csproj");
                var service = new MsBuildFeedbackService(() => project);

                service.BuildAsync("Debug").GetAwaiter().GetResult();

                var buildOutput = service.GetOutputWindowTextAsync().GetAwaiter().GetResult();
                Assert.That(buildOutput, Does.StartWith("MSBuild"));
                Assert.That(buildOutput, Does.Contain(service.DescribeToolchain().Split(':')[0]));
                Assert.That(File.Exists(Path.Combine(output, "bin", "Debug", "ChatBuildTest.dll")), Is.True);
            }
            finally
            {
                if (Directory.Exists(output))
                    Directory.Delete(output, true);
            }
        }

        private static string TemplateRoot
        {
            get
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                while (dir != null)
                {
                    string candidate = Path.Combine(dir, "AiRpaStudio", "VSTemplates", "ActivityLibrary2010", "ActivityLibrary.vstemplate");
                    if (File.Exists(candidate))
                        return Path.Combine(dir, "AiRpaStudio", "VSTemplates");

                    dir = Path.GetDirectoryName(dir);
                }

                Assert.Fail("Could not locate the VSTemplates source folder.");
                return null;
            }
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
