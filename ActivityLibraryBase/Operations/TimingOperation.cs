using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ActivityLibraryBase.Operations
{
    public class TimingOperation : IOperationInvoker
    {
        public void InvokeAction(Action action, Timing timing, string operation, bool isNeedLog, Dictionary<string, object> args = null)
        {
            _ = InvokeFunc(() => { action(); return true; }, timing, operation, isNeedLog, args);
        }

        public TResult InvokeFunc<TResult>(Func<TResult> func, Timing timing, string operation, bool isNeedLog, Dictionary<string, object> args = null)
        {
            Pause(timing.DelayBefore);

            Task<TResult> task = Task.Run(func);
            bool isTaskComplited;
            try
            {
                isTaskComplited = task.Wait(timing.Timeout);
            }
            catch (AggregateException ex)
            {
                throw ex;
            }

            if (!isTaskComplited)
            {
                throw new TimeoutException();
            }

            Pause(timing.DelayAfter);

            return task.Result;
        }

        private void Pause(TimeSpan delay)
        {
            if (delay > TimeSpan.Zero)
            {
                Thread.Sleep(delay);
            }
        }
    }
}
