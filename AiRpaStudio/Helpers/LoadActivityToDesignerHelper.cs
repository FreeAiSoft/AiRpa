using System;
using System.Activities.Debugger;
using System.Activities.Presentation;
using System.Activities.Presentation.Debug;
using System.Activities.Presentation.Hosting;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.Services;
using System.Activities.Presentation.Validation;
using System.Activities.Presentation.View;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MahApps.Metro.Controls;
using Microsoft.CSharp.Activities;
using Newtonsoft.Json;
using RPA.Core;
using RPA.CSharpExpressionEditor;

namespace RPA.Helpers
{
    internal class LoadActivityToDesignerHelper
    {
        /// <summary>
        /// Creates a new Workflow Designer instance with C# Expression Editor
        /// </summary>
        /// <param name="sourceFile">Workflow FileName</param>
        internal static WorkflowDesignerEx LoadActivityToDesigner(string sourceFile)
        {
            if (!sourceFile.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
                || !File.Exists(sourceFile))
            {
                return null;
            }

            // проверяем не загружена ли уже активность в какой-либо дизайнер
            WorkflowDesignerEx workflowDesignerEx = GetDesignerByFile(sourceFile);

            if (workflowDesignerEx == null)
            {
                ExpressionTextBox.RegisterExpressionActivityEditor(new CSharpValue<string>().Language, typeof(RoslynExpressionEditor), CSharpExpressionHelper.CreateExpressionFromString);

                workflowDesignerEx = new WorkflowDesignerEx();
                workflowDesignerEx.Context.Services.GetService<DesignerConfigurationService>().TargetFrameworkName = new System.Runtime.Versioning.FrameworkName(".NETFramework", new Version(4, 5));
                workflowDesignerEx.Context.Services.GetService<DesignerConfigurationService>().LoadingFromUntrustedSourceEnabled = true;
                workflowDesignerEx.Context.Services.Publish<IExpressionEditorService>(new RoslynExpressionEditorService());
                workflowDesignerEx.Context.Services.Publish<IValidationErrorService>(new DebugValidationErrorService());
                //load Workflow Xaml
                workflowDesignerEx.Load(sourceFile);

                CustomWfDesigner.GetInstance().WorkflowDesigners.Add(workflowDesignerEx);
                CustomWfDesigner.GetInstance().ActiveWorkflowDesignerEx = workflowDesignerEx;

                CommandService commandService = new CommandService(workflowDesignerEx);
                workflowDesignerEx.Context.Services.Publish<ICommandService>(commandService);
                try
                {
                    DebuggerMappingsHelper.RegenerateSourceDebuggerMappings(workflowDesignerEx);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(ex.Message);
                }
            }

            SetCurrentWorkflowFile(workflowDesignerEx);
            LoadBreakpoints(workflowDesignerEx, sourceFile);
            SetVariablesStyle(workflowDesignerEx);

            // Инициализировать отслеживание VariableDesigner
            workflowDesignerEx.DesignerVariableContextMenuService.InitializeVariableDesignerTracking();

            // Добавить ссылку на netstandard.dll
            //EnsureNetstandardReference(workflowDesignerEx);

            workflowDesignerEx.Save(sourceFile);

            return workflowDesignerEx;
        }

        private static void SetVariablesStyle(WorkflowDesignerEx workflowDesignerEx)
        {
            IEnumerable<DependencyObject> childs = workflowDesignerEx.View.FindChildren<DependencyObject>();
            FrameworkElement shellBar = childs
                .Select(c => c is FrameworkElement element ? element : null)
                .First(f => f?.Name == "shellBar");
            StatusBar sb = (StatusBar)shellBar;
            sb.Background = Brushes.Gray;
            sb.FontFamily = new FontFamily("Consolas");
        }

        /// <summary>
        /// Get Designer By laoded File
        /// </summary>
        /// <param name="sourceFile"></param>
        /// <returns></returns>
        public static WorkflowDesignerEx GetDesignerByFile(string sourceFile)
        {
            return CustomWfDesigner.GetInstance().WorkflowDesigners
                .FirstOrDefault(d => ((WorkflowFileItem)d.Context.Items.First(i => i.ItemType == typeof(WorkflowFileItem))).LoadedFile == sourceFile);
        }

        private static void SetCurrentWorkflowFile(WorkflowDesignerEx workflowDesignerEx)
        {
            Properties.Settings.Default.CurrentWorkflowFile = workflowDesignerEx.Context.Items.GetValue<WorkflowFileItem>().LoadedFile;
            Properties.Settings.Default.Save();
        }

