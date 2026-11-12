using System;
using System.Activities.Presentation.Hosting;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.View;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.CodeAnalysis;

namespace RPA.CSharpExpressionEditor
{
    public class RoslynExpressionEditorService : IExpressionEditorService
    {
        public static RoslynExpressionEditorService Instance { get; } = new RoslynExpressionEditorService();

        internal string UsingNamespaces { get; private set; } = string.Empty;

        internal MetadataReference[] BaseAssemblies { get; private set; } = new MetadataReference[0];

        public void CloseExpressionEditors()
        {

        }

        public IExpressionEditorInstance CreateExpressionEditor(AssemblyContextControlItem assemblies, ImportedNamespaceContextItem importedNamespaces, List<ModelItem> variables, string text)
        {
            return CreateExpressionEditor(assemblies, importedNamespaces, variables, text, null, Size.Empty);
        }

        public IExpressionEditorInstance CreateExpressionEditor(AssemblyContextControlItem assemblies, ImportedNamespaceContextItem importedNamespaces, List<ModelItem> variables, string text, Size initialSize)
        {
            return CreateExpressionEditor(assemblies, importedNamespaces, variables, text, null, initialSize);
        }

        public IExpressionEditorInstance CreateExpressionEditor(AssemblyContextControlItem assemblies, ImportedNamespaceContextItem importedNamespaces, List<ModelItem> variables, string text, Type expressionType)
        {
            return CreateExpressionEditor(assemblies, importedNamespaces, variables, text, expressionType, Size.Empty);
        }

        public IExpressionEditorInstance CreateExpressionEditor(AssemblyContextControlItem assemblies, ImportedNamespaceContextItem importedNamespaces, List<ModelItem> variables, string text, Type expressionType, Size initialSize)
        {
            UpdateContext(assemblies, importedNamespaces);
            RoslynExpressionEditorInstance2 editor = new RoslynExpressionEditorInstance2(initialSize, text);

            // ВАЖНО: UpdateInstance должен быть вызван перед FinalizeInitialization
            editor.UpdateInstance(variables);

            // Завершаем инициализацию с учетом переменных
            editor.FinalizeInitialization();

            return editor;
        }

        public void UpdateContext(AssemblyContextControlItem assemblies, ImportedNamespaceContextItem importedNamespaces)
        {
            List<MetadataReference> references = new List<MetadataReference>();

            foreach (string assembly in assemblies.AllAssemblyNamesInContext)
            {
                // WinRT contracts (Windows.UI, Windows.Foundation, ...) come from the document's
                // ReferencesForImplementation but cannot be loaded via Assembly.Load(string):
                // that throws FileLoadException HRESULT 0x80131515 ("operation not supported") and
                // keeps the UI thread busy (the "Namespaces" panel retries every ~250 ms).
                if (IsWindowsRuntimeContract(assembly))
                {
                    continue;
                }

                System.Reflection.Assembly asm = LoadAssembly(assembly);
                if (asm != null)
                {
                    if (File.Exists(asm.Location) && !asm.Location.Contains(".resources.dll") &&
                        (asm.Location.Contains("Puppeteer")
                        || asm.Location.Contains("System.Runtime")
                        || asm.Location.Contains("System.Core")
                        || asm.Location.Contains("mscorlib")
                        || asm.Location.Contains("System.Private.CoreLib")))
                    {
                        references.Add(MetadataReference.CreateFromFile(asm.Location));
                    }
                }
            }
            BaseAssemblies = references.ToArray();
            UsingNamespaces = string.Join("", importedNamespaces.ImportedNamespaces.Select(ns => "using " + ns + ";\n").ToArray());

            RoslynExpressionValidator.Instance.UsingNamespaces = UsingNamespaces;
        }

        /// <summary>
        /// True for WinRT contracts (ContentType=WindowsRuntime). They are not needed by the
        /// expression editor and cannot be loaded via Assembly.Load(string).
        /// </summary>
        private static bool IsWindowsRuntimeContract(string assembly)
        {
            if (string.IsNullOrWhiteSpace(assembly))
            {
                return false;
            }

            try
            {
                System.Reflection.AssemblyName name = new System.Reflection.AssemblyName(assembly);
                return name.ContentType == System.Reflection.AssemblyContentType.WindowsRuntime;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Loads an assembly without throwing: an unresolvable or broken reference
        /// (FileLoadException / FileNotFoundException / BadImageFormatException) must not break
        /// the expression editor context update.
        /// </summary>
        private static System.Reflection.Assembly LoadAssembly(string assembly)
        {
            try
            {
                return System.Reflection.Assembly.Load(assembly);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
