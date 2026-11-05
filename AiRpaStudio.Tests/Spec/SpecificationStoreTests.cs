using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using RPA.Chat.Services.Spec;

namespace AiRpaStudio.Tests.Spec
{
    /// <summary>
    /// <c>TZ-01</c>: приём ТЗ в проект — версии, дедупликация по sha, дифф, отказ без проекта.
    /// Каждый тест работает в своей временной папке: настоящий проект не трогается.
    /// </summary>
    [TestFixture]
    public class SpecificationStoreTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "spec-store-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, true);
                }
            }
            catch (Exception)
            {
                // Временная папка могла остаться занятой — это не предмет теста.
            }
        }

        private SpecificationStore CreateStore(string root = null)
        {
            var clock = new DateTime(2026, 11, 8, 10, 0, 0, DateTimeKind.Utc);
            return new SpecificationStore(() => root ?? _root, () => clock);
        }

        [Test]
        public void Import_Text_CreatesFirstVersionAndKeepsSource()
        {
            var store = CreateStore();

            var result = store.Import(new SpecImportRequest
            {
                Text = "# ТЗ\n\nЦель: собрать отчёт.",
                Name = "Отчёт.md"
            });

            Assert.Multiple(() =>
            {
                Assert.That(result.Ok, Is.True, result.Reason);
                Assert.That(result.Outcome, Is.EqualTo(SpecImportOutcome.Created));
                Assert.That(result.Version, Is.EqualTo(1));
                Assert.That(result.Sha256, Has.Length.EqualTo(64));
                Assert.That(File.Exists(result.TextPath), Is.True, "spec.md не создан");
                Assert.That(File.Exists(result.VersionPath), Is.True, "версия не создана");
                Assert.That(result.VersionPath, Does.Contain("v0001-"));
                Assert.That(File.Exists(result.SourcePath), Is.True, "исходник не сохранён");
                Assert.That(File.Exists(Path.Combine(result.SpecDirectory, "meta.json")), Is.True);
                Assert.That(File.ReadAllText(result.TextPath), Does.Contain("собрать отчёт"));
            });
        }

        [Test]
        public void Import_SameContent_DoesNotCreateVersion()
        {
            var store = CreateStore();
            string text = "# ТЗ\n\nЦель: один и тот же текст.";

            var first = store.Import(new SpecImportRequest { Text = text, Name = "spec.md" });
            var second = store.Import(new SpecImportRequest { Text = text, Name = "spec.md" });

            int versions = Directory.GetFiles(Path.Combine(first.SpecDirectory, "versions")).Length;

            Assert.Multiple(() =>
            {
                Assert.That(second.Outcome, Is.EqualTo(SpecImportOutcome.Unchanged));
                Assert.That(second.Version, Is.EqualTo(1));
                Assert.That(versions, Is.EqualTo(1), "дедупликация по sha не сработала");
                Assert.That(second.Describe(), Does.Contain("без изменений"));
            });
        }

        [Test]
        public void Import_ChangedText_CreatesVersionWithDiff()
        {
            var store = CreateStore();
            var first = store.Import(new SpecImportRequest { Text = "шаг 1: открыть\nшаг 2: закрыть", Name = "tz.md" });
            var second = store.Import(new SpecImportRequest { Text = "шаг 1: открыть\nшаг 2: закрыть окно", Name = "tz.md" });

            Assert.Multiple(() =>
            {
                Assert.That(second.Outcome, Is.EqualTo(SpecImportOutcome.Updated));
                Assert.That(second.Version, Is.EqualTo(2));
                Assert.That(second.Diff, Does.Contain("изменения относительно предыдущей версии"));
                Assert.That(second.Diff, Does.Contain("- шаг 2: закрыть"));
                Assert.That(second.Diff, Does.Contain("+ шаг 2: закрыть окно"));
                Assert.That(Directory.GetFiles(Path.Combine(first.SpecDirectory, "versions")).Length,
                    Is.EqualTo(2));
            });
        }

        [Test]
        public void Import_NoProject_IsRefusedWithReason()
        {
            var store = CreateStore(string.Empty);

            var result = store.Import(new SpecImportRequest { Text = "текст", Name = "tz.md" });

            Assert.Multiple(() =>
            {
                Assert.That(result.Ok, Is.False);
                Assert.That(result.Outcome, Is.EqualTo(SpecImportOutcome.Failed));
                Assert.That(result.Reason, Does.Contain("проект не открыт"));
                Assert.That(result.Reason, Does.Contain("%AppData%"));
            });
        }

        [Test]
        public void Import_MissingFile_IsRefusedWithReason()
        {
            var store = CreateStore();

            var result = store.Import(new SpecImportRequest { Path = Path.Combine(_root, "нет-такого.md") });

            Assert.Multiple(() =>
            {
                Assert.That(result.Ok, Is.False);
                Assert.That(result.Reason, Does.Contain("не найден"));
            });
        }

        [Test]
        public void Import_LongText_IsTruncatedWithFlag()
        {
            var store = CreateStore();
            string text = new string('я', DocumentTextLimit + 500);

            var result = store.Import(new SpecImportRequest { Text = text, Name = "big.md" });

            Assert.Multiple(() =>
            {
                Assert.That(result.Ok, Is.True, result.Reason);
                Assert.That(result.Truncated, Is.True);
                Assert.That(result.Chars, Is.EqualTo(DocumentTextLimit));
                Assert.That(result.Reason, Does.Contain("усечён"));
            });
        }

        [Test]
        public void Import_CyrillicName_KeepsReadableSlug()
        {
            var store = CreateStore();

            var result = store.Import(new SpecImportRequest { Text = "текст", Name = "ТЗ склада.md" });

            Assert.Multiple(() =>
            {
                Assert.That(result.Ok, Is.True, result.Reason);
                Assert.That(result.Slug, Is.EqualTo("тз-склада"));
                Assert.That(Directory.Exists(result.SpecDirectory), Is.True);
            });
        }

        [Test]
        public void AppendVersion_IncrementsVersionAndShowsDiff()
        {
            var store = CreateStore();
            store.Import(new SpecImportRequest { Text = "версия один", Name = "tz.md" });

            var result = store.AppendVersion("tz", "версия два", "ответ на вопрос Q1");

            Assert.Multiple(() =>
            {
                Assert.That(result.Ok, Is.True, result.Reason);
                Assert.That(result.Version, Is.EqualTo(2));
                Assert.That(result.Diff, Does.Contain("+ версия два"));
                Assert.That(File.ReadAllText(result.TextPath), Is.EqualTo("версия два"));
            });
        }

        [Test]
        public void AppendVersion_WithoutImport_IsRefused()
        {
            var store = CreateStore();

            var result = store.AppendVersion("нет-такого", "текст", null);

            Assert.Multiple(() =>
            {
                Assert.That(result.Ok, Is.False);
                Assert.That(result.Reason, Does.Contain("spec_import"));
            });
        }

        [Test]
        public void SaveAndLoad_RoundTripsModelAndWritesMarkdown()
        {
            var store = CreateStore();
            store.Import(new SpecImportRequest { Text = "текст ТЗ", Name = "tz.md" });

            var specification = ValidSpecification();
            var saved = store.Save(specification);

            Assert.That(saved.Ok, Is.True, saved.Error);

            var loaded = store.TryLoad("tz", out var restored, out string error);

            Assert.Multiple(() =>
            {
                Assert.That(loaded, Is.True, error);
                Assert.That(restored.Goal, Is.EqualTo(specification.Goal));
                Assert.That(restored.Steps.Count, Is.EqualTo(1));
                Assert.That(restored.Steps[0].DataRefs, Is.EqualTo(new[] { "D1" }));
                Assert.That(File.Exists(saved.MarkdownPath), Is.True);
                Assert.That(File.ReadAllText(saved.MarkdownPath), Does.Contain("## Шаги"));
                Assert.That(File.ReadAllText(saved.MarkdownPath),
                    Is.EqualTo(SpecificationText.RenderMarkdown(restored)), "markdown нестабилен между вызовами");
            });
        }

        [Test]
        public void TryLoad_WithoutModel_ExplainsInsteadOfFailing()
        {
            var store = CreateStore();

            bool ok = store.TryLoad("нет-модели", out var specification, out string error);

            Assert.Multiple(() =>
            {
                Assert.That(ok, Is.False);
                Assert.That(specification, Is.Null);
                Assert.That(error, Does.Contain("spec_extract"));
            });
        }

        [Test]
        public void List_ReturnsImportedSpecifications()
        {
            var store = CreateStore();
            store.Import(new SpecImportRequest { Text = "первое", Name = "alpha.md" });
            store.Import(new SpecImportRequest { Text = "второе", Name = "beta.md" });

            var list = store.List();

            Assert.That(list.Select(item => item.Slug), Is.EquivalentTo(new[] { "alpha", "beta" }));
        }

        private const int DocumentTextLimit = 200000;

        private static RpaSpecification ValidSpecification()
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
    }
}
