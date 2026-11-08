using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RPA.Chat.Services.Workflow;
using RPA.Core.Generation;
using RPA.Core.Workflow;

namespace AiRpaStudio.Tests.Workflow
{
    /// <summary>
    /// AG-01: каталог активностей как данные. Проверяется не «список непустой», а совпадение
    /// с реальными типами WF: категория, направления и типы аргументов, контейнеры для дочерних
    /// активностей и устойчивость поиска к разным написаниям имени.
    /// </summary>
    [TestFixture]
    public class ActivityCatalogTests
    {
        private StudioActivityCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = StudioActivityCatalog.Default;
        }

        [Test]
        public void Catalog_IsBuiltFromRealActivityTypes()
        {
            Assert.That(_catalog.Count, Is.GreaterThan(10));
            Assert.That(_catalog.Categories, Does.Contain("Control Flow"));
            Assert.That(_catalog.Categories, Does.Contain("Primitives"));
        }

        [Test]
        public void Sequence_IsControlFlow_AndContainsActivitiesCollection()
        {
            ActivityInfo sequence = _catalog.Describe("Sequence");

            Assert.That(sequence, Is.Not.Null);
            Assert.That(sequence.TypeName, Is.EqualTo("System.Activities.Statements.Sequence"));
            Assert.That(sequence.Category, Is.EqualTo("Control Flow"));
            Assert.That(sequence.CanContainActivities, Is.True);
            Assert.That(sequence.Containers.Select(container => container.Name), Does.Contain("Activities"));
            Assert.That(sequence.Containers.Single(container => container.Name == "Activities").Kind,
                Is.EqualTo("collection"));
        }

        [Test]
        public void WriteLine_HasTextInArgumentOfTypeString()
        {
            ActivityInfo writeLine = _catalog.Describe("WriteLine");

            Assert.That(writeLine, Is.Not.Null);

            ActivityArgumentInfo text = writeLine.FindArgument("Text");
            Assert.That(text, Is.Not.Null, "у WriteLine обязан быть аргумент Text");
            Assert.That(text.Direction, Is.EqualTo(ActivityArgumentDirection.In));
            Assert.That(text.TypeName, Is.EqualTo("String"));
            Assert.That(writeLine.CanContainActivities, Is.False);
        }

        [Test]
        public void If_HasConditionArgument_AndThenElseContainers()
        {
            ActivityInfo branch = _catalog.Describe("If");

            Assert.That(branch, Is.Not.Null);

            ActivityArgumentInfo condition = branch.FindArgument("Condition");
            Assert.That(condition, Is.Not.Null, "у If обязан быть аргумент Condition");
            Assert.That(condition.Direction, Is.EqualTo(ActivityArgumentDirection.In));
            Assert.That(condition.TypeName, Is.EqualTo("Boolean"));

            string[] containers = branch.Containers.Select(container => container.Name).ToArray();
            Assert.That(containers, Does.Contain("Then"));
            Assert.That(containers, Does.Contain("Else"));
        }

        [Test]
        public void TryCatch_HasTryAndFinallyContainers()
        {
            ActivityInfo tryCatch = _catalog.Describe("TryCatch");

            Assert.That(tryCatch, Is.Not.Null);

            string[] containers = tryCatch.Containers.Select(container => container.Name).ToArray();
            Assert.That(containers, Does.Contain("Try"));
            Assert.That(containers, Does.Contain("Finally"));
        }

        [Test]
        public void GenericActivity_ReportsArity()
        {
            ActivityInfo forEach = _catalog.Describe("ForEach");

            Assert.That(forEach, Is.Not.Null);
            Assert.That(forEach.IsGeneric, Is.True);
            Assert.That(forEach.GenericArity, Is.EqualTo(1));
            Assert.That(forEach.DisplayName, Is.EqualTo("ForEach<T>"));
        }

        [TestCase("Sequence")]
        [TestCase("sequence")]
        [TestCase("  Sequence  ")]
        [TestCase("System.Activities.Statements.Sequence")]
        [TestCase("ForEach<T>")]
        [TestCase("ForEach<>")]
        [TestCase("ForEach`1")]
        [TestCase("System.Activities.Statements.ForEach`1")]
        public void Describe_AcceptsEveryNameSpelling(string name)
        {
            Assert.That(_catalog.Describe(name), Is.Not.Null, "не найдено по имени «" + name + "»");
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("НетТакойАктивности")]
        public void Describe_UnknownName_ReturnsNull(string name)
        {
            Assert.That(_catalog.Describe(name), Is.Null);
        }

        [Test]
        public void Search_BySubstring_FindsActivity()
        {
            IReadOnlyList<ActivityInfo> found = _catalog.Search("seq");

            Assert.That(found.Select(info => info.Key), Does.Contain("Sequence"));
        }

