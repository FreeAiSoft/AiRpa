using Activities;
using System;
using System.Activities;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;

namespace ActivityCodeTemplate
{
    [Description("")]
    //[Designer(typeof(Designer))]
    public sealed class CodeActivity1 : CodeActivity
    {
        [RequiredArgument]
        [Description("")]
        public InArgument<string> Value1 { get; set; }

        [Description("")]
        public InArgument<string> Value2 { get; set; }

        [Category("Общие")]
        [Description("Включено")]
        public InArgument<bool> IsEnabled { get; set; } = true;

        [Category("Общие")]
        [Description("Логгировать")]
        public InArgument<bool> IsLogging { get; set; } = true;

        [Category("Общие")]
        [Description("Логгировать")]
        public InArgument<uint> Timeout { get; set; } = 30;

        protected override void Execute(CodeActivityContext context)
        {
            if (!IsEnabled.Get(context))
            {
                return;
            }
            
            bool isLogging = IsLogging.Get(context);

            string value1 = Value1.Get(context);
            string value2 = Value2.Get(context);

            ActivityLogger.Info($"{value1}{value2}", isLogging);
        }
    }
}
