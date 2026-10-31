using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Microsoft.CodeAnalysis;

namespace RPA.CSharpExpressionEditor
{
    public class QueryCompletionData : ICompletionData
    {
        private static readonly ImageSource MethodIcon;
        private static readonly ImageSource PropertyIcon;
        private static readonly ImageSource FieldIcon;
        private static readonly ImageSource EventIcon;

        private readonly IconType iconType;

        static QueryCompletionData()
        {
            MethodIcon = GetImageSourceFromResource("Method.png");
            PropertyIcon = GetImageSourceFromResource("Property.png");
            FieldIcon = GetImageSourceFromResource("Field.png");
            EventIcon = GetImageSourceFromResource("Event.png");
        }

        internal static ImageSource GetImageSourceFromResource(string resourceName)
        {
            try
            {
                System.IO.Stream stream = typeof(QueryCompletionData).Assembly.GetManifestResourceStream("RPA.Resources.ExpressionEditor." + resourceName);
                if (stream != null)
                {
                    return BitmapFrame.Create(stream);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load resource {resourceName}: {ex.Message}");
            }
            return null;
        }

        public QueryCompletionData(string name, ISymbol[] symbols)
        {
            Text = name;
            if (symbols == null)
            {
                iconType = IconType.Field;
                return;
            }
#pragma warning disable IDE0010 // Add missing cases
            switch (symbols[0].Kind)
            {
                case SymbolKind.Event:
                    iconType = IconType.Event;
                    break;
                case SymbolKind.Field:
                    iconType = IconType.Field;
                    break;
                case SymbolKind.Method:
                    iconType = IconType.Method;
                    Text = name + "()";
                    break;
                case SymbolKind.Property:
                    iconType = IconType.Property;
                    break;
                default:
                    break;
            }
#pragma warning restore IDE0010 // Add missing cases
        }

        public ImageSource Image
        {
            get
            {
#pragma warning disable IDE0010 // Add missing cases
                switch (iconType)
                {
                    case IconType.Event: return EventIcon;
                    case IconType.Field: return FieldIcon;
                    case IconType.Property: return PropertyIcon;
                    default: return MethodIcon;
                }
#pragma warning restore IDE0010 // Add missing cases
            }
        }

        public string Text { get; private set; }

        public string HintText { get; private set; }

        // Use this property if you want to show a fancy UIElement in the list.
        public object Content => Text;

        public object Description => "Description for " + Text;

        public void Complete(TextArea textArea, ISegment completionSegment,
            EventArgs insertionRequestEventArgs)
        {
            textArea.Document.Replace(completionSegment, Text);
        }

        public double Priority => 1.0;

        private enum IconType
        {
            Property,
            Field,
            Method,
            Event,
        }
    }
}
