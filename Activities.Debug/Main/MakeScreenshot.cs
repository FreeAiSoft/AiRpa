using System.Activities;
using System.ComponentModel;
using Activities.Debug.Designers;
using Activities.Debug.Helpers;
using ActivityLibraryBase.Attributes;
using ActivityLibraryBase.BaseActivities;
using ActivityLibraryBase.Resources;

namespace Activities.Debug.Main
{
    [LocalizedDescription("make_screenshot_desc", typeof(Resources.ActivitiesDebug))]
    [LocalizedDisplayName("make_screenshot", typeof(Resources.ActivitiesDebug))]
    [Designer(typeof(MakeScreenshotDesigner))]
    public sealed class MakeScreenshot : BaseCodeActivity
    {
        [LocalizedDescription("full_file_path_desc", typeof(Resources.ActivitiesDebug))]
        [LocalizedDisplayName("full_file_path", typeof(Resources.ActivitiesDebug))]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        public InArgument<string> FullFilePath { get; set; }

        public override void Run(CodeActivityContext context)
        {
            string fullFilePath = FullFilePath.Get(context);
            using (ScreenshotHelper screenshotHelper = new ScreenshotHelper())
            {
                screenshotHelper.Save(fullFilePath);
            }
        }
    }
}
