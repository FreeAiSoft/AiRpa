using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RPA.Chat.Services;
using RPA.Chat.Services.Workflow;
using RPA.Core.Generation;
using RPA.Core.Workflow;

// «WorkflowEditResult» есть и у моста правок (RPA.Chat), и у правки модели конструктора (RPA.Core.Generation):
// тесты AG-02 проверяют ответ агенту, поэтому имя закреплено за ним.
using WorkflowEditResult = RPA.Chat.Services.Workflow.WorkflowEditResult;

namespace AiRpaStudio.Tests.Workflow
{
    /// <summary>
    /// AG-02: мост «агент ↔ открытый дизайнер» — контракт, отказ «файл не открыт» и текст ответа.
    /// <para>
    /// Всё headless: настоящий конструктор здесь не нужен, потому что проверяется не модель, а поведение
    /// моста на его границах — неоткрытый файл, неактивная пачка, готовый ответ агенту. Живая модель,
    /// <c>ModelEditingScope</c> и <c>Ctrl+Z</c> проверяются отдельно, на стенде конструктора
    /// (<c>WorkflowEditBridgeTests</c>).
    /// </para>
    /// </summary>
    [TestFixture]
    public class WorkflowEditingTests
    {

        // --- Отказ «файл не открыт»: структурированный, с подсказкой. ---

        [Test]
        public void BeginEdit_FileNotOpen_ReturnsInactiveSession()
        {
            IWorkflowEditSession session = CreateService().BeginEdit(FixturePath("sequence.xaml"), "тест");

            Assert.Multiple(() =>
            {
                Assert.That(session.IsActive, Is.False, "пачка правок не может быть открыта без вкладки конструктора");
                Assert.That(session, Is.InstanceOf<UnavailableWorkflowEditSession>());
                Assert.That(((UnavailableWorkflowEditSession)session).Reason, Does.Contain("не открыт"),
                    "агент обязан увидеть причину отказа, а не пустой ответ");
            });
        }

        [Test]
        public void BeginEdit_FileNotOpen_InsertExplainsReason()
        {
            IWorkflowEditSession session = CreateService().BeginEdit(FixturePath("sequence.xaml"), "тест");

            WorkflowEditResult result = session.Insert(new WorkflowInsertRequest { ActivityType = "WriteLine" });

            Assert.Multiple(() =>
            {
                Assert.That(result.Ok, Is.False);
                Assert.That(result.HasErrors, Is.True, "причина отказа приходит замечанием");
                Assert.That(result.SessionActive, Is.False);
                Assert.That(
                    result.Issues.Any(issue => issue.Code == "hint"),
                    Is.True,
                    "ответ обязан подсказывать, что делать: открыть файл вкладкой конструктора");
            });
        }

        [Test]
        public void BeginEdit_FileNotOpen_DeleteMoveCompleteRevertFail()
        {
            IWorkflowEditSession session = CreateService().BeginEdit(FixturePath("sequence.xaml"), "тест");

            Assert.Multiple(() =>
            {
                Assert.That(session.Delete("root/Activities[0]").Ok, Is.False);
                Assert.That(session.Move(new WorkflowMoveRequest { ElementId = "root/Activities[0]" }).Ok, Is.False);
                Assert.That(session.Complete().Ok, Is.False);
                Assert.That(session.Revert().Ok, Is.False);
                Assert.That(session.Outline(), Is.Null, "структуры открытого документа без вкладки нет");
            });
        }

        [Test]
        public void BeginEdit_EmptyPath_ReportsInvalidPath()
        {
            IWorkflowEditSession session = CreateService().BeginEdit("   ", "тест");

            Assert.That(((UnavailableWorkflowEditSession)session).Reason, Does.Contain("путь"));
        }

        [Test]
        public void GetLiveOutline_FileNotOpen_ReturnsNull()
        {
            Assert.That(CreateService().GetLiveOutline(FixturePath("sequence.xaml")), Is.Null,
                "null означает «вкладки нет»: структуру читают с диска, а не считают пустой");
        }

        [Test]
        public void IsOpenInDesigner_FileNotOpen_False()
        {
            var service = CreateService();

            Assert.Multiple(() =>
            {
                Assert.That(service.IsOpenInDesigner(FixturePath("sequence.xaml")), Is.False);
                Assert.That(service.IsOpenInDesigner(null), Is.False);
            });
        }

        [Test]
        public void UndoRedo_FileNotOpen_StructuredFailure()
        {
            var service = CreateService();

            Assert.Multiple(() =>
            {
                Assert.That(service.Undo(FixturePath("sequence.xaml")).Ok, Is.False);
                Assert.That(service.Redo(FixturePath("sequence.xaml")).Ok, Is.False);
                Assert.That(service.Undo(FixturePath("sequence.xaml")).HasErrors, Is.True);
            });
        }

