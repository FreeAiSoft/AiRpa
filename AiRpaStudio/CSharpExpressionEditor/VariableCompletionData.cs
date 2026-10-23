using System;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;

namespace RPA.CSharpExpressionEditor
{
    /// <summary>
    /// Completion data for variables and arguments
    /// </summary>
    public class VariableCompletionData : ICompletionData
    {
        private static readonly ImageSource VariableIcon;

        static VariableCompletionData()
        {
            // Use the Field icon for variables
            VariableIcon = QueryCompletionData.GetImageSourceFromResource("Field.png");
        }

        public VariableCompletionData(string name, string typeName)
        {
            Text = name;
            Description = $"{typeName} {name}";
        }

        public ImageSource Image => VariableIcon;

        public string Text { get; private set; }

        public object Content => Text;

        public object Description { get; private set; }

        public double Priority => 1.0;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            // Replace the partial word with the full variable name
            textArea.Document.Replace(completionSegment, Text);
        }
    }
}