        private static void LoadBreakpoints(WorkflowDesigner designer, string filePath)
        {
            try
            {
                string breakPointsFile = $"{filePath}.breakpoints";

                if (!File.Exists(breakPointsFile))
                {
                    return;
                }

                DebuggerService debuggerService = (DebuggerService)designer.DebugManagerView;
                if (debuggerService != null)
                {
                    string json = File.ReadAllText(breakPointsFile);
                    List<BreakpointData> breakpointData = JsonConvert.DeserializeObject<List<BreakpointData>>(json);

                    if (breakpointData != null && breakpointData.Count > 0)
                    {
                        WorkflowDesignerEx designerEx = (WorkflowDesignerEx)designer;
                        foreach (BreakpointData bp in breakpointData)
                        {
                            SourceLocation sourceLocation = new SourceLocation(filePath, bp.StartLine, bp.StartColumn, bp.EndLine, bp.EndColumn);
                            debuggerService.InsertBreakpoint(sourceLocation, BreakpointTypes.Bounded | BreakpointTypes.Enabled);

                            // Проверка дубликатов перед добавлением точки останова
                            if (!designerEx.BreakpointList.Contains(sourceLocation))
                            {
                                designerEx.BreakpointList.Add(sourceLocation);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CustomWfDesigner.GetInstance().Log($"Ошибка при загрузке точек останова: {ex.Message}", (int)RpaCommon.Enums.LogLevel.Error);
            }
        }

        /// <summary>
        /// Ensures that netstandard.dll reference is added to the workflow
        /// </summary>
#pragma warning disable IDE0051 // Remove unused private members
        private static void EnsureNetstandardReference(WorkflowDesignerEx workflowDesignerEx)
#pragma warning restore IDE0051 // Remove unused private members
        {
            try
            {
                ModelService modelService = workflowDesignerEx.Context.Services.GetService<ModelService>();
                if (modelService == null)
                {
                    CustomWfDesigner.GetInstance().Log("Failed to get ModelService for adding netstandard reference", (int)RpaCommon.Enums.LogLevel.Warn);
                    return;
                }

                ModelItem root = modelService.Root;
                if (root == null)
                {
                    CustomWfDesigner.GetInstance().Log("Failed to get root ModelItem for adding netstandard reference", (int)RpaCommon.Enums.LogLevel.Warn);
                    return;
                }

                // Get TextExpression.ReferencesForImplementation
                ModelProperty referencesProperty = root.Properties["TextExpression.ReferencesForImplementation"];
                if (referencesProperty == null)
                {
                    System.Diagnostics.Debug.WriteLine("⚠ TextExpression.ReferencesForImplementation property not found in workflow. This is normal for workflows without C# expressions.");
                    return;
                }

                ModelItemCollection references = referencesProperty.Collection;
                if (references == null)
                {
                    System.Diagnostics.Debug.WriteLine("⚠ References collection is null. Cannot add netstandard reference.");
                    return;
                }

                // Check if netstandard is already referenced
                string netstandardAssemblyName = "netstandard, Version=2.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51";
                bool alreadyExists = references.Any(item =>
                {
                    object assemblyRef = item.GetCurrentValue();
                    if (assemblyRef != null)
                    {
                        PropertyInfo nameProperty = assemblyRef.GetType().GetProperty("AssemblyName");
                        if (nameProperty != null)
                        {
                            object value = nameProperty.GetValue(assemblyRef);
                            string name = value?.ToString();
                            return name != null && (name == netstandardAssemblyName || name.StartsWith("netstandard,"));
                        }
                    }
                    return false;
                });

                if (alreadyExists)
                {
                    System.Diagnostics.Debug.WriteLine("✓ netstandard reference already exists in workflow");
                    return;
                }

                // Create AssemblyReference type using reflection
                Type assemblyReferenceType = Type.GetType("System.Activities.Presentation.View.AssemblyReference, System.Activities.Presentation");
                if (assemblyReferenceType == null)
                {
                    CustomWfDesigner.GetInstance().Log("Failed to find AssemblyReference type", (int)RpaCommon.Enums.LogLevel.Warn);
                    return;
                }

                // Create new AssemblyReference instance
                object assemblyReference = Activator.CreateInstance(assemblyReferenceType);
                PropertyInfo assemblyNameProperty = assemblyReferenceType.GetProperty("AssemblyName");
                if (assemblyNameProperty != null)
                {
                    // Create AssemblyName object
                    AssemblyName netstandardName = new AssemblyName(netstandardAssemblyName);
                    assemblyNameProperty.SetValue(assemblyReference, netstandardName);

                    // Add to collection
                    _ = references.Add(assemblyReference);

                    System.Diagnostics.Debug.WriteLine("✓ Successfully added netstandard reference to workflow");
                    CustomWfDesigner.GetInstance().Log("Added netstandard reference to workflow", (int)RpaCommon.Enums.LogLevel.Trace);
                }
            }
            catch (Exception ex)
            {
                CustomWfDesigner.GetInstance().Log($"Failed to add netstandard reference: {ex.Message}", (int)RpaCommon.Enums.LogLevel.Warn);
                System.Diagnostics.Debug.WriteLine($"Failed to add netstandard reference: {ex}");
            }
        }

        private class BreakpointData
        {
            public int StartLine { get; set; }
            public int StartColumn { get; set; }
            public int EndLine { get; set; }
            public int EndColumn { get; set; }
        }
    }
}
