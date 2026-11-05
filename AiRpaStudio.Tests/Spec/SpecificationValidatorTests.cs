using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RPA.Chat.Services.Spec;

namespace AiRpaStudio.Tests.Spec
{
    /// <summary>
    /// <c>TZ-03</c>/<c>TZ-05</c>/<c>TZ-08</c>: проверки модели ТЗ, декомпозиции и тест-кейсов.
    /// Правило карточки: замечание обязано нести адрес пункта, а «модель не собрана» — не выглядеть как «ошибок нет».
    /// </summary>
    [TestFixture]
    public class SpecificationValidatorTests
    {
        [Test]
        public void Validate_MissingModel_IsNotBuiltState()
        {
            var report = SpecificationValidator.Validate(null);

            Assert.Multiple(() =>
            {
                Assert.That(report.State, Is.EqualTo(SpecValidationState.NotBuilt));
                Assert.That(report.Ready, Is.False);
                Assert.That(report.Describe(), Does.Contain("не собрана"));
                Assert.That(report.Describe(), Does.Contain("не «ошибок нет»"));
            });
        }

        [Test]
        public void Validate_CompleteModel_IsReady()
        {
            var report = SpecificationValidator.Validate(Valid());

            Assert.Multiple(() =>
            {
                Assert.That(report.State, Is.EqualTo(SpecValidationState.Ready), report.Describe());
                Assert.That(report.IssueCount, Is.EqualTo(0));
                Assert.That(report.Describe(), Does.Contain("готова к генерации"));
            });
        }

        [Test]
        public void Validate_EmptyGoal_BlocksWithAddress()
        {
            var spec = Valid();
            spec.Goal = "   ";

            var report = SpecificationValidator.Validate(spec);

            Assert.Multiple(() =>
            {
                Assert.That(report.BlockingCount, Is.EqualTo(1));
                Assert.That(report.Issues.Single().Address, Is.EqualTo("goal"));
            });
        }

        [Test]
        public void Validate_StepWithoutText_Blocks()
        {
            var spec = Valid();
            spec.Steps[0].Text = "";

            var report = SpecificationValidator.Validate(spec);

            Assert.That(report.Issues.Select(issue => issue.Address), Does.Contain("steps[0].text"));
            Assert.That(report.Issues.Single(issue => issue.Address == "steps[0].text").Blocking, Is.True);
        }

        [Test]
        public void Validate_StepWithoutExpected_IsIssueWithoutBlocking()
        {
            var spec = Valid();
            spec.Steps[0].Expected = "";

            var report = SpecificationValidator.Validate(spec);

            var issue = report.Issues.Single(item => item.Address == "steps[0].expected");
            Assert.Multiple(() =>
            {
                Assert.That(issue.Blocking, Is.False);
                Assert.That(report.State, Is.EqualTo(SpecValidationState.HasIssues));
            });
        }

        [Test]
        public void Validate_CriterionWithoutCheck_IsReported()
        {
            var spec = Valid();
            spec.AcceptanceCriteria[0].Check = "";

            var report = SpecificationValidator.Validate(spec);

            Assert.That(report.Issues.Select(issue => issue.Address),
                Does.Contain("acceptanceCriteria[0].check"));
        }

        [Test]
        public void Validate_DuplicateIds_Block()
        {
            var spec = Valid();
            spec.Steps.Add(new SpecStep { Id = "S1", Text = "дубль", Expected = "есть" });

            var report = SpecificationValidator.Validate(spec);

            Assert.That(report.Issues.Any(issue =>
                issue.Blocking && issue.Message.Contains("повторяющийся идентификатор")), Is.True,
                report.Describe());
        }

        [Test]
        public void Validate_DanglingDataReference_IsReported()
        {
            var spec = Valid();
            spec.Steps[0].DataRefs = new List<string> { "D9" };

            var report = SpecificationValidator.Validate(spec);

            Assert.That(report.Issues.Select(issue => issue.Address), Does.Contain("steps[0].dataRefs"));
        }

        [Test]
        public void Validate_OpenAmbiguity_IsListedAsNote()
        {
            var spec = Valid();
            spec.Ambiguities.Add(new SpecAmbiguity
            {
                Id = "Q1",
                ItemId = "S1",
                Question = "Какой файл открывать?",
                Status = SpecAmbiguityStatus.Open
            });

            var report = SpecificationValidator.Validate(spec);

            Assert.Multiple(() =>
            {
                Assert.That(report.State, Is.EqualTo(SpecValidationState.HasIssues));
                Assert.That(report.BlockingCount, Is.EqualTo(0));
                Assert.That(report.Describe(), Does.Contain("Какой файл открывать?"));
            });
        }

        [Test]
        public void ValidateDecomposition_NoTasks_Blocks()
        {
            var report = SpecificationValidator.ValidateDecomposition(Valid());

            Assert.Multiple(() =>
            {
                Assert.That(report.BlockingCount, Is.EqualTo(1));
                Assert.That(report.Issues.Single().Address, Is.EqualTo("tasks"));
            });
        }

        [Test]
        public void ValidateDecomposition_CoveredStepsAndCriteria_IsReady()
        {
            var spec = Covered();

            var report = SpecificationValidator.ValidateDecomposition(spec);

            Assert.That(report.State, Is.EqualTo(SpecValidationState.Ready), report.Describe());
        }

