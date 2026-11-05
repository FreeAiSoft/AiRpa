using System;
using System.Activities;
using System.Activities.Validation;
using System.Activities.XamlIntegration;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xaml;
using NUnit.Framework;
using RPA.Core.Generation;

namespace AiRpaStudio.Tests.Workflow
{
    /// <summary>
    /// AG-12: каркас фикстур WF-XAML. Один и тот же набор файлов используется негативными проверками
    /// валидации, round-trip и будущими outline/правками, поэтому фикстуры живут в репозитории
    /// (<c>AiRpaStudio.Tests\Fixtures\Workflow</c>), а не собираются строками в каждом тесте.
    /// <para>
    /// Все проверки headless: ни конструктора, ни <c>MessageBox</c>, ни диспетчера — только WF-загрузка,
    /// сериализатор студии (<see cref="WorkflowXamlSerializer"/>) и модель-ридер
    /// (<see cref="WorkflowModelReader"/>).
    /// </para>
    /// </summary>
    [TestFixture]
    public class WorkflowFixtureTests
    {
        private const string ActivitiesStatements = "System.Activities.Statements.";

        private static readonly ActivityXamlServicesSettings LoadSettings = new ActivityXamlServicesSettings
        {
            CompileExpressions = false
        };

        private static readonly string[] ValidFixtures =
        {
            "sequence.xaml",
            "if.xaml",
            "trycatch.xaml",
            "flowchart.xaml",
            "variables.xaml"
        };

        private static IEnumerable<TestCaseData> FixtureRoots()
        {
            yield return new TestCaseData("sequence.xaml", ActivitiesStatements + "Sequence");
            yield return new TestCaseData("if.xaml", ActivitiesStatements + "If");
            yield return new TestCaseData("trycatch.xaml", ActivitiesStatements + "TryCatch");
            yield return new TestCaseData("flowchart.xaml", ActivitiesStatements + "Flowchart");
            yield return new TestCaseData("variables.xaml", ActivitiesStatements + "Sequence");

        }

        [TestCaseSource(nameof(ValidFixtures))]
        public void Fixture_LoadsAsWfActivity(string fileName)
        {
            Activity activity = LoadActivity(FixturePath(fileName));

            Assert.That(activity, Is.Not.Null);
            Assert.That(ActivityTypeNames(activity), Is.Not.Empty, "дерево фикстуры " + fileName + " пустое");
        }

        [TestCaseSource(nameof(FixtureRoots))]
        public void Fixture_HasExpectedRootActivity(string fileName, string expectedRootType)
        {
            Activity activity = LoadActivity(FixturePath(fileName));

            Assert.That(activity, Is.Not.Null);
            Assert.That(RootImplementationTypeName(activity), Is.EqualTo(expectedRootType),
                "корень фикстуры " + fileName + " изменился");
        }

        [TestCaseSource(nameof(ValidFixtures))]
        public void Fixture_IsReadableByStudioModelReader(string fileName)
        {
            WorkflowSnapshot snapshot = WorkflowModelReader.Read(FixturePath(fileName));

            Assert.That(snapshot.Ok, Is.True, string.Join(Environment.NewLine, snapshot.Errors));
            Assert.That(snapshot.ActivityCount, Is.GreaterThan(0));
            Assert.That(snapshot.Root, Is.Not.Null);
        }

        [TestCaseSource(nameof(ValidFixtures))]
        public void Fixture_SurvivesRoundTripThroughStudioSerializer(string fileName)
        {
            string path = FixturePath(fileName);
            string[] before = ActivityTypeNames(LoadActivity(path));

            ActivityBuilder builder = LoadBuilder(path);
            Assert.That(builder, Is.Not.Null, "фикстура " + fileName + " обязана читаться как ActivityBuilder");

            string xaml = WorkflowXamlSerializer.Serialize(WorkflowCanonicalizer.Canonicalize(
                builder,
                new WorkflowCanonicalizationOptions("Fixture", "Main", "Fixture")));

            string temp = Path.Combine(Path.GetTempPath(), "airpa_fixture_" + Guid.NewGuid().ToString("N") + ".xaml");
            File.WriteAllText(temp, xaml, new UTF8Encoding(false));

            try
            {
                string[] after = ActivityTypeNames(LoadActivity(temp));

                Assert.That(after, Is.EqualTo(before),
                    "round-trip фикстуры " + fileName + " изменил дерево активностей" + Environment.NewLine + xaml);
            }
            finally
            {
                File.Delete(temp);
            }
        }

