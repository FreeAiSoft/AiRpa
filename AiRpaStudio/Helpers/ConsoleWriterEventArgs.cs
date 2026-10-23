using System;

namespace RPA.Helpers
{
    public sealed class ConsoleWriterEventArgs : EventArgs
    {
        public string Value { get; }

        public ConsoleWriterEventArgs(string value)
        {
            Value = value;
        }
    }
}
