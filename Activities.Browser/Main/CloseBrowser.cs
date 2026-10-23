using System.Activities;
using Activities.Browser.Resources;
using ActivityLibraryBase.Attributes;
using ActivityLibraryBase.BaseActivities;
using ActivityLibraryBase.Resources;
using PuppeteerSharp;

namespace Activities.Browser
{
    [LocalizedDescription("close_browser", typeof(ActivitiesBrowser))]
    [LocalizedDisplayName("close_browser", typeof(ActivitiesBrowser))]
    public sealed class CloseBrowser : TimeoutCodeActivity
    {
        [RequiredArgument]
        [LocalizedDescription("browser_desc", typeof(ActivitiesBrowser))]
        [LocalizedDisplayName("browser", typeof(ActivitiesBrowser))]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        public InArgument<IBrowser> Browser { get; set; }

        public override void RunTimeoutOperation(CodeActivityContext context)
        {
            IBrowser browser = Browser.Get(context);
            browser.CloseAsync().Wait();
        }
    }
}
