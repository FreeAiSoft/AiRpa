using System;
using System.IO;
using System.Windows.Threading;
using RPA.Helpers;

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

        /// <summary>
        /// Падение процесса (не UI-поток, например рабочий поток WF или MCP-сервер).
        /// Текст ошибки пишется в <c>log.txt</c> и показывается окном ошибки.
        /// </summary>
        public static void AppDomain_CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            ReportExceptionAndShowMessage(e.ExceptionObject as Exception);
            ShutdownApplication();
        }

        /// <summary>
        /// Падение UI-потока WPF. Раньше здесь стоял WinForms-обработчик
        /// (WinForms ThreadException), который для WPF-приложения
        /// не срабатывает: исключения диспетчера ловятся именно этим событием.
        /// </summary>
        public static void Application_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            ReportExceptionAndShowMessage(e.Exception);
            e.Handled = true;
            ShutdownApplication();
        }

        /// <summary>
        /// Логирует падение и показывает его окном ошибки студии (с версией, временем и стеком).
        /// </summary>
        public static void ReportExceptionAndShowMessage(Exception ex)
        {
            try
            {
                ReportException(ErrorDialog.FormatCrashMessage(ex));
            }
            catch (Exception)
            {
                // Лог не должен помешать показать саму ошибку.
            }

            ErrorDialog.ShowCrash(ex);
        }

        public static void ReportException(string text)
        {
            Write(text);
        }

        internal static void Write(Exception ex2)
        {
            throw new NotImplementedException();
        }

        private static void ShutdownApplication()
        {
            try
            {
                System.Windows.Application.Current?.Shutdown();
            }
            catch (Exception)
            {
                // Приложение уже завершается — повторный Shutdown не нужен.
            }
        }
    }
}
