using RpaCommon.Enums;

namespace RPA.Logging
{
    public class LogItem
    {
        public string Message { get; set; }

        public LogLevel LogLevel { get; set; } = LogLevel.Info;

        public LogItem() { }

        public LogItem(string message)
        {
            Message = message;
        }
        public override string ToString()
        {
            return $"{LogLevel}: {Message}";
        }
    }
}
