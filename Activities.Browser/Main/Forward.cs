using Activities.Browser.Resources;
using ActivityLibraryBase.Attributes;
using ActivityLibraryBase.BaseActivities;
using ActivityLibraryBase.Resources;
using PuppeteerSharp;
using System.Activities;

namespace Activities.Browser
{
    [LocalizedDescription("forward_desc", typeof(ActivitiesBrowser))]
    [LocalizedDisplayName("forward", typeof(ActivitiesBrowser))]
    public sealed class Forward : TimeoutCodeActivity
    {
        [RequiredArgument]
        [LocalizedDescription("page_desc", typeof(ActivitiesBrowser))]
        [LocalizedDisplayName("page", typeof(ActivitiesBrowser))]
        [LocalizedCategory("activities_base_input_data", typeof(ActivityLibraryBaseResorces))]
        public InArgument<IPage> Page { get; set; }

        public override void RunTimeoutOperation(CodeActivityContext context)
        {
            IPage page = Page.Get(context);
            _ = page.GoForwardAsync().GetAwaiter().GetResult();
        }
    }
}
