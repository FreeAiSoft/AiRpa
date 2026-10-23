using System;
using System.Collections.Generic;
using System.Linq;

namespace ActivityLibraryBase.Operations
{
    public class Waiter
    {
        private static TimeSpan DefaultSleepTimeout => TimeSpan.FromMilliseconds(500);
        private TimeSpan _timeout = DefaultSleepTimeout;
        private TimeSpan _sleepInterval = DefaultSleepTimeout;
#pragma warning disable IDE0032 // Use auto property
        private string _message = string.Empty;
#pragma warning restore IDE0032 // Use auto property
        private readonly List<Type> _ignoreExceptions = new List<Type>();

        public TimeSpan Timeout
        {
            get => _timeout;
            set => _timeout = value;
        }

        public TimeSpan PollingInterval
        {
            get => _sleepInterval;
            set => _sleepInterval = value;
        }

        public string Message
        {
            get => _message;
            set => _message = value;
        }

        public void IgnoreExceptionTypes(params Type[] types)
        {
            if (types == null)
            {
                throw new ArgumentNullException();
            }

            foreach (Type exceptionType in types)
            {
                if (!typeof(Exception).IsAssignableFrom(exceptionType))
                {
                    throw new ArgumentException("not exception");
                }
            }

            _ignoreExceptions.AddRange(types);
        }

        public virtual TResult Until<TResult>(Func<TResult> condition)
        {
            if (condition == null)
            {
                throw new ArgumentNullException("null condition");
            }

            Type resultType = typeof(TResult);
            if ((resultType.IsValueType && resultType != typeof(bool)) || !typeof(object).IsAssignableFrom(resultType))
            {
                throw new ArgumentException();
            }

            Exception lastExceptio = null;
            DateTime pollingStartTime = DateTime.Now;

            while (true)
            {
                DateTime operationStartTime = DateTime.Now;
                try
                {
                    TResult result = condition();
                    if (resultType == typeof(bool))
                    {
                        bool? boolResult = result as bool?;
                        if (boolResult.HasValue && boolResult.Value)
                        {
                            return result;
                        }
                        else
                        {
                            if (result != null)
                            {
                                return result;
                            }
                        }
                    }
                }
                catch (Exception ex) when (IsIgnoresException(ex))
                {
                    lastExceptio = ex;
                }

                DateTime now = DateTime.Now;
                TimeSpan operationTime = now - operationStartTime;
                TimeSpan pollingTime = now - pollingStartTime;

                if (_timeout < pollingTime + _sleepInterval + operationTime)
                {
                    ThrowTimeoutException(Message, lastExceptio);
                }
            }
        }

        private bool IsIgnoresException(Exception ex)
        {
            return _ignoreExceptions.Any(type => type.IsAssignableFrom(ex.GetType()));
        }

        protected virtual void ThrowTimeoutException(string exceptionMessage, Exception exception)
        {
            throw new TimeoutException(exceptionMessage, exception);
        }
    }
}
