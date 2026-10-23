using ActivityLibraryBase.Attributes;
using ActivityLibraryBase.BaseActivities;
using ActivityLibraryBase.Resources;
using System.Activities;
using System.Diagnostics;
using System.IO;

namespace Activities.OS.Main
{
    [LocalizedDescription("powershell_script_desc", typeof(Resources.ActivitiesOS))]
    [LocalizedDisplayName("powershell_script", typeof(Resources.ActivitiesOS))]
    public sealed class PowerShell : TimeoutCodeActivity
    {
        [LocalizedDescription("script_desc", typeof(Resources.ActivitiesOS))]
        [LocalizedDisplayName("script", typeof(Resources.ActivitiesOS))]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        public InArgument<string> Script { get; set; }

        [LocalizedDescription("output_powershell_desc", typeof(Resources.ActivitiesOS))]
        [LocalizedDisplayName("output_command_line", typeof(Resources.ActivitiesOS))]
        [LocalizedCategory("activities_base_output_data", typeof(ActivityLibraryBaseResorces))]
        public OutArgument<string> Output { get; set; }

        public override void RunTimeoutOperation(CodeActivityContext context)
        {
            string script = context.GetValue(Script);
            string output = RunScript(script);
            Output.Set(context, output);
        }

        private string RunScript(string scriptText)
        {
            Process proc = new Process();
            ProcessStartInfo startInfo = new ProcessStartInfo()
            {
                FileName = "powershell.exe",
                Arguments = scriptText,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            proc.StartInfo = startInfo;
            _ = proc.Start();
            StreamReader sr = proc.StandardOutput;
            string output = sr.ReadToEnd();
            proc.WaitForExit();

            return output;
        }
    }
}