        [Test]
        public void ValidateDecomposition_UncoveredStep_BlocksWithAddress()
        {
            var spec = Covered();
            spec.Tasks[0].SpecRefs = new List<string> { "A1" };

            var report = SpecificationValidator.ValidateDecomposition(spec);

            Assert.Multiple(() =>
            {
                Assert.That(report.BlockingCount, Is.GreaterThanOrEqualTo(1));
                Assert.That(report.Issues.Select(issue => issue.Address), Does.Contain("steps[S1]"));
                Assert.That(report.Describe(), Does.Contain("не покрыт"));
            });
        }

        [Test]
        public void ValidateDecomposition_UncoveredCriterion_Blocks()
        {
            var spec = Covered();
            spec.Tasks[0].SpecRefs = new List<string> { "S1" };

            var report = SpecificationValidator.ValidateDecomposition(spec);

            Assert.That(report.Issues.Select(issue => issue.Address), Does.Contain("acceptanceCriteria[A1]"));
        }

        [Test]
        public void ValidateDecomposition_UnknownReference_Blocks()
        {
            var spec = Covered();
            spec.Tasks[0].SpecRefs = new List<string> { "S1", "A1", "S99" };

            var report = SpecificationValidator.ValidateDecomposition(spec);

            Assert.That(report.Issues.Any(issue =>
                issue.Blocking && issue.Message.Contains("S99")), Is.True, report.Describe());
        }

        [Test]
        public void ValidateDecomposition_TaskWithoutSteps_IsReported()
        {
            var spec = Covered();
            spec.Tasks[0].Steps.Clear();

            var report = SpecificationValidator.ValidateDecomposition(spec);

            Assert.That(report.Issues.Select(issue => issue.Address), Does.Contain("tasks[0].steps"));
        }

        [Test]
        public void ValidateTestCases_MissingCases_Blocks()
        {
            var report = SpecificationValidator.ValidateTestCases(Covered());

            Assert.Multiple(() =>
            {
                Assert.That(report.BlockingCount, Is.GreaterThanOrEqualTo(1));
                Assert.That(report.Issues.Select(issue => issue.Address), Does.Contain("testCases"));
            });
        }

        [Test]
        public void ValidateTestCases_CriterionWithoutCase_IsNamed()
        {
            var spec = Covered();
            spec.TestCases.Add(new SpecTestCase
            {
                Id = "C1",
                CriterionId = "A1",
                ExpectedOutputs = new Dictionary<string, string> { { "Result", "OK" } }
            });
            spec.AcceptanceCriteria.Add(new SpecCriterion { Id = "A2", Text = "Второй", Check = "журнал" });

            var report = SpecificationValidator.ValidateTestCases(spec);

            Assert.That(report.Issues.Select(issue => issue.Address), Does.Contain("acceptanceCriteria[A2]"));
        }

        [Test]
        public void JsonRoundTrip_KeepsEnumsAsStringsAndFields()
        {
            var spec = Covered();
            spec.Tasks[0].Status = SpecTaskStatus.Done;
            spec.Ambiguities.Add(new SpecAmbiguity
            {
                Id = "Q1",
                Question = "?",
                Status = SpecAmbiguityStatus.Deferred
            });

            string json = spec.ToJson();
            var restored = RpaSpecification.FromJson(json);

            Assert.Multiple(() =>
            {
                Assert.That(json, Does.Contain("\"status\": \"done\""));
                Assert.That(json, Does.Contain("\"deferred\""));
                Assert.That(restored.Tasks[0].Status, Is.EqualTo(SpecTaskStatus.Done));
                Assert.That(restored.Ambiguities[0].Status, Is.EqualTo(SpecAmbiguityStatus.Deferred));
                Assert.That(restored.Steps[0].Id, Is.EqualTo("S1"));
                Assert.That(restored.SchemaVersion, Is.EqualTo(RpaSpecification.CurrentSchemaVersion));
            });
        }

        [Test]
        public void Show_UnknownSection_NamesAvailableSections()
        {
            string text = SpecificationText.Show(Covered(), "нет-такого");

            Assert.That(text, Does.Contain("steps"));
            Assert.That(text, Does.Contain("criteria"));
        }

        [Test]
        public void Show_StepsSection_ContainsExpectedResult()
        {
            string text = SpecificationText.Show(Covered(), "steps");

            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Contain("S1"));
                Assert.That(text, Does.Contain("ожидание: Файл открыт"));
            });
        }

        private static RpaSpecification Valid()
        {
            return new RpaSpecification
            {
                Slug = "tz",
                Title = "Отчёт",
                Goal = "Собрать отчёт",
                Steps = new List<SpecStep>
                {
                    new SpecStep
                    {
                        Id = "S1",
                        Text = "Открыть файл",
                        Expected = "Файл открыт",
                        DataRefs = new List<string> { "D1" }
                    }
                },
                Data = new List<SpecData>
                {
                    new SpecData { Id = "D1", Name = "Путь", Type = "string", Source = "ввод" }
                },
                AcceptanceCriteria = new List<SpecCriterion>
                {
                    new SpecCriterion { Id = "A1", Text = "Отчёт создан", Check = "файл существует" }
                }
            };
        }

        private static RpaSpecification Covered()
        {
            var spec = Valid();
            spec.Tasks.Add(new SpecTask
            {
                Id = "T1",
                Title = "Открыть файл и проверить",
                SpecRefs = new List<string> { "S1", "A1" },
                Steps = new List<SpecTaskStep>
                {
                    new SpecTaskStep { ActivityType = "Sequence", Parent = "root" }
                }
            });
            return spec;
        }
    }
}
