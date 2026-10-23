using System;
using System.Activities;
using System.Activities.Core.Presentation;
//using System.Activities;
using System.Activities.Presentation.Toolbox;
using System.Activities.Statements;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RPA.Controls;

namespace RPA.Helpers
{
    internal class WFToolboxHelper
    {
        public static int ActivitiesCount { get; set; } = 0;

        internal ToolboxPanel Build()
        {
            ToolboxPanel toolboxPanel = new ToolboxPanel();
            ToolboxControl wfToolbox = new ToolboxControl();

            LoadPuppeteer();
            _ = AppDomain.CurrentDomain.Load("System.Activities"); //some time not load
            _ = AppDomain.CurrentDomain.Load("Activities.Flow");
            _ = AppDomain.CurrentDomain.Load("Activities.Debug");
            _ = AppDomain.CurrentDomain.Load("Activities.OS");

            Assembly activityLibraryAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .First(a => a.GetName().Name == "System.Activities");
            Assembly activityBrowserLibraryAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .First(a => a.GetName().Name == "Activities.Browser");
            Assembly activityFlowLibraryAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .First(a => a.GetName().Name == "Activities.Flow");
            Assembly activityDebugLibraryAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .First(a => a.GetName().Name == "Activities.Debug");
            Assembly activityOsLibraryAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .First(a => a.GetName().Name == "Activities.OS");

            List<Type> flowTypes = GetActivitiesTypes(activityFlowLibraryAssembly);
            AddToolboxCategory(wfToolbox, flowTypes, LocalizeHelper.Get("lang_toolbox_flow"));

            List<Type> debugTypes = GetActivitiesTypes(activityDebugLibraryAssembly);
            AddToolboxCategory(wfToolbox, debugTypes, LocalizeHelper.Get("lang_main_debug"));

            List<Type> controlFlowTypes = new List<Type>
            {
                typeof(DoWhile),
                typeof(ForEach<>),
                typeof(If),
                typeof(Sequence),
                typeof(Switch<>),
                typeof(While)
            };
            AddToolboxCategory(wfToolbox, controlFlowTypes, LocalizeHelper.Get("lang_toolbox_control_flow"));

            List<Type> flowChartTypes = new List<Type>
            {
                typeof(Flowchart),
                typeof(FlowDecision),
                typeof(FlowSwitch<>)
            };
            AddToolboxCategory(wfToolbox, flowChartTypes, LocalizeHelper.Get("lang_toolbox_flowchart"));

            List<Type> stateMachineTypes = new List<Type>
            {
                typeof(StateMachine),
                typeof(State),
                typeof(FinalState)
            };
            AddToolboxCategory(wfToolbox, stateMachineTypes, LocalizeHelper.Get("lang_toolbox_state_machine"));

            List<Type> primitivesTypes = new List<Type>
            {
                typeof(Assign),
                typeof(Delay),
                typeof(InvokeMethod),
                typeof(WriteLine)
            };
            AddToolboxCategory(wfToolbox, primitivesTypes, LocalizeHelper.Get("lang_toolbox_primitives"));

            List<Type> collectionTypes = new List<Type>
            {
                typeof(AddToCollection<>),
                typeof(ClearCollection<>),
                typeof(ExistsInCollection<>),
                typeof(RemoveFromCollection<>)
            };
            AddToolboxCategory(wfToolbox, collectionTypes, LocalizeHelper.Get("lang_toolbox_flowchart"));

            List<Type> errorHandlingTypes = new List<Type>
            {
                typeof(Rethrow),
                typeof(Throw),
                typeof(TryCatch)
            };
            AddToolboxCategory(wfToolbox, errorHandlingTypes, LocalizeHelper.Get("lang_toolbox_error_handling"));

            List<Type> browserTypes = GetActivitiesTypes(activityBrowserLibraryAssembly);
            AddToolboxCategory(wfToolbox, browserTypes, LocalizeHelper.Get("lang_toolbox_browser"));

            List<Type> osTypes = GetActivitiesTypes(activityOsLibraryAssembly);
            AddToolboxCategory(wfToolbox, osTypes, LocalizeHelper.Get("lang_toolbox_os"));

            toolboxPanel.SetToolboxControl(wfToolbox);
            return toolboxPanel;
        }