        [Test]
        public void IsOpenInDesigner_TabWithoutDesigner_IsNotEditable()
        {
            // Вкладка без загруженного дизайнера (создана до открытия файла) править нечего:
            // «открыт» означает «есть живая модель», а не «есть запись в панели документов».
            var host = new FakeDesignerHost();
            host.Tabs.Add(new DesignerTab(FixturePath("sequence.xaml"), false));

            var service = new StudioWorkflowEditingService(host);

            Assert.That(service.IsOpenInDesigner(FixturePath("sequence.xaml")), Is.False);
        }

        // --- Null-object: чат без хоста. ---

        [Test]
        public void NullService_BeginEdit_InactiveAndExplains()
        {
            IWorkflowEditSession session = NullWorkflowEditingService.Instance.BeginEdit("C:\\work\\Main.xaml");

            Assert.Multiple(() =>
            {
                Assert.That(session.IsActive, Is.False);
                Assert.That(session.Insert(new WorkflowInsertRequest { ActivityType = "WriteLine" }).Ok, Is.False);
                Assert.That(NullWorkflowEditingService.Instance.GetLiveOutline("C:\\work\\Main.xaml"), Is.Null);
                Assert.That(NullWorkflowEditingService.Instance.IsOpenInDesigner("C:\\work\\Main.xaml"), Is.False);
                Assert.That(NullWorkflowEditingService.Instance.Undo("C:\\work\\Main.xaml").Ok, Is.False);
            });
        }

        [Test]
        public void ChatEnvironment_EditingService_IsConnected()
        {
            Assert.That(ChatEnvironment.WorkflowEditing, Is.Not.Null,
                "чат обязан иметь службу правки (по умолчанию — null-object, а не null)");
        }

        /// <summary>
        /// Страж подключения: без этих строк в студии инструменты правки отвечают «мост не подключён»,
        /// а тесты этого не замечают — они подключают службу сами.
        /// </summary>
        [Test]
        public void StudioHost_ProvidesEditingService()
        {
            string mainView = File.ReadAllText(
                Path.Combine(RepositoryRoot(), "AiRpaStudio", "Views", "MainView.xaml.cs"));

            Assert.Multiple(() =>
            {
                Assert.That(mainView, Does.Contain("ChatEnvironment.WorkflowEditing"),
                    "MainView обязан отдать мост правок в ChatEnvironment");
                Assert.That(mainView, Does.Contain("LiveOutlineSource"),
                    "структура открытого документа обязана подключаться к AG-03 в MainView");
            });
        }

        // --- Запросы: значения по умолчанию. ---

        [Test]
        public void InsertRequest_DefaultParent_IsRoot()
        {
            var request = new WorkflowInsertRequest { ActivityType = "WriteLine" };

            Assert.Multiple(() =>
            {
                Assert.That(request.ParentId, Is.EqualTo("root"), "родитель по умолчанию — корень процесса");
                Assert.That(request.ToString(), Does.Contain("root"));
                Assert.That(request.Index, Is.Null, "индекс не задан — вставка в конец");
            });
        }

        [Test]
        public void InsertRequest_ParentIsTrimmed()
        {
            var request = new WorkflowInsertRequest { ParentId = "  root/Activities[1]  " };

            Assert.That(request.ParentId, Is.EqualTo("root/Activities[1]"));
        }

        [Test]
        public void MoveRequest_DefaultParent_IsRoot_AndDescribesTarget()
        {
            var request = new WorkflowMoveRequest { ElementId = "root/Activities[2]", Container = "Then", Index = 1 };

            Assert.Multiple(() =>
            {
                Assert.That(request.ParentId, Is.EqualTo("root"));
                Assert.That(request.ToString(), Does.Contain("root/Activities[2]"));
                Assert.That(request.ToString(), Does.Contain(".Then#1"));
            });
        }

        // --- Результат правки. ---

