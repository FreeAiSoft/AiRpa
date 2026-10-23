using Rpa.Activities.Primitives.Designers;
using System;
using System.Activities;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Markup;

namespace Rpa.Activities.Primitives.Main
{
    [Description("InvokeMehod")]
    [Designer(typeof(InvokeMethodDesigner))]
    [ContentProperty("Parameters")]
    public sealed class InvokeMethod : CodeActivity
    {
        //public string MethodName
        //{

        //}

        private readonly System.Activities.Statements.InvokeMethod _invokeMethod;
        private RuntimeArgument _resultArgument;
        //private MethodResolver _

        protected override void Execute(CodeActivityContext context)
        {
            throw new NotImplementedException();
        }
    }
}
