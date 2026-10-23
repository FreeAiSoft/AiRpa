using System;
using System.Collections.Generic;

namespace ActivityLibraryBase.Operations
{
    public interface IOperationInvoker
    {
        void InvokeAction(Action action, Timing timing, string operation, bool isNeedLog, Dictionary<string, object> args = null);

        TResult InvokeFunc<TResult>(Func<TResult> func, Timing timing, string operation, bool isNeedLog, Dictionary<string, object> args = null);
    }
}