        [Test]
        public void Result_Success_IsClean_AndKeepsAddresses()
        {
            WorkflowEditResult result = WorkflowEditResult.Success("insert", "вставлено")
                .WithElements("root", "root/Activities[2]")
                .WithState(sessionActive: true, canUndo: false, canRedo: false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Ok, Is.True);
                Assert.That(result.IsClean, Is.True);
                Assert.That(result.ElementId, Is.EqualTo("root"));
                Assert.That(result.NewElementId, Is.EqualTo("root/Activities[2]"));
                Assert.That(result.SessionActive, Is.True);
                Assert.That(result.ToString(), Does.Contain("вставлено"));
            });
        }

        [Test]
        public void Result_Failure_HasErrorAndHint()
        {
            WorkflowEditResult result = WorkflowEditResult.Failure("delete", "элемент не найден", "возьмите адрес из outline");

            Assert.Multiple(() =>
            {
                Assert.That(result.Ok, Is.False);
                Assert.That(result.IsClean, Is.False);
                Assert.That(result.HasErrors, Is.True);
                Assert.That(result.Issues, Has.Count.EqualTo(2), "ошибка и подсказка — разные замечания");
                Assert.That(result.Issues.Any(issue => issue.IsError), Is.True);
                Assert.That(result.Issues.Any(issue => issue.Code == "hint"), Is.True);
            });
        }

        [Test]
        public void Result_Merge_AddsModelErrorsAndHints()
        {
            WorkflowEditResult result = WorkflowEditResult.Failure("insert", string.Empty)
                .Merge(new[] { "ошибка модели" }, new[] { "подсказка модели" });

            Assert.Multiple(() =>
            {
                Assert.That(result.Issues.Any(issue => issue.Message == "ошибка модели"), Is.True);
                Assert.That(result.Issues.Any(issue => issue.Message == "подсказка модели"), Is.True);
            });
        }

        // --- Текст ответа агенту. ---

        [Test]
        public void EditText_ReportsAppliedEditWithAddressesAndHistory()
        {
            WorkflowEditResult result = WorkflowEditResult.Success("insert", "вставлено: WriteLine")
                .WithElements("root", "root/Activities[3]")
                .WithState(sessionActive: true);

            string text = WorkflowEditText.Describe(result, includeOutline: false);

            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Contain("insert"));
                Assert.That(text, Does.Contain("применена"));
                Assert.That(text, Does.Contain("root/Activities[3]"));
                Assert.That(text, Does.Contain("появился"));
                Assert.That(text, Does.Contain("пачка открыта"), "агент обязан знать, что пачку надо закрыть");
            });
        }

        [Test]
        public void EditText_ReportsRejectedEditWithIssues()
        {
            WorkflowEditResult result = WorkflowEditResult.Failure(
                    "delete",
                    "«root/Activities[99]» не найден в модели конструктора",
                    "адреса берутся из workflow_outline")
                .WithState(sessionActive: false);

            string text = WorkflowEditText.Describe(result, includeOutline: false);

            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Contain("отклонена"));
                Assert.That(text, Does.Contain("root/Activities[99]"));
                Assert.That(text, Does.Contain("workflow_outline"), "подсказка обязана попасть в ответ");
                Assert.That(text, Does.Contain("Замечания"));
            });
        }

        [Test]
        public void EditText_ReportsHistoryAvailability()
        {
            string text = WorkflowEditText.Describe(
                WorkflowEditResult.Success("undo", "последняя правка отменена")
                    .WithState(sessionActive: false, canUndo: false, canRedo: true),
                includeOutline: false);

            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Contain("отмена: нет"));
                Assert.That(text, Does.Contain("возврат: есть"));
            });
        }

        [Test]
        public void EditText_HistoryUnavailable_WhenNothingKnown()
        {
            string text = WorkflowEditText.Describe(
                WorkflowEditResult.Failure("insert", "файл не открыт"),
                includeOutline: false);

            Assert.That(text, Does.Contain("история: недоступна"));
        }

        [Test]
        public void EditText_IncludesLiveOutline()
        {
            WorkflowOutline outline = ReadOutline("sequence.xaml");

            WorkflowEditResult result = WorkflowEditResult.Success("insert", "вставлено")
                .WithOutline(outline);

            string text = WorkflowEditText.Describe(result);

            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Contain("Структура процесса"));
                Assert.That(text, Does.Contain("root/Activities[0]"), "адреса для следующей правки — в том же ответе");
            });
        }

        [Test]
        public void EditText_IssueListIsLimited()
        {
            WorkflowEditResult result = WorkflowEditResult.Success("insert", "вставлено");

            for (int index = 0; index < WorkflowEditText.MaxIssues + 5; index++)
            {
                result.AddWarning("root/Activities[0]", "замечание " + index);
            }

            string text = WorkflowEditText.Describe(result, includeOutline: false);

            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Contain("не показаны"), "длинный список обязан обрезаться");
                Assert.That(text, Does.Not.Contain("замечание " + (WorkflowEditText.MaxIssues + 4)));
            });
        }

        // --- Помощники. ---

        private static StudioWorkflowEditingService CreateService()
        {
            return new StudioWorkflowEditingService(new FakeDesignerHost());
        }

        private static WorkflowOutline ReadOutline(string fixture)
        {
            return StudioWorkflowOutlineService.Default.GetOutline(FixturePath(fixture));
        }

        private static string FixturePath(string fixture)
        {
            return Path.Combine(RepositoryRoot(), "AiRpaStudio.Tests", "Fixtures", "Workflow", fixture);
        }

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

        /// <summary>Среда вкладок без конструктора: студия не запущена, вкладок нет (или они подставлены тестом).</summary>
        private sealed class FakeDesignerHost : IWorkflowDesignerHost
        {
            internal List<DesignerTab> Tabs { get; } = new List<DesignerTab>();

            public IReadOnlyList<DesignerTab> GetOpenTabs()
            {
                return Tabs;
            }

            public DesignerTab ActiveTab => Tabs.FirstOrDefault();

            public DesignerTab Open(string filePath)
            {
                var tab = new DesignerTab(filePath, false);
                Tabs.Add(tab);
                return tab;
            }

            public void Activate(DesignerTab tab)
            {
            }

            public void Close(DesignerTab tab)
            {
                Tabs.Remove(tab);
            }

            public void MarkClean(DesignerTab tab)
            {
                tab.IsEdited = false;
            }
        }
    }
}