        [Test]
        public void Search_ByCategory_FiltersByReadableName()
        {
            IReadOnlyList<ActivityInfo> found = _catalog.Search(null, "Primitives", 50);

            Assert.That(found, Is.Not.Empty);
            Assert.That(found.Select(info => info.Key), Does.Contain("WriteLine"));
            Assert.That(found.All(info => info.Category == "Primitives"), Is.True,
                "фильтр по категории обязан отсекать чужие активности");
        }

        [Test]
        public void Search_ByCategoryKey_WorksToo()
        {
            IReadOnlyList<ActivityInfo> found = _catalog.Search(null, "lang_toolbox_error_handling", 50);

            Assert.That(found.Select(info => info.Key), Does.Contain("TryCatch"));
        }

        [Test]
        public void Search_EmptyQuery_ReturnsCatalogLimitedByDefault()
        {
            IReadOnlyList<ActivityInfo> found = _catalog.Search(null);

            Assert.That(found.Count, Is.EqualTo(Math.Min(StudioActivityCatalog.DefaultSearchLimit, _catalog.Count)));
        }

        [Test]
        public void Search_RespectsLimit()
        {
            Assert.That(_catalog.Search(null, null, 3).Count, Is.EqualTo(3));
        }

        [Test]
        public void Search_UnknownQuery_ReturnsEmpty()
        {
            Assert.That(_catalog.Search("НетТакойАктивности"), Is.Empty);
        }

        [Test]
        public void Search_MatchesDescriptionAndTypeName()
        {
            Assert.That(_catalog.Search("System.Activities.Statements.WriteLine").Select(info => info.Key),
                Does.Contain("WriteLine"));
        }

        /// <summary>
        /// Каталог обязан покрывать всю панель «Активности»: иначе агент получит отказ на активность,
        /// которую пользователь видит в Toolbox.
        /// </summary>
        [Test]
        public void Catalog_CoversEveryPaletteActivity()
        {
            string[] missing = ActivityPaletteRegistry.Default.All
                .Where(descriptor => _catalog.Describe(descriptor.Key) == null)
                .Select(descriptor => descriptor.Key)
                .ToArray();

            Assert.That(missing, Is.Empty, "в каталоге нет активностей палитры: " + string.Join(", ", missing));
        }

        [Test]
        public void Catalog_IsBuiltFromGivenCategories_SoTestsDoNotDependOnLoadedLibraries()
        {
            var catalog = new StudioActivityCatalog(new[]
            {
                new ActivityPaletteCategory(StudioActivityPalette.ControlFlowCategory, new[] { typeof(System.Activities.Statements.Sequence) }),
                new ActivityPaletteCategory(StudioActivityPalette.PrimitivesCategory, new[] { typeof(System.Activities.Statements.WriteLine) })
            });

            Assert.That(catalog.Count, Is.EqualTo(2));
            Assert.That(catalog.Categories, Is.EqualTo(new[] { "Control Flow", "Primitives" }));
            Assert.That(catalog.Describe("WriteLine").Category, Is.EqualTo("Primitives"));
        }

        [Test]
        public void NullCatalog_IsSafe()
        {
            IActivityCatalog catalog = NullActivityCatalog.Instance;

            Assert.That(catalog.Count, Is.EqualTo(0));
            Assert.That(catalog.Categories, Is.Empty);
            Assert.That(catalog.Search("Sequence"), Is.Empty);
            Assert.That(catalog.Describe("Sequence"), Is.Null);
        }

        /// <summary>
        /// Браузерные активности приходят из того же источника, что и панель «Активности»:
        /// после загрузки сборки каталог обязан видеть их в категории «Browser».
        /// </summary>
        [Test]
        public void BrowserActivities_AreVisible_WhenBrowserAssemblyIsLoaded()
        {
            System.Reflection.Assembly browser = null;
            try
            {
                browser = System.Reflection.Assembly.Load("Activities.Browser");
            }
            catch (Exception)
            {
            }

            Assume.That(browser, Is.Not.Null, "Activities.Browser не загружена в тестовый домен");

            IActivityCatalog catalog = new StudioActivityCatalog(StudioActivityPalette.Categories());

            Assert.Multiple(() =>
            {
                Assert.That(catalog.Describe("StartBrowser"), Is.Not.Null);
                Assert.That(catalog.Describe("GoToUrl"), Is.Not.Null);
                Assert.That(catalog.Describe("ClickWebElement"), Is.Not.Null);
                Assert.That(catalog.Describe("StartBrowser").Category, Is.EqualTo("Browser"));
            });
        }
    }
}
