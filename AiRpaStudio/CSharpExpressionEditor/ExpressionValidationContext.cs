using System;
using System.Activities.Presentation;
using System.Activities.Presentation.Hosting;
using System.Activities.Presentation.Model;
using System.Collections.Generic;

namespace RPA.CSharpExpressionEditor
{
    public class ExpressionValidationContext
    {
#pragma warning disable IDE0052 // Remove unread private members
        private readonly RoslynExpressionEditor etb;
#pragma warning restore IDE0052 // Remove unread private members

        public ExpressionValidationContext(RoslynExpressionEditor etb)
        {
            this.etb = etb;
            Update(etb);
        }

        internal ParserContext ParserContext { get; set; }
        internal Type ExpressionType { get; set; }
        internal string ExpressionText { get; set; }
        internal EditingContext EditingContext { get; set; }
        internal string ValidatedExpressionText { get; set; }
        internal bool UseLocationExpression { get; set; }

        internal void Update(RoslynExpressionEditor etb)
        {
            EditingContext = etb.OwnerActivity.GetEditingContext();

            //setup ParserContext
            ParserContext = new ParserContext(etb.OwnerActivity)
            {
                //callee is a ExpressionTextBox
                Instance = etb,
                //pass property descriptor belonging to epression's model property (if one exists)
                //TODO: etb should have expressionModelProperty and the propertyDescriptor should be used here instead of passing null
                PropertyDescriptor = null,
            };

            if (etb.ExpressionType != null)
            {
                ExpressionType = etb.ExpressionType;
            }

            ValidatedExpressionText = ExpressionText;
            ExpressionText = etb.ExpressionEditorInstance != null
                ? etb.ExpressionEditorInstance.Text
                : etb.EditingTextBox != null ? etb.EditingTextBox.Text : etb.Text;
            UseLocationExpression = etb.UseLocationExpression;
        }

        internal IEnumerable<string> ReferencedAssemblies
        {
            get
            {
                AssemblyContextControlItem assemblyContext = EditingContext.Items.GetValue<AssemblyContextControlItem>();
                return assemblyContext?.AllAssemblyNamesInContext;
            }
        }

    }
}
