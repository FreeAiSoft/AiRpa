using System;

namespace ActivityLibraryBase.Operations
{
    public class WaiterThrowLastException : Waiter
    {
        protected override void ThrowTimeoutException(string exceptionMessage, Exception exception)
        {
            if (exception != null)
            {
                throw exception;
            }
            base.ThrowTimeoutException(exceptionMessage, exception);
        }
    }
}
