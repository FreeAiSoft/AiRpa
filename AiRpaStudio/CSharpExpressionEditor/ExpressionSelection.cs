using System;
using System.Activities.Presentation;
using System.Activities.Presentation.Model;

namespace RPA.CSharpExpressionEditor
{
    internal class ExpressionSelection : ContextItem
    {
        public ExpressionSelection()
        {
        }

        public ExpressionSelection(ModelItem modelItem)
        {
            ModelItem = modelItem;
        }

        public ModelItem ModelItem { get; }

        public override Type ItemType => typeof(ExpressionSelection);
    }
}