        [Test]
        public void BrokenFixture_LoadsButFailsValidation()
        {
            Activity activity = LoadActivity(FixturePath("sequence-broken.xaml"));

            ValidationResults results = ActivityValidationServices.Validate(activity);

            Assert.That(results.Errors.Count, Is.GreaterThan(0),
                "фикстура sequence-broken.xaml обязана загружаться, но не проходить валидацию WF");
        }

        [Test]
        public void BrokenFixture_IsReadableByModelReader()
        {
            // Битый по валидации XAML — не битый по XML: ридер обязан его прочитать, а не упасть.
            WorkflowSnapshot snapshot = WorkflowModelReader.Read(FixturePath("sequence-broken.xaml"));

            Assert.That(snapshot.FilePath, Is.EqualTo(FixturePath("sequence-broken.xaml")));
            Assert.That(snapshot.Root, Is.Not.Null);
        }

        [Test]
        public void MalformedFixture_ThrowsOnLoad()
        {
            Assert.That(() => LoadActivity(FixturePath("malformed.xaml")), Throws.Exception);
        }

        [Test]
        public void Fixtures_AreUtf8WithoutBom()
        {
            string[] files = Directory.GetFiles(FixturesDirectory, "*.xaml");
            Assert.That(files.Length, Is.GreaterThanOrEqualTo(ValidFixtures.Length + 2));

            foreach (string file in files)
            {
                byte[] bytes = File.ReadAllBytes(file);
                bool hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

                Assert.That(hasBom, Is.False, "BOM запрещён: " + Path.GetFileName(file));
            }
        }

        /// <summary>
        /// Страж каркаса: каталог фикстур на месте. Иначе переименование каталога превратило бы
        /// негативные проверки в «пустой список» вместо явного падения.
        /// </summary>
        [Test]
        public void FixturesDirectory_IsPresent()
        {
            Assert.That(Directory.Exists(FixturesDirectory), Is.True, "нет каталога фикстур: " + FixturesDirectory);
        }

        /// <summary>
        /// Корень файла без <c>x:Class</c> грузится как <see cref="DynamicActivity"/>, а настоящая
        /// активность лежит в <see cref="DynamicActivity.Implementation"/> — сравниваем именно её.
        /// </summary>
        private static string RootImplementationTypeName(Activity activity)
        {
            if (activity == null)
            {
                return null;
            }

            var dynamicActivity = activity as DynamicActivity;
            Activity implementation = dynamicActivity != null ? dynamicActivity.Implementation() : activity;

            return implementation.GetType().FullName;
        }


        private static Activity LoadActivity(string path)
        {
            return ActivityXamlServices.Load(path, LoadSettings);
        }

        private static ActivityBuilder LoadBuilder(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (XmlReader xml = XmlReader.Create(stream))
            using (XamlReader reader = ActivityXamlServices.CreateBuilderReader(new XamlXmlReader(xml)))
            {
                return XamlServices.Load(reader) as ActivityBuilder;
            }
        }

        private static string[] ActivityTypeNames(Activity activity)
        {
            if (activity == null)
            {
                return Array.Empty<string>();
            }

            return WorkflowInspectionServices.GetActivities(activity)
                .Select(item => item.GetType().FullName)
                .ToArray();
        }

        private static string FixturesDirectory =>
            Path.Combine(RepositoryRoot(), "AiRpaStudio.Tests", "Fixtures", "Workflow");

        private static string FixturePath(string fileName) => Path.Combine(FixturesDirectory, fileName);

        private static string RepositoryRoot()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AiRpa.sln")))
            {
                directory = directory.Parent;
            }

            Assert.That(directory, Is.Not.Null, "не найден корень репозитория (AiRpa.sln)");

            return directory.FullName;
        }
    }
}
