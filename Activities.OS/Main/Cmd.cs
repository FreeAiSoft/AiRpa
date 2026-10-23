using Activities.OS.Designers;
using ActivityLibraryBase.Attributes;
using ActivityLibraryBase.BaseActivities;
using ActivityLibraryBase.Resources;
using System.Activities;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace Activities.OS.Main
{
    [LocalizedDescription("cmd_line_desc", typeof(Resources.ActivitiesOS))]
    [LocalizedDisplayName("cmd_line", typeof(Resources.ActivitiesOS))]
    [Designer(typeof(CmdDesigner))]
    public sealed class Cmd : TimeoutCodeActivity
    {
        [LocalizedDescription("command_desc", typeof(Resources.ActivitiesOS))]
        [LocalizedDisplayName("command", typeof(Resources.ActivitiesOS))]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        public InArgument<string> Command { get; set; }

        [LocalizedDescription("output_command_line_desc", typeof(Resources.ActivitiesOS))]
        [LocalizedDisplayName("output_command_line", typeof(Resources.ActivitiesOS))]
        [LocalizedCategory("activities_base_output_data", typeof(ActivityLibraryBaseResorces))]
        public OutArgument<string> Output { get; set; }

        public override void RunTimeoutOperation(CodeActivityContext context)
        {
            string command = context.GetValue(Command);
            string output = RunCommand(command);
            Output.Set(context, output);
        }

        private string RunCommand(string command)
        {
            Process proc = new Process();
            ProcessStartInfo startInfo = new ProcessStartInfo()
            {
                FileName = "cmd.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                StandardOutputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage)
            };
            proc.StartInfo = startInfo;
            _ = proc.Start();
            using (StreamWriter sw = proc.StandardInput)
            {
                if (sw.BaseStream.CanWrite)
                {
                    sw.WriteLine(command);
                }
            }
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();

            return output;
        }
    }
}
