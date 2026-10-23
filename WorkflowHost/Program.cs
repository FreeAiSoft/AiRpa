using System;
using System.Activities;
using System.Activities.Tracking;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Xml;
using EnvDTE80;

namespace WorkflowHost
{
    internal class Program
    {
        private static CustomTrackingParticipant _trackingParticipant;
        private static WorkflowApplication _wfApp;
        private static bool _isComplete = false;

        //[STAThread]
        private static void Main()
        {
            //string activeTabFilePath = GetActiveTabFilePath();
            string activeTabFilePath = "C:\\Users\\Maxxx\\source\\repos\\Mybot\\RPA-1\\Main.xaml";

            try
            {
                Activity activity = null;
                IDictionary<string, object> parameters = new Dictionary<string, object>();
                try
                {
                    GetAssemblyAndType(activeTabFilePath, out string assemblyName, out string typeName);
                    System.Runtime.Remoting.ObjectHandle obj = Activator.CreateInstance(assemblyName, typeName);
                    activity = (Activity)obj.Unwrap();
                }
                catch (Exception ex)
                {
                    _ = MessageBox.Show($"Не удалось загрузить активность по пути {activeTabFilePath}{Environment.NewLine}{ex}{Environment.NewLine}{ex.StackTrace}");
                    return;
                }

                _trackingParticipant = CustomTrackingParticipant.Get();

                _wfApp = new WorkflowApplication(activity, parameters);
                _wfApp.Extensions.Add(_trackingParticipant);
                _wfApp.Completed = WorkflowApplicationCompleted;
                _wfApp.OnUnhandledException = WorkflowAppOnUnhandledException;
                _wfApp.Run();

                _trackingParticipant.TrackingRecordReceived += TrackingParticipant_TrackingRecordReceived;
                while (!_isComplete)
                {
                    Thread.Sleep(1000);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
            _ = Console.ReadKey();
        }

        private static void TrackingParticipant_TrackingRecordReceived(object sender, TrackingEventArgs e)
        {
            if (e.ActivityInfo != null)
            {
                try
                {
                    string variables = string.Empty;
                    string arguments = string.Empty;
                    if (e.Record != null)
                    {
                        ActivityStateRecord activityStateRecord = e.Record as ActivityStateRecord;
                        if (activityStateRecord.Arguments.Count > 0)
                        {
                            arguments = $"arguments:{Environment.NewLine}";
                            foreach (KeyValuePair<string, object> pair in activityStateRecord.Arguments)
                            {
                                arguments += $"{pair.Key}={pair.Value}{Environment.NewLine}";
                            }
                        }

                        if (activityStateRecord.Variables.Count > 0)
                        {
                            variables = $"variables:{Environment.NewLine}";
                            foreach (KeyValuePair<string, object> pair in activityStateRecord.Variables)
                            {
                                variables += $"{pair.Key}={pair.Value}{Environment.NewLine}";
                            }
                        }
                    }
                    string message = $"[{e.Record.EventTime}]: {e.ActivityInfo.Name}({e.ActivityInfo.Id}){Environment.NewLine}{arguments}{variables}";
                    Log(message);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.StackTrace);
                }
            }
        }

        private static void Log(string message)
        {
            Console.WriteLine(message);
        }

        public static void WorkflowApplicationCompleted(WorkflowApplicationCompletedEventArgs args)
        {
            _isComplete = true;
        }

        public static UnhandledExceptionAction WorkflowAppOnUnhandledException(WorkflowApplicationUnhandledExceptionEventArgs args)
        {
            Log(args.UnhandledException.Message);
            _ = MessageBox.Show($"{args.UnhandledException.Message}{Environment.NewLine}{args.UnhandledException.StackTrace}");
            return UnhandledExceptionAction.Terminate;
        }

        private static void GetAssemblyAndType(string activeTabFilePath, out string assemblyName, out string typeName)
        {
            assemblyName = GetAssemblyName(activeTabFilePath, out string projectDir);
            if (assemblyName == null)
            {
                typeName = null;
                return;
            }
            typeName = GetTypeName(activeTabFilePath, projectDir);
        }

        private static string GetTypeName(string activeTabFilePath, string projectDir)
        {
            string extName = Path.GetExtension(activeTabFilePath);
            string pathToExecutingFile = extName == ".xaml" ? activeTabFilePath : Path.Combine(projectDir, "Main.xaml");
            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.Load(pathToExecutingFile);
            string typeName = xmlDoc.GetElementsByTagName("Activity")[0].Attributes.GetNamedItem("x:Class").Value;
            return typeName;
        }

        private static string GetAssemblyName(string activeTabFilePath, out string projectDir)
        {
            projectDir = null;
            string dir = Path.GetDirectoryName(activeTabFilePath);

            if (dir == null)
            {
                return null;
            }

            string[] files = Directory.GetFiles(dir, "*.csproj");
            if (files.Length > 0)
            {
                projectDir = dir;
                XmlDocument xmlDoc = new XmlDocument();
                xmlDoc.Load(files[0]);
                return xmlDoc.GetElementsByTagName("AssemblyName")[0].FirstChild.Value;
            }
            string left = Directory.GetParent(activeTabFilePath)?.ToString();
            return GetAssemblyName(left, out projectDir);
        }

#pragma warning disable IDE0051 // Remove unused private members
        private static string GetActiveTabFilePath()
#pragma warning restore IDE0051 // Remove unused private members
        {
            DTE2 dte = (DTE2)Marshal.GetActiveObject("VisualStudio.DTE.17.0");
            MessageFilter.Register();
            Thread.Sleep(1000);

            string path = string.Empty;
            bool isOk = false;
            Exception exception = null;

            for (int i = 0; i < 10; i++)
            {
                try
                {
                    path = dte.ActiveDocument.FullName;
                    isOk = true;
                    break;
                }
                catch (Exception ex)
                {
                    exception = ex;
                    Thread.Sleep(500);
                }
            }

            if (!isOk)
            {
                Debug.WriteLine(exception);
            }

            MessageFilter.Revoke();
            return path;
        }
    }
}
