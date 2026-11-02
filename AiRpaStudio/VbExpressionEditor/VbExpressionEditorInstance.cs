using System;
using System.Activities.Presentation.View;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using RPA.CSharpExpressionEditor;
using RPA.Helpers;

namespace RehostedWorkflowDesigner.VbExpressionEditor
{
    public class VbExpressionEditorInstance : TextEditor, IExpressionEditorInstance
    {
        private CompletionWindow completionWindow;

        //private readonly TextBox textBox = new TextBox();

        public VbExpressionEditorInstance()
        {
            TextArea.TextEntering += TextArea_TextEntering;
            TextArea.TextEntered += TextArea_TextEntered;
            TextArea.LostKeyboardFocus += TextArea_LostFocus;

            SyntaxHighlighting = StudioEditorTheme.GetDefinition("VB");

            // Ссылки (URL в тексте выражения): #3794FF вместо Brushes.Blue — иначе на тёмном фоне не видно.
            StudioEditorTheme.ApplyLinkColor(this);
            FontFamily = StudioFonts.CodeFont;
            FontSize = 12;
        }

        private void TextArea_TextEntered(object sender, TextCompositionEventArgs e)
        {
            if (e.Text == ".")
            {
                try
                {
                    string startString = @"
Imports System
Imports System.Collections.Generic
Imports System.Text

Namespace SomeNamespace

Public Class NotAProgram 
Private Sub SomeMethod()
"
+ "var blah = ";

                    string endString = @" 

        End Sub
    End Class
End Namespace                       
";
                    string codeString = startString + Text.Substring(0, CaretOffset) + endString;

                    SyntaxTree tree = VisualBasicSyntaxTree.ParseText(codeString);

                    Microsoft.CodeAnalysis.VisualBasic.Syntax.CompilationUnitSyntax root = (Microsoft.CodeAnalysis.VisualBasic.Syntax.CompilationUnitSyntax)tree.GetRoot();

                    VisualBasicCompilation compilation = VisualBasicCompilation.Create("CustomIntellisense")
                                               .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                                               .AddSyntaxTrees(tree);

                    SemanticModel model = compilation.GetSemanticModel(tree);

                    SyntaxNode exprString = root.FindToken(codeString.LastIndexOf('.') - 1).Parent;

                    Microsoft.CodeAnalysis.TypeInfo literalInfo = model.GetTypeInfo(exprString);

                    INamedTypeSymbol stringTypeSymbol = (INamedTypeSymbol)literalInfo.Type;
                    IList<ISymbol> symbols = new List<ISymbol>() { };
                    foreach (ISymbol s in (from method in stringTypeSymbol.GetMembers() where method.DeclaredAccessibility == Accessibility.Public select method).Distinct())
                    {
                        symbols.Add(s);
                    }

                    if (symbols != null && symbols.Count > 0)
                    {
                        completionWindow = new CompletionWindow(TextArea);
                        IList<ICompletionData> data = completionWindow.CompletionList.CompletionData;
                        data.Clear();
                        var distinctSymbols = from s in symbols group s by s.Name into g select new { Name = g.Key, Symbols = g };
                        foreach (var g in distinctSymbols.OrderBy(s => s.Name))
                        {
                            data.Add(new QueryCompletionData(g.Name, g.Symbols.ToArray()));
                        }

                        completionWindow.Show();
                        completionWindow.Closed += delegate
                        {
                            completionWindow = null;
                        };
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
        }

        private void TextArea_LostFocus(object sender, RoutedEventArgs e)
        {
            LostAggregateFocus?.Invoke(sender, e);
        }

        private void TextArea_TextEntering(object sender, TextCompositionEventArgs e)
        {
            if (e.Text.Length > 0 && completionWindow != null)
            {
                if (!char.IsLetterOrDigit(e.Text[0]))
                {
                    // Whenever a non-letter is typed while the completion window is open,
                    // insert the currently selected element.
                    completionWindow.CompletionList.RequestInsertion(e);
                }
            }
            // Do not set e.Handled=true.
            // We still want to insert the character that was typed.
        }

        #region IExpressionEditorInstance implicit

        public bool AcceptsReturn { get; set; }

        public bool AcceptsTab { get; set; }

        public bool HasAggregateFocus => true;

        public Control HostControl => this;

        public int MaxLines { get; set; }

        public int MinLines { get; set; }

        public event EventHandler Closing;
        public event EventHandler GotAggregateFocus;
        public event EventHandler LostAggregateFocus;

        public bool CanCompleteWord()
        {
            return true;
        }

        public bool CanCopy()
        {
            return true;
        }

        public bool CanCut()
        {
            return true;
        }

        public bool CanDecreaseFilterLevel()
        {
            return true;
        }

        public bool CanGlobalIntellisense()
        {
            return true;
        }

        public bool CanIncreaseFilterLevel()
        {
            return true;
        }

        public bool CanParameterInfo()
        {
            return true;
        }

        public bool CanPaste()
        {
            return true;
        }

        public bool CanQuickInfo()
        {
            return true;
        }

        public void ClearSelection()
        {

        }

        public void Close()
        {

        }

        public bool CompleteWord()
        {
            return true;
        }

        public string GetCommittedText()
        {
            return Text;
        }

        public bool GlobalIntellisense()
        {
            return true;
        }
        public bool DecreaseFilterLevel()
        {
            return true;
        }

        public bool IncreaseFilterLevel()
        {
            return true;
        }

        public bool ParameterInfo()
        {
            return true;
        }

        public bool QuickInfo()
        {
            return true;
        }

        #endregion

        #region IExpressionEditorInstance explicit

        void IExpressionEditorInstance.Focus()
        {
            _ = Focus();
        }

        bool IExpressionEditorInstance.Cut()
        {
            Cut();
            return true;
        }

        bool IExpressionEditorInstance.Copy()
        {
            Copy();
            return true;
        }

        bool IExpressionEditorInstance.Paste()
        {
            Paste();
            return true;
        }

        bool IExpressionEditorInstance.Undo()
        {
            return Undo();
        }

        bool IExpressionEditorInstance.Redo()
        {
            return Redo();
        }

        bool IExpressionEditorInstance.CanUndo()
        {
            return CanUndo;
        }

        bool IExpressionEditorInstance.CanRedo()
        {
            return CanRedo;
        }

        event EventHandler IExpressionEditorInstance.TextChanged
        {
            add => TextChanged += value;

            remove => TextChanged -= value;
        }

        string IExpressionEditorInstance.Text
        {
            get => Text;

            set => Text = value;
        }

        ScrollBarVisibility IExpressionEditorInstance.VerticalScrollBarVisibility
        {
            get => VerticalScrollBarVisibility;

            set => VerticalScrollBarVisibility = value;
        }

        ScrollBarVisibility IExpressionEditorInstance.HorizontalScrollBarVisibility
        {
            get => HorizontalScrollBarVisibility;

            set => HorizontalScrollBarVisibility = value;
        }


        #endregion
    }
}
