using System;
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

namespace AiRpaStudio.Tests
{
    /// <summary>
    /// Тесты <see cref="CommandService"/>: fallback резолва SourceLocation из ручного маппинга
    /// и контракт команд (поддержка/выполнение/события). Юнит-кейсы без дизайнера выполняются на
    /// STA-стенде, потому что конструктор создаёт singleton <see cref="CustomWfDesigner"/>.
    /// </summary>
    [TestFixture]
    public class CommandServiceTests
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
        public void IsCommandSupported_ReturnsTrueForKnownCommands_FalseForUnknown()
        {
            DesignerTestHost.Shared.Invoke(() =>
            {
                var service = new CommandService(null);

                Assert.Multiple(() =>
                {
                    Assert.That(service.IsCommandSupported(CommandValues.ShowProperties), Is.True);
                    Assert.That(service.IsCommandSupported(CommandValues.InsertBreakpoint), Is.True);
                    Assert.That(service.IsCommandSupported(CommandValues.DeleteBreakpoint), Is.True);
                    Assert.That(service.IsCommandSupported(CommandValues.EnableBreakpoint), Is.True);
                    Assert.That(service.IsCommandSupported(CommandValues.DisableBreakpoint), Is.True);
                    Assert.That(service.IsCommandSupported(int.MaxValue), Is.False);
                });
            });
        }

        [Test]
        public void CanExecuteCommand_AlwaysReturnsTrue()
        {
            DesignerTestHost.Shared.Invoke(() =>
            {
                var service = new CommandService(null);

                Assert.That(service.CanExecuteCommand(CommandValues.InsertBreakpoint), Is.True);
                Assert.That(service.CanExecuteCommand(int.MaxValue), Is.True);
            });
        }

        [Test]
        public void ExecuteCommand_ShowProperties_RaisesEvent()
        {
            DesignerTestHost.Shared.Invoke(() =>
            {
                var service = new CommandService(null);
                int raised = 0;
                service.ShowPropertiesRequested += (s, e) => raised++;

                service.ExecuteCommand(CommandValues.ShowProperties, null);

                Assert.That(raised, Is.EqualTo(1));
            });
        }

        [Test]
        public void ExecuteCommand_InsertBreakpoint_WithoutSelection_DoesNotThrow()
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
                    var designer = OpenLooseDesigner(mainXaml, xamlFiles);
                    DebuggerMappingsHelper.RegenerateSourceDebuggerMappings(designer);

                    var service = new CommandService(designer);

                    Assert.DoesNotThrow(() => service.ExecuteCommand(CommandValues.InsertBreakpoint, null));

                    IDesignerDebugView debugView = designer.Context.Services.GetService<IDesignerDebugView>();
                    Assert.That(debugView.GetBreakpointLocations().Count, Is.EqualTo(0),
                        "без выделения точка останова не должна вставляться.");
                });
            }
            finally
            {
                RestoreFile(breakpointsFile, originalBreakpoints);
            }
        }

        [Test]
        public void ExecuteCommand_DeleteBreakpoint_FallsBackToManualMapping()
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
                    var designer = OpenLooseDesigner(mainXaml, xamlFiles);
                    DebuggerMappingsHelper.RegenerateSourceDebuggerMappings(designer);
                    SelectRootSequence(designer);

                    var service = new CommandService(designer);
                    IDesignerDebugView debugView = designer.Context.Services.GetService<IDesignerDebugView>();

                    service.ExecuteCommand(CommandValues.InsertBreakpoint, null);
                    Assert.That(debugView.GetBreakpointLocations().Count, Is.EqualTo(1));

                    service.ExecuteCommand(CommandValues.DeleteBreakpoint, null);
                    Assert.That(debugView.GetBreakpointLocations().Count, Is.EqualTo(0),
                        "точка останова должна удалиться через fallback на ручной маппинг.");
                });
            }
            finally
            {
                RestoreFile(breakpointsFile, originalBreakpoints);
            }
        }

        [Test]
        public void ExecuteCommand_EnableDisable_ChangesBreakpointTypes()
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
                    var designer = OpenLooseDesigner(mainXaml, xamlFiles);
                    DebuggerMappingsHelper.RegenerateSourceDebuggerMappings(designer);
                    SelectRootSequence(designer);

                    var service = new CommandService(designer);
                    IDesignerDebugView debugView = designer.Context.Services.GetService<IDesignerDebugView>();

                    service.ExecuteCommand(CommandValues.InsertBreakpoint, null);
                    Assert.That(
                        debugView.GetBreakpointLocations().Values.First(),
                        Is.EqualTo(BreakpointTypes.Bounded | BreakpointTypes.Enabled));

                    service.ExecuteCommand(CommandValues.DisableBreakpoint, null);
                    Assert.That(
                        debugView.GetBreakpointLocations().Values.First(),
                        Is.EqualTo(BreakpointTypes.Bounded));

                    service.ExecuteCommand(CommandValues.EnableBreakpoint, null);
                    Assert.That(
                        debugView.GetBreakpointLocations().Values.First(),
                        Is.EqualTo(BreakpointTypes.Bounded | BreakpointTypes.Enabled));
                });
            }
            finally
            {
                RestoreFile(breakpointsFile, originalBreakpoints);
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
