using ActivityLibraryBase.Attributes;
using ActivityLibraryBase.BaseActivities;
using ActivityLibraryBase.Resources;
using System;
using System.Activities;
using System.Diagnostics;

namespace Activities.OS.Main
{
    [LocalizedDescription("kill_process_desc", typeof(Resources.ActivitiesOS))]
    [LocalizedDisplayName("kill_process", typeof(Resources.ActivitiesOS))]
    public sealed class KillProcess : TimeoutCodeActivity
    {
        [LocalizedDescription("process_name_desc", typeof(Resources.ActivitiesOS))]
        [LocalizedDisplayName("process_name", typeof(Resources.ActivitiesOS))]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        public InArgument<string> ProcessName { get; set; }

        [LocalizedDescription("path_to_process_exe_desc", typeof(Resources.ActivitiesOS))]
        [LocalizedDisplayName("path_to_process_exe", typeof(Resources.ActivitiesOS))]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        public InArgument<string> ProcessFilePath { get; set; }

        public override void RunTimeoutOperation(CodeActivityContext context)
        {
            string processFilePath = context.GetValue(ProcessFilePath);
            string processName = context.GetValue(ProcessName);
            try
            {
                Process[] runningProcesses = Process.GetProcesses();

                foreach (Process process in runningProcesses)
                {
                    // now check the modules of the process
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(processFilePath))
                        {
                            foreach (ProcessModule module in process.Modules)
                            {
                                if (module.FileName.Equals(processFilePath.Trim()))
                                {
                                    process.Kill();
                                }
                            }
                        }
                    }
                    catch { }

                    try
                    {
                        if (!string.IsNullOrWhiteSpace(processName))
                        {
                            foreach (ProcessModule module in process.Modules)
                            {
                                if (module.ModuleName.Equals(processName.Trim()))
                                {
                                    process.Kill();
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message + ex.StackTrace);
            }
        }
    }
}
