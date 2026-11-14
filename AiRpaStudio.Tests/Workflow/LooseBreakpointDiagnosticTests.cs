using System;
using System.Activities;
using System.Activities.Debugger;
using System.Activities.Presentation.Debug;
using System.Activities.Presentation.Hosting;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.View;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AiRpaStudio.Tests.Design;
using NUnit.Framework;
using RPA.Core;
using RPA.Core.Workflow;
using RPA.Core.Services;
using RPA.Helpers;
using RPA.Logging;

namespace AiRpaStudio.Tests.Workflow
{
    [TestFixture]
    public class LooseBreakpointDiagnosticTests
    {
        private string _previousBreakpointRoot;
        private string _tempBreakpointRoot;

        [SetUp]
        public void SetUp()
        {
            _previousBreakpointRoot = BreakpointStore.RootOverride;
            _tempBreakpointRoot = Path.Combine(Path.GetTempPath(), "airpa-bp-" + Guid.NewGuid().ToString("N"));
            BreakpointStore.RootOverride = _tempBreakpointRoot;
        }

        [TearDown]
        public void TearDown()
        {
            BreakpointStore.RootOverride = _previousBreakpointRoot;
            TryDeleteDirectory(_tempBreakpointRoot);
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

        [Test]
        public void RegenerateSourceDebuggerMappings_ProducesNonEmptyMapping()
        {
            string rpa1 = FindRpa1();
            string[] xamlFiles = Directory.GetFiles(rpa1, "*.xaml", SearchOption.TopDirectoryOnly);

            DesignerTestHost.Shared.Invoke(() =>
            {
                var registry = WorkflowTypeRegistry.Scan(xamlFiles);
                var designer = new WorkflowDesignerEx();
                WfDesignerEnvironment.Configure(designer);
                LooseXamlDesignerLoader.Open(designer, Path.Combine(rpa1, "Main.xaml"), registry);

                DebuggerMappingsHelper.RegenerateSourceDebuggerMappings(designer);

                Assert.That(designer.DesignerSourceLocationMapping.Count, Is.GreaterThan(0),
                    "SourceLocation-маппинг должен быть непустым после loose-загрузки.");
                Assert.That(
                    designer.DesignerSourceLocationMapping.Keys.OfType<System.Activities.Statements.Sequence>().Any(),
                    Is.True,
                    "в маппинге должна присутствовать корневая Sequence.");
            });
        }

        [Test]
        public void RegenerateSourceDebuggerMappings_WritesDiagnosticLog()
        {
            string rpa1 = FindRpa1();
            string[] xamlFiles = Directory.GetFiles(rpa1, "*.xaml", SearchOption.TopDirectoryOnly);

            string originalPath = SourceLocationMappingDiagnostics.LogPath;
            string tempLog = Path.Combine(Path.GetTempPath(), "airpa-slm-" + Guid.NewGuid().ToString("N") + ".log");
            SourceLocationMappingDiagnostics.LogPath = tempLog;

            try
            {
                DesignerTestHost.Shared.Invoke(() =>
                {
                    SourceLocationMappingDiagnostics.Reset();

                    var registry = WorkflowTypeRegistry.Scan(xamlFiles);
                    var designer = new WorkflowDesignerEx();
                    WfDesignerEnvironment.Configure(designer);
                    LooseXamlDesignerLoader.Open(designer, Path.Combine(rpa1, "Main.xaml"), registry);

                    DebuggerMappingsHelper.RegenerateSourceDebuggerMappings(designer);

                    string log = File.ReadAllText(tempLog);
                    Assert.That(log, Does.Contain("SourceLocationProvider.CollectMapping begin"),
                        "лог должен содержать начало CollectMapping.");
                    Assert.That(log, Does.Contain("CollectMapping produced"),
                        "лог должен содержать итог CollectMapping.");
                    Assert.That(log, Does.Contain("UpdateSourceLocations"),
                        "лог должен содержать вызов UpdateSourceLocations.");
                    Assert.That(log, Does.Contain("map: key="),
                        "лог должен перечислять записи маппинга.");
                    Assert.That(log, Does.Contain("ResetRequiresUpdateSourceLocation"),
                        "лог должен фиксировать защиту ручного маппинга от перезаписи.");
                    Assert.That(log, Does.Contain("RepairDebuggerServiceModelItemMapping"),
                        "лог должен фиксировать досопоставление ModelItem для вложенных активностей.");
                    Assert.That(log, Does.Contain("LooseXamlDocumentPersistenceService.Load:"),
                        "лог должен фиксировать loose-загрузку документа.");
                });
            }
            finally
            {
                SourceLocationMappingDiagnostics.LogPath = originalPath;
                TryDeleteFile(tempLog);
            }
        }

        [Test]
        public void LooseLoad_SelectedLocation_IsResolved()
        {
            string rpa1 = FindRpa1();
            string[] xamlFiles = Directory.GetFiles(rpa1, "*.xaml", SearchOption.TopDirectoryOnly);

            DesignerTestHost.Shared.Invoke(() =>
            {
                var registry = WorkflowTypeRegistry.Scan(xamlFiles);
                var designer = new WorkflowDesignerEx();
                WfDesignerEnvironment.Configure(designer);
                LooseXamlDesignerLoader.Open(designer, Path.Combine(rpa1, "Main.xaml"), registry);

                DebuggerMappingsHelper.RegenerateSourceDebuggerMappings(designer);

                var modelService = designer.Context.Services.GetService<System.Activities.Presentation.Services.ModelService>();
                ModelItem sequenceModelItem = modelService?.Root?.Properties["Implementation"]?.Value;
                Assert.That(sequenceModelItem, Is.Not.Null, "в корне отсутствует Implementation.");

                object sequenceInstance = sequenceModelItem.GetCurrentValue();
                Assert.That(designer.DesignerSourceLocationMapping.ContainsKey(sequenceInstance), Is.True,
                    "выбранная Sequence обязана быть в ручном маппинге.");

                Selection.SelectOnly(designer.Context, sequenceModelItem);
                IDesignerDebugView debugView = designer.Context.Services.GetService<IDesignerDebugView>();

                SourceLocation selected = debugView.SelectedLocation;
                Assert.That(selected, Is.Not.Null,
                    "SelectedLocation обязан резолвиться из ручного маппинга: штатный маппинг не должен его перезаписывать.");

                SourceLocation manual = designer.DesignerSourceLocationMapping[sequenceInstance];
                Assert.That(selected.StartLine, Is.EqualTo(manual.StartLine));
                Assert.That(selected.StartColumn, Is.EqualTo(manual.StartColumn));
            });
        }

        [Test]
        public void CommandService_InsertBreakpoint_FallsBackToManualMapping()
        {
            string rpa1 = FindRpa1();
            string[] xamlFiles = Directory.GetFiles(rpa1, "*.xaml", SearchOption.TopDirectoryOnly);
            string mainXaml = Path.Combine(rpa1, "Main.xaml");
            string breakpointsFile = mainXaml + ".breakpoints";
            string originalBreakpoints = File.Exists(breakpointsFile) ? File.ReadAllText(breakpointsFile) : null;

            try
            {
                DesignerTestHost.Shared.Invoke(() =>
                {
                    var registry = WorkflowTypeRegistry.Scan(xamlFiles);
                    var designer = new WorkflowDesignerEx();
                    WfDesignerEnvironment.Configure(designer);
                    LooseXamlDesignerLoader.Open(designer, mainXaml, registry);
                    DebuggerMappingsHelper.RegenerateSourceDebuggerMappings(designer);

                    var modelService = designer.Context.Services.GetService<System.Activities.Presentation.Services.ModelService>();
                    ModelItem sequenceModelItem = modelService?.Root?.Properties["Implementation"]?.Value;
                    Assert.That(sequenceModelItem, Is.Not.Null, "в корне отсутствует Implementation.");

                    Selection.SelectOnly(designer.Context, sequenceModelItem);

                    // Имитируем DesignerView.ExecuteCommand(InsertBreakpoint): параметр SourceLocation == null.
                    var commandService = new CommandService(designer);
                    commandService.ExecuteCommand(CommandValues.InsertBreakpoint, new Dictionary<string, object>());

                    IDesignerDebugView debugView = designer.Context.Services.GetService<IDesignerDebugView>();
                    Assert.That(debugView.GetBreakpointLocations().Count, Is.GreaterThan(0),
                        "точка останова должна вставиться через fallback на ручной маппинг.");
                });
            }
            finally
            {
                RestoreFile(breakpointsFile, originalBreakpoints);
            }
        }

        [Test]
        public void SaveBreakpoints_AfterSave_KeepsBreakpoints()
        {
            string rpa1 = FindRpa1();
            string[] xamlFiles = Directory.GetFiles(rpa1, "*.xaml", SearchOption.TopDirectoryOnly);
            string tempXaml = Path.Combine(Path.GetTempPath(), "airpa-bp-" + Guid.NewGuid().ToString("N") + ".xaml");
            File.Copy(Path.Combine(rpa1, "Main.xaml"), tempXaml, overwrite: true);

            try
            {
                DesignerTestHost.Shared.Invoke(() =>
                {
                    var designer = OpenLooseDesigner(tempXaml, xamlFiles);
                    DebuggerMappingsHelper.RegenerateSourceDebuggerMappings(designer);
                    SelectRootSequence(designer);

                    var commandService = new CommandService(designer);
                    commandService.ExecuteCommand(CommandValues.InsertBreakpoint, null);

                    IDesignerDebugView debugView = designer.Context.Services.GetService<IDesignerDebugView>();
                    Assert.That(debugView.GetBreakpointLocations().Count, Is.EqualTo(1));

                    // Save инвалидирует внутренний маппинг DebuggerService — раньше точки после этого терялись.
                    designer.Save(tempXaml);
                    CustomWfDesigner.GetInstance().SaveBreakpoints(designer, tempXaml);

                    List<BreakpointStore.BreakpointRecord> stored = BreakpointStore.Load(tempXaml);
                    Assert.That(stored, Is.Not.Null.And.Not.Empty,
                        "точка должна пережить Save + SaveBreakpoints.");
                });
            }
            finally
            {
                TryDeleteFile(tempXaml);
            }
        }

        [Test]
        public void DeletedActivity_Breakpoint_DoesNotResurrect()
        {
            string rpa1 = FindRpa1();
            string[] xamlFiles = Directory.GetFiles(rpa1, "*.xaml", SearchOption.TopDirectoryOnly);
            string tempXaml = Path.Combine(Path.GetTempPath(), "airpa-bp-" + Guid.NewGuid().ToString("N") + ".xaml");
            File.Copy(Path.Combine(rpa1, "Main.xaml"), tempXaml, overwrite: true);

            try
            {
                DesignerTestHost.Shared.Invoke(() =>
                {
                    var designer = OpenLooseDesigner(tempXaml, xamlFiles);
                    DebuggerMappingsHelper.RegenerateSourceDebuggerMappings(designer);

                    ModelItem first = SelectFirstChildActivity(designer);
                    var commandService = new CommandService(designer);
                    commandService.ExecuteCommand(CommandValues.InsertBreakpoint, null);

                    IDesignerDebugView debugView = designer.Context.Services.GetService<IDesignerDebugView>();
                    Assert.That(debugView.GetBreakpointLocations().Count, Is.EqualTo(1));

                    RemoveActivity(designer, first);

                    // Сохраняем точки без предварительного Save: именно этот «устаревший» путь
                    // раньше оставлял в store запись-призрак удалённой активности.
                    CustomWfDesigner.GetInstance().SaveBreakpoints(designer, tempXaml);

                    List<BreakpointStore.BreakpointRecord> stored = BreakpointStore.Load(tempXaml);
                    Assert.That(stored == null || stored.Count == 0,
                        "точка удалённой активности не должна переживать удаление (иначе она воскреснет на новой активности).");
                });
            }
            finally
            {
                TryDeleteFile(tempXaml);
            }
        }

        [Test]
        public void LoadBreakpoints_SkipsOrphanedBreakpoint()
        {
            string rpa1 = FindRpa1();
            string[] xamlFiles = Directory.GetFiles(rpa1, "*.xaml", SearchOption.TopDirectoryOnly);
            string tempXaml = Path.Combine(Path.GetTempPath(), "airpa-bp-" + Guid.NewGuid().ToString("N") + ".xaml");
            File.Copy(Path.Combine(rpa1, "Main.xaml"), tempXaml, overwrite: true);

            try
            {
                DesignerTestHost.Shared.Invoke(() =>
                {
                    var designer = OpenLooseDesigner(tempXaml, xamlFiles);
                    DebuggerMappingsHelper.RegenerateSourceDebuggerMappings(designer);

                    // Запись-призрак: ActivityId и координаты не соответствуют ни одной живой активности.
                    BreakpointStore.Save(tempXaml, new List<BreakpointStore.BreakpointRecord>
                    {
                        new BreakpointStore.BreakpointRecord
                        {
                            ActivityId = "999.999",
                            DisplayName = "Missing",
                            StartLine = 1,
                            StartColumn = 1,
                            EndLine = 1,
                            EndColumn = 1,
                            BreakpointType = (int)(BreakpointTypes.Bounded | BreakpointTypes.Enabled)
                        }
                    });

                    LoadActivityToDesignerHelper.LoadBreakpoints(designer, tempXaml);

                    IDesignerDebugView debugView = designer.Context.Services.GetService<IDesignerDebugView>();
                    Assert.That(debugView.GetBreakpointLocations().Count, Is.EqualTo(0),
                        "точка-призрак не должна вставляться как unmapped и воскресать на новой активности.");
                });
            }
            finally
            {
                TryDeleteFile(tempXaml);
            }
        }

        private static WorkflowDesignerEx OpenLooseDesigner(string mainXaml, string[] xamlFiles)
        {
            var registry = WorkflowTypeRegistry.Scan(xamlFiles);
            var designer = new WorkflowDesignerEx();
            WfDesignerEnvironment.Configure(designer);
            LooseXamlDesignerLoader.Open(designer, mainXaml, registry);
            return designer;
        }

        private static void SelectRootSequence(WorkflowDesignerEx designer)
        {
            var modelService = designer.Context.Services.GetService<System.Activities.Presentation.Services.ModelService>();
            ModelItem sequenceModelItem = modelService?.Root?.Properties["Implementation"]?.Value;
            Assert.That(sequenceModelItem, Is.Not.Null, "в корне отсутствует Implementation.");
            Selection.SelectOnly(designer.Context, sequenceModelItem);
        }

        private static ModelItem SelectFirstChildActivity(WorkflowDesignerEx designer)
        {
            var modelService = designer.Context.Services.GetService<System.Activities.Presentation.Services.ModelService>();
            ModelItem sequence = modelService?.Root?.Properties["Implementation"]?.Value;
            ModelItemCollection activities = sequence?.Properties["Activities"]?.Collection;
            Assert.That(activities, Is.Not.Null.And.Not.Empty, "нет дочерних активностей.");
            ModelItem first = activities[0];
            Selection.SelectOnly(designer.Context, first);
            return first;
        }

        private static void RemoveActivity(WorkflowDesignerEx designer, ModelItem activity)
        {
            var modelService = designer.Context.Services.GetService<System.Activities.Presentation.Services.ModelService>();
            ModelItem sequence = modelService?.Root?.Properties["Implementation"]?.Value;
            ModelItemCollection activities = sequence?.Properties["Activities"]?.Collection;
            Assert.That(activities, Is.Not.Null);
            using (ModelEditingScope edit = sequence.BeginEdit())
            {
                activities.Remove(activity);
                edit.Complete();
            }
        }

        private static string FindRpa1()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AiRpa.sln")))
            {
                directory = directory.Parent;
            }

            if (directory == null)
            {
                throw new InvalidOperationException("AiRpa.sln не найден.");
            }

            string rpa1 = Path.Combine(directory.FullName, "RPA-1");
            Assert.That(Directory.Exists(rpa1), Is.True, "нет эталонного проекта RPA-1: " + rpa1);
            return rpa1;
        }

        private static void RestoreFile(string path, string originalContent)
        {
            try
            {
                if (originalContent != null)
                {
                    File.WriteAllText(path, originalContent);
                }
                else
                {
                    TryDeleteFile(path);
                }
            }
            catch (Exception)
            {
                // восстановление побочного файла не должно ронять тест
            }
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
