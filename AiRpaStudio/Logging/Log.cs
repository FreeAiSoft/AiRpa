using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace RPA.Logging
{
    internal class Log
    {
        private static readonly string log = "log.txt";

        public static void Write(string logString, Exception exc = null)
        {
            string text = $"{string.Format("[{0:H:mm:ss:fff}] ", DateTime.Now)} {logString}\n";
            if (exc != null)
            {
                text += $"{exc}\n{exc.StackTrace}";
            }

            try
            {
                using StreamWriter sw = File.AppendText(log);
                sw.WriteLine(text);
                Console.WriteLine(text);
                sw.Close();
            }
            catch (Exception)
            {
                //Console.WriteLine($"Исключение при доступе к логу\n{ex}");
            }
        }

        public static void AppDomain_CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            ReportExceptionAndShowMessage((Exception)e.ExceptionObject);
            Application.Exit();
        }

        public static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            ReportExceptionAndShowMessage(e.Exception);
            Application.Exit();
        }

        public static void ReportExceptionAndShowMessage(Exception ex)
        {
            string text = $"{Assembly.GetEntryAssembly()?.GetName()?.Version.ToString()} {DateTime.Now}\n\r\n\r{ex}";
            try
            {
                ReportException(text);
                _ = System.Windows.MessageBox.Show(text, "Упс!! Исключение");
            }
            catch (Exception)
            {
                _ = System.Windows.MessageBox.Show(text, "Исключение");
            }
        }

        public static void ReportException(string text)
        {
            Write(text);
        }

        internal static void Write(Exception ex2)
        {
            throw new NotImplementedException();
        }
    }
}
