using System;
using System.Collections.Generic;

namespace ActivityLibraryBase.Operations
{
    public class PollingOperation : IOperationInvoker
    {
        private readonly Waiter _waiter;
        private readonly IOperationInvoker _operationInvoker;

        public List<Type> IgnoreExceptionTypes { get; set; } = new List<Type>();

        public TimeSpan PollingInterval
        {
            get => _waiter.PollingInterval;
            set => _waiter.PollingInterval = value;
        }

        public PollingOperation()
            : this(new WaiterThrowLastException(), new TimingOperation())
        {

        }

        public PollingOperation(TimeSpan pollingInterval)
            : this(new WaiterThrowLastException { PollingInterval = pollingInterval }, new TimingOperation())
        {

        }

        public PollingOperation(Waiter waiter, IOperationInvoker operationInvoker)
        {
            _waiter = waiter ?? throw new ArgumentNullException(nameof(waiter));
            _operationInvoker = operationInvoker ?? throw new ArgumentNullException(nameof(operationInvoker));
        }

        public void InvokeAction(Action action, Timing timing, string operation, bool isNeedLog, Dictionary<string, object> args)
        {
            _ = InvokeFunc(() => { action(); return true; }, timing, operation, isNeedLog, args);
        }

        public TResult InvokeFunc<TResult>(Func<TResult> func, Timing timing, string operation, bool isNeedLog, Dictionary<string, object> args)
        {
            _waiter.IgnoreExceptionTypes(IgnoreExceptionTypes.ToArray());
            _waiter.Timeout = timing.Timeout;

            return _operationInvoker.InvokeFunc(
                () => _waiter.Until(func),
                timing,
                operation,
                isNeedLog,
                args);
        }
    }
}