        private void LoadPuppeteer()
        {
            Assembly assembly = AppDomain.CurrentDomain.Load("Activities.Browser");

            // Вывод всех зависимостей сборки Activities.Browser через Debug
            System.Diagnostics.Debug.WriteLine("=== Activities.Browser Dependencies ===");
            AssemblyName[] referencedAssemblies = assembly.GetReferencedAssemblies();
            foreach (AssemblyName refAssembly in referencedAssemblies)
            {
                System.Diagnostics.Debug.WriteLine($"  - {refAssembly.FullName}");
            }
            System.Diagnostics.Debug.WriteLine($"Total dependencies: {referencedAssemblies.Length}");
            System.Diagnostics.Debug.WriteLine("=====================================");

            // Принудительная загрузка всех зависимостей в память
            System.Diagnostics.Debug.WriteLine("=== Loading Activities.Browser Dependencies ===");
            foreach (AssemblyName refAssembly in referencedAssemblies)
            {
                try
                {
                    Assembly loadedAssembly = AppDomain.CurrentDomain.Load(refAssembly);
                    System.Diagnostics.Debug.WriteLine($"  ✓ Loaded: {refAssembly.Name}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"  ✗ Failed to load {refAssembly.Name}: {ex.Message}");
                }
            }
            System.Diagnostics.Debug.WriteLine("============================");

            // Загрузка и вывод зависимостей сборки PuppeteerSharp
            try
            {
                Assembly puppeteerAssembly = AppDomain.CurrentDomain.Load("PuppeteerSharp");

                System.Diagnostics.Debug.WriteLine("");
                System.Diagnostics.Debug.WriteLine("=== PuppeteerSharp Dependencies ===");
                AssemblyName[] puppeteerReferences = puppeteerAssembly.GetReferencedAssemblies();
                foreach (AssemblyName refAssembly in puppeteerReferences)
                {
                    System.Diagnostics.Debug.WriteLine($"  - {refAssembly.FullName}");
                }
                System.Diagnostics.Debug.WriteLine($"Total dependencies: {puppeteerReferences.Length}");
                System.Diagnostics.Debug.WriteLine("====================================");

                // Принудительная загрузка всех зависимостей PuppeteerSharp в память
                System.Diagnostics.Debug.WriteLine("=== Loading PuppeteerSharp Dependencies ===");
                foreach (AssemblyName refAssembly in puppeteerReferences)
                {
                    try
                    {
                        Assembly loadedAssembly = AppDomain.CurrentDomain.Load(refAssembly);
                        System.Diagnostics.Debug.WriteLine($"  ✓ Loaded: {refAssembly.Name}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"  ✗ Failed to load {refAssembly.Name}: {ex.Message}");
                    }
                }
                System.Diagnostics.Debug.WriteLine("===========================================");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load PuppeteerSharp assembly: {ex.Message}");
            }

            // Принудительная загрузка netstandard и всех его зависимостей
            try
            {
                AssemblyName netstandardName = new AssemblyName("netstandard, Version=2.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51");
                Assembly netstandardAssembly = AppDomain.CurrentDomain.Load(netstandardName);

                System.Diagnostics.Debug.WriteLine("");
                System.Diagnostics.Debug.WriteLine("=== netstandard Assembly Loaded ===");
                System.Diagnostics.Debug.WriteLine($"  Assembly: {netstandardAssembly.FullName}");
                System.Diagnostics.Debug.WriteLine($"  Location: {netstandardAssembly.Location}");
                System.Diagnostics.Debug.WriteLine("====================================");

                System.Diagnostics.Debug.WriteLine("");
                System.Diagnostics.Debug.WriteLine("=== netstandard Dependencies ===");
                AssemblyName[] netstandardReferences = netstandardAssembly.GetReferencedAssemblies();
                foreach (AssemblyName refAssembly in netstandardReferences)
                {
                    System.Diagnostics.Debug.WriteLine($"  - {refAssembly.FullName}");
                }
                System.Diagnostics.Debug.WriteLine($"Total dependencies: {netstandardReferences.Length}");
                System.Diagnostics.Debug.WriteLine("=================================");

                // Принудительная загрузка всех зависимостей netstandard в память
                System.Diagnostics.Debug.WriteLine("=== Loading netstandard Dependencies ===");
                foreach (AssemblyName refAssembly in netstandardReferences)
                {
                    try
                    {
                        Assembly loadedAssembly = AppDomain.CurrentDomain.Load(refAssembly);
                        System.Diagnostics.Debug.WriteLine($"  ✓ Loaded: {refAssembly.Name}");

                        // Рекурсивная загрузка зависимостей второго уровня
                        AssemblyName[] secondLevelRefs = loadedAssembly.GetReferencedAssemblies();
                        foreach (AssemblyName secondLevelRef in secondLevelRefs)
                        {
                            try
                            {
                                _ = AppDomain.CurrentDomain.Load(secondLevelRef);
                                System.Diagnostics.Debug.WriteLine($"    ✓ Loaded (2nd level): {secondLevelRef.Name}");
                            }
                            catch (Exception ex2)
                            {
                                System.Diagnostics.Debug.WriteLine($"    ✗ Failed to load (2nd level) {secondLevelRef.Name}: {ex2.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"  ✗ Failed to load {refAssembly.Name}: {ex.Message}");
                    }
                }
                System.Diagnostics.Debug.WriteLine("=========================================");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load netstandard assembly: {ex.Message}");
            }
        }

        private List<Type> GetActivitiesTypes(Assembly assembly)
        {
            Type baseType = typeof(Activity);
            List<Type> derivedTypes = assembly.GetTypes()
                                       .Where(t => t.IsSubclassOf(baseType) && !t.IsAbstract)
                                       .OrderBy(t => t.Name)
                                       .ToList();
            return derivedTypes;
        }

        private void AddToolboxCategory(ToolboxControl wfToolbox, List<Type> types, string categoryName)
        {
            ToolboxCategory controlFlowToolboxCategory = new ToolboxCategory(categoryName);
            IEnumerable<ToolboxItemWrapper> activities = types
                .Select(a => new ToolboxItemWrapper(a, FixName(a)));
            activities.ToList().ForEach(controlFlowToolboxCategory.Add);
            wfToolbox.Categories.Add(controlFlowToolboxCategory);
            ActivitiesCount += controlFlowToolboxCategory.Tools.Count;
        }

        private string FixName(Type type)
        {
            string displayName = type.Name;
            string[] splitName = displayName.Split('`');
            displayName = splitName[0];
            if (type.GetCustomAttributes(typeof(System.ComponentModel.DisplayNameAttribute), true).FirstOrDefault() is System.ComponentModel.DisplayNameAttribute displayNameAttribute)
            {
                displayName = displayNameAttribute.DisplayName;
            }

            if (splitName.Length > 1)
            {
                displayName = $"{displayName}<T>";
            }

            return displayName;
        }
    }
}
